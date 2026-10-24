using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Queries;

/// <summary>
/// <c>GET /ProductDocumentTypes</c> (SPEC 70): the company's live catalog; inactive rows on request. SPEC 72.1
/// adds <c>IncludeDeleted</c>, which only a SuperAdmin sees (SPEC 41-post).
/// </summary>
public sealed record GetProductDocumentTypesQuery(bool? IncludeInactive = null, bool? IncludeDeleted = null)
    : IRequest<Response<IReadOnlyList<ProductDocumentTypeDto>>>;
