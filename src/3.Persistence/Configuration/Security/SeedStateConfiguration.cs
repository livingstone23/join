// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// Configures the database mapping for the <see cref="SeedState"/> entity.
/// One row per company, holding the last applied menu/permissions seed checksum.
/// </summary>
public class SeedStateConfiguration : IEntityTypeConfiguration<SeedState>
{
    /// <summary>
    /// Configures the <see cref="SeedState"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<SeedState> builder)
    {
        // Map to table "SeedState" in schema "Security"
        builder.ToTable("SeedState", "Security");

        // Primary key
        builder.HasKey(s => s.Id);

        // SHA-256 hex string fits in 64 chars
        builder.Property(s => s.Checksum)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(s => s.LastAppliedAt)
            .IsRequired();

        // One row per company
        builder.HasIndex(s => s.CompanyId)
            .IsUnique();
    }
}
