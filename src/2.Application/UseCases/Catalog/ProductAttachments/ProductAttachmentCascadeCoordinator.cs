using System.Linq.Expressions;
using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Audit;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UseCases.Catalog.ProductAttachments;

/// <summary>
/// SPEC 70 — soft-delete cascade of a product's images and documents, mirroring
/// <c>PersonCascadeCoordinator</c>. Deleting a product marks every live image and document as deleted with
/// the same <c>GcRecord</c> stamp; restoring brings them back. Files stay on disk.
/// </summary>
public sealed class ProductAttachmentCascadeCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>Marks the live images and documents of <paramref name="productId"/> as deleted with the same stamp as the product.</summary>
    public async Task MarkChildrenAsDeletedAsync(Guid productId, int stamp, CancellationToken cancellationToken)
    {
        await MarkAsync<ProductImage>(x => x.ProductId == productId && x.GcRecord == 0, stamp, cancellationToken);
        await MarkAsync<ProductDocument>(x => x.ProductId == productId && x.GcRecord == 0, stamp, cancellationToken);
    }

    /// <summary>Restores the images and documents deleted in the same cascade as the product (matched by <c>GcRecord == stamp</c>).</summary>
    public async Task<Response<Guid>?> RestoreChildrenAsync(Guid productId, int stamp, CancellationToken cancellationToken)
    {
        var images = await unitOfWork.GetRepository<ProductImage>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == productId && x.GcRecord == stamp);
        foreach (var image in images)
        {
            image.Restore();
            await unitOfWork.GetRepository<ProductImage>().UpdateAsync(image);
        }

        var documents = await unitOfWork.GetRepository<ProductDocument>()
            .GetAllIncludingDeletedAsync(x => x.ProductId == productId && x.GcRecord == stamp);
        foreach (var document in documents)
        {
            document.Restore();
            await unitOfWork.GetRepository<ProductDocument>().UpdateAsync(document);
        }

        return null;
    }

    private async Task MarkAsync<T>(Expression<Func<T, bool>> predicate, int stamp, CancellationToken cancellationToken)
        where T : BaseAuditableEntity
    {
        var repository = unitOfWork.GetRepository<T>();
        foreach (var row in await repository.GetAllIncludingDeletedAsync(predicate))
        {
            row.MarkAsDeleted();
            // Force the GcRecord stamp to match the parent's so the restore can pair them.
            row.GcRecord = stamp;
            await repository.UpdateAsync(row);
        }
    }
}