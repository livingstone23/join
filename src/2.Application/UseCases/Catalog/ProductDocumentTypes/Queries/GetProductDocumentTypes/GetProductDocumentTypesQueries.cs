using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Queries;

/// <summary><c>GET /ProductDocumentTypes</c> (SPEC 70): the company's live catalog; inactive rows on request.</summary>
public sealed record GetProductDocumentTypesQuery(bool? IncludeInactive = null) : IRequest<Response<IReadOnlyList<ProductDocumentTypeDto>>>;
