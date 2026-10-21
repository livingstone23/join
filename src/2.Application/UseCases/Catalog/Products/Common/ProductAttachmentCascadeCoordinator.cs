using System.Linq.Expressions;
using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Audit;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UseCases.Catalog.Products.Common;

/// <summary>
/// SPEC 70 — soft-delete cascade of a product's images and documents, mirroring
/// <c>PersonCascadeCoordinator</c>. Deleting a product marks every live image and document as deleted with
/// the same <c>GcRecord</c> stamp; restoring brings back the ones carrying that stamp. Files stay in storage.
/// Rows are found untracked and updated through their tracked instance (identity-resolved re-read).
/// </summary>
public sealed class ProductAttachmentCascadeCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>Marks the live images and documents of <paramref name="productId"/> as deleted with <paramref name="stamp"/>.</summary>
    public async Task MarkChildrenAsDeletedAsync(Guid productId, int stamp)
    {
        await MarkAsync<ProductImage>(x => x.ProductId == productId && x.GcRecord == 0, stamp);
        await MarkAsync<ProductDocument>(x => x.ProductId == productId && x.GcRecord == 0, stamp);
    }

    /// <summary>Restores the images and documents deleted in the same cascade as the product (<c>GcRecord == stamp</c>).</summary>
    public async Task<Response<Guid>?> RestoreChildrenAsync(Guid productId, int stamp)
    {
        await RestoreAsync<ProductImage>(x => x.ProductId == productId && x.GcRecord == stamp);
        await RestoreAsync<ProductDocument>(x => x.ProductId == productId && x.GcRecord == stamp);
        return null;
    }

    private async Task MarkAsync<T>(Expression<Func<T, bool>> predicate, int stamp)
        where T : BaseAuditableEntity
    {
        var repository = unitOfWork.GetRepository<T>();
        foreach (var id in (await repository.GetAllIncludingDeletedAsync(predicate)).Select(x => x.Id).ToList())
        {
            var row = (await repository.GetIncludingDeletedAsync(id))!;
            // Same stamp as the parent so the restore can pair them.
            row.GcRecord = stamp;
            await repository.UpdateAsync(row);
        }
    }

    private async Task RestoreAsync<T>(Expression<Func<T, bool>> predicate)
        where T : BaseAuditableEntity
    {
        var repository = unitOfWork.GetRepository<T>();
        foreach (var id in (await repository.GetAllIncludingDeletedAsync(predicate)).Select(x => x.Id).ToList())
        {
            var row = (await repository.GetIncludingDeletedAsync(id))!;
            row.Restore();
            await repository.UpdateAsync(row);
        }
    }
}
