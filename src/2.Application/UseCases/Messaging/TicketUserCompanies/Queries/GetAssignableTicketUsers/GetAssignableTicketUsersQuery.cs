using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetAssignableTicketUsers;

/// <summary>
/// Tenant-scoped query that returns the active users the current user may
/// assign a ticket to (SPEC 53). Same <c>CanResolveTicket = TRUE</c> filter that
/// <c>CreateTicket</c> (initial assignee) and <c>ReassignTicket</c> (target)
/// validate against, so the FE-17 dropdown cannot list destinations the
/// backend would reject. Takes no parameters: always the active company of the
/// token, SuperAdmin included (SPEC 53, same rule as SPEC 52).
/// </summary>
public sealed record GetAssignableTicketUsersQuery
    : IRequest<Response<IReadOnlyList<TicketAssignableUserDto>>>;