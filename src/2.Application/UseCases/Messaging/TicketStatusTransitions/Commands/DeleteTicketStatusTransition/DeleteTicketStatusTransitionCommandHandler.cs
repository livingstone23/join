using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.DeleteTicketStatusTransition;

/// <summary>
/// Soft-deletes a single ticket status transition rule for the current tenant.
/// </summary>
public sealed class DeleteTicketStatusTransitionCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteTicketStatusTransitionCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    public async Task<Response<Guid>> Handle(
        DeleteTicketStatusTransitionCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketStatusTransition>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null || entity.CompanyId != tenantId)
        {
            return Response<Guid>.Error(
                "TICKET_STATUS_TRANSITION_NOT_FOUND",
                ["Ticket status transition not found for the current company."]);
        }

        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "TICKET_STATUS_TRANSITION_NOT_FOUND",
                ["No records were affected while deleting the ticket status transition."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket status transition deleted successfully.",
            Data = entity.Id
        };
    }
}