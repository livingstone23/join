using System.Linq.Expressions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Domain.Audit;
using JOIN.Domain.Enums;
using MediatR;
using AttachmentSettings = JOIN.Domain.Catalog.ProductAttachmentSettings;
using ProductEntity = JOIN.Domain.Catalog.Product;
using ProductImageEntity = JOIN.Domain.Catalog.ProductImage;

namespace JOIN.Application.UseCases.Catalog.ProductImages;

/// <summary>
/// SPEC 70 — invariants around the "one primary image per product" rule (mirrors
/// <c>PersonAddressDefaultCoordinator</c>). Used by upload, mark-primary, delete and restore so every path
/// lands on exactly one live <c>IsPrimary = TRUE</c> row.
/// </summary>
public sealed class ProductImagePrimaryCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>True when at least one live primary image exists for the product.</summary>
    public async Task<bool> HasPrimaryAsync(Guid productId, CancellationToken cancellationToken)
    {
        var live = (await unitOfWork.GetRepository<ProductImageEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == productId && x.GcRecord == 0 && x.DeletedUtc == null && x.IsPrimary))
            .ToList();
        return live.Count > 0;
    }

    /// <summary>Promotes the given image to primary, demoting whatever was primary before.</summary>
    public async Task DemoteAsync(Guid productId, Guid keepPrimaryId, CancellationToken cancellationToken)
    {
        var others = await unitOfWork.GetRepository<ProductImageEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == productId && x.GcRecord == 0 && x.DeletedUtc == null && x.IsPrimary && x.Id != keepPrimaryId);
        foreach (var other in others)
        {
            other.IsPrimary = false;
            await unitOfWork.GetRepository<ProductImageEntity>().UpdateAsync(other);
        }
    }

    /// <summary>Promotes the next live image by <c>SortOrder</c> when the current primary is deleted.</summary>
    public async Task PromoteNextAsync(Guid productId, CancellationToken cancellationToken)
    {
        var next = (await unitOfWork.GetRepository<ProductImageEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == productId && x.GcRecord == 0 && x.DeletedUtc == null))
            .OrderBy(x => x.SortOrder)
            .FirstOrDefault();
        if (next is not null)
        {
            next.IsPrimary = true;
            await unitOfWork.GetRepository<ProductImageEntity>().UpdateAsync(next);
        }
    }
}

/// <summary>Loads the product of the tenant or returns <c>PRODUCT_NOT_FOUND</c>.</summary>
internal static class ProductAccessGuard
{
    public static async Task<(ProductEntity? Product, Response<T>? Error)> ResolveAsync<T>(
        IUnitOfWork unitOfWork, ICurrentUserService currentUserService, Guid productId, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return (null, Response<T>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]));
        }

        var product = await unitOfWork.GetRepository<ProductEntity>().GetAsync(productId);
        if (product is null || product.CompanyId != currentUserService.CompanyId || product.GcRecord != 0)
        {
            return (null, Response<T>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."]));
        }

        return (product, null);
    }
}

/// <summary>Detects the image format of an upload by signature (first bytes), in addition to the extension.</summary>
internal static class ProductImageSignature
{
    public static ProductImageFormat? Detect(ReadOnlySpan<byte> head, string extension)
    {
        // JPEG: FF D8 FF
        if (head.Length >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return ProductImageFormat.Jpeg;
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (head.Length >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47
            && head[4] == 0x0D && head[5] == 0x0A && head[6] == 0x1A && head[7] == 0x0A) return ProductImageFormat.Png;
        // WEBP: "RIFF" .... "WEBP"
        if (head.Length >= 12 && head[0] == 0x52 && head[1] == 0x49 && head[2] == 0x46 && head[3] == 0x46
            && head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50) return ProductImageFormat.Webp;

        // Fallback to extension-only detection for streams whose head cannot be inspected (zero-length files).
        return extension switch
        {
            ".jpg" or ".jpeg" => ProductImageFormat.Jpeg,
            ".png" => ProductImageFormat.Png,
            ".webp" => ProductImageFormat.Webp,
            _ => null
        };
    }
}

/// <summary><c>POST /Products/{id}/images</c>: multipart upload, applies every limit and signature check.</summary>
public sealed record UploadProductImageCommand(
    Guid ProductId,
    string OriginalName,
    string ContentType,
    byte[] Content) : ITransactionalCommand<Response<ProductImageDto>>;

public sealed class UploadProductImageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IFileStorageResolver fileStorageResolver,
    ProductImagePrimaryCoordinator primary)
    : IRequestHandler<UploadProductImageCommand, Response<ProductImageDto>>
{
    public async Task<Response<ProductImageDto>> Handle(UploadProductImageCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<ProductImageDto>(unitOfWork, currentUserService, request.ProductId, cancellationToken);
        if (error is not null) return error;
        if (!product!.IsActive)
        {
            return Response<ProductImageDto>.Error(CatalogErrorCodes.ProductNotActive, ["The product is inactive; reactivate it to add images."]);
        }

        var companyId = product.CompanyId;
        var settings = (await unitOfWork.GetRepository<AttachmentSettings>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();
        if (settings is null)
        {
            return Response<ProductImageDto>.Error(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured, ["The company has no product attachment settings row yet."]);
        }

        var extension = (Path.GetExtension(request.OriginalName) ?? string.Empty).ToLowerInvariant();
        var expectedFormat = extension switch
        {
            ".jpg" or ".jpeg" => ProductImageFormat.Jpeg,
            ".png" => ProductImageFormat.Png,
            ".webp" => ProductImageFormat.Webp,
            _ => (ProductImageFormat?)null
        };
        var headLength = (int)Math.Min(16, request.Content.LongLength);
        var signature = ProductImageSignature.Detect(new ReadOnlySpan<byte>(request.Content, 0, headLength), extension);
        if (expectedFormat is null || signature is null || expectedFormat.Value != signature.Value)
        {
            return Response<ProductImageDto>.Error(
                CatalogErrorCodes.ProductImageFormatNotAllowed,
                [$"The extension '{extension}' does not match the file signature."]);
        }

        if (!settings.AllowedImageFormats.HasFlag(signature.Value))
        {
            return Response<ProductImageDto>.Error(
                CatalogErrorCodes.ProductImageFormatNotAllowed,
                [$"The format '{signature.Value}' is not in the company's allowed image formats."]);
        }

        if (request.Content.LongLength > settings.MaxImageSizeBytes)
        {
            return Response<ProductImageDto>.Error(
                CatalogErrorCodes.ProductImageTooLarge,
                [$"Image size {request.Content.LongLength} bytes exceeds the maximum allowed ({settings.MaxImageSizeBytes} bytes)."]);
        }

        var liveCount = (await unitOfWork.GetRepository<ProductImageEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == product.Id && x.GcRecord == 0 && x.DeletedUtc == null)).Count();
        if (liveCount >= settings.MaxImagesPerProduct)
        {
            return Response<ProductImageDto>.Error(
                CatalogErrorCodes.ProductImageLimit,
                [$"The product already has {liveCount} live images (max {settings.MaxImagesPerProduct})."]);
        }

        var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var storageKey = $"products/{companyId}/{product.Id}/images/{newName}";
        var activeProvider = fileStorageResolver.Current;
        await using var content = new MemoryStream(request.Content);
        var saveResult = await activeProvider.SaveAsync(content, storageKey, request.ContentType, cancellationToken);

        int nextSortOrder;
        if (liveCount == 0)
        {
            nextSortOrder = 1;
        }
        else
        {
            var existing = await unitOfWork.GetRepository<ProductImageEntity>()
                .GetAllIncludingDeletedAsync(x => x.ProductId == product.Id && x.GcRecord == 0 && x.DeletedUtc == null);
            nextSortOrder = existing.Max(x => x.SortOrder) + 1;
        }
        var isFirst = !await primary.HasPrimaryAsync(product.Id, cancellationToken);

        var image = new ProductImageEntity
        {
            CompanyId = companyId,
            ProductId = product.Id,
            OriginalName = request.OriginalName,
            NewName = newName,
            Path = storageKey,
            StorageProvider = activeProvider.Kind,
            ContentType = request.ContentType,
            SizeBytes = saveResult.SizeBytes,
            SortOrder = nextSortOrder,
            IsPrimary = isFirst,
            UploadedByUserId = currentUserService.UserId is { } id && Guid.TryParse(id, out var u) ? u : Guid.Empty
        };
        await unitOfWork.GetRepository<ProductImageEntity>().InsertAsync(image);
        if (isFirst)
        {
            await primary.DemoteAsync(product.Id, image.Id, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductImageDto>
        {
            IsSuccess = true,
            Message = "Product image uploaded successfully.",
            Data = new ProductImageDto
            {
                Id = image.Id,
                ProductId = image.ProductId,
                OriginalName = image.OriginalName,
                NewName = image.NewName,
                ContentType = image.ContentType,
                SizeBytes = image.SizeBytes,
                SortOrder = image.SortOrder,
                IsPrimary = image.IsPrimary,
                UploadedByUserId = image.UploadedByUserId,
                CreatedAt = image.Created
            }
        };
    }
}

/// <summary><c>PUT /Products/{id}/images/order</c>: must contain exactly the live images of the product.</summary>
public sealed record ReorderProductImagesCommand(Guid ProductId, IReadOnlyList<Guid> ImageIds) : ITransactionalCommand<Response<bool>>;

public sealed class ReorderProductImagesCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<ReorderProductImagesCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(ReorderProductImagesCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<bool>(unitOfWork, currentUserService, request.ProductId, cancellationToken);
        if (error is not null) return error;

        var liveIds = (await unitOfWork.GetRepository<ProductImageEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == product!.Id && x.GcRecord == 0 && x.DeletedUtc == null))
            .Select(x => x.Id)
            .OrderBy(x => x)
            .ToList();
        var requestedIds = request.ImageIds.OrderBy(x => x).ToList();
        if (!liveIds.SequenceEqual(requestedIds))
        {
            return Response<bool>.Error(CatalogErrorCodes.InvalidProductImageOrder,
                ["The reorder list must contain exactly the live image ids of the product."]);
        }

        var images = (await unitOfWork.GetRepository<ProductImageEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == product.Id && x.GcRecord == 0 && x.DeletedUtc == null)).ToList();
        var order = 1;
        foreach (var id in request.ImageIds)
        {
            var image = images.First(x => x.Id == id);
            image.SortOrder = order++;
            await unitOfWork.GetRepository<ProductImageEntity>().UpdateAsync(image);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<bool> { IsSuccess = true, Message = "Product images reordered successfully.", Data = true };
    }
}

/// <summary><c>PUT /Products/{id}/images/{imageId}/primary</c>: demotes the previous primary.</summary>
public sealed record SetPrimaryProductImageCommand(Guid ProductId, Guid ImageId) : ITransactionalCommand<Response<bool>>;

public sealed class SetPrimaryProductImageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ProductImagePrimaryCoordinator primary)
    : IRequestHandler<SetPrimaryProductImageCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(SetPrimaryProductImageCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<bool>(unitOfWork, currentUserService, request.ProductId, cancellationToken);
        if (error is not null) return error;

        var image = await unitOfWork.GetRepository<ProductImageEntity>().GetAsync(request.ImageId);
        if (image is null || image.ProductId != product!.Id || image.GcRecord != 0 || image.DeletedUtc is not null)
        {
            return Response<bool>.Error(CatalogErrorCodes.ProductImageNotFound, ["Image not found on this product."]);
        }

        await primary.DemoteAsync(product.Id, image.Id, cancellationToken);
        image.IsPrimary = true;
        await unitOfWork.GetRepository<ProductImageEntity>().UpdateAsync(image);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<bool> { IsSuccess = true, Message = "Primary image updated successfully.", Data = true };
    }
}

/// <summary><c>DELETE /Products/{id}/images/{imageId}</c>: logical delete; promotes the next live image if it was primary.</summary>
public sealed record DeleteProductImageCommand(Guid ProductId, Guid ImageId) : ITransactionalCommand<Response<Guid>>;

public sealed class DeleteProductImageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ProductImagePrimaryCoordinator primary)
    : IRequestHandler<DeleteProductImageCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteProductImageCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<Guid>(unitOfWork, currentUserService, request.ProductId, cancellationToken);
        if (error is not null) return error;

        var image = await unitOfWork.GetRepository<ProductImageEntity>().GetAsync(request.ImageId);
        if (image is null || image.ProductId != product!.Id || image.GcRecord != 0)
        {
            return Response<Guid>.Error(CatalogErrorCodes.ProductImageNotFound, ["Image not found on this product."]);
        }

        image.MarkAsDeleted();
        await unitOfWork.GetRepository<ProductImageEntity>().UpdateAsync(image);

        if (image.IsPrimary)
        {
            await primary.PromoteNextAsync(product.Id, cancellationToken);
        }
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Product image deleted successfully.", Data = image.Id };
    }
}

/// <summary><c>POST /Products/{id}/images/{imageId}/restore</c>: SuperAdmin only, restores a deleted image.</summary>
public sealed record RestoreProductImageCommand(Guid ProductId, Guid ImageId) : ITransactionalCommand<Response<Guid>>;

public sealed class RestoreProductImageCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreProductImageCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreProductImageCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(CatalogErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        var result = await restorer.RestoreAsync<ProductImageEntity>(
            request.ImageId,
            requestedCompanyId: null,
            activeDuplicateExists: null,
            cancellationToken: cancellationToken);

        return result.IsSuccess
            ? new Response<Guid> { IsSuccess = true, Message = "Product image restored successfully.", Data = request.ImageId }
            : result;
    }
}

/// <summary><c>GET /Products/{id}/images</c>: ordered list of live images.</summary>
public sealed record GetProductImagesQuery(Guid ProductId) : IRequest<Response<IReadOnlyList<ProductImageDto>>>;

public sealed class GetProductImagesQueryHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductImagesQuery, Response<IReadOnlyList<ProductImageDto>>>
{
    public async Task<Response<IReadOnlyList<ProductImageDto>>> Handle(GetProductImagesQuery request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<IReadOnlyList<ProductImageDto>>(unitOfWork, currentUserService, request.ProductId, cancellationToken);
        if (error is not null) return error;

        var images = (await unitOfWork.GetRepository<ProductImageEntity>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == product!.Id && x.GcRecord == 0 && x.DeletedUtc == null))
            .OrderBy(x => x.SortOrder)
            .ToList();

        var uploaderIds = images.Select(x => x.UploadedByUserId).Distinct().ToList();
        var uploaders = uploaderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await unitOfWork.GetRepository<Domain.Security.ApplicationUser>()
                .GetAllIncludingDeletedAsync(x => uploaderIds.Contains(x.Id)))
                .ToDictionary(x => x.Id, x => $"{x.FirstName} {x.LastName}".Trim());

        var items = images.Select(i => new ProductImageDto
        {
            Id = i.Id,
            ProductId = i.ProductId,
            OriginalName = i.OriginalName,
            NewName = i.NewName,
            ContentType = i.ContentType,
            SizeBytes = i.SizeBytes,
            SortOrder = i.SortOrder,
            IsPrimary = i.IsPrimary,
            UploadedByUserId = i.UploadedByUserId,
            UploadedByUserName = uploaders.TryGetValue(i.UploadedByUserId, out var name) ? name : string.Empty,
            CreatedAt = i.Created
        }).ToList();

        return new Response<IReadOnlyList<ProductImageDto>>
        {
            IsSuccess = true,
            Message = "Product images retrieved successfully.",
            Data = items
        };
    }
}