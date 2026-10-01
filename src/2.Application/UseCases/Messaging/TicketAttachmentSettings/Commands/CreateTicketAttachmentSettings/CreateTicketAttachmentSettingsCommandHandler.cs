using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using MediatR;
using TicketAttachmentSettingsEntity = JOIN.Domain.Messaging.TicketAttachmentSettings;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Maneja la creación de la configuración de adjuntos. Una sola fila activa
/// por empresa — el chequeo de duplicado se hace en memoria vía
/// <c>GetAllAsync()</c> + filtro <c>GcRecord == 0</c>, mismo patrón que
/// <c>CreateTicketCompanyDefaultCommandHandler</c>.
/// </summary>
public sealed class CreateTicketAttachmentSettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ITicketAttachmentSettingsMapper mapper)
    : IRequestHandler<CreateTicketAttachmentSettingsCommand, Response<TicketAttachmentSettingsDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ITicketAttachmentSettingsMapper _mapper = mapper;

    /// <summary>
    /// Crea la configuración si no existe una activa para el tenant.
    /// </summary>
    public async Task<Response<TicketAttachmentSettingsDto>> Handle(CreateTicketAttachmentSettingsCommand request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketAttachmentSettingsDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketAttachmentSettingsEntity>();
        var existing = await repository.GetAllAsync();
        var activeExists = existing.Any(x => x.CompanyId == currentUserService.CompanyId && x.GcRecord == 0);

        if (activeExists)
        {
            return Response<TicketAttachmentSettingsDto>.Error("CONFIG_ALREADY_EXISTS", ["An active ticket attachment settings configuration already exists for the current tenant."]);
        }

        var entity = _mapper.ToEntity(request);
        entity.CompanyId = currentUserService.CompanyId;

        await repository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketAttachmentSettingsDto>.Error("CREATE_FAILED", ["No records were affected while creating the configuration."]);
        }

        return new Response<TicketAttachmentSettingsDto>
        {
            IsSuccess = true,
            Message = "Ticket attachment settings created successfully.",
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