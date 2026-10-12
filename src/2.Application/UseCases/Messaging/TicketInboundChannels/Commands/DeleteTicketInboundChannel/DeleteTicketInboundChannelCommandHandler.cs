using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.DeleteTicketInboundChannel;

/// <summary>
/// Maneja el borrado lógico de un <c>TicketInboundChannel</c>. Marcar
/// <c>GcRecord = yyyyMMdd</c> saca la fila del índice único y de cualquier lookup
/// activo: el siguiente request al webhook con ese token recibe un 404 desnudo.
/// </summary>
public sealed class DeleteTicketInboundChannelCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteTicketInboundChannelCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(
        DeleteTicketInboundChannelCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketInboundChannel>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null || entity.GcRecord != BaseAuditableEntity.ActiveGcRecord || entity.CompanyId != _currentUserService.CompanyId)
        {
            return Response<Guid>.Error("TICKET_INBOUND_CHANNEL_NOT_FOUND", ["Ticket inbound channel not found for the current tenant."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the inbound channel."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket inbound channel deleted successfully.",
            Data = entity.Id
        };
    }
}
