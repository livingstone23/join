using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands;

/// <summary>
/// SPEC 69 — updates the unique live row of calendar attachment limits for the current tenant. Permission is
/// <c>CalendarSettings.CanUpdate</c>; the controller enforces it.
/// </summary>
public sealed record UpdateCalendarAttachmentSettingsCommand : ITransactionalCommand<Response<CalendarAttachmentSettingsDto>>
{
    public DocumentType AllowedDocumentTypes { get; init; }
    public long MaxFileSizeBytes { get; init; }
    public int MaxFilesPerComment { get; init; }
    public int? MaxFilesPerDay { get; init; }
}