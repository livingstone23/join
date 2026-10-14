using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetMyTicketCapabilities;

/// <summary>
/// Tenant-scoped query that returns the current user's ticket capabilities
/// (SPEC 53). Takes no parameters: always the user and the active company of
/// the token, SuperAdmin included.
/// </summary>
public sealed record GetMyTicketCapabilitiesQuery
    : IRequest<Response<TicketMyCapabilitiesDto>>;