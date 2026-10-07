using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Messaging;

/// <summary>
/// Configures persistence rules for tenant ticket-attachment settings.
///
/// The unique index over <c>CompanyId</c> is intentionally <c>FILTERED WHERE GcRecord = 0</c>
/// (unlike <see cref="TicketCompanyDefaultConfiguration"/>) — a soft delete of the
/// configuration must allow a fresh one to be created afterwards, otherwise the
/// company is permanently locked out of uploads (no API path exists to resurrect
/// a soft-deleted row, so without the filter the unique index would block the
/// only recovery route).
/// </summary>
public sealed class TicketAttachmentSettingsConfiguration : IEntityTypeConfiguration<TicketAttachmentSettings>
{
    /// <summary>
    /// Configures the entity schema, constraints, indexes, and relationships.
    /// </summary>
    public void Configure(EntityTypeBuilder<TicketAttachmentSettings> builder)
    {
        builder.ToTable("TicketAttachmentSettings", "Messaging");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AllowedDocumentTypes)
            .IsRequired();

        builder.Property(x => x.MaxFileSizeBytes)
            .IsRequired();

        builder.Property(x => x.MaxFilesPerTicket)
            .IsRequired();

        builder.Property(x => x.MaxFilesPerDay)
            .IsRequired(false);

        // Filtered unique index — see class summary.
        builder.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_TicketAttachmentSettings_Company_Active");
    }
}