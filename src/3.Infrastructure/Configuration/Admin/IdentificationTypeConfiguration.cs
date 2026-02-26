// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="IdentificationType"/> entity.
/// Defines the table for the catalog of identification document types.
/// </summary>
public class IdentificationTypeConfiguration : IEntityTypeConfiguration<IdentificationType>
{
    /// <summary>
    /// Configures the <see cref="IdentificationType"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<IdentificationType> builder)
    {
        // Map to table "IdentificationTypes" in schema "Admin"
        builder.ToTable("IdentificationTypes", "Admin");

        // Set the primary key
        builder.HasKey(it => it.Id);

        // --- Properties ---

        builder.Property(it => it.Name).IsRequired().HasMaxLength(50);
        builder.Property(it => it.Description).HasMaxLength(200);
        builder.Property(it => it.ValidationPattern).HasMaxLength(200);

        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(it => it.GcRecord == 0);
    }
}
