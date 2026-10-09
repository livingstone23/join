// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using JOIN.Domain.Audit;
using JOIN.Persistence.Contexts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JOIN.IntegrationTests.Persistence;

/// <summary>
/// SPEC 40-post (C) — guard over the EF model: no unique index may include <c>GcRecord</c> in its key.
/// Uniqueness among active rows is expressed with a filtered index (<c>"gcrecord" = 0</c>); a key that
/// contains <c>GcRecord</c> makes two rows deleted on the same day collide (the stamp is <c>yyyyMMdd</c>).
/// Model-only: builds <see cref="ApplicationDbContext"/> with Npgsql, never opens a connection.
/// </summary>
public sealed class UniqueIndexSoftDeleteGuardPostgreSqlTests
{
    [Fact]
    public void UniqueIndexes_MustNotIncludeGcRecordInTheirKey()
    {
        using var context = new ModelOnlyDbContext();

        var violations = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetIndexes()
                .Where(index => index.IsUnique
                    && index.Properties.Any(p => p.Name == nameof(BaseAuditableEntity.GcRecord)))
                .Select(index => $"{entityType.ClrType.Name}: {index.GetDatabaseName()}"))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "Unique indexes with GcRecord in the key (use a filtered index on \"gcrecord\" = 0 instead, SPEC 40-post): "
            + string.Join(", ", violations));
    }

    private sealed class ModelOnlyDbContext : ApplicationDbContext
    {
        public ModelOnlyDbContext()
            : base(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseNpgsql(
                        "Host=localhost;Database=model_only",
                        builder => builder.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                    .Options,
                null!,
                new StubCurrentUserService())
        {
        }
    }

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
