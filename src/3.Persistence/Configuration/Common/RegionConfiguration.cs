// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Common;



/// <summary>
/// Configures the database mapping for the <see cref="Region"/> entity.
/// Enforces multi-tenant isolation, geographical hierarchy integrity, and soft-delete filters.
/// </summary>
public class RegionConfiguration : IEntityTypeConfiguration<Region>
{
    /// <summary>
    /// Configures the <see cref="Region"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Region> builder)
    {
        // --- Table & Schema ---
        // Map to table "Regions" in schema "Admin"
        builder.ToTable("Regions", "Admin");

        // --- Primary Key ---
        // Inherited from BaseAuditableEntity
        builder.HasKey(r => r.Id);

        // --- Properties ---

        // Region names are usually not extremely long. 150 is a safe limit.
        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(150);

        // ISO codes or internal codes (e.g., "MAD" for Madrid, "CA-ON" for Ontario)
        builder.Property(r => r.Code)
            .HasMaxLength(50)
            .IsRequired(false);

        // Soft delete flag mapping (Inherited from BaseAuditableEntity)
        builder.Property(r => r.GcRecord)
            .IsRequired()
            .HasDefaultValue(0); // 0 indicates an active record

        // --- Indexes & Unique Constraints ---

        // CRITICAL: Integrity Constraint.
        // A specific Tenant cannot have two active Regions with the exact same Name within the same Country.
        // E.g., You can't have two "Madrid"s in "Spain" for the same company.
        builder.HasIndex(r => new { r.CompanyId, r.CountryId, r.Name, r.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Regions_Company_Country_Name_GcRecord");

        // Similar constraint for the Code (if they use one, it shouldn't be duplicated in the same country)
        builder.HasIndex(r => new { r.CompanyId, r.CountryId, r.Code, r.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Regions_Company_Country_Code_GcRecord");

        // --- Relationships ---

        // 1. Required relationship with Company (Tenant)
        // Ensures full data isolation per the Hybrid Multi-Tenancy architecture.
        builder.HasOne(r => r.Company)
            .WithMany()
            .HasForeignKey(r => r.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // 2. Required relationship with parent Country
        // Assuming your Country entity has a collection: public virtual ICollection<Region> Regions { get; set; }
        builder.HasOne(r => r.Country)
            .WithMany(c => c.Regions) // Inverse navigation property
            .HasForeignKey(r => r.CountryId)
            // Restrict prevents accidentally deleting a Country that has Regions linked to it.
            // Enforces soft-delete from the Application layer.
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---

        // Apply the universal soft-delete filter: 0 means active.
        builder.HasQueryFilter(r => r.GcRecord == 0);
    }
}
