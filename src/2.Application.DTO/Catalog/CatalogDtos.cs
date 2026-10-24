using JOIN.Application.DTO.Common;

namespace JOIN.Application.DTO.Catalog;

/// <summary>
/// A product or service of the company (SPEC 57). <see cref="ReferencePrice"/> is in <see cref="BaseCurrencyCode"/>.
/// </summary>
public sealed record ProductDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    /// <summary><c>ProductKind</c>: 1 Product, 2 Service.</summary>
    public int Kind { get; init; }
    public Guid? ProductCategoryId { get; init; }
    public string? CategoryName { get; init; }
    public decimal ReferencePrice { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }

    /// <summary>SPEC 70 — id of the primary (display) image, or <c>null</c> when the product has none.</summary>
    public Guid? PrimaryImageId { get; init; }

    /// <summary>SPEC 70 — how many live images the product carries.</summary>
    public int ImageCount { get; init; }
}

/// <summary>
/// Compact product row for the lead form search boxes (SPEC 57, <c>GET /Products/lookup</c>).
/// </summary>
public sealed record ProductLookupItemDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    /// <summary><c>ProductKind</c>: 1 Product, 2 Service.</summary>
    public int Kind { get; init; }
    public decimal ReferencePrice { get; init; }
    public string BaseCurrencyCode { get; init; } = string.Empty;
    /// <summary>SPEC 70 — id of the primary image, or <c>null</c>.</summary>
    public Guid? PrimaryImageId { get; init; }
    /// <summary>SPEC 70 — how many live images the product carries.</summary>
    public int ImageCount { get; init; }
}

/// <summary>
/// A product category, with its parent and how many live products and subcategories it holds (SPEC 57).
/// </summary>
public sealed record ProductCategoryDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public string? ParentName { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public int ProductsCount { get; init; }
    public int ChildrenCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// SPEC 70 — one image attached to a product or service, ordered by <see cref="SortOrder"/>. The
/// <see cref="IsPrimary"/> flag is rendered with a "Primary" badge in the UI; only one live primary per
/// product (filtered unique index).
/// </summary>
public sealed record ProductImageDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public string OriginalName { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int SortOrder { get; init; }
    public bool IsPrimary { get; init; }
    public Guid UploadedByUserId { get; init; }
    public string UploadedByUserName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsDeleted { get; init; }
    public Guid? DeletedByUserId { get; init; }
    public string? DeletedByUserName { get; init; }
    public DateTime? DeletedUtc { get; init; }
    public string? DeleteReason { get; init; }
}

/// <summary>
/// SPEC 70 — one document attached to a product or service, classified by a <see cref="ProductDocumentTypeDto"/>.
/// </summary>
public sealed record ProductDocumentDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public Guid ProductDocumentTypeId { get; init; }
    public string ProductDocumentTypeName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int DocumentType { get; init; }
    public string OriginalName { get; init; } = string.Empty;
    public string NewName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public Guid UploadedByUserId { get; init; }
    public string UploadedByUserName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public bool IsDeleted { get; init; }
    public Guid? DeletedByUserId { get; init; }
    public string? DeletedByUserName { get; init; }
    public DateTime? DeletedUtc { get; init; }
    public string? DeleteReason { get; init; }
}

/// <summary>
/// SPEC 70 — stream + metadata of a downloaded product document. The controller turns it into a
/// <see cref="Microsoft.AspNetCore.Mvc.FileStreamResult"/>.
/// </summary>
public sealed record ProductDocumentDownloadResult(Stream Content, string ContentType, string OriginalName);

/// <summary>
/// SPEC 70 — stream + metadata of a product image. The controller streams it as <c>inline</c> with a
/// <c>Cache-Control: private, max-age=3600</c> header.
/// </summary>
public sealed record ProductImageContentResult(Stream Content, string ContentType, string FileName);

/// <summary>
/// SPEC 70 — one entry of the company's product document type catalog. <see cref="Code"/> is upper-case
/// without spaces and unique per company among live rows. SPEC 72.1 adds the soft-delete fields, so a
/// SuperAdmin can list the deleted types (<c>includeDeleted=true</c>) and restore them.
/// </summary>
public sealed record ProductDocumentTypeDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// SPEC 70 — the company's per-tenant attachment limits for the product catalog. Without a row
/// nothing can be uploaded (<c>PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED</c>).
/// </summary>
public sealed record ProductAttachmentSettingsDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public int AllowedDocumentTypes { get; init; }
    public IReadOnlyList<string> AllowedDocumentTypeNames { get; init; } = [];
    public long MaxDocumentSizeBytes { get; init; }
    public int MaxDocumentsPerProduct { get; init; }
    public int AllowedImageFormats { get; init; }
    public IReadOnlyList<string> AllowedImageFormatNames { get; init; } = [];
    public long MaxImageSizeBytes { get; init; }
    public int MaxImagesPerProduct { get; init; }
    public DateTime CreatedAt { get; init; }
}