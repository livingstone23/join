using JOIN.Domain.Audit;
using JOIN.Domain.Enums;

namespace JOIN.Domain.Catalog;

/// <summary>
/// SPEC 70 — per-company attachment limits for the product catalog (table <c>Catalog.ProductAttachmentSettings</c>,
/// one row per company). Image and document quotas are independent (rule 5). Without a row nothing can be
/// uploaded (<c>PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED</c>).
/// </summary>
public class ProductAttachmentSettings : BaseTenantEntity
{
    public const long DefaultMaxDocumentSizeBytes = 10L * 1024 * 1024;
    public const int DefaultMaxDocumentsPerProduct = 20;
    public const long DefaultMaxImageSizeBytes = 5L * 1024 * 1024;
    public const int DefaultMaxImagesPerProduct = 5;
    public const DocumentType DefaultAllowedDocumentTypes = DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Image;
    public const ProductImageFormat DefaultAllowedImageFormats = ProductImageFormat.Jpeg | ProductImageFormat.Png | ProductImageFormat.Webp;

    public DocumentType AllowedDocumentTypes { get; set; }
    public long MaxDocumentSizeBytes { get; set; }
    public int MaxDocumentsPerProduct { get; set; }
    public ProductImageFormat AllowedImageFormats { get; set; }
    public long MaxImageSizeBytes { get; set; }
    /// <summary> Between 1 and 5; the hard upper bound (<see cref="ProductImage.MaxImagesPerProduct"/>) is 5. </summary>
    public int MaxImagesPerProduct { get; set; }
}