// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Common;

/// <summary>
/// Configures the database mapping for the <see cref="Province"/> entity.
/// Defines the table for the geographical catalog of provinces/states.
/// </summary>
public class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    /// <summary>
    /// Configures the <see cref="Province"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Province> builder)
    {
        // Map to table "Provinces" in schema "Common"
        builder.ToTable("Provinces", "Common");

        // Set the primary key
        builder.HasKey(p => p.Id);

        // --- Properties ---

        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Code).IsRequired().HasMaxLength(20);

        // --- Relationships ---

        // Required relationship with Country
        builder.HasOne(p => p.Country)
            .WithMany(c => c.Provinces)
            .HasForeignKey(p => p.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional relationship with Region
        builder.HasOne(p => p.Region)
            .WithMany(r => r.Provinces)
            .HasForeignKey(p => p.RegionId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // One-to-many relationship with Municipality
        builder.HasMany(p => p.Municipalities)
            .WithOne(m => m.Province)
            .HasForeignKey(m => m.ProvinceId);

        // One-to-many relationship with Municipality
        builder.HasMany(p => p.CustomerAddresses)
            .WithOne(m => m.Province)
            .HasForeignKey(m => m.ProvinceId);    
            
        // --- Indexes ---

        builder.HasIndex(p => new { p.CountryId, p.Code }).IsUnique();
        
        // --- Query Filters ---
        
        // Apply a soft-delete filter.
        builder.HasQueryFilter(p => p.GcRecord == 0);
    }
}
