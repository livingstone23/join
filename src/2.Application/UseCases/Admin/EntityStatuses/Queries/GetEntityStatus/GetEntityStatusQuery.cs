using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Queries;

/// <summary>
/// Query used to retrieve a paginated list of administrative entity statuses.
/// </summary>
/// <param name="CompanyId">The tenant identifier used to validate the request scope.</param>
/// <param name="PageNumber">Optional page number requested by the client.</param>
/// <param name="PageSize">Optional page size requested by the client.</param>
/// <param name="Name">Optional partial-match filter applied to the status name.</param>
/// <param name="ModuleName">Optional secondary partial-match filter applied to the status description.</param>
/// <param name="CreatedFrom">Optional inclusive lower bound used to filter by creation date.</param>
/// <param name="CreatedTo">Optional inclusive upper bound used to filter by creation date.</param>
public sealed record GetEntityStatusQuery(
    Guid CompanyId,
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    string? ModuleName = null,
    DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null)
    : IRequest<Response<PagedResult<EntityStatusListItemDto>>>;
