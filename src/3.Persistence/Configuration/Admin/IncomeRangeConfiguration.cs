// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Admin;



/// <summary>
/// Configures the database mapping for the <see cref="IncomeRange"/> entity.
/// Defines constraints, precision for financial data, and multi-tenant unique indexes.
/// </summary>
public class IncomeRangeConfiguration : IEntityTypeConfiguration<IncomeRange>
{
    /// <summary>
    /// Configures the <see cref="IncomeRange"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<IncomeRange> builder)
    {
        // --- Table & Schema ---
        // Map to table "IncomeRanges" in schema "Admin"
        builder.ToTable("IncomeRanges", "Admin");

        // --- Primary Key ---
        builder.HasKey(ir => ir.Id);

        // --- Properties ---

        // Display name for the dropdowns (e.g., "$1,000 - $5,000 USD")
        builder.Property(ir => ir.DisplayName)
            .IsRequired()
            .HasMaxLength(100);

        // Precision configuration for decimal properties. 
        // 18 digits in total, 2 decimal places (Standard for financial data).
        builder.Property(ir => ir.MinimumValue)
            .IsRequired()
            .HasPrecision(18, 2);

        // MaximumValue is nullable (for ranges like "Above $10,000")
        builder.Property(ir => ir.MaximumValue)
            .HasPrecision(18, 2)
            .IsRequired(false);

        // ISO 4217 Currency Code (e.g., "USD", "PEN", "EUR")
        // Fixed or strict length to optimize database storage and queries.
        builder.Property(ir => ir.CurrencyCode)
            .IsRequired()
            .HasMaxLength(3) 
            .IsFixedLength(false); // Can set to true if you strictly enforce 3 chars always

        // Business state flag
        builder.Property(ir => ir.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // --- Indexes & Unique Constraints ---

        // CRITICAL: Multi-tenant unique constraints.
        // Ensures that a single Tenant (Company) cannot create duplicate DisplayNames.
        // The GcRecord ensures soft-deleted records do not block new creations.
        builder.HasIndex(ir => new { ir.CompanyId, ir.DisplayName, ir.GcRecord })
            .IsUnique()
            .HasDatabaseName("IX_IncomeRanges_CompanyId_DisplayName_GcRecord");

        // --- Relationships ---

        // Required relationship with Company (Tenant)
        builder.HasOne(ir => ir.Company)
            .WithMany()
            .HasForeignKey(ir => ir.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // --- Query Filters ---

        // Apply a soft-delete filter automatically for all Entity Framework queries.
        // (Assuming GcRecord exists in your class as previously discussed).
        builder.HasQueryFilter(cm => cm.GcRecord == 0);
    }
}
