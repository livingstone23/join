using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Catalog.Products.Queries;

/// <summary>
/// Shared SQL of the product queries: category name and the company's base currency code.
/// </summary>
internal static class ProductQuerySql
{
    internal const string BaseCurrencyJoin = """
        LEFT JOIN finance.companycurrencies bcc ON bcc.CompanyId = p.CompanyId AND bcc.IsBase = TRUE AND bcc.GcRecord = 0
        LEFT JOIN finance.currencies bc ON bc.Id = bcc.CurrencyId
        """;

    internal const string Select = $"""
        SELECT
            p.Id,
            p.GcRecord,
            p.CompanyId,
            p.Code,
            p.Name,
            p.Description,
            p.Kind,
            p.ProductCategoryId,
            pc.Name AS CategoryName,
            p.ReferencePrice,
            COALESCE(bc.Code, '') AS BaseCurrencyCode,
            p.IsActive,
            p.Created AS CreatedAt
        FROM catalog.products p
        LEFT JOIN catalog.productcategories pc ON pc.Id = p.ProductCategoryId
        {BaseCurrencyJoin}
        """;
}

/// <summary>
/// Handles <see cref="GetProductsQuery"/>.
/// </summary>
public sealed class GetProductsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductsQuery, Response<PagedResult<ProductDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<ProductDto>>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<ProductDto>>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", TenantResolver.Resolve(currentUserService, request.CompanyId));
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE p.CompanyId = @CompanyId");
        if (!SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted))
        {
            where.Append(" AND p.GcRecord = 0");
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (p.Code ILIKE @Search OR p.Name ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.Kind.HasValue)
        {
            where.Append(" AND p.Kind = @Kind");
            parameters.Add("Kind", request.Kind.Value);
        }

        if (request.ProductCategoryId is { } categoryId && categoryId != Guid.Empty)
        {
            where.Append(" AND p.ProductCategoryId = @ProductCategoryId");
            parameters.Add("ProductCategoryId", categoryId);
        }

        if (request.IsActive.HasValue)
        {
            where.Append(" AND p.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var sql = $"""
            {ProductQuerySql.Select}
            {where}
            ORDER BY p.Name, p.Code
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM catalog.products p {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<ProductDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<ProductDto>>
        {
            IsSuccess = true,
            Message = "Products retrieved successfully.",
            Data = new PagedResult<ProductDto> { Items = items, PageNumber = pageNumber, PageSize = pageSize, TotalCount = total }
        };
    }
}

/// <summary>
/// Handles <see cref="GetProductByIdQuery"/>.
/// </summary>
public sealed class GetProductByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductByIdQuery, Response<ProductDto>>
{
    public async Task<Response<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<ProductDto>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND p.GcRecord = 0";
        var sql = $"""
            {ProductQuerySql.Select}
            WHERE p.Id = @Id
              AND p.CompanyId = @CompanyId
              {deleted};
            """;

        using var connection = connectionFactory.CreateConnection();
        var product = await connection.QuerySingleOrDefaultAsync<ProductDto>(new CommandDefinition(
            sql,
            new { request.Id, CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId) },
            cancellationToken: cancellationToken));

        return product is null
            ? Response<ProductDto>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."])
            : new Response<ProductDto> { IsSuccess = true, Message = "Product retrieved successfully.", Data = product };
    }
}

/// <summary>
/// Handles <see cref="GetProductLookupQuery"/>.
/// </summary>
public sealed class GetProductLookupQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductLookupQuery, Response<IReadOnlyList<ProductLookupItemDto>>>
{
    private const int MaxResults = 20;

    public async Task<Response<IReadOnlyList<ProductLookupItemDto>>> Handle(GetProductLookupQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<ProductLookupItemDto>>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", currentUserService.CompanyId);
        parameters.Add("Limit", MaxResults);
        var search = string.Empty;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            search = "AND (p.Code ILIKE @Search OR p.Name ILIKE @Search)";
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        var sql = $"""
            SELECT p.Id, p.Code, p.Name, p.Kind, p.ReferencePrice, COALESCE(bc.Code, '') AS BaseCurrencyCode
            FROM catalog.products p
            {ProductQuerySql.BaseCurrencyJoin}
            WHERE p.CompanyId = @CompanyId
              AND p.GcRecord = 0
              AND p.IsActive = TRUE
              {search}
            ORDER BY p.Name, p.Code
            LIMIT @Limit;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ProductLookupItemDto>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))).ToList();

        return new Response<IReadOnlyList<ProductLookupItemDto>>
        {
            IsSuccess = true,
            Message = "Products retrieved successfully.",
            Data = items
        };
    }
}
