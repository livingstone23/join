// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.



using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Security;



/// <summary>
/// Fluent API configuration for the User-Company intersection.
/// Secures the multi-tenant architecture by defining how users are linked to tenants (Companies).
/// </summary>
public class UserCompanyConfiguration : IEntityTypeConfiguration<UserCompany>
{
    /// <summary>
    /// Applies the configuration rules using the provided builder.
    /// </summary>
    public void Configure(EntityTypeBuilder<UserCompany> builder)
    {
        // 1. Table & Schema Mapping
        // Added to the "Security" schema to maintain consistency with Identity tables.
        builder.ToTable("UserCompanies", "Security");
        
        // 2. Primary Key
        builder.HasKey(uc => uc.Id);

        // 3. Business Rules & Indexes
        // Composite Unique Index: Prevents granting a user access to the same company more than once.
        builder.HasIndex(uc => new { uc.UserId, uc.CompanyId })
            .IsUnique();

        // Filtered Unique Index: A user can only have one ACTIVE default company.
        // SQL Server syntax:
        builder.HasIndex(uc => uc.UserId)
            .HasDatabaseName("UX_UserCompanies_UserId_Default")
            .IsUnique()
            .HasFilter("[IsDefault] = 1 AND [GcRecord] = 0");

        // PostgreSQL note:
        // Use the following filter instead when generating provider-specific migrations:
        // .HasFilter("\"IsDefault\" = TRUE AND \"GcRecord\" = 0");

        // 4. Relationships (Foreign Keys & Delete Behaviors)
        
        // Relationship with User
        builder.HasOne(uc => uc.User)
            .WithMany(u => u.UserCompanies)
            .HasForeignKey(uc => uc.UserId)
            .HasPrincipalKey(u => u.Id) // <-- Fuerza el enlace explícito a la PK del Usuario
            .OnDelete(DeleteBehavior.Cascade);

        // Relationship with Company (Tenant)
        builder.HasOne(uc => uc.Company)
            .WithMany(c => c.UserCompanies)
            .HasForeignKey(uc => uc.CompanyId)
            .HasPrincipalKey(c => c.Id) // <-- LA SOLUCIÓN AL WARNING: Fuerza el enlace a la PK de la Empresa
            .OnDelete(DeleteBehavior.Restrict);

        // 5. Global Query Filters
        // Automatically excludes soft-deleted access records from queries.
        builder.HasQueryFilter(uc => uc.GcRecord == 0);
    }
}