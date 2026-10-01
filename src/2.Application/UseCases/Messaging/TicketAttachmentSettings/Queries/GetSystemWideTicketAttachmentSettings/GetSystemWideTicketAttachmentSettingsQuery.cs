using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;

/// <summary>
/// Query cross-tenant para que usuarios <c>SuperAdmin</c> listen todas las
/// configuraciones de adjuntos del sistema con filtros opcionales.
/// </summary>
/// <param name="PageNumber">Página solicitada.</param>
/// <param name="PageSize">Tamaño de página solicitado.</param>
/// <param name="CompanyName">Filtro parcial por nombre de empresa.</param>
public sealed record GetSystemWideTicketAttachmentSettingsQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? CompanyName = null)
    : IRequest<Response<PagedResult<TicketAttachmentSettingsDto>>>;