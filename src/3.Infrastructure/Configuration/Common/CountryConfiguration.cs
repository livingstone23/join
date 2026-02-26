// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Common;

/// <summary>
/// Configures the database mapping for the <see cref="Country"/> entity.
/// Defines the table for the geographical catalog of countries.
/// </summary>
public class CountryConfiguration : IEntityTypeConfiguration<Country>
{
    /// <summary>
    /// Configures the <see cref="Country"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Country> builder)
    {
        // Map to table "Countries" in schema "Common"
        builder.ToTable("Countries", "Common");
        
        // Set the primary key
        builder.HasKey(c => c.Id);

        // --- Properties ---
        
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.IsoCode).IsRequired().HasMaxLength(10);

        // --- Relationships ---

        // One-to-many relationship with Province
        builder.HasMany(c => c.Provinces)
            .WithOne(p => p.Country)
            .HasForeignKey(p => p.CountryId);

        // One-to-many relationship with Region
        builder.HasMany(c => c.Regions)
            .WithOne(r => r.Country)
            .HasForeignKey(r => r.CountryId);

        // One-to-many relationship with Province
        builder.HasMany(c => c.CustomerAddresses)
            .WithOne(p => p.Country)
            .HasForeignKey(p => p.CountryId);

        // --- Indexes ---

        builder.HasIndex(c => c.IsoCode).IsUnique();

        // --- Query Filters ---
        
        // Apply a soft-delete filter.
        builder.HasQueryFilter(c => c.GcRecord == 0);
    }
}
