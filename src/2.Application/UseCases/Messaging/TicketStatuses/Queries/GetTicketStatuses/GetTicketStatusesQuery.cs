using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Queries;

/// <summary>
/// Query to retrieve a paginated list of ticket statuses with optional filters.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="Name">Optional search term to filter by ticket status name.</param>
/// <param name="IsActive">Optional exact-match filter for the active flag.</param>
public record GetTicketStatusesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    bool? IsActive = null)
    : IRequest<Response<PagedResult<TicketStatusDto>>>;
