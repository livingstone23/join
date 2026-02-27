using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Security;



/// <summary>
/// Fluent API configuration for assigning roles per tenant.
/// Guarantees that Authorization logic (RBAC) is strictly bound to a specific Company context.
/// </summary>
public class UserRoleCompanyConfiguration : IEntityTypeConfiguration<UserRoleCompany>
{
    /// <summary>
    /// Applies the configuration rules using the provided builder.
    /// </summary>
    public void Configure(EntityTypeBuilder<UserRoleCompany> builder)
    {
        // 1. Table & Schema Mapping
        // Placed in the "Security" schema for consistency.
        builder.ToTable("UserRoleCompanies", "Security");

        // 2. Primary Key
        builder.HasKey(urc => urc.Id);

        // 3. Business Rules & Indexes
        // Composite Unique Index: A user cannot have the EXACT same role twice within the SAME company.
        // However, they can have 'Admin' in Company A and 'Admin' in Company B.
        builder.HasIndex(urc => new { urc.UserId, urc.RoleId, urc.CompanyId })
            .IsUnique();

        // 4. Relationships (Foreign Keys & Delete Behaviors)
        
        // Relationship with ApplicationRole
        builder.HasOne(urc => urc.Role)
            .WithMany(r => r.UserRoleCompanies)
            .HasForeignKey(urc => urc.RoleId)
            .OnDelete(DeleteBehavior.Restrict); // Do not delete a role if it is currently assigned.

        // Relationship with Company (Tenant)
        builder.HasOne(urc => urc.Company)
            .WithMany(c => c.UserRoleCompanies) // Assuming you added this collection to Company
            .HasForeignKey(urc => urc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict); // Do not delete a company if roles are actively assigned.

        // Relationship with ApplicationUser
        builder.HasOne(urc => urc.User)
            .WithMany(u => u.UserRoleCompanies)
            .HasForeignKey(urc => urc.UserId)
            .OnDelete(DeleteBehavior.Cascade); // If the user is hard-deleted, remove all their tenant-role assignments.

        // 5. Global Query Filters
        // Automatically excludes soft-deleted role assignments from queries.
        builder.HasQueryFilter(urc => urc.GcRecord == 0);
    }
}
