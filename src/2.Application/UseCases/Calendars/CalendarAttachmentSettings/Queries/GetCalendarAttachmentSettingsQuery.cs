using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Queries;

/// <summary>
/// SPEC 69 — returns the current company's calendar attachment limits (regla 7). A missing row answers
/// <c>CALENDAR_ATTACHMENT_SETTINGS_NOT_CONFIGURED</c> (400) so the UI can drive the "Configuración de
/// adjuntos" tab.
/// </summary>
public sealed record GetCalendarAttachmentSettingsQuery : IRequest<Response<CalendarAttachmentSettingsDto>>;