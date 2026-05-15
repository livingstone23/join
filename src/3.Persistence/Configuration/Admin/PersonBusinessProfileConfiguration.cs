// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="PersonBusinessProfile"/> entity.
/// Defines constraints, foreign keys to catalogs, and soft-delete filters for B2B profiles.
/// </summary>
public class PersonBusinessProfileConfiguration : IEntityTypeConfiguration<PersonBusinessProfile>
{
    /// <summary>
    /// Configures the <see cref="PersonBusinessProfile"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<PersonBusinessProfile> builder)
    {
        // --- Table & Schema ---
        // Map to table "PersonBusinessProfiles" in schema "Admin"
        builder.ToTable("PersonBusinessProfiles", "Admin");

        // --- Primary Key ---
        // Inherited from BaseAuditableEntity
        builder.HasKey(pbp => pbp.Id);

        // --- Properties ---

        // Websites can be reasonably long, 255 characters is a safe standard
        builder.Property(pbp => pbp.Website)
            .HasMaxLength(255)
            .IsRequired(false);

        // Foundation Date usually only requires the Date part, not the exact time
        builder.Property(pbp => pbp.FoundationDate)
            .HasColumnType("date") 
            .IsRequired(false);

        // State flags
        builder.Property(pbp => pbp.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Soft delete flag (0 means active)
        builder.Property(pbp => pbp.GcRecord)
            .IsRequired()
            .HasDefaultValue(0);

        // --- Indexes ---

        // Index to significantly speed up Dapper queries when looking for a person's business profile.
        builder.HasIndex(pbp => new { pbp.CompanyId, pbp.PersonId, pbp.GcRecord })
            .HasDatabaseName("IX_PersonBusinessProfiles_Company_Person_GcRecord");

        // --- Relationships ---

        // 1. Relationship with Company (Tenant)
        builder.HasOne(pbp => pbp.Company)
            .WithMany()
            .HasForeignKey(pbp => pbp.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // 2. Relationship with the Core Person
        builder.HasOne(pbp => pbp.Person)
            .WithMany(p => p.BusinessProfiles) // Matches the collection in the Person entity
            .HasForeignKey(pbp => pbp.PersonId)
            .OnDelete(DeleteBehavior.Restrict); // Enforces Application Layer soft-delete

        // 3. Relationship with the Industry Catalog
        builder.HasOne(pbp => pbp.Industry)
            .WithMany()
            .HasForeignKey(pbp => pbp.IndustryId)
            .OnDelete(DeleteBehavior.Restrict);

        // 4. Relationship with the Tax Regime Catalog
        builder.HasOne(pbp => pbp.TaxRegime)
            .WithMany()
            .HasForeignKey(pbp => pbp.TaxRegimeId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---

        // Apply the universal soft-delete filter: 0 means active.
        builder.HasQueryFilter(pbp => pbp.GcRecord == 0);
    }
}
