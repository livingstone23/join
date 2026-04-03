using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Queries;

/// <summary>
/// Query to retrieve a paginated list of street types with optional search.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested page size.</param>
/// <param name="SearchTerm">Optional search term for name or abbreviation.</param>
public record GetStreetTypesPagedQuery(int PageNumber = 1, int PageSize = 10, string? SearchTerm = null)
    : IRequest<Response<PagedResult<StreetTypeListItemDto>>>;
