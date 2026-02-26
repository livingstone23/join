// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Common;



/// <summary>
/// Configures the database mapping for the <see cref="StreetType"/> entity.
/// Defines the table for the catalog of street types (e.g., Avenue, Street).
/// </summary>
public class StreetTypeConfiguration : IEntityTypeConfiguration<StreetType>
{
    /// <summary>
    /// Configures the <see cref="StreetType"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<StreetType> builder)
    {
        // Map to table "StreetTypes" in schema "Common"
        builder.ToTable("StreetTypes", "Common");
        
        // Set the primary key
        builder.HasKey(st => st.Id);

        // --- Properties ---
        
        builder.Property(st => st.Name).IsRequired().HasMaxLength(50);
        builder.Property(st => st.Abbreviation).IsRequired().HasMaxLength(10);

        // --- Relationships ---

        // One-to-many relationship with CustomerAddress
        builder.HasMany(st => st.CustomerAddresses)
            .WithOne(ca => ca.StreetType)
            .HasForeignKey(ca => ca.StreetTypeId);
            
        // --- Indexes ---

        builder.HasIndex(st => st.Name).IsUnique();
        builder.HasIndex(st => st.Abbreviation).IsUnique();

        // --- Query Filters ---
        
        // Apply a soft-delete filter.
        builder.HasQueryFilter(st => st.GcRecord == 0);
    }
}
