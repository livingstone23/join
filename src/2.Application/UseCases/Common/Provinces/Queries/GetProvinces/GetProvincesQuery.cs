using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Provinces.Queries;

/// <summary>
/// Query to retrieve a paginated list of provinces with optional filters.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="Name">Optional search term to filter by province name.</param>
/// <param name="Code">Optional exact-match filter for the province code.</param>
/// <param name="CountryId">Optional exact-match filter for the parent country.</param>
public record GetProvincesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    string? Code = null,
    Guid? CountryId = null)
    : IRequest<Response<PagedResult<ProvinceDto>>>;