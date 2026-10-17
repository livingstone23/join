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
