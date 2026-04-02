using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Queries;

/// <summary>
/// Query to retrieve a paginated list of countries with optional search.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="SearchTerm">Optional search term to filter by country name.</param>
public record GetCountriesPagedQuery(int PageNumber = 1, int PageSize = 10, string? SearchTerm = null)
    : IRequest<Response<PagedResult<CountryListItemDto>>>;
