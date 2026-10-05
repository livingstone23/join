using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.ReassignTicket;

/// <summary>
/// Handles <see cref="ReassignTicketCommand"/>. Enforces the
/// "actor must be IsSuperAdminTicket, target must be CanResolveTicket" contract
/// introduced by SPEC 35. A reassignment to the already-assigned user is a no-op
/// success that does not emit a new <c>Reassignment</c> log entry — but the target
/// still has to pass the same existence/tenant/capability gates so that stale
/// assignees (revoked <c>CanResolveTicket</c>) do not silently lock the ticket.
/// </summary>
public sealed class ReassignTicketCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    TicketUserCompanyCapabilityResolver capabilityResolver,
    TicketDtoAssembler ticketDtoAssembler)
    : IRequestHandler<ReassignTicketCommand, Response<TicketDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly TicketUserCompanyCapabilityResolver _capabilityResolver = capabilityResolver;
    private readonly TicketDtoAssembler _ticketDtoAssembler = ticketDtoAssembler;

    public async Task<Response<TicketDto>> Handle(
        ReassignTicketCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<TicketDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(_currentUserService.UserId, out var actorUserId))
        {
            return Response<TicketDto>.Error("USER_REQUIRED", ["The authenticated user identifier is required."]);
        }

        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        var entity = await ticketRepository.GetAsync(request.TicketId);

        if (entity is null || entity.CompanyId != tenantId)
        {
            return Response<TicketDto>.Error("TICKET_NOT_FOUND", ["Ticket not found for the current company."]);
        }

        var actorCapability = await _capabilityResolver.ResolveAsync(actorUserId, tenantId, cancellationToken);
        if (!actorCapability.IsSuperAdminTicket)
        {
            return Response<TicketDto>.Error(
                "TICKET_REASSIGN_FORBIDDEN",
                ["Only ticket-super-admin users can reassign tickets."]);
        }

        var userRepository = _unitOfWork.GetRepository<ApplicationUser>();
        if (await userRepository.GetAsync(request.NewAssignedToUserId) is null)
        {
            return Response<TicketDto>.Error("INVALID_ASSIGNED_USER", ["The assigned user does not exist or is inactive."]);
        }

        var targetHasTenant = await _unitOfWork.UserCompanies
            .IsActiveMemberAsync(request.NewAssignedToUserId, tenantId, cancellationToken);

        if (!targetHasTenant)
        {
            return Response<TicketDto>.Error("INVALID_ASSIGNED_USER_TENANT", ["The assigned user is not linked to the current company."]);
        }

        var targetCapability = await _capabilityResolver.ResolveAsync(request.NewAssignedToUserId, tenantId, cancellationToken);
        if (!targetCapability.CanResolveTicket)
        {
            return Response<TicketDto>.Error(
                "TARGET_NOT_ELIGIBLE_RESOLVER",
                ["The target user does not have ticket-resolution capability for the current company."]);
        }

        if (entity.AssignedToUserId == request.NewAssignedToUserId)
        {
            // No-op (SPEC 35 step 8): same user already on the ticket. The mutation+log
            // in step 9 is skipped; we still rebuild the DTO so the caller sees the
            // current projection. SaveChangesAsync is intentionally NOT called — EF
            // would report 0 affected rows for an unchanged entity, and step 10's
            // `result <= 0 → REASSIGN_FAILED` would turn this success into a 400.
            var company = await _unitOfWork.GetRepository<Company>().GetAsync(tenantId);
            var dto = await _ticketDtoAssembler.BuildAsync(entity, company!, cancellationToken);
            return new Response<TicketDto>
            {
                IsSuccess = true,
                Message = "Ticket reassigned successfully.",
                Data = dto
            };
        }

        entity.AssignedToUserId = request.NewAssignedToUserId;
        entity.LastModified = DateTime.UtcNow;
        entity.LastModifiedBy = actorUserId.ToString();
        entity.AddLog(
            actorUserId,
            LogType.Reassignment,
            "Ticket reasignado",
            newAssignedToUserId: request.NewAssignedToUserId);

        await ticketRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketDto>.Error("REASSIGN_FAILED", ["No records were affected while reassigning the ticket."]);
        }

        var companyForDto = await _unitOfWork.GetRepository<Company>().GetAsync(tenantId);
        var finalDto = await _ticketDtoAssembler.BuildAsync(entity, companyForDto!, cancellationToken);

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = "Ticket reassigned successfully.",
            Data = finalDto
        };
    }
}