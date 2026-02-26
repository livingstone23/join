


// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Infrastructure.Configuration.Common;

/// <summary>
/// Configures the database mapping for the <see cref="Company"/> entity.
/// Defines table structure, constraints, and relationships for the Tenant entity.
/// </summary>
public class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    /// <summary>
    /// Configures the <see cref="Company"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        // Map to table "Companies" in schema "Common"
        builder.ToTable("Companies", "Common");
        
        // Set the primary key
        builder.HasKey(c => c.Id);

        // --- Properties ---
        
        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.TaxId).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.Email).HasMaxLength(100);
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.WebSite).HasMaxLength(200);
        builder.Property(c => c.IsActive).IsRequired();

        // --- Relationships ---

        builder.HasMany(c => c.Customers)
            .WithOne(cu => cu.Company)
            .HasForeignKey(cu => cu.CompanyId);
            
        builder.HasMany(c => c.Areas)
            .WithOne(a => a.Company)
            .HasForeignKey(a => a.CompanyId);
            
        builder.HasMany(c => c.Projects)
            .WithOne(p => p.Company)
            .HasForeignKey(p => p.CompanyId);

        builder.HasMany(c => c.Tickets)
            .WithOne(t => t.Company)
            .HasForeignKey(t => t.CompanyId);
            
        // --- Indexes ---
        
        // The TaxId (RUC/CIF) is critical for billing and must be unique.
        builder.HasIndex(c => c.TaxId).IsUnique();

        // --- Query Filters ---
        
        // Apply a soft-delete filter.
        builder.HasQueryFilter(c => c.GcRecord == 0);
    }
}