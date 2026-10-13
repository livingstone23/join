using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetMyTicketCapabilities;

/// <summary>
/// Tenant-scoped query that returns the current user's ticket capabilities
/// (SPEC 53). Always reads the active company from the token; the
/// <paramref name="CompanyId"/> override is honored only for SuperAdmin.
/// </summary>
public sealed record GetMyTicketCapabilitiesQuery(Guid? CompanyId = null)
    : IRequest<Response<TicketMyCapabilitiesDto>>;