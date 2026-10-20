using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>
/// Configures <c>Admin.EmailCompanySettings</c>, one live row per company (SPEC 72).
/// </summary>
public class EmailCompanySettingsConfiguration : IEntityTypeConfiguration<EmailCompanySettings>
{
    public void Configure(EntityTypeBuilder<EmailCompanySettings> builder)
    {
        builder.ToTable("EmailCompanySettings", "Admin");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FromEmail).HasMaxLength(EmailCompanySettings.MaxFromEmailLength);
        builder.Property(x => x.FromName).HasMaxLength(EmailCompanySettings.MaxFromNameLength);
        builder.Property(x => x.MaxEmailsPerUserPerDay).IsRequired();
        builder.Property(x => x.MaxAttachmentsTotalBytes).IsRequired();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CompanyId).IsUnique().HasFilter("\"gcrecord\" = 0").HasDatabaseName("UX_EmailCompanySettings_Company");
    }
}
