using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;

namespace JOIN.Application.UseCases.Catalog.ProductCategories.Commands;

/// <summary>
/// Fields shared by the create and update commands of a product category.
/// </summary>
public interface IProductCategoryInput
{
    string Name { get; }
    Guid? ParentId { get; }
    int SortOrder { get; }
    bool IsActive { get; }
}

/// <summary>
/// <c>POST /ProductCategories</c> (SPEC 57).
/// </summary>
public sealed record CreateProductCategoryCommand : IProductCategoryInput, ITransactionalCommand<Response<ProductCategoryDto>>
{
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>PUT /ProductCategories/{id}</c> (SPEC 57). A category cannot become its own ancestor
/// (<c>PRODUCT_CATEGORY_CYCLE</c>).
/// </summary>
public sealed record UpdateProductCategoryCommand : IProductCategoryInput, ITransactionalCommand<Response<ProductCategoryDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// <c>DELETE /ProductCategories/{id}</c>: blocked while live products or subcategories use it
/// (<c>PRODUCT_CATEGORY_IN_USE</c>).
/// </summary>
public sealed record DeleteProductCategoryCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>
/// <c>POST /ProductCategories/{id}/restore</c> (SPEC 41-post, SuperAdmin). Not while its parent is deleted.
/// </summary>
public sealed record RestoreProductCategoryCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
