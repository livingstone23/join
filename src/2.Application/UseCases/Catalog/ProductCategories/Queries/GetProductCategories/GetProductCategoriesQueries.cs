using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductCategories.Queries;

/// <summary>
/// <c>GET /ProductCategories</c> (SPEC 57): every category of the company (the front builds the tree), not paged.
/// </summary>
public sealed record GetProductCategoriesQuery(
    bool? IncludeInactive = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null) : IRequest<Response<IReadOnlyList<ProductCategoryDto>>>;

/// <summary>
/// <c>GET /ProductCategories/{id}</c>.
/// </summary>
public sealed record GetProductCategoryByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<ProductCategoryDto>>;
