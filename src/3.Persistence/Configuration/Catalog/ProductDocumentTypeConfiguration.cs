using JOIN.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Catalog;

/// <summary>Configures <c>Catalog.ProductDocumentTypes</c> (SPEC 70) — one seed-friendly catalog per company.</summary>
public class ProductDocumentTypeConfiguration : IEntityTypeConfiguration<ProductDocumentType>
{
    public void Configure(EntityTypeBuilder<ProductDocumentType> builder)
    {
        builder.ToTable("ProductDocumentTypes", "Catalog");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Code).IsRequired().HasMaxLength(ProductDocumentType.MaxCodeLength);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(ProductDocumentType.MaxNameLength);
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CompanyId, x.Code })
            .IsUnique()
            .HasFilter("\"gcrecord\" = 0")
            .HasDatabaseName("UX_ProductDocumentTypes_Company_Code");
    }
}