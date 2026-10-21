using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.Products.Common;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.Products.Documents.Queries;

/// <summary>Handles <see cref="GetProductDocumentsQuery"/> with Dapper.</summary>
public sealed class GetProductDocumentsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductDocumentsQuery, Response<IReadOnlyList<ProductDocumentDto>>>
{
    public async Task<Response<IReadOnlyList<ProductDocumentDto>>> Handle(GetProductDocumentsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAccessGuard.CompanyRequired<IReadOnlyList<ProductDocumentDto>>();
        }

        using var connection = connectionFactory.CreateConnection();
        if (!await ProductAttachmentQuerySql.ProductExistsAsync(connection, request.ProductId, currentUserService.CompanyId, cancellationToken))
        {
            return Response<IReadOnlyList<ProductDocumentDto>>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."]);
        }

        const string sql = """
            SELECT
                d.Id, d.ProductId, d.ProductDocumentTypeId, COALESCE(t.Name, '') AS ProductDocumentTypeName,
                d.DisplayName, d.Description, d.DocumentType, d.OriginalName, d.NewName, d.ContentType, d.SizeBytes,
                d.UploadedByUserId, CONCAT(u.FirstName, ' ', u.LastName) AS UploadedByUserName, d.Created AS CreatedAt
            FROM catalog.productdocuments d
            LEFT JOIN catalog.productdocumenttypes t ON t.Id = d.ProductDocumentTypeId
            LEFT JOIN security.users u ON u.Id = d.UploadedByUserId
            WHERE d.ProductId = @ProductId
              AND d.CompanyId = @CompanyId
              AND d.GcRecord = 0
            ORDER BY d.Created DESC;
            """;

        var rows = (await connection.QueryAsync<ProductDocumentDto>(new CommandDefinition(
                sql, new { request.ProductId, currentUserService.CompanyId }, cancellationToken: cancellationToken)))
            .Select(r => r with { UploadedByUserName = r.UploadedByUserName.Trim() })
            .ToList();

        return new Response<IReadOnlyList<ProductDocumentDto>>
        {
            IsSuccess = true,
            Message = "Product documents retrieved successfully.",
            Data = rows
        };
    }
}

/// <summary>Handles <see cref="DownloadProductDocumentQuery"/>: streams the file from the provider that stored it.</summary>
public sealed class DownloadProductDocumentQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IFileStorageResolver fileStorageResolver)
    : IRequestHandler<DownloadProductDocumentQuery, Response<ProductDocumentDownloadResult>>
{
    public async Task<Response<ProductDocumentDownloadResult>> Handle(DownloadProductDocumentQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAccessGuard.CompanyRequired<ProductDocumentDownloadResult>();
        }

        const string sql = """
            SELECT d.Path, d.StorageProvider, d.ContentType, d.OriginalName
            FROM catalog.productdocuments d
            WHERE d.Id = @DocumentId
              AND d.ProductId = @ProductId
              AND d.CompanyId = @CompanyId
              AND d.GcRecord = 0;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<ProductAttachmentQuerySql.FileRow>(new CommandDefinition(
            sql, new { request.DocumentId, request.ProductId, currentUserService.CompanyId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return Response<ProductDocumentDownloadResult>.Error(CatalogErrorCodes.ProductDocumentNotFound, ["Product document not found."]);
        }

        var provider = fileStorageResolver.Resolve((StorageProviderKind)row.StorageProvider);
        if (provider is null)
        {
            return Response<ProductDocumentDownloadResult>.Error(
                CatalogErrorCodes.StorageProviderNotConfigured,
                [$"The storage provider '{(StorageProviderKind)row.StorageProvider}' of this document is not configured in the current environment."]);
        }

        var stream = await provider.OpenReadAsync(row.Path, cancellationToken);
        return stream is null
            ? Response<ProductDocumentDownloadResult>.Error(CatalogErrorCodes.FileNotFoundInStorage, ["The underlying file is missing from storage."])
            : new Response<ProductDocumentDownloadResult>
            {
                IsSuccess = true,
                Message = "Product document retrieved successfully.",
                Data = new ProductDocumentDownloadResult(stream, row.ContentType, row.OriginalName)
            };
    }
}
