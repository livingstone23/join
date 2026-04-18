using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Queries;

/// <summary>
/// Query used by SuperAdmin users to retrieve ticket statuses across all companies with optional filters.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="Name">Optional search term to filter by ticket status name.</param>
/// <param name="IsActive">Optional exact-match filter for the active flag.</param>
/// <param name="IsInitial">Optional exact-match filter for the initial flag.</param>
/// <param name="IsPaused">Optional exact-match filter for the paused flag.</param>
/// <param name="IsFinal">Optional exact-match filter for the final flag.</param>
/// <param name="CompanyName">Optional partial-match filter applied to the company name.</param>
/// <param name="Code">Optional exact-match filter applied to the status code.</param>
public sealed record GetSystemWideTicketStatusesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    bool? IsActive = null,
    bool? IsInitial = null,
    bool? IsPaused = null,
    bool? IsFinal = null,
    string? CompanyName = null,
    int? Code = null)
    : IRequest<Response<PagedResult<TicketStatusDto>>>;
