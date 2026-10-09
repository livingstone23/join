// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.Common;

/// <summary>
/// Unit tests for <see cref="SoftDeleteRestorer"/> (SPEC 41, section B): the seven restore steps
/// in order — load, not found, tenant check, not deleted, parent deleted, active duplicate,
/// restore + save.
/// </summary>
public sealed class SoftDeleteRestorerTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly DateTime DeletedOnUtc = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RestoreAsync_WhenEntityDoesNotExist_ShouldReturnNotFound()
    {
        var context = new TestContext();

        var response = await context.CreateRestorer().RestoreAsync<Gender>(Guid.NewGuid(), null);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("NOT_FOUND");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_WhenTenantEntityBelongsToAnotherCompany_ShouldReturnNotFound()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(Guid.NewGuid());

        var response = await context.CreateRestorer().RestoreAsync<Gender>(gender.Id, null);

        response.Message.Should().Be("NOT_FOUND");
        gender.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAsync_WhenNonSuperAdminRequestsAnotherCompany_ShouldIgnoreOverrideAndReturnNotFound()
    {
        var context = new TestContext();
        var otherCompanyId = Guid.NewGuid();
        var gender = context.SetupDeletedGender(otherCompanyId);

        var response = await context.CreateRestorer().RestoreAsync<Gender>(gender.Id, otherCompanyId);

        response.Message.Should().Be("NOT_FOUND");
        gender.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAsync_WhenSuperAdminRequestsTheEntityCompany_ShouldRestore()
    {
        var context = new TestContext(isSuperAdmin: true);
        var otherCompanyId = Guid.NewGuid();
        var gender = context.SetupDeletedGender(otherCompanyId);
        context.SetupSaveChanges(1);

        var response = await context.CreateRestorer().RestoreAsync<Gender>(gender.Id, otherCompanyId);

        response.IsSuccess.Should().BeTrue();
        gender.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_WhenEntityIsActive_ShouldReturnNotDeleted()
    {
        var context = new TestContext();
        var gender = Gender.Create(CompanyId, "M", "Male");
        context.GenderRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(gender.Id)).ReturnsAsync(gender);

        var response = await context.CreateRestorer().RestoreAsync<Gender>(gender.Id, null);

        response.Message.Should().Be("NOT_DELETED");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_WhenParentIsDeleted_ShouldReturnParentDeleted()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(CompanyId);
        var duplicateCheckCalled = false;

        var response = await context.CreateRestorer().RestoreAsync<Gender>(
            gender.Id,
            null,
            isParentDeleted: (_, _) => Task.FromResult(true),
            activeDuplicateExists: (_, _) =>
            {
                duplicateCheckCalled = true;
                return Task.FromResult(false);
            });

        response.Message.Should().Be("PARENT_DELETED");
        duplicateCheckCalled.Should().BeFalse("the parent check runs before the duplicate check");
        gender.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task RestoreAsync_WhenActiveDuplicateExists_ShouldReturnActiveDuplicateExists()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(CompanyId);

        var response = await context.CreateRestorer().RestoreAsync<Gender>(
            gender.Id,
            null,
            isParentDeleted: (_, _) => Task.FromResult(false),
            activeDuplicateExists: (_, _) => Task.FromResult(true));

        response.Message.Should().Be("ACTIVE_DUPLICATE_EXISTS");
        gender.IsDeleted.Should().BeTrue();
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_WhenChecksPass_ShouldRestoreAndSave()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(CompanyId);
        context.SetupSaveChanges(1);
        Gender? checkedEntity = null;

        var response = await context.CreateRestorer().RestoreAsync<Gender>(
            gender.Id,
            null,
            isParentDeleted: (entity, _) =>
            {
                checkedEntity = entity;
                return Task.FromResult(false);
            },
            activeDuplicateExists: (_, _) => Task.FromResult(false));

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(gender.Id);
        gender.GcRecord.Should().Be(BaseAuditableEntity.ActiveGcRecord);
        checkedEntity.Should().BeSameAs(gender);
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_WhenSaveAffectsNoRows_ShouldReturnRestoreFailed()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(CompanyId);
        context.SetupSaveChanges(0);

        var response = await context.CreateRestorer().RestoreAsync<Gender>(gender.Id, null);

        response.Message.Should().Be("RESTORE_FAILED");
    }

    [Fact]
    public async Task RestoreAsync_WhenGlobalEntity_ShouldSkipTenantCheck()
    {
        var context = new TestContext();
        var country = new Country { Name = "Testland", IsoCode = "TL" };
        country.MarkAsDeleted(DeletedOnUtc);
        var countryRepositoryMock = new Mock<IGenericRepository<Country>>();
        countryRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(country.Id)).ReturnsAsync(country);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<Country>()).Returns(countryRepositoryMock.Object);
        context.SetupSaveChanges(1);

        var response = await context.CreateRestorer().RestoreAsync<Country>(country.Id, null);

        response.IsSuccess.Should().BeTrue();
        country.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_WhenChecksPass_ShouldRunBeforeRestoreBeforeRestoring()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(CompanyId);
        context.SetupSaveChanges(1);
        bool? wasDeletedDuringHook = null;

        var response = await context.CreateRestorer().RestoreAsync<Gender>(
            gender.Id,
            null,
            beforeRestore: (entity, _) =>
            {
                wasDeletedDuringHook = entity.IsDeleted;
                return Task.CompletedTask;
            });

        response.IsSuccess.Should().BeTrue();
        wasDeletedDuringHook.Should().BeTrue("the hook runs right before the restore");
        gender.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_WhenAnActiveDuplicateExists_ShouldNotRunBeforeRestore()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(CompanyId);
        var hookCalled = false;

        await context.CreateRestorer().RestoreAsync<Gender>(
            gender.Id,
            null,
            activeDuplicateExists: (_, _) => Task.FromResult(true),
            beforeRestore: (_, _) =>
            {
                hookCalled = true;
                return Task.CompletedTask;
            });

        hookCalled.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_WhenCascadeSucceeds_ShouldPassTheOriginalStampAndRestore()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(CompanyId);
        var stamp = gender.GcRecord;
        context.SetupSaveChanges(1);
        int? receivedStamp = null;

        var response = await context.CreateRestorer().RestoreAsync<Gender>(
            gender.Id,
            null,
            restoreCascade: (_, originalStamp, _) =>
            {
                receivedStamp = originalStamp;
                return Task.FromResult<Response<Guid>?>(null);
            });

        response.IsSuccess.Should().BeTrue();
        receivedStamp.Should().Be(stamp);
        gender.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_WhenCascadeFails_ShouldAbortWithoutSaving()
    {
        var context = new TestContext();
        var gender = context.SetupDeletedGender(CompanyId);

        var response = await context.CreateRestorer().RestoreAsync<Gender>(
            gender.Id,
            null,
            restoreCascade: (_, _, _) => Task.FromResult<Response<Guid>?>(Response<Guid>.Error("PARENT_DELETED", ["child"])));

        response.Message.Should().Be("PARENT_DELETED");
        gender.IsDeleted.Should().BeTrue();
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(20260928, true)]
    public async Task IsParentDeletedAsync_ForIdentityUser_ShouldReadItsGcRecord(int gcRecord, bool expected)
    {
        var context = new TestContext();
        var user = new ApplicationUser { Id = Guid.NewGuid(), GcRecord = gcRecord };
        var userRepositoryMock = new Mock<IGenericRepository<ApplicationUser>>();
        userRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(user.Id)).ReturnsAsync(user);
        context.UnitOfWorkMock.Setup(x => x.GetRepository<ApplicationUser>()).Returns(userRepositoryMock.Object);

        var isDeleted = await context.CreateRestorer().IsParentDeletedAsync<ApplicationUser>(user.Id);

        isDeleted.Should().Be(expected);
    }

    [Fact]
    public async Task IsParentDeletedAsync_WhenParentIsMissing_ShouldBeTrue()
    {
        var context = new TestContext();
        var repositoryMock = new Mock<IGenericRepository<Country>>();
        context.UnitOfWorkMock.Setup(x => x.GetRepository<Country>()).Returns(repositoryMock.Object);

        var isDeleted = await context.CreateRestorer().IsParentDeletedAsync<Country>(Guid.NewGuid());

        isDeleted.Should().BeTrue();
    }

    private sealed class TestContext
    {
        public TestContext(bool isSuperAdmin = false)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(CompanyId);
            CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(isSuperAdmin);
            UnitOfWorkMock.Setup(x => x.GetRepository<Gender>()).Returns(GenderRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<Gender>> GenderRepositoryMock { get; } = new();

        public Gender SetupDeletedGender(Guid companyId)
        {
            var gender = Gender.Create(companyId, "M", "Male");
            gender.MarkAsDeleted(DeletedOnUtc);
            GenderRepositoryMock.Setup(x => x.GetIncludingDeletedAsync(gender.Id)).ReturnsAsync(gender);
            return gender;
        }

        public void SetupSaveChanges(int affectedRows)
            => UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(affectedRows);

        public SoftDeleteRestorer CreateRestorer()
            => new(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}
