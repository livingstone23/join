using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;

/// <summary>
/// Query para recuperar una configuración de adjuntos por identificador.
/// </summary>
/// <param name="Id">Identificador de la configuración.</param>
public record GetTicketAttachmentSettingByIdQuery(Guid Id) : IRequest<Response<TicketAttachmentSettingsDto>>;