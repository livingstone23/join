using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.CreateTicketStatusTransition;

/// <summary>
/// Handles <see cref="CreateTicketStatusTransitionCommand"/>: validates that both endpoints
/// exist and belong to the current tenant, ensures no duplicate active rule exists, and inserts
/// the row. Returns the resulting <see cref="TicketStatusTransitionDto"/>.
/// </summary>
public sealed class CreateTicketStatusTransitionCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateTicketStatusTransitionCommand, Response<TicketStatusTransitionDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    public async Task<Response<TicketStatusTransitionDto>> Handle(
        CreateTicketStatusTransitionCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<TicketStatusTransitionDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (request.FromStatusId == request.ToStatusId)
        {
            return Response<TicketStatusTransitionDto>.Error(
                "SAME_STATUS_TRANSITION",
                ["Source and destination status must be different."]);
        }

        var statusRepository = _unitOfWork.GetRepository<TicketStatus>();
        var statuses = await statusRepository.GetAllAsync();

        var fromStatus = statuses.FirstOrDefault(s =>
            s.Id == request.FromStatusId
            && s.GcRecord == 0
            && s.CompanyId == tenantId);

        if (fromStatus is null)
        {
            return Response<TicketStatusTransitionDto>.Error(
                "INVALID_FROM_STATUS",
                ["The source status does not exist or does not belong to the current company."]);
        }

        var toStatus = statuses.FirstOrDefault(s =>
            s.Id == request.ToStatusId
            && s.GcRecord == 0
            && s.CompanyId == tenantId);

        if (toStatus is null)
        {
            return Response<TicketStatusTransitionDto>.Error(
                "INVALID_TO_STATUS",
                ["The destination status does not exist or does not belong to the current company."]);
        }

        var transitionRepository = _unitOfWork.GetRepository<TicketStatusTransition>();
        var existing = await transitionRepository.GetAllAsync();
        var duplicate = existing.Any(x =>
            x.GcRecord == 0
            && x.CompanyId == tenantId
            && x.FromStatusId == request.FromStatusId
            && x.ToStatusId == request.ToStatusId);

        if (duplicate)
        {
            return Response<TicketStatusTransitionDto>.Error(
                "TICKET_STATUS_TRANSITION_DUPLICATE",
                ["A rule for this source/destination pair already exists for the current company."]);
        }

        var entity = new TicketStatusTransition
        {
            CompanyId = tenantId,
            FromStatusId = request.FromStatusId,
            ToStatusId = request.ToStatusId
        };

        await transitionRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketStatusTransitionDto>.Error(
                "CREATE_FAILED",
                ["No records were affected while creating the ticket status transition."]);
        }

        return new Response<TicketStatusTransitionDto>
        {
            IsSuccess = true,
            Message = "Ticket status transition created successfully.",
            Data = new TicketStatusTransitionDto
            {
                Id = entity.Id,
                FromStatusId = entity.FromStatusId,
                FromStatusName = fromStatus.Name,
                ToStatusId = entity.ToStatusId,
                ToStatusName = toStatus.Name,
                CreatedAt = entity.Created
            }
        };
    }
}