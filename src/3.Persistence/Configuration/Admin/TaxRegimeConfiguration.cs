// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="TaxRegime"/> entity.
/// Enforces multi-tenant isolation and unique constraints for tax codes.
/// </summary>
public class TaxRegimeConfiguration : IEntityTypeConfiguration<TaxRegime>
{


    /// <summary>
    /// Configures the <see cref="TaxRegime"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<TaxRegime> builder)
    {
        // --- Table & Schema ---
        // Map to table "TaxRegimes" in schema "Admin" (or "Catalogs" if you prefer to separate them)
        builder.ToTable("TaxRegimes", "Admin");

        // --- Primary Key ---
        builder.HasKey(tr => tr.Id);

        // --- Properties ---

        // Tax codes vary by country, but usually don't exceed 50 characters.
        builder.Property(tr => tr.Code)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(tr => tr.Name)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(tr => tr.Description)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(tr => tr.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // --- Indexes & Unique Constraints ---

        // CRITICAL: A tenant cannot have two tax regimes with the exact same Code.
        // The GcRecord ensures that if they delete "REG-01", they can recreate it later.
        builder.HasIndex(tr => new { tr.CompanyId, tr.Code, tr.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_TaxRegimes_CompanyId_Code_GcRecord");

        // A tenant cannot have two tax regimes with the exact same Name either.
        builder.HasIndex(tr => new { tr.CompanyId, tr.Name, tr.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_TaxRegimes_CompanyId_Name_GcRecord");

        // --- Relationships ---

        // Required relationship with Company (Tenant)
        builder.HasOne(tr => tr.Company)
            .WithMany()
            .HasForeignKey(tr => tr.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---

        // Automatically hide logically deleted records in EF Core queries.
        builder.HasQueryFilter(cm => cm.GcRecord == 0);
        
    }
}
