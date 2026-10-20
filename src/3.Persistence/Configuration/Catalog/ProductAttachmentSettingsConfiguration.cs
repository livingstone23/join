using JOIN.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Catalog;

/// <summary>Configures <c>Catalog.ProductAttachmentSettings</c> (SPEC 70), one live row per company.</summary>
public class ProductAttachmentSettingsConfiguration : IEntityTypeConfiguration<ProductAttachmentSettings>
{
    public void Configure(EntityTypeBuilder<ProductAttachmentSettings> builder)
    {
        builder.ToTable("ProductAttachmentSettings", "Catalog");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AllowedDocumentTypes).IsRequired();
        builder.Property(x => x.MaxDocumentSizeBytes).IsRequired();
        builder.Property(x => x.MaxDocumentsPerProduct).IsRequired();
        builder.Property(x => x.AllowedImageFormats).IsRequired();
        builder.Property(x => x.MaxImageSizeBytes).IsRequired();
        builder.Property(x => x.MaxImagesPerProduct).IsRequired();

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.CompanyId)
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_ProductAttachmentSettings_Company");
    }
}