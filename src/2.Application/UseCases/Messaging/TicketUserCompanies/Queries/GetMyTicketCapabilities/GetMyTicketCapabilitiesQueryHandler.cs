using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Messaging.Tickets;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetMyTicketCapabilities;

/// <summary>
/// Resolves the current user's ticket capabilities for the active tenant via
/// <see cref="TicketUserCompanyCapabilityResolver"/> (SPEC 53). Reuses the
/// resolver's <c>ResolveWithMembershipAsync</c> so this handler never reimplements
/// the roster lookup — the three capability flags and the <c>IsMember</c>
/// marker stay in lockstep with the rest of the application.
/// </summary>
public sealed class GetMyTicketCapabilitiesQueryHandler(
    ICurrentUserService currentUserService,
    TicketUserCompanyCapabilityResolver resolver)
    : IRequestHandler<GetMyTicketCapabilitiesQuery, Response<TicketMyCapabilitiesDto>>
{
    /// <summary>
    /// Returns the caller's ticket capabilities for the active tenant. A user
    /// outside the roster still receives a 200 with <c>IsMember = false</c> and
    /// all three capability flags <c>false</c> — the front only hides actions,
    /// it does not surface a missing-row error.
    /// </summary>
    public async Task<Response<TicketMyCapabilitiesDto>> Handle(
        GetMyTicketCapabilitiesQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketMyCapabilitiesDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId) || userId == Guid.Empty)
        {
            return Response<TicketMyCapabilitiesDto>.Error(
                "USER_REQUIRED",
                ["The authenticated user identifier is required."]);
        }

        var tenantId = TenantResolver.Resolve(currentUserService, request.CompanyId);
        var capability = await resolver.ResolveWithMembershipAsync(userId, tenantId, cancellationToken);

        return new Response<TicketMyCapabilitiesDto>
        {
            IsSuccess = true,
            Message = "Ticket capabilities retrieved successfully.",
            Data = new TicketMyCapabilitiesDto
            {
                IsMember = capability.IsMember,
                IsSuperAdminTicket = capability.IsSuperAdminTicket,
                CanFinishTicket = capability.CanFinishTicket,
                CanResolveTicket = capability.CanResolveTicket
            }
        };
    }
}