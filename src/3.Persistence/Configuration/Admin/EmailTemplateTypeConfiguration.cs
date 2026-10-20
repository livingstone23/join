using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>Configures <c>Admin.EmailTemplateTypes</c> (SPEC 71) — one classification catalog per company.</summary>
public class EmailTemplateTypeConfiguration : IEntityTypeConfiguration<EmailTemplateType>
{
    public void Configure(EntityTypeBuilder<EmailTemplateType> builder)
    {
        builder.ToTable("EmailTemplateTypes", "Admin");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(EmailTemplateType.MaxCodeLength);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(EmailTemplateType.MaxNameLength);
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CompanyId, x.Code })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_EmailTemplateTypes_Company_Code");
    }
}
