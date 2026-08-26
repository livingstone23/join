// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Audit;

/// <summary>
/// Configures the database mapping for the <see cref="AuditLog"/> entity.
/// Append-only bitácora: no <c>GcRecord</c>, no <c>HasQueryFilter</c>, no FKs to the
/// audited entities (a log row must survive deletion of the entity it records).
/// </summary>
public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", "Security");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityName)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.EntityId)
            .IsRequired();

        builder.Property(a => a.EntityLabel)
            .HasMaxLength(256);

        builder.Property(a => a.Action)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(a => a.CompanyId)
            .IsRequired();

        builder.Property(a => a.ChangedBy)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.ChangedAtUtc)
            .IsRequired();

        builder.Property(a => a.IpAddress)
            .HasMaxLength(45);

        builder.Property(a => a.OldValuesJson)
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.NewValuesJson)
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.MetadataJson)
            .HasColumnType("nvarchar(max)");

        // Drives the per-entity history lookup.
        builder.HasIndex(a => new { a.EntityName, a.EntityId, a.ChangedAtUtc })
            .HasDatabaseName("IX_AuditLogs_Entity")
            .IsDescending(false, false, true);

        // Drives the tenant-scoped activity feed.
        builder.HasIndex(a => new { a.CompanyId, a.ChangedAtUtc })
            .HasDatabaseName("IX_AuditLogs_Company_ChangedAt")
            .IsDescending(false, true);

        // Intentionally NO foreign keys to Security.Users / Security.Roles / Common.Companies:
        // a log row must outlive deletion of the entity it documents.
    }
}
