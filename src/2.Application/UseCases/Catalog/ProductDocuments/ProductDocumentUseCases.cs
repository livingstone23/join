using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Domain.Enums;
using MediatR;
using AttachmentSettings = JOIN.Domain.Catalog.ProductAttachmentSettings;
using ProductEntity = JOIN.Domain.Catalog.Product;
using ProductDocumentEntity = JOIN.Domain.Catalog.ProductDocument;
using ProductDocumentTypeEntity = JOIN.Domain.Catalog.ProductDocumentType;
using SettingsType = JOIN.Domain.Catalog.ProductDocumentType;

namespace JOIN.Application.UseCases.Catalog.ProductDocuments;

/// <summary>
/// Same extension → <see cref="DocumentType"/> table as <c>UploadTicketDocumentCommandHandler.TryMapExtension</c>
/// (SPEC 36).
/// </summary>
internal static class ProductDocumentTypeMapper
{
    public static bool TryMapExtension(string extension, out DocumentType documentType)
    {
        documentType = DocumentType.None;
        return extension switch
        {
            ".pdf" => Assign(DocumentType.Pdf, out documentType),
            ".doc" or ".docx" => Assign(DocumentType.Word, out documentType),
            ".xls" or ".xlsx" => Assign(DocumentType.Excel, out documentType),
            ".txt" or ".csv" => Assign(DocumentType.Text, out documentType),
            ".png" or ".jpg" or ".jpeg" => Assign(DocumentType.Image, out documentType),
            ".zip" => Assign(DocumentType.Other, out documentType),
            _ => false
        };

        static bool Assign(DocumentType value, out DocumentType slot)
        {
            slot = value;
            return true;
        }
    }
}

/// <summary><c>POST /Products/{id}/documents</c>: multipart upload with the document type and the display name.</summary>
public sealed record UploadProductDocumentCommand(
    Guid ProductId,
    Guid ProductDocumentTypeId,
    string DisplayName,
    string? Description,
    string OriginalName,
    string ContentType,
    byte[] Content) : ITransactionalCommand<Response<ProductDocumentDto>>;

public sealed class UploadProductDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IFileStorageResolver fileStorageResolver)
    : IRequestHandler<UploadProductDocumentCommand, Response<ProductDocumentDto>>
{
    public async Task<Response<ProductDocumentDto>> Handle(UploadProductDocumentCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<ProductDocumentDto>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var product = await unitOfWork.GetRepository<ProductEntity>().GetAsync(request.ProductId);
        if (product is null || product.CompanyId != companyId || product.GcRecord != 0)
        {
            return Response<ProductDocumentDto>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."]);
        }

        if (!product.IsActive)
        {
            return Response<ProductDocumentDto>.Error(CatalogErrorCodes.ProductNotActive, ["The product is inactive; reactivate it to add documents."]);
        }

        var documentType = await unitOfWork.GetRepository<ProductDocumentTypeEntity>().GetAsync(request.ProductDocumentTypeId);
        if (documentType is null || documentType.CompanyId != companyId || !documentType.IsActive || documentType.GcRecord != 0)
        {
            return Response<ProductDocumentDto>.Error(CatalogErrorCodes.ProductDocumentTypeNotFound, ["Product document type not found or inactive."]);
        }

        var settings = (await unitOfWork.GetRepository<AttachmentSettings>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();
        if (settings is null)
        {
            return Response<ProductDocumentDto>.Error(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured, ["The company has no product attachment settings row yet."]);
        }

        var extension = (Path.GetExtension(request.OriginalName) ?? string.Empty).ToLowerInvariant();
        if (!ProductDocumentTypeMapper.TryMapExtension(extension, out var mappedType))
        {
            return Response<ProductDocumentDto>.Error(
                CatalogErrorCodes.ProductDocumentFileTypeNotAllowed,
                [$"The extension '{extension}' is not supported."]);
        }

        if (!settings.AllowedDocumentTypes.HasFlag(mappedType))
        {
            return Response<ProductDocumentDto>.Error(
                CatalogErrorCodes.ProductDocumentFileTypeNotAllowed,
                [$"The document type '{mappedType}' is not allowed for the current tenant."]);
        }

        if (request.Content.LongLength > settings.MaxDocumentSizeBytes)
        {
            return Response<ProductDocumentDto>.Error(
                CatalogErrorCodes.ProductDocumentTooLarge,
                [$"Document size {request.Content.LongLength} bytes exceeds the maximum allowed ({settings.MaxDocumentSizeBytes} bytes)."]);
        }

        var liveCount = (await unitOfWork.GetRepository<ProductDocumentEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == product.Id && x.GcRecord == 0 && x.DeletedUtc == null)).Count();
        if (liveCount >= settings.MaxDocumentsPerProduct)
        {
            return Response<ProductDocumentDto>.Error(
                CatalogErrorCodes.ProductDocumentLimit,
                [$"The product already has {liveCount} live documents (max {settings.MaxDocumentsPerProduct})."]);
        }

        var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var storageKey = $"products/{companyId}/{product.Id}/documents/{newName}";
        var activeProvider = fileStorageResolver.Current;
        await using var content = new MemoryStream(request.Content);
        var saveResult = await activeProvider.SaveAsync(content, storageKey, request.ContentType, cancellationToken);

        var document = new ProductDocumentEntity
        {
            CompanyId = companyId,
            ProductId = product.Id,
            ProductDocumentTypeId = documentType.Id,
            DisplayName = request.DisplayName.Trim(),
            Description = request.Description?.Trim(),
            DocumentType = mappedType,
            OriginalName = request.OriginalName,
            NewName = newName,
            Path = storageKey,
            StorageProvider = activeProvider.Kind,
            ContentType = request.ContentType,
            SizeBytes = saveResult.SizeBytes,
            UploadedByUserId = currentUserService.UserId is { } id && Guid.TryParse(id, out var u) ? u : Guid.Empty
        };
        await unitOfWork.GetRepository<ProductDocumentEntity>().InsertAsync(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductDocumentDto>
        {
            IsSuccess = true,
            Message = "Product document uploaded successfully.",
            Data = new ProductDocumentDto
            {
                Id = document.Id,
                ProductId = document.ProductId,
                ProductDocumentTypeId = document.ProductDocumentTypeId,
                ProductDocumentTypeName = documentType.Name,
                DisplayName = document.DisplayName,
                Description = document.Description,
                DocumentType = (int)document.DocumentType,
                OriginalName = document.OriginalName,
                NewName = document.NewName,
                ContentType = document.ContentType,
                SizeBytes = document.SizeBytes,
                UploadedByUserId = document.UploadedByUserId,
                CreatedAt = document.Created
            }
        };
    }
}

/// <summary><c>GET /Products/{id}/documents</c>: lists documents of the product.</summary>
public sealed record GetProductDocumentsQuery(Guid ProductId) : IRequest<Response<IReadOnlyList<ProductDocumentDto>>>;

public sealed class GetProductDocumentsQueryHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductDocumentsQuery, Response<IReadOnlyList<ProductDocumentDto>>>
{
    public async Task<Response<IReadOnlyList<ProductDocumentDto>>> Handle(GetProductDocumentsQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<IReadOnlyList<ProductDocumentDto>>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var product = await unitOfWork.GetRepository<ProductEntity>().GetAsync(request.ProductId);
        if (product is null || product.CompanyId != companyId || product.GcRecord != 0)
        {
            return Response<IReadOnlyList<ProductDocumentDto>>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."]);
        }

        var documents = (await unitOfWork.GetRepository<ProductDocumentEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == product.Id && x.GcRecord == 0))
            .OrderByDescending(x => x.Created)
            .ToList();

        var uploaderIds = documents.Select(x => x.UploadedByUserId).Distinct().ToList();
        var uploaders = uploaderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await unitOfWork.GetRepository<Domain.Security.ApplicationUser>()
                .GetAllIncludingDeletedAsync(x => uploaderIds.Contains(x.Id)))
                .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim());

        var typeIds = documents.Select(x => x.ProductDocumentTypeId).Distinct().ToList();
        var types = (await unitOfWork.GetRepository<ProductDocumentTypeEntity>()
            .GetAllIncludingDeletedAsync(x => typeIds.Contains(x.Id)))
            .ToDictionary(x => x.Id, x => x.Name);

        var items = documents.Select(d => new ProductDocumentDto
        {
            Id = d.Id,
            ProductId = d.ProductId,
            ProductDocumentTypeId = d.ProductDocumentTypeId,
            ProductDocumentTypeName = types.TryGetValue(d.ProductDocumentTypeId, out var n) ? n : string.Empty,
            DisplayName = d.DisplayName,
            Description = d.Description,
            DocumentType = (int)d.DocumentType,
            OriginalName = d.OriginalName,
            NewName = d.NewName,
            ContentType = d.ContentType,
            SizeBytes = d.SizeBytes,
            UploadedByUserId = d.UploadedByUserId,
            UploadedByUserName = uploaders.TryGetValue(d.UploadedByUserId, out var u) ? u : string.Empty,
            CreatedAt = d.Created
        }).ToList();

        return new Response<IReadOnlyList<ProductDocumentDto>>
        {
            IsSuccess = true,
            Message = "Product documents retrieved successfully.",
            Data = items
        };
    }
}

/// <summary><c>GET /Products/{id}/documents/{docId}/download</c>: streams the file from the provider.</summary>
public sealed record DownloadProductDocumentQuery(Guid ProductId, Guid DocumentId) : IRequest<Response<ProductDocumentDownloadResult>>;

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
            return Response<ProductDocumentDownloadResult>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        const string sql = """
            SELECT Path, StorageProvider, ContentType, OriginalName
            FROM catalog.productdocuments
            WHERE Id = @Id
              AND ProductId = @ProductId
              AND CompanyId = @CompanyId
              AND GcRecord = 0
              AND DeletedUtc IS NULL;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            sql,
            new { request.DocumentId, request.ProductId, CompanyId = currentUserService.CompanyId },
            cancellationToken: cancellationToken));

        if (row is null)
        {
            return Response<ProductDocumentDownloadResult>.Error(CatalogErrorCodes.ProductDocumentNotFound, ["Product document not found."]);
        }

        var provider = fileStorageResolver.Resolve((StorageProviderKind)row.StorageProvider);
        if (provider is null)
        {
            return Response<ProductDocumentDownloadResult>.Error("STORAGE_PROVIDER_NOT_CONFIGURED", [$"The storage provider '{(StorageProviderKind)row.StorageProvider}' of this document is not configured in the current environment."]);
        }

        var stream = await provider.OpenReadAsync(row.Path, cancellationToken);
        return stream is null
            ? Response<ProductDocumentDownloadResult>.Error("FILE_NOT_FOUND_IN_STORAGE", ["The underlying file is missing from storage."])
            : new Response<ProductDocumentDownloadResult>
            {
                IsSuccess = true,
                Message = "Product document retrieved successfully.",
                Data = new ProductDocumentDownloadResult(stream, row.ContentType, row.OriginalName)
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

/// <summary><c>DELETE /Products/{id}/documents/{docId}</c>: logical delete; the file is preserved.</summary>
public sealed record DeleteProductDocumentCommand(Guid ProductId, Guid DocumentId) : ITransactionalCommand<Response<Guid>>;

public sealed class DeleteProductDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteProductDocumentCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteProductDocumentCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<Guid>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var document = await unitOfWork.GetRepository<ProductDocumentEntity>().GetAsync(request.DocumentId);
        if (document is null
            || document.CompanyId != companyId
            || document.ProductId != request.ProductId
            || document.GcRecord != 0)
        {
            return Response<Guid>.Error(CatalogErrorCodes.ProductDocumentNotFound, ["Document not found on this product in the current tenant."]);
        }

        document.MarkAsDeleted();
        await unitOfWork.GetRepository<ProductDocumentEntity>().UpdateAsync(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Product document deleted successfully.", Data = document.Id };
    }
}

/// <summary><c>POST /Products/{id}/documents/{docId}/restore</c>: SuperAdmin only.</summary>
public sealed record RestoreProductDocumentCommand(Guid ProductId, Guid DocumentId) : ITransactionalCommand<Response<Guid>>;

public sealed class RestoreProductDocumentCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreProductDocumentCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreProductDocumentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(CatalogErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        var result = await restorer.RestoreAsync<ProductDocumentEntity>(
            request.DocumentId,
            requestedCompanyId: null,
            activeDuplicateExists: null,
            cancellationToken: cancellationToken);
        return result.IsSuccess
            ? new Response<Guid> { IsSuccess = true, Message = "Product document restored successfully.", Data = request.DocumentId }
            : result;
    }
}