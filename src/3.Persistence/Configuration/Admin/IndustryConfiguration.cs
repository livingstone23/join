// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;




/// <summary>
/// Configures the database mapping for the <see cref="Industry"/> entity.
/// Enforces multi-tenant isolation, unique constraints, and the soft-delete pattern.
/// </summary>
public class IndustryConfiguration : IEntityTypeConfiguration<Industry>
{
    /// <summary>
    /// Configures the <see cref="Industry"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Industry> builder)
    {
        // --- Table & Schema ---
        // Map to table "Industries" in schema "Admin" (or "Catalogs")
        builder.ToTable("Industries", "Admin");

        // --- Primary Key ---
        builder.HasKey(i => i.Id);

        // --- Properties ---

        // Industry codes (e.g., "TECH", "FIN-01") are usually short and required.
        builder.Property(i => i.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(i => i.Name)
            .IsRequired()
            .HasMaxLength(150);

        // Optional description
        builder.Property(i => i.Description)
            .HasMaxLength(500)
            .IsRequired(false);

        // Business state flag
        builder.Property(i => i.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // --- Indexes & Unique Constraints ---

        // CRITICAL: Multi-tenant unique constraints.
        // A tenant cannot have two industries with the exact same Code.
        // Including GcRecord allows deleted codes (> 0) to exist alongside an active one (0).
        builder.HasIndex(i => new { i.CompanyId, i.Code, i.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Industries_CompanyId_Code_GcRecord");

        // A tenant cannot have two industries with the exact same Name.
        builder.HasIndex(i => new { i.CompanyId, i.Name, i.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_Industries_CompanyId_Name_GcRecord");

        // --- Relationships ---

        // Required relationship with Company (Tenant)
        builder.HasOne(i => i.Company)
            .WithMany()
            .HasForeignKey(i => i.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---

        // Apply the soft-delete filter according to the strict rule: 
        // 0 means active, anything else is logically deleted.
        builder.HasQueryFilter(i => i.GcRecord == 0);
    }
}
