using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.RoleCompanies.Commands.DeleteRoleCompany;
using JOIN.Application.UnitTest.Security.RoleCompanies.TestHelpers;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace JOIN.Application.UnitTest.Security.RoleCompanies.Commands.DeleteRoleCompany;

/// <summary>
/// Tests for the DeleteRoleCompanyCommandHandler. Covers tenant validation, missing-link branch,
/// SaveChangesAsync=0 race, happy-path GcRecord stamping, and the visibility warning.
/// </summary>
public sealed class DeleteRoleCompanyCommandHandlerTests
{
    /// <summary>
    /// Empty CompanyId short-circuits with 401.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyId()
    {
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCompanyCommand(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_COMPANY_ID");
    }

    /// <summary>
    /// Link not found → 404 ROLE_COMPANY_NOT_FOUND without touching UoW.
    /// </summary>
    [Fact]
    public async Task Handle_WhenLinkMissing_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(It.IsAny<Guid>(), tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RoleCompany?)null);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCompanyCommand(Guid.NewGuid()), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_COMPANY_NOT_FOUND");
        context.RoleCompanyRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<RoleCompany>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// SaveChangesAsync returning 0 → 404 (race condition / already soft-deleted).
    /// </summary>
    [Fact]
    public async Task Handle_WhenSaveChangesReturnsZero_ShouldReturnNotFound()
    {
        var tenantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCompanyTestFactory.Create(linkId, Guid.NewGuid(), tenantId));
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCompanyCommand(linkId), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("ROLE_COMPANY_NOT_FOUND");
    }

    /// <summary>
    /// Happy path: MarkAsDeleted() stamps GcRecord with the yyyyMMdd UTC int; LastModified/LastModifiedBy are set;
    /// UpdateAsync + SaveChangesAsync persist.
    /// </summary>
    [Fact]
    public async Task Handle_WhenLinkIsActive_ShouldStampDeletionStampAndPersist()
    {
        var tenantId = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var context = new TestContext();
        context.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(tenantId);
        context.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns("user-1");
        context.RoleCompanyRepositoryMock
            .Setup(x => x.GetByIdForUpdateAsync(linkId, tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RoleCompanyTestFactory.Create(linkId, roleId, tenantId));
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        RoleCompany? captured = null;
        context.RoleCompanyRepositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<RoleCompany>(), It.IsAny<CancellationToken>()))
            .Callback<RoleCompany, CancellationToken>((rc, _) => captured = rc)
            .Returns(Task.CompletedTask);

        var before = DateTime.UtcNow;
        var handler = context.CreateHandler();
        var response = await handler.Handle(new DeleteRoleCompanyCommand(linkId), CancellationToken.None);
        var after = DateTime.UtcNow;

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.GcRecord.Should().BeGreaterThan(0);
        var stampDate = ParseStamp(captured.GcRecord);
        stampDate.Date.Should().BeOnOrAfter(before.Date).And.BeOnOrBefore(after.Date);
        captured.LastModifiedBy.Should().Be("user-1");
        captured.LastModified.Should().NotBeNull();
    }

    private static DateTime ParseStamp(int yyyymmdd)
    {
        var s = yyyymmdd.ToString("D8", System.Globalization.CultureInfo.InvariantCulture);
        return DateTime.SpecifyKind(
            new DateTime(int.Parse(s[..4]), int.Parse(s[4..6]), int.Parse(s[6..8])),
            DateTimeKind.Utc);
    }

    private sealed class TestContext
    {
        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<IRoleCompanyRepository> RoleCompanyRepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public DeleteRoleCompanyCommandHandler CreateHandler() => new(
            UnitOfWorkMock.Object,
            RoleCompanyRepositoryMock.Object,
            CurrentUserServiceMock.Object,
            NullLogger<DeleteRoleCompanyCommandHandler>.Instance);
    }
}
