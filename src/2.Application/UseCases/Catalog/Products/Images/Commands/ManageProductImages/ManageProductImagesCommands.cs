using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;

namespace JOIN.Application.UseCases.Catalog.Products.Images.Commands;

/// <summary><c>POST /Products/{id}/images</c> (SPEC 70): multipart upload of one image to an existing product.</summary>
public sealed record UploadProductImageCommand(
    Guid ProductId,
    string OriginalName,
    string ContentType,
    byte[] Content) : ITransactionalCommand<Response<ProductImageDto>>;

/// <summary><c>PUT /Products/{id}/images/order</c> (SPEC 70): the live image ids of the product in their new order.</summary>
public sealed record ReorderProductImagesCommand : ITransactionalCommand<Response<bool>>
{
    public Guid ProductId { get; init; }
    public IReadOnlyList<Guid> ImageIds { get; init; } = [];
}

/// <summary><c>PUT /Products/{id}/images/{imageId}/primary</c> (SPEC 70).</summary>
public sealed record SetPrimaryProductImageCommand(Guid ProductId, Guid ImageId) : ITransactionalCommand<Response<bool>>;

/// <summary><c>DELETE /Products/{id}/images/{imageId}</c> (SPEC 70): logical delete; the file stays in storage.</summary>
public sealed record DeleteProductImageCommand(Guid ProductId, Guid ImageId) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /Products/{id}/images/{imageId}/restore</c> (SPEC 70): SuperAdmin only.</summary>
public sealed record RestoreProductImageCommand(Guid ProductId, Guid ImageId) : ITransactionalCommand<Response<Guid>>;
