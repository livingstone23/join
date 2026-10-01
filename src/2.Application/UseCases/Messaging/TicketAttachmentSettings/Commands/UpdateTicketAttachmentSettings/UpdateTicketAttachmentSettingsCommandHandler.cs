using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using MediatR;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Maneja la actualización de la configuración de adjuntos.
/// </summary>
public sealed class UpdateTicketAttachmentSettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ITicketAttachmentSettingsMapper mapper)
    : IRequestHandler<UpdateTicketAttachmentSettingsCommand, Response<TicketAttachmentSettingsDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ITicketAttachmentSettingsMapper _mapper = mapper;

    /// <summary>
    /// Actualiza la configuración si pertenece al tenant activo.
    /// </summary>
    public async Task<Response<TicketAttachmentSettingsDto>> Handle(UpdateTicketAttachmentSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketAttachmentSettingsDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketAttachmentSettingsEntity>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null || entity.GcRecord != 0 || entity.CompanyId != currentUserService.CompanyId)
        {
            return Response<TicketAttachmentSettingsDto>.Error("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND", ["Ticket attachment settings configuration not found for the current tenant."]);
        }

        _mapper.ApplyUpdate(request, entity);

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketAttachmentSettingsDto>.Error("UPDATE_FAILED", ["No records were affected while updating the configuration."]);
        }

        return new Response<TicketAttachmentSettingsDto>
        {
            IsSuccess = true,
            Message = "Ticket attachment settings updated successfully.",
            Data = BuildDto(entity, companyName: null)
        };
    }

    private static TicketAttachmentSettingsDto BuildDto(TicketAttachmentSettingsEntity entity, string? companyName) => new()
    {
        Id = entity.Id,
        CompanyId = entity.CompanyId,
        CompanyName = companyName,
        GcRecord = entity.GcRecord,
        AllowedDocumentTypes = (int)entity.AllowedDocumentTypes,
        MaxFileSizeBytes = entity.MaxFileSizeBytes,
        MaxFilesPerTicket = entity.MaxFilesPerTicket,
        MaxFilesPerDay = entity.MaxFilesPerDay,
        CreatedAt = entity.Created
    };
}