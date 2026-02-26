// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Common;

/// <summary>
/// Configures the database mapping for the <see cref="Municipality"/> entity.
/// Defines the table for the geographical catalog of municipalities/cities.
/// </summary>
public class MunicipalityConfiguration : IEntityTypeConfiguration<Municipality>
{
    /// <summary>
    /// Configures the <see cref="Municipality"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Municipality> builder)
    {
        // Map to table "Municipalities" in schema "Common"
        builder.ToTable("Municipalities", "Common");
        
        // Set the primary key
        builder.HasKey(m => m.Id);

        // --- Properties ---
        
        builder.Property(m => m.Name).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Code).HasMaxLength(20);

        // --- Relationships ---

        // Required relationship with Province
        builder.HasOne(m => m.Province)
            .WithMany(p => p.Municipalities)
            .HasForeignKey(m => m.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        // One-to-many relationship with CustomerAddress
        builder.HasMany(m => m.CustomerAddresses)
            .WithOne(ca => ca.Municipality)
            .HasForeignKey(ca => ca.MunicipalityId);

        // --- Indexes ---

        builder.HasIndex(m => new { m.ProvinceId, m.Name }).IsUnique();
        
        // --- Query Filters ---
        
        // Apply a soft-delete filter.
        builder.HasQueryFilter(m => m.GcRecord == 0);
    }
}
