using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Query used to retrieve a paginated list of tickets for the current company.
/// </summary>
/// <param name="Unassigned">
/// Server-side filter for tickets without an assignee. SPEC 52 forbids
/// combining it with <paramref name="AssignedToUserId"/> — the handler
/// answers <c>INVALID_ASSIGNEE_FILTER</c> when both arrive.
/// </param>
public record GetTicketsQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Search = null,
    Guid? TicketStatusId = null,
    Guid? TicketComplexityId = null,
    Guid? AssignedToUserId = null,
    Guid? PersonId = null,
    Guid? ProjectId = null,
    bool? IsVisibleToExternals = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    bool? IncludeDeleted = null,
    Guid? CompanyId = null,
    bool? Unassigned = null)
    : IRequest<Response<PagedResult<TicketListItemDto>>>;
