using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Tenant-scoped inbox summary (SPEC 52). Always counts the active company
/// from the token; the optional <paramref name="CompanyId"/> override is
/// honored only for SuperAdmin, like every other tenant-scoped query.
/// </summary>
public sealed record GetTicketSummaryQuery(Guid? CompanyId = null)
    : IRequest<Response<TicketSummaryDto>>;