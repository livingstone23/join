// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using JOIN.Domain.Audit;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// Guard test that enforces the SPEC 40 contract: no unique index may include
/// <see cref="BaseAuditableEntity.GcRecord"/> as a key column. <c>GcRecord</c> stores the
/// deletion date (<c>yyyyMMdd</c>), so two rows with the same natural key soft-deleted on the
/// same day would share the same index key and the second <c>SaveChanges</c> would fail.
/// Uniqueness over active rows must be expressed as a filtered unique index
/// (<c>.IsUnique().HasFilter("[GcRecord] = 0")</c>) instead.
/// Runs against the real DbContext model (no SQL needed — uses <c>OnModelCreating</c>).
/// </summary>
public sealed class UniqueIndexSoftDeleteGuardTests
{
    [Fact]
    public void UniqueIndexes_MustNotIncludeGcRecordInKey()
    {
        using var context = new TestApplicationDbContext();

        var violations = new List<string>();

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            foreach (var index in entityType.GetIndexes())
            {
                if (!index.IsUnique)
                {
                    continue;
                }

                if (index.Properties.Any(p => p.Name == nameof(BaseAuditableEntity.GcRecord)))
                {
                    var indexName = index.GetDatabaseName() ?? index.Name ?? "(unnamed)";
                    violations.Add($"{entityType.ClrType.Name}.{indexName}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "The following unique indexes include GcRecord in their key. Two rows with the same "
            + "natural key soft-deleted on the same day would collide. Remove GcRecord from the key "
            + "and use .IsUnique().HasFilter(\"[GcRecord] = 0\") instead (see SPEC 40). "
            + "Offenders: " + string.Join(", ", violations));
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
    /// Minimal ICurrentUserService stub for the model-only context — never queried by the guard.
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
