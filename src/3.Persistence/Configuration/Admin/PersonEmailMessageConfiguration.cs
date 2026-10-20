using JOIN.Domain.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Admin;

/// <summary>
/// Configures <c>Admin.PersonEmailMessages</c> (SPEC 72) — the emails sent to the persons of the company,
/// whatever their origin (person, customer or lead).
/// </summary>
public class PersonEmailMessageConfiguration : IEntityTypeConfiguration<PersonEmailMessage>
{
    public void Configure(EntityTypeBuilder<PersonEmailMessage> builder)
    {
        builder.ToTable("PersonEmailMessages", "Admin");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ToAddress).IsRequired().HasMaxLength(PersonEmailMessage.MaxToAddressLength);
        builder.Property(x => x.CcAddresses).HasMaxLength(2000);
        builder.Property(x => x.FromAddress).IsRequired().HasMaxLength(PersonEmailMessage.MaxToAddressLength);
        builder.Property(x => x.ReplyToAddress).HasMaxLength(PersonEmailMessage.MaxToAddressLength);
        builder.Property(x => x.Subject).IsRequired().HasMaxLength(PersonEmailMessage.MaxSubjectLength);
        builder.Property(x => x.BodyHtml).IsRequired().HasColumnType("text");
        builder.Property(x => x.ProviderMessageId).HasMaxLength(PersonEmailMessage.MaxProviderMessageIdLength);
        builder.Property(x => x.ErrorMessage).HasMaxLength(PersonEmailMessage.MaxErrorMessageLength);
        builder.Property(x => x.Origin).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.SentAt).IsRequired();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JOIN.Domain.Customers.Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JOIN.Domain.Customers.Leads.Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmailTemplate>().WithMany().HasForeignKey(x => x.EmailTemplateId).OnDelete(DeleteBehavior.Restrict);

        // The history is read per person, per lead and per author.
        builder.HasIndex(x => new { x.CompanyId, x.PersonId, x.SentAt })
            .HasDatabaseName("IX_PersonEmailMessages_Company_Person_SentAt");
        builder.HasIndex(x => new { x.CompanyId, x.LeadId, x.SentAt })
            .HasDatabaseName("IX_PersonEmailMessages_Company_Lead_SentAt");
        builder.HasIndex(x => new { x.CompanyId, x.SentByUserId, x.SentAt })
            .HasDatabaseName("IX_PersonEmailMessages_Company_User_SentAt");
    }
}

/// <summary>Configures <c>Admin.PersonEmailMessageAttachments</c> (SPEC 72).</summary>
public class PersonEmailMessageAttachmentConfiguration : IEntityTypeConfiguration<PersonEmailMessageAttachment>
{
    public void Configure(EntityTypeBuilder<PersonEmailMessageAttachment> builder)
    {
        builder.ToTable("PersonEmailMessageAttachments", "Admin");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SourceKind).IsRequired();
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(150);
        builder.Property(x => x.SizeBytes).IsRequired();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PersonEmailMessage>()
            .WithMany()
            .HasForeignKey(x => x.PersonEmailMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.PersonEmailMessageId }).HasDatabaseName("IX_PersonEmailMessageAttachments_Message");
    }
}
