// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Persistence.Configuration.Security;



/// <summary>
/// Configures the database mapping for the <see cref="RoleSystemOption"/> entity.
/// This join table defines the specific permissions a role has for a given system option (screen).
/// </summary>
public class RoleSystemOptionConfiguration : IEntityTypeConfiguration<RoleSystemOption>
{
    /// <summary>
    /// Configures the <see cref="RoleSystemOption"/> entity.
    /// </summary>
    /// <param name="builder">The builder to be used for configuring the entity.</param>
    public void Configure(EntityTypeBuilder<RoleSystemOption> builder)
    {
        // Map to table "RoleSystemOptions" in schema "Admin"
        builder.ToTable("RoleSystemOptions", "Security");

        // --- Primary Key ---
        // Define a composite primary key. This ensures a role can only have one
        // permission entry per system option.
        builder.HasKey(rso => new { rso.RoleId, rso.SystemOptionId });

        // --- Properties ---
        // Set default values for permissions to false. Access must be explicitly granted.
        builder.Property(rso => rso.CanRead).HasDefaultValue(false);
        builder.Property(rso => rso.CanCreate).HasDefaultValue(false);
        builder.Property(rso => rso.CanUpdate).HasDefaultValue(false);
        builder.Property(rso => rso.CanDelete).HasDefaultValue(false);

        // --- Relationships ---

        // Relationship with ApplicationRole
        // Each permission entry belongs to one role.
        builder.HasOne(rso => rso.Role)
            .WithMany() // A role can have many permission entries.
            .HasForeignKey(rso => rso.RoleId)
            .OnDelete(DeleteBehavior.Cascade); // If a role is deleted, its permissions are also deleted.

        // Relationship with SystemOption
        // Each permission entry applies to one system option.
        builder.HasOne(rso => rso.SystemOption)
            .WithMany(so => so.RoleOptions) // A system option can have permissions for many roles.
            .HasForeignKey(rso => rso.SystemOptionId)
            .OnDelete(DeleteBehavior.Cascade); // If a system option is deleted, its permissions are deleted.

        // --- Query Filters ---

        // Apply a soft-delete filter.
        builder.HasQueryFilter(rso => rso.GcRecord == 0);


        builder.HasOne(x => x.Company)
            .WithMany(c => c.RoleSystemOptions)
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
            
    }
}
