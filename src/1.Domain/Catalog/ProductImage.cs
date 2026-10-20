using JOIN.Domain.Audit;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;

namespace JOIN.Domain.Catalog;

/// <summary>
/// SPEC 70 — image attached to a product or service (table <c>Catalog.ProductImages</c>). 0 to 5 per product,
/// ordered by <see cref="SortOrder"/>, with exactly one live <c>IsPrimary = TRUE</c> enforced by a filtered
/// unique index on <c>ProductId</c>. The file stays on disk while the row exists (logical delete).
/// </summary>
public class ProductImage : BaseTenantEntity
{
    public const int MaxImagesPerProduct = 5;
    public const int MaxNameLength = 150;
    public const int MaxPathLength = 500;
    public const int MaxContentTypeLength = 100;

    public Guid ProductId { get; set; }

    public string OriginalName { get; set; } = string.Empty;

    public string NewName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public StorageProviderKind StorageProvider { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary> 1-based display order; assigned by <c>ReorderProductImagesCommand</c>. </summary>
    public int SortOrder { get; set; }

    public bool IsPrimary { get; set; }

    public Guid UploadedByUserId { get; set; }

    public DateTime? DeletedUtc { get; set; }

    public Guid? DeletedByUserId { get; set; }

    public string? DeleteReason { get; set; }

    // --- Navigation ---
    public virtual Product Product { get; set; } = null!;
    public virtual ApplicationUser UploadedByUser { get; set; } = null!;
}