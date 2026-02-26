using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Security;



/// <summary>
/// Fluent API configuration for assigning roles per tenant.
/// Guarantees that Authorization logic is strictly bound to the specific Company context.
/// </summary>
public class UserRoleCompanyConfiguration : IEntityTypeConfiguration<UserRoleCompany>
{
    public void Configure(EntityTypeBuilder<UserRoleCompany> builder)
    {
        builder.ToTable("UserRoleCompanies");
        builder.HasKey(urc => urc.Id);

        // REGLA DE NEGOCIO: Un usuario solo tiene un rol específico una vez por empresa
        builder.HasIndex(urc => new { urc.UserId, urc.RoleId, urc.CompanyId }).IsUnique();

        builder.HasOne(urc => urc.Role)
            .WithMany(r => r.UserRoleCompanies)
            .HasForeignKey(urc => urc.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(urc => urc.GcRecord == 0);
    }
}