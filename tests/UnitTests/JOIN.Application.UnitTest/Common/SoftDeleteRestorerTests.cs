// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
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
