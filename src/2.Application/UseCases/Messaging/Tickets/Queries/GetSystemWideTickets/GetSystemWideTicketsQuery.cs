using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Query used by SuperAdmin users to retrieve a paginated system-wide list of tickets with optional filters.
/// </summary>
public sealed record GetSystemWideTicketsQuery(
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
    string? CompanyName = null)
    : IRequest<Response<PagedResult<TicketListItemDto>>>;
