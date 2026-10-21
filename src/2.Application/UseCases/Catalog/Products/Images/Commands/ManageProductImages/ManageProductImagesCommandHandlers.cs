using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.Products.Common;
using JOIN.Domain.Catalog;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.Products.Images.Commands;

/// <summary>
/// Handles <see cref="UploadProductImageCommand"/>. Checks, in the order of SPEC 70 B.7: product of the company,
/// product active, limits configured, format by extension and by signature, size and count. The first live image
/// becomes the primary one.
/// </summary>
public sealed class UploadProductImageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IFileStorageResolver fileStorageResolver,
    ProductImagePrimaryCoordinator primary)
    : IRequestHandler<UploadProductImageCommand, Response<ProductImageDto>>
{
    public async Task<Response<ProductImageDto>> Handle(UploadProductImageCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveActiveAsync<ProductImageDto>(unitOfWork, currentUserService, request.ProductId);
        if (error is not null)
        {
            return error;
        }

        var settings = await ProductAccessGuard.GetSettingsAsync(unitOfWork, product!.CompanyId);
        if (settings is null)
        {
            return ProductAccessGuard.SettingsNotConfigured<ProductImageDto>();
        }

        var extension = Path.GetExtension(request.OriginalName).ToLowerInvariant();
        var claimed = ProductImageInspector.FromExtension(extension);
        var actual = ProductImageInspector.FromSignature(request.Content);
        if (claimed is null || actual is null || claimed != actual)
        {
            return Response<ProductImageDto>.Error(
                CatalogErrorCodes.ProductImageFormatNotAllowed,
                [$"The extension '{extension}' is not a supported image format or does not match the file content."]);
        }

        if (!settings.AllowedImageFormats.HasFlag(actual.Value))
        {
            return Response<ProductImageDto>.Error(
                CatalogErrorCodes.ProductImageFormatNotAllowed,
                [$"The format '{actual.Value}' is not in the company's allowed image formats."]);
        }

        if (request.Content.LongLength > settings.MaxImageSizeBytes)
        {
            return Response<ProductImageDto>.Error(
                CatalogErrorCodes.ProductImageTooLarge,
                [$"Image size {request.Content.LongLength} bytes exceeds the maximum allowed ({settings.MaxImageSizeBytes} bytes)."]);
        }

        var live = await primary.GetLiveAsync(product.Id);
        if (live.Count >= settings.MaxImagesPerProduct)
        {
            return Response<ProductImageDto>.Error(
                CatalogErrorCodes.ProductImageLimit,
                [$"The product already has {live.Count} live images (max {settings.MaxImagesPerProduct})."]);
        }

        var newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}";
        var storageKey = $"products/{product.CompanyId}/{product.Id}/images/{newName}";
        var provider = fileStorageResolver.Current;
        await using var content = new MemoryStream(request.Content);
        var saved = await provider.SaveAsync(content, storageKey, ProductImageContentType(actual.Value), cancellationToken);
        var (width, height) = ProductImageInspector.ReadDimensions(actual.Value, request.Content);

        var image = new ProductImage
        {
            CompanyId = product.CompanyId,
            ProductId = product.Id,
            OriginalName = request.OriginalName,
            NewName = newName,
            Path = storageKey,
            StorageProvider = provider.Kind,
            // Fixed by the server from the signature, never taken from the client (inline rendering, SPEC 70 risks).
            ContentType = ProductImageContentType(actual.Value),
            SizeBytes = saved.SizeBytes,
            Width = width,
            Height = height,
            SortOrder = live.Count == 0 ? 1 : live.Max(x => x.SortOrder) + 1,
            IsPrimary = !live.Any(x => x.IsPrimary),
            UploadedByUserId = ProductAccessGuard.ActorUserId(currentUserService)
        };
        await unitOfWork.GetRepository<ProductImage>().InsertAsync(image);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductImageDto>
        {
            IsSuccess = true,
            Message = "Product image uploaded successfully.",
            Data = ProductImageRules.ToDto(image)
        };
    }

    internal static string ProductImageContentType(ProductImageFormat format) => format switch
    {
        ProductImageFormat.Png => "image/png",
        ProductImageFormat.Webp => "image/webp",
        _ => "image/jpeg"
    };
}

/// <summary>Handles <see cref="ReorderProductImagesCommand"/>: the list must hold exactly the live images; assigns 1..n.</summary>
public sealed class ReorderProductImagesCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ProductImagePrimaryCoordinator primary)
    : IRequestHandler<ReorderProductImagesCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(ReorderProductImagesCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<bool>(unitOfWork, currentUserService, request.ProductId);
        if (error is not null)
        {
            return error;
        }

        var images = await primary.GetLiveAsync(product!.Id);
        if (images.Count != request.ImageIds.Count || !images.Select(x => x.Id).ToHashSet().SetEquals(request.ImageIds))
        {
            return Response<bool>.Error(
                CatalogErrorCodes.InvalidProductImageOrder,
                ["The reorder list must contain exactly the live image ids of the product."]);
        }

        var repository = unitOfWork.GetRepository<ProductImage>();
        var order = 1;
        foreach (var id in request.ImageIds)
        {
            // Tracked re-read: the live list above is untracked.
            var image = (await repository.GetIncludingDeletedAsync(id))!;
            image.SortOrder = order++;
            await repository.UpdateAsync(image);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<bool> { IsSuccess = true, Message = "Product images reordered successfully.", Data = true };
    }
}

/// <summary>Handles <see cref="SetPrimaryProductImageCommand"/> through <see cref="ProductImagePrimaryCoordinator"/>.</summary>
public sealed class SetPrimaryProductImageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ProductImagePrimaryCoordinator primary)
    : IRequestHandler<SetPrimaryProductImageCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(SetPrimaryProductImageCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<bool>(unitOfWork, currentUserService, request.ProductId);
        if (error is not null)
        {
            return error;
        }

        var image = await ProductImageRules.FindLiveAsync(unitOfWork, product!, request.ImageId);
        if (image is null)
        {
            return ProductImageRules.NotFound<bool>();
        }

        if (!image.IsPrimary)
        {
            await primary.MakePrimaryAsync(image, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new Response<bool> { IsSuccess = true, Message = "Primary image updated successfully.", Data = true };
    }
}

/// <summary>Handles <see cref="DeleteProductImageCommand"/>: deleting the primary promotes the next one by <c>SortOrder</c>.</summary>
public sealed class DeleteProductImageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ProductImagePrimaryCoordinator primary)
    : IRequestHandler<DeleteProductImageCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteProductImageCommand request, CancellationToken cancellationToken)
    {
        var (product, error) = await ProductAccessGuard.ResolveAsync<Guid>(unitOfWork, currentUserService, request.ProductId);
        if (error is not null)
        {
            return error;
        }

        var image = await ProductImageRules.FindLiveAsync(unitOfWork, product!, request.ImageId);
        if (image is null)
        {
            return ProductImageRules.NotFound<Guid>();
        }

        image.MarkAsDeleted();
        await unitOfWork.GetRepository<ProductImage>().UpdateAsync(image);
        // Saved first: the deleted row leaves the filtered unique index before the next one is promoted.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (image.IsPrimary)
        {
            await primary.PromoteNextAsync(product!.Id);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new Response<Guid> { IsSuccess = true, Message = "Product image deleted successfully.", Data = image.Id };
    }
}

/// <summary>
/// Handles <see cref="RestoreProductImageCommand"/> with <see cref="SoftDeleteRestorer"/>: not while the product is
/// deleted, not past the image limit, and the primary flag is settled by <see cref="ProductImagePrimaryCoordinator"/>.
/// </summary>
public sealed class RestoreProductImageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer,
    ProductImagePrimaryCoordinator primary)
    : IRequestHandler<RestoreProductImageCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreProductImageCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductAccessGuard.CompanyRequired<Guid>();
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(CatalogErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        var image = await unitOfWork.GetRepository<ProductImage>().GetIncludingDeletedAsync(request.ImageId);
        if (image is null || image.CompanyId != currentUserService.CompanyId || image.ProductId != request.ProductId)
        {
            return ProductImageRules.NotFound<Guid>();
        }

        if (image.IsDeleted)
        {
            var settings = await ProductAccessGuard.GetSettingsAsync(unitOfWork, image.CompanyId);
            var max = settings?.MaxImagesPerProduct ?? ProductImage.MaxImagesPerProduct;
            var liveCount = (await primary.GetLiveAsync(image.ProductId)).Count;
            if (liveCount >= max)
            {
                return Response<Guid>.Error(
                    CatalogErrorCodes.ProductImageLimit,
                    [$"The product already has {liveCount} live images (max {max}); delete one before restoring."]);
            }
        }

        return await restorer.RestoreAsync<ProductImage>(
            request.ImageId,
            requestedCompanyId: null,
            isParentDeleted: (entity, _) => restorer.IsParentDeletedAsync<Product>(entity.ProductId),
            beforeRestore: (entity, _) => primary.PrepareRestoreAsync(entity),
            cancellationToken: cancellationToken);
    }
}

/// <summary>Lookups and mapping shared by the image commands.</summary>
internal static class ProductImageRules
{
    public static async Task<ProductImage?> FindLiveAsync(IUnitOfWork unitOfWork, Product product, Guid imageId)
    {
        var image = await unitOfWork.GetRepository<ProductImage>().GetAsync(imageId);
        return image is null || image.ProductId != product.Id || image.CompanyId != product.CompanyId || image.GcRecord != 0
            ? null
            : image;
    }

    public static Response<T> NotFound<T>() =>
        Response<T>.Error(CatalogErrorCodes.ProductImageNotFound, ["Image not found on this product."]);

    public static ProductImageDto ToDto(ProductImage image) => new()
    {
        Id = image.Id,
        ProductId = image.ProductId,
        OriginalName = image.OriginalName,
        NewName = image.NewName,
        ContentType = image.ContentType,
        SizeBytes = image.SizeBytes,
        Width = image.Width,
        Height = image.Height,
        SortOrder = image.SortOrder,
        IsPrimary = image.IsPrimary,
        UploadedByUserId = image.UploadedByUserId,
        CreatedAt = image.Created
    };
}
