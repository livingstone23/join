using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Domain.Catalog;
using AttachmentSettings = JOIN.Domain.Catalog.ProductAttachmentSettings;

namespace JOIN.Application.UseCases.Catalog.Products.Common;

/// <summary>
/// SPEC 70 — resolves the live product of the current tenant for the image and document use cases, or the
/// error response the handler returns as is (<c>COMPANY_REQUIRED</c> or <c>PRODUCT_NOT_FOUND</c>).
/// </summary>
internal static class ProductAccessGuard
{
    public static async Task<(Product? Product, Response<T>? Error)> ResolveAsync<T>(
        IUnitOfWork unitOfWork, ICurrentUserService currentUserService, Guid productId)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return (null, CompanyRequired<T>());
        }

        var product = await unitOfWork.GetRepository<Product>().GetAsync(productId);
        if (product is null || product.CompanyId != currentUserService.CompanyId || product.GcRecord != 0)
        {
            return (null, Response<T>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."]));
        }

        return (product, null);
    }

    /// <summary>Same as <see cref="ResolveAsync{T}"/> and also rejects inactive products (rule 7: no new uploads).</summary>
    public static async Task<(Product? Product, Response<T>? Error)> ResolveActiveAsync<T>(
        IUnitOfWork unitOfWork, ICurrentUserService currentUserService, Guid productId)
    {
        var (product, error) = await ResolveAsync<T>(unitOfWork, currentUserService, productId);
        if (error is not null)
        {
            return (null, error);
        }

        return product!.IsActive
            ? (product, null)
            : (null, Response<T>.Error(CatalogErrorCodes.ProductNotActive, ["The product is inactive; reactivate it to add attachments."]));
    }

    /// <summary>The attachment limits of the company, or <c>null</c> when the row is missing.</summary>
    public static async Task<AttachmentSettings?> GetSettingsAsync(IUnitOfWork unitOfWork, Guid companyId) =>
        (await unitOfWork.GetRepository<AttachmentSettings>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();

    public static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    public static Response<T> SettingsNotConfigured<T>() =>
        Response<T>.Error(CatalogErrorCodes.ProductAttachmentSettingsNotConfigured, ["The company has no product attachment settings row yet."]);

    /// <summary>The uploader id from the token; <see cref="Guid.Empty"/> when the claim is missing.</summary>
    public static Guid ActorUserId(ICurrentUserService currentUserService) =>
        Guid.TryParse(currentUserService.UserId, out var userId) ? userId : Guid.Empty;
}
