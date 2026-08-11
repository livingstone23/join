// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

/// <summary>
/// Fluent API configuration for the RoleCompany junction.
/// Maps which Roles are available within a specific Company (tenant).
/// </summary>
public class RoleCompanyConfiguration : IEntityTypeConfiguration<RoleCompany>
{
    /// <summary>
    /// Applies the configuration rules using the provided builder.
    /// </summary>
    public void Configure(EntityTypeBuilder<RoleCompany> builder)
    {
        // 1. Table & Schema Mapping
        builder.ToTable("RoleCompanies", "Security");

        // 2. Primary Key
        builder.HasKey(rc => rc.Id);

        // 3. Business Rules & Indexes
        // Filtered unique index: prevents two active links for the same (Role, Company).
        // Filtered to GcRecord = 0 so soft-deleted rows do not block resurrection.
        builder.HasIndex(rc => new { rc.RoleId, rc.CompanyId })
            .IsUnique()
            .HasFilter("[GcRecord] = 0");

        // 4. Relationships (Foreign Keys & Delete Behaviors)

        // Relationship with ApplicationRole
        builder.HasOne(rc => rc.Role)
            .WithMany(r => r.RoleCompanies)
            .HasForeignKey(rc => rc.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationship with Company (Tenant)
        builder.HasOne(rc => rc.Company)
            .WithMany()
            .HasForeignKey(rc => rc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // 5. Global Query Filters
        builder.HasQueryFilter(rc => rc.GcRecord == 0);
    }
}
