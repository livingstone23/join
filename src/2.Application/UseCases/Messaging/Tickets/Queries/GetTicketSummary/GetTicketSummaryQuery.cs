using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Tenant-scoped inbox summary (SPEC 52). Takes no parameters: it always counts
/// the active company from the token, also for SuperAdmin (no <c>companyId</c> override).
/// </summary>
public sealed record GetTicketSummaryQuery
    : IRequest<Response<TicketSummaryDto>>;