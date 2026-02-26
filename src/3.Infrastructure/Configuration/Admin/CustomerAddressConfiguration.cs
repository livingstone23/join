// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="CustomerAddress"/> entity.
/// Defines table structure, constraints, and relationships for customer addresses,
/// including the geographical hierarchy.
/// </summary>
public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    /// <summary>
    /// Configures the <see cref="CustomerAddress"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        // Map to table "CustomerAddresses" in schema "Admin"
        builder.ToTable("CustomerAddresses", "Admin");
        
        // Set the primary key
        builder.HasKey(ca => ca.Id);

        // --- Properties ---

        builder.Property(ca => ca.AddressLine1).IsRequired().HasMaxLength(200);
        builder.Property(ca => ca.AddressLine2).HasMaxLength(200);
        builder.Property(ca => ca.ZipCode).IsRequired().HasMaxLength(20);
        builder.Property(ca => ca.IsDefault).IsRequired();

        // --- Relationships ---
        
        // Required relationship with Customer
        builder.HasOne(ca => ca.Customer)
            .WithMany(c => c.Addresses)
            .HasForeignKey(ca => ca.CustomerId)
            .OnDelete(DeleteBehavior.Cascade); // Addresses are deleted if the Customer is deleted.

        // Required relationship with Country
        builder.HasOne(ca => ca.Country)
            .WithMany(c => c.CustomerAddresses)
            .HasForeignKey(ca => ca.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Required relationship with Province
        builder.HasOne(ca => ca.Province)
            .WithMany(p => p.CustomerAddresses)
            .HasForeignKey(ca => ca.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Required relationship with Municipality
        builder.HasOne(ca => ca.Municipality)
            .WithMany(m => m.CustomerAddresses)
            .HasForeignKey(ca => ca.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Required relationship with StreetType
        builder.HasOne(ca => ca.StreetType)
            .WithMany(st => st.CustomerAddresses)
            .HasForeignKey(ca => ca.StreetTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---
        
        // Apply a soft-delete filter.
        builder.HasQueryFilter(ca => ca.GcRecord == 0);
    }
}