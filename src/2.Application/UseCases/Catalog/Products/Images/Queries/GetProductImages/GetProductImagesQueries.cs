using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.Products.Images.Queries;

/// <summary><c>GET /Products/{id}/images</c> (SPEC 70): the live images of the product, by <c>SortOrder</c>, with their author.</summary>
public sealed record GetProductImagesQuery(Guid ProductId) : IRequest<Response<IReadOnlyList<ProductImageDto>>>;

/// <summary>
/// <c>GET /Products/{id}/images/{imageId}/content</c> and <c>Leads/products-lookup/{productId}/images/{imageId}/content</c>
/// (SPEC 70): the bytes of a live image, read from the provider that stored them.
/// </summary>
public sealed record GetProductImageContentQuery(Guid ProductId, Guid ImageId) : IRequest<Response<ProductImageContentResult>>;
