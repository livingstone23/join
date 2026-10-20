using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>Configures <c>Admin.EmailTemplates</c> (SPEC 71) — the company's reusable email templates.</summary>
public class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("EmailTemplates", "Admin");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.EmailTemplateTypeId).IsRequired();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(EmailTemplate.MaxNameLength);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(EmailTemplate.MaxSubjectLength);
        builder.Property(x => x.BodyHtml).IsRequired().HasColumnType("text");
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.EmailTemplateType)
            .WithMany()
            .HasForeignKey(x => x.EmailTemplateTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // The name is unique per company among live rows (SPEC 71, rule 1).
        builder.HasIndex(x => new { x.CompanyId, x.Name })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_EmailTemplates_Company_Name");

        // The list filters by type and by active state.
        builder.HasIndex(x => new { x.CompanyId, x.EmailTemplateTypeId, x.IsActive })
            .HasDatabaseName("IX_EmailTemplates_Company_Type_Active");
    }
}
