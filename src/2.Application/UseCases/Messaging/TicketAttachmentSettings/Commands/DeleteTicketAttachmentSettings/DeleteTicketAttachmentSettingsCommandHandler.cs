using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using MediatR;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Maneja el borrado lógico de la configuración de adjuntos.
/// </summary>
public sealed class DeleteTicketAttachmentSettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteTicketAttachmentSettingsCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Marca la configuración como borrada lógicamente.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteTicketAttachmentSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketAttachmentSettingsEntity>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null || entity.GcRecord != 0 || entity.CompanyId != currentUserService.CompanyId)
        {
            return Response<Guid>.Error("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND", ["Ticket attachment settings configuration not found for the current tenant."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the configuration."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket attachment settings deleted successfully.",
            Data = entity.Id
        };
    }
}