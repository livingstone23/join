// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Common;



/// <summary>
/// Configures the database mapping for the <see cref="Region"/> entity.
/// Defines the table for the geographical catalog of regions.
/// </summary>
public class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    /// <summary>
    /// Configures the <see cref="Region"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        // Map to table "Regions" in schema "Common"
        builder.ToTable("Regions", "Common");
        
        // Set the primary key
        builder.HasKey(r => r.Id);

        // --- Properties ---

        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Code).HasMaxLength(20);

        // --- Relationships ---

        // Required relationship with Country
        builder.HasOne(r => r.Country)
            .WithMany(c => c.Regions)
            .HasForeignKey(r => r.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        // One-to-many relationship with Province
        builder.HasMany(r => r.Provinces)
            .WithOne(p => p.Region)
            .HasForeignKey(p => p.RegionId);

        // --- Indexes ---

        builder.HasIndex(r => new { r.CountryId, r.Code }).IsUnique();

        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(r => r.GcRecord == 0);
    }
}
