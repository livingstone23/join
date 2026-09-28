using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.DeleteTicketUserCompany;

/// <summary>
/// Soft-deletes a ticket-roster entry. The "at least one active IsSuperAdminTicket"
/// invariant is enforced by the handler before any persistence call.
/// </summary>
public sealed record DeleteTicketUserCompanyCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
