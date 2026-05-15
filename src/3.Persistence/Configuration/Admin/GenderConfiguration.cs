// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="Gender"/> entity.
/// Defines constraints, indexes, and behaviors to ensure data integrity
/// within a multi-tenant catalog environment.
/// </summary>
public class GenderConfiguration : IEntityTypeConfiguration<Gender>
{


    /// <summary>
    /// Configures the <see cref="Gender"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Gender> builder)
    {
        // --- Table & Schema ---
        // Map to table "Genders" in schema "Admin" to keep catalogs organized
        builder.ToTable("Genders", "Admin");

        // --- Primary Key ---
        // Inherited from BaseAuditableEntity, explicitly mapped for clarity
        builder.HasKey(g => g.Id);

        // --- Properties ---
        
        // The code should be short, strictly required, and optimized for searching
        builder.Property(g => g.Code)
            .IsRequired()
            .HasMaxLength(20); // e.g., "M", "F", "NB"

        // The display name for the UI
        builder.Property(g => g.Name)
            .IsRequired()
            .HasMaxLength(100);

        // Business state flag
        builder.Property(g => g.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // --- Indexes & Unique Constraints ---
        
        // CRITICAL: Multi-tenant unique constraints.
        // Ensures that a specific Company cannot have duplicate Codes or Names.
        // Including GcRecord ensures that logically deleted records do not block the creation of new ones.
        builder.HasIndex(g => new { g.CompanyId, g.Code, g.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Genders_CompanyId_Code_GcRecord");

        builder.HasIndex(g => new { g.CompanyId, g.Name, g.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Genders_CompanyId_Name_GcRecord");

        // --- Relationships ---

        // Required relationship with Company (Tenant)
        // Inherited from BaseTenantEntity
        builder.HasOne(g => g.Company)
            .WithMany() // A company can have multiple genders
            .HasForeignKey(g => g.CompanyId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a company if it has genders mapped.

        // --- Query Filters ---

        // Apply a soft-delete filter.
        // Note: If you have a global multi-tenant interceptor, you only need the GcRecord filter here.
        // Assumes GcRecord is int? and null means "active/not deleted" based on previous Domain definitions.
        builder.HasQueryFilter(cm => cm.GcRecord == 0);
    }

    
}
