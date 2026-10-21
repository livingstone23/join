using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.Products.Documents.Queries;

/// <summary><c>GET /Products/{id}/documents</c> (SPEC 70): the live documents of the product, newest first, with type and author.</summary>
public sealed record GetProductDocumentsQuery(Guid ProductId) : IRequest<Response<IReadOnlyList<ProductDocumentDto>>>;

/// <summary><c>GET /Products/{id}/documents/{docId}/download</c> (SPEC 70): the bytes of a live document.</summary>
public sealed record DownloadProductDocumentQuery(Guid ProductId, Guid DocumentId) : IRequest<Response<ProductDocumentDownloadResult>>;
