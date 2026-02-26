


using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;



namespace JOIN.Infrastructure.Configuration.Security;



/// <summary>
/// Fluent API configuration for the User-Company intersection.
/// Secures the multi-tenant architecture by ensuring a user cannot be duplicated within the same tenant.
/// </summary>
public class UserCompanyConfiguration : IEntityTypeConfiguration<UserCompany>
{
    public void Configure(EntityTypeBuilder<UserCompany> builder)
    {
        builder.ToTable("UserCompanies");
        builder.HasKey(uc => uc.Id);

        // REGLA DE NEGOCIO: Composite Unique Index para evitar duplicidad de accesos
        builder.HasIndex(uc => new { uc.UserId, uc.CompanyId }).IsUnique();

        builder.HasOne(uc => uc.User)
            .WithMany(u => u.UserCompanies)
            .HasForeignKey(uc => uc.UserId)
            .OnDelete(DeleteBehavior.Cascade); 

        builder.HasOne(uc => uc.Company)
            .WithMany()
            .HasForeignKey(uc => uc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict); 

        builder.HasQueryFilter(uc => uc.GcRecord == 0);
        
    }
}