using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductImages;

/// <summary>
/// <c>GET /Products/{id}/images/{imageId}/content</c> (SPEC 70) and <c>Leads/products-lookup/{productId}/images/{imageId}/content</c>: streams the bytes
/// from the storage provider that stored them. The handler does not load the product through the access guard
/// because the leads lookup route is exposed to sellers without <c>Products</c> permission.
/// </summary>
public sealed record GetProductImageContentQuery(Guid ProductId, Guid ImageId) : IRequest<Response<ProductImageContentResult>>;

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
            return Response<ProductImageContentResult>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        const string sql = """
            SELECT Path, StorageProvider, ContentType, OriginalName
            FROM catalog.productimages
            WHERE Id = @Id
              AND ProductId = @ProductId
              AND CompanyId = @CompanyId
              AND GcRecord = 0
              AND DeletedUtc IS NULL;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            sql,
            new { request.ImageId, request.ProductId, CompanyId = currentUserService.CompanyId },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return Response<ProductImageContentResult>.Error(CatalogErrorCodes.ProductImageNotFound, ["Product image not found."]);
        }

        var provider = fileStorageResolver.Resolve((StorageProviderKind)row.StorageProvider);
        if (provider is null)
        {
            return Response<ProductImageContentResult>.Error("STORAGE_PROVIDER_NOT_CONFIGURED", [$"The storage provider '{(StorageProviderKind)row.StorageProvider}' of this image is not configured in the current environment."]);
        }

        var stream = await provider.OpenReadAsync(row.Path, cancellationToken);
        return stream is null
            ? Response<ProductImageContentResult>.Error("FILE_NOT_FOUND_IN_STORAGE", ["The underlying image is missing from storage."])
            : new Response<ProductImageContentResult>
            {
                IsSuccess = true,
                Message = "Product image retrieved successfully.",
                Data = new ProductImageContentResult(stream, row.ContentType, row.OriginalName)
            };
    }

    private sealed class Row
    {
        public string Path { get; set; } = string.Empty;
        public int StorageProvider { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
    }
}