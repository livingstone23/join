using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;

/// <summary>
/// Query para obtener la configuración de adjuntos activa del tenant actual.
/// Devuelve 0 o 1 fila (configuración singleton por empresa).
/// </summary>
public record GetTicketAttachmentSettingsQuery(bool? IncludeDeleted = null, Guid? CompanyId = null) : IRequest<Response<IReadOnlyCollection<TicketAttachmentSettingsDto>>>;