using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Areas.Queries;

/// <summary>
/// Query used to retrieve a paginated and filterable area list for a specific tenant.
/// </summary>
/// <param name="CompanyId">The tenant identifier used to scope the result set.</param>
/// <param name="PageNumber">The optional page number to retrieve. When omitted, the configured default value is used.</param>
/// <param name="PageSize">The optional page size to retrieve. When omitted, the configured default value is used.</param>
/// <param name="Name">Optional inclusive partial-match filter applied to the area name.</param>
/// <param name="CreatedFrom">Optional inclusive lower bound used to filter by creation date.</param>
/// <param name="CreatedTo">Optional inclusive upper bound used to filter by creation date.</param>
public sealed record GetAreasQuery(
    Guid CompanyId,
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null)
    : IRequest<Response<PagedResult<AreaListItemDto>>>;
