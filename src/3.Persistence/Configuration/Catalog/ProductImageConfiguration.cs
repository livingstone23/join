using JOIN.Domain.Catalog;
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Catalog;

/// <summary>
/// Configures <c>Catalog.ProductImages</c> (SPEC 70). The filtered unique index on <c>(ProductId)</c> with
/// <c>"isprimary" = TRUE AND "gcrecord" = 0</c> enforces the "one primary per product" invariant.
/// </summary>
public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ProductImages", "Catalog");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.OriginalName).IsRequired().HasMaxLength(ProductImage.MaxNameLength);
        builder.Property(x => x.NewName).IsRequired().HasMaxLength(ProductImage.MaxNameLength);
        builder.Property(x => x.Path).IsRequired().HasMaxLength(ProductImage.MaxPathLength);
        builder.Property(x => x.StorageProvider).IsRequired();
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(ProductImage.MaxContentTypeLength);
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.IsPrimary).IsRequired();
        builder.Property(x => x.DeleteReason).HasMaxLength(500);

        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CompanyId, x.ProductId, x.SortOrder }).HasDatabaseName("IX_ProductImages_Product_Sort");
        builder.HasIndex(x => new { x.CompanyId, x.ProductId }).HasDatabaseName("IX_ProductImages_Product");
        builder.HasIndex(x => x.ProductId)
            .IsUnique()
            .HasFilter("\"isprimary\" = TRUE AND \"gcrecord\" = 0")
            .HasDatabaseName("UX_ProductImages_OnePrimary");
    }
}