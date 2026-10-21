using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UseCases.Catalog.Products.Images.Commands;

/// <summary>
/// SPEC 70 — invariant "one live primary image per product" (pattern of <c>PersonAddressDefaultCoordinator</c>),
/// backed by the filtered unique index <c>UX_ProductImages_OnePrimary</c>. Used by upload, mark-primary, delete
/// and restore. PostgreSQL checks that index per statement, so a demotion is saved before the promotion that
/// follows it: two rows never hold <c>IsPrimary = TRUE</c> at the same time, whatever order EF sends the updates.
/// </summary>
public sealed class ProductImagePrimaryCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>The live images of the product, ordered by <c>SortOrder</c> (untracked: read-only).</summary>
    public async Task<List<ProductImage>> GetLiveAsync(Guid productId) =>
        (await unitOfWork.GetRepository<ProductImage>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == productId && x.GcRecord == 0))
            .OrderBy(x => x.SortOrder)
            .ToList();

    /// <summary>Makes <paramref name="image"/> the only primary: demotes the others and saves, then promotes it (the caller saves).</summary>
    public async Task MakePrimaryAsync(ProductImage image, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<ProductImage>();
        var others = (await GetLiveAsync(image.ProductId)).Where(x => x.IsPrimary && x.Id != image.Id).ToList();
        if (others.Count > 0)
        {
            foreach (var other in others)
            {
                var tracked = await TrackedAsync(other.Id);
                tracked.IsPrimary = false;
                await repository.UpdateAsync(tracked);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        image.IsPrimary = true;
        await repository.UpdateAsync(image);
    }

    /// <summary>
    /// After the primary was deleted (and saved): promotes the next live image by <c>SortOrder</c>, if any.
    /// The caller saves.
    /// </summary>
    public async Task PromoteNextAsync(Guid productId)
    {
        var live = await GetLiveAsync(productId);
        if (live.Count == 0 || live.Any(x => x.IsPrimary))
        {
            return;
        }

        var next = await TrackedAsync(live[0].Id);
        next.IsPrimary = true;
        await unitOfWork.GetRepository<ProductImage>().UpdateAsync(next);
    }

    /// <summary>
    /// Before an image is restored: it goes to the end of the order and keeps the primary flag only when no
    /// other live image holds it; when no live image is primary, the restored one becomes primary.
    /// </summary>
    public async Task PrepareRestoreAsync(ProductImage image)
    {
        var live = (await GetLiveAsync(image.ProductId)).Where(x => x.Id != image.Id).ToList();
        image.IsPrimary = !live.Any(x => x.IsPrimary);
        image.SortOrder = live.Count == 0 ? 1 : live.Max(x => x.SortOrder) + 1;
        await unitOfWork.GetRepository<ProductImage>().UpdateAsync(image);
    }

    /// <summary>The tracked instance of a row (identity-resolved), so an update never attaches a second copy.</summary>
    private async Task<ProductImage> TrackedAsync(Guid id) =>
        (await unitOfWork.GetRepository<ProductImage>().GetIncludingDeletedAsync(id))!;
}
