using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Municipalities.Queries;

/// <summary>
/// Query to retrieve a paginated list of municipalities with optional filters.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="Name">Optional search term to filter by municipality name.</param>
/// <param name="Code">Optional exact-match filter for the municipality code.</param>
/// <param name="ProvinceId">Optional exact-match filter for the parent province.</param>
public record GetMunicipalitiesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    string? Code = null,
    Guid? ProvinceId = null)
    : IRequest<Response<PagedResult<MunicipalityDto>>>;
