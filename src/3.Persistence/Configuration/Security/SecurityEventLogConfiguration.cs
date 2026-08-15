// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// Configures the database mapping for the <see cref="SecurityEventLog"/> audit entity.
/// No soft-delete (GcRecord is absent on the entity); the table is append-only.
/// </summary>
public class SecurityEventLogConfiguration : IEntityTypeConfiguration<SecurityEventLog>
{
    /// <summary>
    /// Configures the <see cref="SecurityEventLog"/> entity for the Security schema.
    /// </summary>
    /// <param name="builder">Builder used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<SecurityEventLog> builder)
    {
        builder.ToTable("SecurityEventLogs", "Security");

        builder.HasKey(log => log.Id);

        builder.Property(log => log.EventType)
            .IsRequired();

        builder.Property(log => log.OccurredAtUtc)
            .IsRequired();

        builder.Property(log => log.Result)
            .IsRequired();

        builder.Property(log => log.IpAddress)
            .HasMaxLength(45);

        builder.Property(log => log.UserAgent)
            .HasMaxLength(500);

        builder.Property(log => log.MetadataJson)
            .HasColumnType("nvarchar(max)");

        builder.HasOne(log => log.User)
            .WithMany()
            .HasForeignKey(log => log.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // Drives the per-user activity feed (F9 GetSecurityActivity).
        builder.HasIndex(log => new { log.UserId, log.OccurredAtUtc })
            .HasDatabaseName("IX_SecurityEventLogs_UserId_OccurredAtUtc")
            .IsDescending(false, true);
    }
}
