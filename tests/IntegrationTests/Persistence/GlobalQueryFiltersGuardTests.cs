// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Linq.Expressions;
using JOIN.Application.Interface;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// Guard test that enforces the SPEC 38 contract on <c>ApplicationDbContext.ConfigureGlobalQueryFilters</c>.
/// Runs against the real DbContext model (no SQL needed — uses <c>OnModelCreating</c>).
/// Fails loudly when a tenant-owned entity loses its filter or a global filter appears on a
/// cross-tenant entity, so the regression is caught at build/test time instead of in production.
/// </summary>
public sealed class GlobalQueryFiltersGuardTests
{
    /// <summary>
    /// BaseTenantEntity subtypes with an explicit "must be cross-tenant" reason.
    /// SPEC 38: UserCompany is the only BaseTenantEntity without a CompanyId filter
    /// (login has no CompanyId claim yet; we read across the user's companies).
    /// </summary>
    private static readonly Type[] BaseTenantEntityCrossTenantExceptions =
    {
        typeof(UserCompany),
    };

    /// <summary>
    /// IAuditableEntity subtypes with an explicit "must have no query filter" reason.
    /// SPEC 38: ApplicationUser is intentionally filter-free so CreateTicket/UpdateTicket
    /// can resolve CreatedByUserName/AssignedToUserName via GetAsync(id) even after the
    /// author/assignee was soft-deleted.
    /// </summary>
    private static readonly Type[] AuditableEntityNoFilterExceptions =
    {
        typeof(ApplicationUser),
    };

    [Fact]
    public void AuditableEntities_MustHaveQueryFilter()
    {
        using var context = new TestApplicationDbContext();

        var violations = new List<string>();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType is null || !typeof(IAuditableEntity).IsAssignableFrom(clrType))
            {
                continue;
            }

            if (AuditableEntityNoFilterExceptions.Any(t => t.IsAssignableFrom(clrType)))
            {
                continue;
            }

            if (entityType.GetQueryFilter() is null)
            {
                violations.Add($"{clrType.Name} (auditable, no query filter — see SPEC 38)");
            }
        }

        Assert.True(
            violations.Count == 0,
            "The following IAuditableEntity types are missing a query filter. "
            + "Add them to ConfigureGlobalQueryFilters, or justify them in AuditableEntityNoFilterExceptions. "
            + "Offenders: " + string.Join(", ", violations));
    }

    [Fact]
    public void BaseTenantEntities_MustHaveCompanyIdInQueryFilter()
    {
        using var context = new TestApplicationDbContext();

        var violations = new List<string>();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (clrType is null || !typeof(BaseTenantEntity).IsAssignableFrom(clrType))
            {
                continue;
            }

            if (BaseTenantEntityCrossTenantExceptions.Any(t => t.IsAssignableFrom(clrType)))
            {
                continue;
            }

            var filter = entityType.GetQueryFilter();
            if (filter is null)
            {
                violations.Add($"{clrType.Name} (tenant-owned, no query filter at all)");
                continue;
            }

            if (!FilterReferencesCompanyId(filter))
            {
                violations.Add($"{clrType.Name} (tenant-owned, filter does not reference CompanyId)");
            }
        }

        Assert.True(
            violations.Count == 0,
            "The following BaseTenantEntity types are missing a CompanyId predicate in their query filter. "
            + "SPEC 38 requires every tenant-owned entity to include CompanyId == _currentUserService.CompanyId. "
            + "Offenders: " + string.Join(", ", violations));
    }

    /// <summary>
    /// Walks the body of the query filter expression looking for a MemberExpression on a
    /// property named exactly "CompanyId". Cheap structural check — does not bind symbols.
    /// </summary>
    private static bool FilterReferencesCompanyId(LambdaExpression filter)
    {
        var visitor = new CompanyIdFinder();
        visitor.Visit(filter.Body);
        return visitor.FoundCompanyId;
    }

    private sealed class CompanyIdFinder : ExpressionVisitor
    {
        public bool FoundCompanyId { get; private set; }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.Name == "CompanyId")
            {
                FoundCompanyId = true;
            }
            return base.VisitMember(node);
        }
    }

    /// <summary>
    /// A minimal DbContext that calls the production OnModelCreating so the guard test
    /// inspects the same model the application uses at runtime. The SQL Server provider is
    /// configured but no connection is opened — OnModelCreating does not need a live DB.
    /// </summary>
    private sealed class TestApplicationDbContext : ApplicationDbContext
    {
        public TestApplicationDbContext()
            : base(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlServer(
                        "Server=(localdb)\\mssqllocaldb;Database=GuardTest;Trusted_Connection=True;",
                        builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                    .Options,
                null!,
                new StubCurrentUserService())
        {
        }
    }

    /// <summary>
    /// Minimal ICurrentUserService stub for the in-memory context — never queried by the guard.
    /// </summary>
    private sealed class StubCurrentUserService : ICurrentUserService
    {
        public string? UserId => null;
        public Guid CompanyId => Guid.Empty;
        public bool IsAuthenticated => false;
        public Guid? RefreshTokenId => null;
        public string? IpAddress => null;
        public string? UserAgent => null;
        public bool IsInRole(string role) => false;
    }
}