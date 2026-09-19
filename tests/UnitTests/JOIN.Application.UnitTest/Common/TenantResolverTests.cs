// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using Moq;

namespace JOIN.Application.UnitTest.Common;

/// <summary>
/// Unit tests for <see cref="TenantResolver"/> — the shared tenant-resolution rule extracted
/// while implementing join_frontb specs/10-role-companies.md, step 2 of the plan. Covers the
/// three cases the spec calls out explicitly: a real SuperAdmin using an explicit override,
/// a SuperAdminCompany-only caller whose override is ignored, and a caller holding both flags.
/// </summary>
public sealed class TenantResolverTests
{
    /// <summary>
    /// (a) A real SuperAdmin (Identity role "SuperAdmin") supplying an explicit CompanyId
    /// different from their own token's tenant → the explicit value wins.
    /// </summary>
    [Fact]
    public void Resolve_WhenCallerIsRealSuperAdminWithExplicitCompanyId_ShouldUseExplicitCompanyId()
    {
        var ownTenantId = Guid.NewGuid();
        var explicitCompanyId = Guid.NewGuid();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ownTenantId);
        currentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(true);

        var resolved = TenantResolver.Resolve(currentUserServiceMock.Object, explicitCompanyId);

        resolved.Should().Be(explicitCompanyId);
        resolved.Should().NotBe(ownTenantId);
    }

    /// <summary>
    /// (b) A SuperAdminCompany-only caller (no "SuperAdmin" Identity role) supplying a
    /// CompanyId → the override is silently ignored, resolution always falls back to the
    /// token's own tenant, same as before this spec.
    /// </summary>
    [Fact]
    public void Resolve_WhenCallerIsSuperAdminCompanyOnlyWithExplicitCompanyId_ShouldIgnoreOverride()
    {
        var ownTenantId = Guid.NewGuid();
        var someOtherCompanyId = Guid.NewGuid();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ownTenantId);
        currentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(false);

        var resolved = TenantResolver.Resolve(currentUserServiceMock.Object, someOtherCompanyId);

        resolved.Should().Be(ownTenantId);
    }

    /// <summary>
    /// (c) A caller holding both flags (real SuperAdmin AND SuperAdminCompany, both surfaced
    /// as Identity roles per LoginCommandHandler.ResolveRoleNamesAsync) still gets the explicit
    /// override honored — having the tenant-scoped role too does not downgrade the SuperAdmin
    /// override.
    /// </summary>
    [Fact]
    public void Resolve_WhenCallerHasBothSuperAdminAndSuperAdminCompanyRoles_ShouldStillUseExplicitCompanyId()
    {
        var ownTenantId = Guid.NewGuid();
        var explicitCompanyId = Guid.NewGuid();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ownTenantId);
        currentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(true);
        currentUserServiceMock.Setup(x => x.IsInRole("SuperAdminCompany")).Returns(true);

        var resolved = TenantResolver.Resolve(currentUserServiceMock.Object, explicitCompanyId);

        resolved.Should().Be(explicitCompanyId);
    }

    /// <summary>
    /// No override supplied (null) → always the token's own tenant, regardless of role —
    /// the common case, unaffected by this spec.
    /// </summary>
    [Fact]
    public void Resolve_WhenNoExplicitCompanyIdSupplied_ShouldUseOwnTenant()
    {
        var ownTenantId = Guid.NewGuid();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        currentUserServiceMock.SetupGet(x => x.CompanyId).Returns(ownTenantId);
        currentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(true);

        var resolved = TenantResolver.Resolve(currentUserServiceMock.Object, requestedCompanyId: null);

        resolved.Should().Be(ownTenantId);
    }
}
