// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>
/// Configures the database mapping for the <see cref="CompanyModule"/> entity.
/// This defines the relationship between a Company (Tenant) and a SystemModule,
/// effectively enabling or disabling a module for that company.
/// </summary>
public class CompanyModuleConfiguration : IEntityTypeConfiguration<CompanyModule>
{
    /// <summary>
    /// Configures the <see cref="CompanyModule"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<CompanyModule> builder)
    {
        // Map to table "CompanyModules" in schema "Admin"
        builder.ToTable("CompanyModules", "Admin");

        // --- Primary Key ---
        // Define a composite primary key based on CompanyId and ModuleId.
        // This ensures that a company can only have one entry per module.
        builder.HasKey(cm => new { cm.CompanyId, cm.ModuleId });

        // --- Properties ---

        // Configure the IsActive property with a default value.
        // This indicates if the module is enabled for the company.
        builder.Property(cm => cm.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // --- Relationships ---

        // Required relationship with Company (Tenant)
        // A CompanyModule belongs to one Company.
        builder.HasOne(cm => cm.Company)
            .WithMany() // A company can have multiple module configurations.
            .HasForeignKey(cm => cm.CompanyId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a company if it has module links.

        // Required relationship with SystemModule
        // A CompanyModule links to one SystemModule.
        builder.HasOne(cm => cm.Module)
            .WithMany() // A module can be linked to many companies.
            .HasForeignKey(cm => cm.ModuleId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a module if it's linked to companies.

        // --- Query Filters ---

        // Apply a soft-delete filter.
        // This ensures that queries for CompanyModules automatically exclude records marked as deleted.
        builder.HasQueryFilter(cm => cm.GcRecord == 0);
    }
}
