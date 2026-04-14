using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Query used to retrieve a paginated list of tickets for the current company.
/// </summary>
public record GetTicketsQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Search = null,
    Guid? TicketStatusId = null,
    Guid? TicketComplexityId = null,
    Guid? AssignedToUserId = null,
    Guid? CustomerId = null,
    Guid? ProjectId = null,
    bool? IsVisibleToExternals = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null)
    : IRequest<Response<PagedResult<TicketListItemDto>>>;
