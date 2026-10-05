using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.FinishTicket;

/// <summary>
/// Handles <see cref="FinishTicketCommand"/>: transitions a ticket to a final status,
/// gated by the actor's <c>CanFinishTicket</c> or <c>IsSuperAdminTicket</c> capability
/// (SPEC 35). A ticket already in a final status cannot be re-finalized through this
/// endpoint — the second transition is blocked with <c>TICKET_ALREADY_FINISHED</c> (409).
/// </summary>
public sealed class FinishTicketCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    TicketUserCompanyCapabilityResolver capabilityResolver,
    TicketDtoAssembler ticketDtoAssembler,
    TicketStatusTransitionGuard transitionGuard)
    : IRequestHandler<FinishTicketCommand, Response<TicketDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly TicketUserCompanyCapabilityResolver _capabilityResolver = capabilityResolver;
    private readonly TicketDtoAssembler _ticketDtoAssembler = ticketDtoAssembler;
    private readonly TicketStatusTransitionGuard _transitionGuard = transitionGuard;

    public async Task<Response<TicketDto>> Handle(
        FinishTicketCommand request,
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
        if (!actorCapability.CanFinishTicket && !actorCapability.IsSuperAdminTicket)
        {
            return Response<TicketDto>.Error(
                "TICKET_FINISH_FORBIDDEN",
                ["Only users with finish or super-admin ticket capability can finalize a ticket."]);
        }

        var statusRepository = _unitOfWork.GetRepository<TicketStatus>();
        var targetStatus = await statusRepository.GetAsync(request.TicketStatusId);
        if (targetStatus is null || targetStatus.CompanyId != tenantId)
        {
            return Response<TicketDto>.Error("INVALID_TICKET_STATUS", ["The target ticket status does not exist or is inactive."]);
        }

        if (!targetStatus.IsFinal)
        {
            return Response<TicketDto>.Error(
                "TICKET_STATUS_NOT_FINAL",
                ["The target status is not marked as final; use the regular update flow to change the status."]);
        }

        // Block "closing" twice (or pivoting from one final status to another via this endpoint).
        var currentStatus = await statusRepository.GetAsync(entity.TicketStatusId);
        if (currentStatus is not null && currentStatus.IsFinal)
        {
            return Response<TicketDto>.Error(
                "TICKET_ALREADY_FINISHED",
                ["The ticket is already in a final status."]);
        }

        var previousStatusId = entity.TicketStatusId;
        var transitionAllowed = await _transitionGuard.IsAllowedAsync(
            tenantId,
            previousStatusId,
            request.TicketStatusId,
            cancellationToken);

        if (!transitionAllowed)
        {
            return Response<TicketDto>.Error(
                "TICKET_STATUS_TRANSITION_NOT_ALLOWED",
                ["The requested status transition is not allowed by the current tenant's rules."]);
        }

        entity.TicketStatusId = request.TicketStatusId;
        entity.LastModified = DateTime.UtcNow;
        entity.LastModifiedBy = actorUserId.ToString();

        var summary = string.IsNullOrWhiteSpace(request.ResolutionSummary)
            ? "Ticket finalizado"
            : request.ResolutionSummary.Trim();
        entity.AddLog(
            actorUserId,
            LogType.Finalization,
            summary,
            previousStatusId: previousStatusId);

        await ticketRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketDto>.Error("FINISH_FAILED", ["No records were affected while finalizing the ticket."]);
        }

        var company = await _unitOfWork.GetRepository<Company>().GetAsync(tenantId);
        var dto = await _ticketDtoAssembler.BuildAsync(entity, company!, cancellationToken);

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = "Ticket finalized successfully.",
            Data = dto
        };
    }
}