using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.Products.Common;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;
using JOIN.Domain.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.Products.Documents.Commands;

/// <summary>
/// Handles <see cref="UploadProductDocumentCommand"/>. Checks, in the order of SPEC 70 B.10: product of the company
/// and active, document type active, limits configured, file type by extension (the SPEC 36 table of
/// <see cref="UploadTicketDocumentCommandHandler.TryMapExtension"/>), size and count per product.
/// </summary>
public sealed class UploadProductDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IFileStorageResolver fileStorageResolver)
    : IRequestHandler<UploadProductDocumentCommand, Response<ProductDocumentDto>>
{
    public async Task<Response<ProductDocumentDto>> Handle(UploadProductDocumentCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveActiveAsync<ProductDocumentDto>(unitOfWork, currentUserService, request.ProductId);
        if (error is not null)
        {
            return error;
        }

        var documentType = await unitOfWork.GetRepository<ProductDocumentType>().GetAsync(request.ProductDocumentTypeId);
        if (documentType is null || documentType.CompanyId != product!.CompanyId || !documentType.IsActive || documentType.GcRecord != 0)
        {
            return Response<ProductDocumentDto>.Error(CatalogErrorCodes.ProductDocumentTypeNotFound, ["Product document type not found or inactive."]);
        }

        var settings = await ProductAccessGuard.GetSettingsAsync(unitOfWork, product.CompanyId);
        if (settings is null)
        {
            return ProductAccessGuard.SettingsNotConfigured<ProductDocumentDto>();
        }

        var extension = Path.GetExtension(request.OriginalName).ToLowerInvariant();
        if (!UploadTicketDocumentCommandHandler.TryMapExtension(extension, out var fileType) || !settings.AllowedDocumentTypes.HasFlag(fileType))
        {
            return Response<ProductDocumentDto>.Error(
                CatalogErrorCodes.ProductDocumentFileTypeNotAllowed,
                [$"The extension '{extension}' is not allowed for product documents of this company."]);
        }

        if (request.Content.LongLength > settings.MaxDocumentSizeBytes)
        {
            return Response<ProductDocumentDto>.Error(
                CatalogErrorCodes.ProductDocumentTooLarge,
                [$"Document size {request.Content.LongLength} bytes exceeds the maximum allowed ({settings.MaxDocumentSizeBytes} bytes)."]);
        }

        var repository = unitOfWork.GetRepository<ProductDocument>();
        var liveCount = (await repository.GetAllIncludingDeletedAsync(x => x.ProductId == product.Id && x.GcRecord == 0)).Count();
        if (liveCount >= settings.MaxDocumentsPerProduct)
        {
            return Response<ProductDocumentDto>.Error(
                CatalogErrorCodes.ProductDocumentLimit,
                [$"The product already has {liveCount} live documents (max {settings.MaxDocumentsPerProduct})."]);
        }

        var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var storageKey = $"products/{product.CompanyId}/{product.Id}/documents/{newName}";
        var provider = fileStorageResolver.Current;
        await using var content = new MemoryStream(request.Content);
        var saved = await provider.SaveAsync(content, storageKey, request.ContentType, cancellationToken);

        var document = new ProductDocument
        {
            CompanyId = product.CompanyId,
            ProductId = product.Id,
            ProductDocumentTypeId = documentType.Id,
            DisplayName = request.DisplayName.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            DocumentType = fileType,
            OriginalName = request.OriginalName,
            NewName = newName,
            Path = storageKey,
            StorageProvider = provider.Kind,
            ContentType = request.ContentType,
            SizeBytes = saved.SizeBytes,
            UploadedByUserId = ProductAccessGuard.ActorUserId(currentUserService)
        };
        await repository.InsertAsync(document);
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

/// <summary>Handles <see cref="DeleteProductDocumentCommand"/>.</summary>
public sealed class DeleteProductDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteProductDocumentCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteProductDocumentCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<Guid>(unitOfWork, currentUserService, request.ProductId);
        if (error is not null)
        {
            return error;
        }

        var repository = unitOfWork.GetRepository<ProductDocument>();
        var document = await repository.GetAsync(request.DocumentId);
        if (document is null || document.ProductId != product!.Id || document.CompanyId != product.CompanyId || document.GcRecord != 0)
        {
            return ProductDocumentRules.NotFound<Guid>();
        }

        document.MarkAsDeleted();
        await repository.UpdateAsync(document);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Product document deleted successfully.", Data = document.Id };
    }
}

/// <summary>
/// Handles <see cref="RestoreProductDocumentCommand"/> with <see cref="SoftDeleteRestorer"/>: not while the product
/// is deleted and not past <c>MaxDocumentsPerProduct</c>.
/// </summary>
public sealed class RestoreProductDocumentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreProductDocumentCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreProductDocumentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAccessGuard.CompanyRequired<Guid>();
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(CatalogErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        var repository = unitOfWork.GetRepository<ProductDocument>();
        var document = await repository.GetIncludingDeletedAsync(request.DocumentId);
        if (document is null || document.CompanyId != currentUserService.CompanyId || document.ProductId != request.ProductId)
        {
            return ProductDocumentRules.NotFound<Guid>();
        }

        if (document.IsDeleted)
        {
            var settings = await ProductAccessGuard.GetSettingsAsync(unitOfWork, document.CompanyId);
            var liveCount = (await repository.GetAllIncludingDeletedAsync(x => x.ProductId == document.ProductId && x.GcRecord == 0)).Count();
            if (settings is not null && liveCount >= settings.MaxDocumentsPerProduct)
            {
                return Response<Guid>.Error(
                    CatalogErrorCodes.ProductDocumentLimit,
                    [$"The product already has {liveCount} live documents (max {settings.MaxDocumentsPerProduct}); delete one before restoring."]);
            }
        }

        return await restorer.RestoreAsync<ProductDocument>(
            request.DocumentId,
            requestedCompanyId: null,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<Product>(entity.ProductId),
            cancellationToken: cancellationToken);
    }
}

internal static class ProductDocumentRules
{
    public static Response<T> NotFound<T>() =>
        Response<T>.Error(CatalogErrorCodes.ProductDocumentNotFound, ["Document not found on this product."]);
}
