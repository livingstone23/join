using JOIN.Domain.Audit;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;

namespace JOIN.Domain.Catalog;

/// <summary>
/// SPEC 70 — document attached to a product or service (table <c>Catalog.ProductDocuments</c>). Each row is
/// classified by a <see cref="ProductDocumentType"/> of the company (Ficha técnica, Propuesta, Manual, Otro).
/// Logical delete only; the file stays on disk.
/// </summary>
public class ProductDocument : BaseTenantEntity
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxNameLength = 150;
    public const int MaxPathLength = 500;
    public const int MaxContentTypeLength = 150;

    public Guid ProductId { get; set; }

    public Guid ProductDocumentTypeId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DocumentType DocumentType { get; set; }

    public string OriginalName { get; set; } = string.Empty;

    public string NewName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public StorageProviderKind StorageProvider { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public Guid UploadedByUserId { get; set; }

    public DateTime? DeletedUtc { get; set; }

    public Guid? DeletedByUserId { get; set; }

    public string? DeleteReason { get; set; }

    // --- Navigation ---
    public virtual Product Product { get; set; } = null!;
    public virtual ProductDocumentType ProductDocumentType { get; set; } = null!;
    public virtual ApplicationUser UploadedByUser { get; set; } = null!;
}