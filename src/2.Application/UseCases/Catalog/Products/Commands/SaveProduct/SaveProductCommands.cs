using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;

namespace JOIN.Application.UseCases.Catalog.Products.Commands;

/// <summary>
/// Fields shared by the create and update commands of a product.
/// </summary>
public interface IProductInput
{
    string Code { get; }
    string Name { get; }
    string? Description { get; }
    /// <summary><c>ProductKind</c>: 1 Product, 2 Service.</summary>
    int Kind { get; }
    Guid? ProductCategoryId { get; }
    decimal ReferencePrice { get; }
    bool IsActive { get; }
}

/// <summary>
/// <c>POST /Products</c> (SPEC 57): adds a product or service to the company's catalog.
/// </summary>
public sealed record CreateProductCommand : IProductInput, ITransactionalCommand<Response<ProductDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Kind { get; init; } = 1;
    public Guid? ProductCategoryId { get; init; }
    public decimal ReferencePrice { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>PUT /Products/{id}</c> (SPEC 57).
/// </summary>
public sealed record UpdateProductCommand : IProductInput, ITransactionalCommand<Response<ProductDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int Kind { get; init; } = 1;
    public Guid? ProductCategoryId { get; init; }
    public decimal ReferencePrice { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>DELETE /Products/{id}</c>: logical delete. SPEC 61 adds the <c>PRODUCT_IN_USE</c> block for offered lines.
/// </summary>
public sealed record DeleteProductCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>
/// <c>POST /Products/{id}/restore</c> (SPEC 41-post, SuperAdmin).
/// </summary>
public sealed record RestoreProductCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
