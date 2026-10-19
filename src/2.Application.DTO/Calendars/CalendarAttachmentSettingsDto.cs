namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// SPEC 69 — per-company attachment limits for calendar activities (regla 7). The fields map one-to-one to
/// <c>CalendarAttachmentSettings</c>; the readable list of allowed types mirrors the bitmask so the frontend
/// does not have to look up flag names.
/// </summary>
public record CalendarAttachmentSettingsDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }

    /// <summary> Bitmask persisted on the row (mirrors <c>JOIN.Domain.Enums.DocumentType</c>). </summary>
    public int AllowedDocumentTypes { get; init; }

    /// <summary> Readable list derived from the bitmask; mirrors <c>TicketAttachmentSettingsDto</c>. </summary>
    public IReadOnlyList<string> AllowedDocumentTypeNames
    {
        get
        {
            const int Pdf = 1 << 0;
            const int Word = 1 << 1;
            const int Excel = 1 << 2;
            const int Text = 1 << 3;
            const int Image = 1 << 4;
            const int Other = 1 << 5;

            var names = new List<string>();
            if ((AllowedDocumentTypes & Pdf) != 0) names.Add("Pdf");
            if ((AllowedDocumentTypes & Word) != 0) names.Add("Word");
            if ((AllowedDocumentTypes & Excel) != 0) names.Add("Excel");
            if ((AllowedDocumentTypes & Text) != 0) names.Add("Text");
            if ((AllowedDocumentTypes & Image) != 0) names.Add("Image");
            if ((AllowedDocumentTypes & Other) != 0) names.Add("Other");
            return names;
        }
    }

    public long MaxFileSizeBytes { get; init; }
    public int MaxFilesPerComment { get; init; }
    public int? MaxFilesPerDay { get; init; }
    public DateTime CreatedAt { get; init; }
}