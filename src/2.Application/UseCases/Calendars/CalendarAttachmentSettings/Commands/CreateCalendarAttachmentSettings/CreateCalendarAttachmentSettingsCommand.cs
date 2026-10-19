using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands;

/// <summary>
/// SPEC 69 — creates the unique live row of calendar attachment limits for the current tenant (regla 7).
/// Fails with <c>CALENDAR_ATTACHMENT_SETTINGS_ALREADY_EXISTS</c> when one already exists; the UI uses
/// <c>PUT</c> in that case.
/// </summary>
public sealed record CreateCalendarAttachmentSettingsCommand : ITransactionalCommand<Response<CalendarAttachmentSettingsDto>>
{
    public DocumentType AllowedDocumentTypes { get; init; } = CalendarAttachmentSettingsDefaults.AllowedTypes;
    public long MaxFileSizeBytes { get; init; } = CalendarAttachmentSettingsDefaults.MaxFileSizeBytes;
    public int MaxFilesPerComment { get; init; } = CalendarAttachmentSettingsDefaults.MaxFilesPerComment;
    public int? MaxFilesPerDay { get; init; } = CalendarAttachmentSettingsDefaults.MaxFilesPerDay;
}

/// <summary>
/// SPEC 69 — defaults that match the seed and the controller convenience defaults so the command and the
/// seed agree on the same starting point.
/// </summary>
public static class CalendarAttachmentSettingsDefaults
{
    public const long MaxFileSizeBytes = 10L * 1024 * 1024;
    public const int MaxFilesPerComment = 5;
    public const int MaxFilesPerDay = 200;
    public const DocumentType AllowedTypes = DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Image;
}