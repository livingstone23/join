using JOIN.Domain.Catalog;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Catalog;

/// <summary>Configures <c>Catalog.ProductDocuments</c> (SPEC 70).</summary>
public class ProductDocumentConfiguration : IEntityTypeConfiguration<ProductDocument>
{
    public void Configure(EntityTypeBuilder<ProductDocument> builder)
    {
        builder.ToTable("ProductDocuments", "Catalog");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(ProductDocument.MaxDisplayNameLength);
        builder.Property(x => x.Description).HasMaxLength(ProductDocument.MaxDescriptionLength);
        builder.Property(x => x.DocumentType).IsRequired();
        builder.Property(x => x.OriginalName).IsRequired().HasMaxLength(ProductDocument.MaxNameLength);
        builder.Property(x => x.NewName).IsRequired().HasMaxLength(ProductDocument.MaxNameLength);
        builder.Property(x => x.Path).IsRequired().HasMaxLength(ProductDocument.MaxPathLength);
        builder.Property(x => x.StorageProvider).IsRequired();
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(ProductDocument.MaxContentTypeLength);
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.DeleteReason).HasMaxLength(500);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProductDocumentType).WithMany().HasForeignKey(x => x.ProductDocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CompanyId, x.ProductId }).HasDatabaseName("IX_ProductDocuments_Product");
    }
}