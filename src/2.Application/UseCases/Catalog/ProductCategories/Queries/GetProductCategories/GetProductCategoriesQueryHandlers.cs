using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductCategories.Queries;

/// <summary>
/// Shared SQL of the product category queries, with live product and subcategory counts.
/// </summary>
internal static class ProductCategoryQuerySql
{
    internal const string Select = """
        SELECT
            c.Id,
            c.GcRecord,
            c.CompanyId,
            c.Name,
            c.ParentId,
            parent.Name AS ParentName,
            c.SortOrder,
            c.IsActive,
            (SELECT COUNT(*)::int FROM catalog.products p WHERE p.ProductCategoryId = c.Id AND p.GcRecord = 0) AS ProductsCount,
            (SELECT COUNT(*)::int FROM catalog.productcategories ch WHERE ch.ParentId = c.Id AND ch.GcRecord = 0) AS ChildrenCount,
            c.Created AS CreatedAt
        FROM catalog.productcategories c
        LEFT JOIN catalog.productcategories parent ON parent.Id = c.ParentId
        """;
}

/// <summary>
/// Handles <see cref="GetProductCategoriesQuery"/>.
/// </summary>
public sealed class GetProductCategoriesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductCategoriesQuery, Response<IReadOnlyList<ProductCategoryDto>>>
{
    public async Task<Response<IReadOnlyList<ProductCategoryDto>>> Handle(GetProductCategoriesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<ProductCategoryDto>>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND c.GcRecord = 0";
        var inactive = request.IncludeInactive == false ? "AND c.IsActive = TRUE" : string.Empty;
        var sql = $"""
            {ProductCategoryQuerySql.Select}
            WHERE c.CompanyId = @CompanyId
              {deleted}
              {inactive}
            ORDER BY c.SortOrder, c.Name;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ProductCategoryDto>(new CommandDefinition(
                sql, new { CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId) }, cancellationToken: cancellationToken)))
            .ToList();

        return new Response<IReadOnlyList<ProductCategoryDto>>
        {
            IsSuccess = true,
            Message = "Product categories retrieved successfully.",
            Data = items
        };
    }
}

/// <summary>
/// Handles <see cref="GetProductCategoryByIdQuery"/>.
/// </summary>
public sealed class GetProductCategoryByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductCategoryByIdQuery, Response<ProductCategoryDto>>
{
    public async Task<Response<ProductCategoryDto>> Handle(GetProductCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<ProductCategoryDto>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND c.GcRecord = 0";
        var sql = $"""
            {ProductCategoryQuerySql.Select}
            WHERE c.Id = @Id
              AND c.CompanyId = @CompanyId
              {deleted};
            """;

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QuerySingleOrDefaultAsync<ProductCategoryDto>(new CommandDefinition(
            sql, new { request.Id, CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId) }, cancellationToken: cancellationToken));

        return item is null
            ? Response<ProductCategoryDto>.Error(CatalogErrorCodes.ProductCategoryNotFound, ["Product category not found."])
            : new Response<ProductCategoryDto> { IsSuccess = true, Message = "Product category retrieved successfully.", Data = item };
    }
}
