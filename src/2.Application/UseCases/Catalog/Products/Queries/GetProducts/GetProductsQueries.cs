using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.Products.Queries;

/// <summary>
/// <c>GET /Products</c> (SPEC 57): the company's catalog, paged, with code/name search (<c>ILIKE</c>).
/// </summary>
public sealed record GetProductsQuery(
    string? Search = null,
    int? Kind = null,
    Guid? ProductCategoryId = null,
    bool? IsActive = null,
    bool? IncludeDeleted = null,
    int? PageNumber = null,
    int? PageSize = null,
    Guid? CompanyId = null) : IRequest<Response<PagedResult<ProductDto>>>;

/// <summary>
/// <c>GET /Products/{id}</c>.
/// </summary>
public sealed record GetProductByIdQuery(Guid Id, bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<ProductDto>>;

/// <summary>
/// <c>GET /Products/lookup?search=</c> (SPEC 57): up to 20 active products of the company, not paged.
/// </summary>
public sealed record GetProductLookupQuery(string? Search = null) : IRequest<Response<IReadOnlyList<ProductLookupItemDto>>>;
