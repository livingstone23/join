using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Queries;

/// <summary>
/// Query to retrieve a paginated list of time units with optional filters.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="Name">Optional search term to filter by time unit name.</param>
/// <param name="IsActive">Optional exact-match filter for the active flag.</param>
public record GetTimeUnitsQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    bool? IsActive = null)
    : IRequest<Response<PagedResult<TimeUnitDto>>>;
