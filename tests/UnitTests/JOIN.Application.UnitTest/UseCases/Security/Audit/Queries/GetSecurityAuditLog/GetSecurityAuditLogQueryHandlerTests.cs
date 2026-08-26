// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Audit;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Audit;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Audit.Queries.GetSecurityAuditLog;
using JOIN.Domain.Audit;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Audit.Queries.GetSecurityAuditLog;

/// <summary>
/// SPEC 29 F11 — read query handler contract.
/// Nine cases: paging + clamping, enum parsing guards, SuperAdmin bypass,
/// tenant isolation, JSON corruption resilience.
/// </summary>
public sealed class GetSecurityAuditLogQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsItemsAndTotalCount_WhenRepositoryProvidesThem()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.IsSuperAdminAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var rows = new List<AuditLog>
        {
            new(Guid.NewGuid())
            {
                EntityName = "Role",
                EntityId = Guid.NewGuid(),
                Action = "Created",
                CompanyId = companyId,
                ChangedBy = Guid.NewGuid().ToString(),
                ChangedAtUtc = DateTime.UtcNow,
                OldValuesJson = null,
                NewValuesJson = "{\"Name\":\"Admin\"}"
            }
        };
        ctx.RepositoryMock.Setup(x => x.ListPagedAsync(
                companyId, "Role", null, null, "Created", null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((rows, new Dictionary<string, string>(), 1));

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(Entity: "Role", Action: "Created"), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().HaveCount(1);
        response.Data.TotalCount.Should().Be(1);
        response.Data!.Items.First().Changes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenPageNumberZero_ClampsToOne()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.IsSuperAdminAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ctx.RepositoryMock.Setup(x => x.ListPagedAsync(
                It.IsAny<Guid?>(), null, null, null, null, null, null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog>(), new Dictionary<string, string>(), 0));

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(PageNumber: 0), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenPageSizeExceedsMax_ClampsTo100()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.IsSuperAdminAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ctx.RepositoryMock.Setup(x => x.ListPagedAsync(
                It.IsAny<Guid?>(), null, null, null, null, null, null, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog>(), new Dictionary<string, string>(), 0));

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(PageSize: 500), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_WhenEntityIsInvalid_ReturnsInvalidEntity()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(Entity: "Inventada"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_ENTITY");
    }

    [Fact]
    public async Task Handle_WhenActionIsInvalid_ReturnsInvalidAction()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(Action: "Inventada"), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("INVALID_ACTION");
    }

    [Fact]
    public async Task Handle_WhenTenantIsEmpty_ReturnsTenantRequired()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.Empty);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TENANT_REQUIRED");
    }

    [Fact]
    public async Task Handle_WhenAllTenantsTrueWithoutSuperAdmin_FiltersByTokenCompany()
    {
        var ctx = new Context();
        var companyId = Guid.NewGuid();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.IsSuperAdminAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        ctx.RepositoryMock.Setup(x => x.ListPagedAsync(
                companyId, null, null, null, null, null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog>(), new Dictionary<string, string>(), 0));

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(AllTenants: true), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.RepositoryMock.Verify(x => x.ListPagedAsync(
            companyId, null, null, null, null, null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAllTenantsTrueWithSuperAdmin_PassesNullCompanyToRepository()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.IsSuperAdminAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ctx.RepositoryMock.Setup(x => x.ListPagedAsync(
                null, null, null, null, null, null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog>(), new Dictionary<string, string>(), 0));

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(AllTenants: true), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        ctx.RepositoryMock.Verify(x => x.ListPagedAsync(
            null, null, null, null, null, null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOldValuesJsonCorrupt_ReturnsEmptyChangesForThatRow()
    {
        var ctx = new Context();
        ctx.CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(Guid.NewGuid());
        ctx.CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
        ctx.UserAdminRepositoryMock.Setup(x => x.IsSuperAdminAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var row = new AuditLog(Guid.NewGuid())
        {
            EntityName = "Role",
            EntityId = Guid.NewGuid(),
            Action = "Updated",
            CompanyId = ctx.CurrentUserServiceMock.Object.CompanyId,
            ChangedBy = Guid.NewGuid().ToString(),
            ChangedAtUtc = DateTime.UtcNow,
            OldValuesJson = "{not json",
            NewValuesJson = "{\"Name\":\"Admin\"}"
        };
        ctx.RepositoryMock.Setup(x => x.ListPagedAsync(
                It.IsAny<Guid?>(), null, null, null, null, null, null, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<AuditLog> { row }, new Dictionary<string, string>(), 1));

        var response = await ctx.Handler.Handle(
            new GetSecurityAuditLogQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Items.Should().HaveCount(1);
        // Corrupt old JSON yields empty changes; new side still parsed.
        response.Data.Items.First().Changes.Should().HaveCount(1);
        response.Data.Items.First().Changes.First().Field.Should().Be("Name");
    }

    private sealed class Context
    {
        public Mock<IAuditLogRepository> RepositoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IUserAdminRepository> UserAdminRepositoryMock { get; } = new();

        public GetSecurityAuditLogQueryHandler Handler => new(
            RepositoryMock.Object,
            CurrentUserServiceMock.Object,
            UserAdminRepositoryMock.Object);
    }
}

