// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="PersonAddress"/> entity.
/// Defines table structure, constraints, multi-tenant isolation, and relationships for customer addresses,
/// including the geographical hierarchy.
/// </summary>
public class PersonAddressConfiguration : IEntityTypeConfiguration<PersonAddress>
{
    /// <summary>
    /// Configures the <see cref="PersonAddress"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<PersonAddress> builder)
    {
        // --- Table & Schema ---
        // Map to table "PersonAddresses" in schema "Admin"
        builder.ToTable("PersonAddresses", "Admin");
        
        // Set the primary key
        builder.HasKey(ca => ca.Id);

        // --- Properties ---

        builder.Property(ca => ca.AddressLine1)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(ca => ca.AddressLine2)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(ca => ca.ZipCode)
            .IsRequired()
            .HasMaxLength(20);

        // State flags
        builder.Property(ca => ca.IsDefault)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(ca => ca.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Soft delete flag mapping (0 means active)
        builder.Property(ca => ca.GcRecord)
            .IsRequired()
            .HasDefaultValue(0);

        // --- Indexes ---

        // Performance Index: Crucial for CQRS/Dapper reads.
        // Quickly locates the active default address for a specific person within a tenant.
        builder.HasIndex(ca => new { ca.CompanyId, ca.PersonId, ca.IsDefault })
            .HasDatabaseName("IX_PersonAddresses_Company_Person_Default");

        // --- Relationships ---

        // 1. Required relationship with Company (Tenant)
        builder.HasOne(ca => ca.Company)
            .WithMany()
            .HasForeignKey(ca => ca.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // 2. Required relationship with Person
        builder.HasOne(ca => ca.Person)
            .WithMany(c => c.Addresses)
            .HasForeignKey(ca => ca.PersonId)
            // Enforces Application Layer soft-delete instead of DB physical cascade
            .OnDelete(DeleteBehavior.Restrict); 

        // --- Geographical Hierarchy Relationships ---

        // Required relationship with Country
        builder.HasOne(ca => ca.Country)
            .WithMany(c => c.PersonAddresses)
            .HasForeignKey(ca => ca.CountryId)
            .OnDelete(DeleteBehavior.Restrict);

        // OPTIONAL relationship with Region (As defined in the Domain with Guid?)
        builder.HasOne(ca => ca.Region)
            .WithMany(r => r.PersonAddresses)
            .HasForeignKey(ca => ca.RegionId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false); // Indicates the Foreign Key is nullable

        // Required relationship with Province
        builder.HasOne(ca => ca.Province)
            .WithMany(p => p.PersonAddresses)
            .HasForeignKey(ca => ca.ProvinceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Required relationship with Municipality
        builder.HasOne(ca => ca.Municipality)
            .WithMany(m => m.PersonAddresses)
            .HasForeignKey(ca => ca.MunicipalityId)
            .OnDelete(DeleteBehavior.Restrict);

        // Required relationship with StreetType
        builder.HasOne(ca => ca.StreetType)
            .WithMany(st => st.PersonAddresses)
            .HasForeignKey(ca => ca.StreetTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---
        
        // Apply a soft-delete filter. 0 indicates the record is active.
        builder.HasQueryFilter(ca => ca.GcRecord == 0);
    }
}