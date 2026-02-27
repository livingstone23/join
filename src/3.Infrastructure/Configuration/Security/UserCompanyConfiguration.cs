


using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Security;



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

        // 4. Relationships (Foreign Keys & Delete Behaviors)
        
        // Relationship with User
        builder.HasOne(uc => uc.User)
            .WithMany(u => u.UserCompanies)
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Cascade); // If the user is hard-deleted, remove their company access.

        // Relationship with Company (Tenant)
        builder.HasOne(uc => uc.Company)
            .WithMany(c => c.UserCompanies) // Assuming you added this collection to Company
            .HasForeignKey(uc => uc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deleting a company if users are still attached.

        // 5. Global Query Filters
        // Automatically excludes soft-deleted access records from queries.
        builder.HasQueryFilter(uc => uc.GcRecord == 0);
    }
}