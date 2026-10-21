using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.Products.Common;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.Products.Images.Queries;

/// <summary>Handles <see cref="GetProductImagesQuery"/> with Dapper.</summary>
public sealed class GetProductImagesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductImagesQuery, Response<IReadOnlyList<ProductImageDto>>>
{
    public async Task<Response<IReadOnlyList<ProductImageDto>>> Handle(GetProductImagesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAccessGuard.CompanyRequired<IReadOnlyList<ProductImageDto>>();
        }

        using var connection = connectionFactory.CreateConnection();
        if (!await ProductAttachmentQuerySql.ProductExistsAsync(connection, request.ProductId, currentUserService.CompanyId, cancellationToken))
        {
            return Response<IReadOnlyList<ProductImageDto>>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."]);
        }

        const string sql = """
            SELECT
                i.Id, i.ProductId, i.OriginalName, i.NewName, i.ContentType, i.SizeBytes, i.Width, i.Height,
                i.SortOrder, i.IsPrimary, i.UploadedByUserId,
                CONCAT(u.FirstName, ' ', u.LastName) AS UploadedByUserName, i.Created AS CreatedAt
            FROM catalog.productimages i
            LEFT JOIN security.users u ON u.Id = i.UploadedByUserId
            WHERE i.ProductId = @ProductId
              AND i.CompanyId = @CompanyId
              AND i.GcRecord = 0
            ORDER BY i.SortOrder;
            """;

        var rows = (await connection.QueryAsync<ProductImageDto>(new CommandDefinition(
                sql, new { request.ProductId, currentUserService.CompanyId }, cancellationToken: cancellationToken)))
            .Select(r => r with { UploadedByUserName = r.UploadedByUserName.Trim() })
            .ToList();

        return new Response<IReadOnlyList<ProductImageDto>>
        {
            IsSuccess = true,
            Message = "Product images retrieved successfully.",
            Data = rows
        };
    }
}

/// <summary>
/// Handles <see cref="GetProductImageContentQuery"/>. Only the tenant is checked, not the <c>Products</c> resource:
/// the <c>Leads</c> thumbnail route reuses it for sellers without <c>Products</c> permission (SPEC 70 C.16).
/// </summary>
public sealed class GetProductImageContentQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IFileStorageResolver fileStorageResolver)
    : IRequestHandler<GetProductImageContentQuery, Response<ProductImageContentResult>>
{
    public async Task<Response<ProductImageContentResult>> Handle(GetProductImageContentQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAccessGuard.CompanyRequired<ProductImageContentResult>();
        }

        const string sql = """
            SELECT i.Path, i.StorageProvider, i.ContentType, i.OriginalName
            FROM catalog.productimages i
            WHERE i.Id = @ImageId
              AND i.ProductId = @ProductId
              AND i.CompanyId = @CompanyId
              AND i.GcRecord = 0;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ProductAttachmentQuerySql.FileRow>(new CommandDefinition(
            sql, new { request.ImageId, request.ProductId, currentUserService.CompanyId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return Response<ProductImageContentResult>.Error(CatalogErrorCodes.ProductImageNotFound, ["Product image not found."]);
        }

        var provider = fileStorageResolver.Resolve((StorageProviderKind)row.StorageProvider);
        if (provider is null)
        {
            return Response<ProductImageContentResult>.Error(
                CatalogErrorCodes.StorageProviderNotConfigured,
                [$"The storage provider '{(StorageProviderKind)row.StorageProvider}' of this image is not configured in the current environment."]);
        }

        var stream = await provider.OpenReadAsync(row.Path, cancellationToken);
        return stream is null
            ? Response<ProductImageContentResult>.Error(CatalogErrorCodes.FileNotFoundInStorage, ["The underlying image is missing from storage."])
            : new Response<ProductImageContentResult>
            {
                IsSuccess = true,
                Message = "Product image retrieved successfully.",
                Data = new ProductImageContentResult(stream, row.ContentType, row.OriginalName)
            };
    }
}
