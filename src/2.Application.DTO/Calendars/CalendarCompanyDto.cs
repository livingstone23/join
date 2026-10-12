namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Calendar configuration of the current company (<c>GET /CalendarCompanies/current</c>). Never 404: without a
/// configuration <see cref="IsConfigured"/> is <c>false</c> and the configuration fields are empty.
/// </summary>
public record CalendarCompanyDto
{
    public bool IsConfigured { get; init; }
    public bool ModuleEnabled { get; init; }
    public int UsersWithoutCalendar { get; init; }
    public Guid? Id { get; init; }
    public Guid? DefaultActivityUserId { get; init; }
    public string? DefaultActivityUserName { get; init; }
    public string? TimeZone { get; init; }
    public bool IsOpen24Hours { get; init; }
    public TimeOnly? BusinessHoursStart { get; init; }
    public TimeOnly? BusinessHoursEnd { get; init; }
    public string? DefaultCalendarName { get; init; }
    public Guid? RescheduledByOthersStatusCompanyId { get; init; }
    public string? RescheduledByOthersStatusName { get; init; }

    /// <summary> Non-blocking notes, e.g. the time zone changed while future activities exist. </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Result of <c>POST /CalendarCompanies/current/sync-catalogs</c>.
/// </summary>
public record CalendarCatalogSyncDto(int ActivitiesAdded, int StatusesAdded);

/// <summary>
/// A user whose default calendar was created by <c>provision-users</c> or by <c>POST /CalendarCompanies</c>.
/// </summary>
public record CalendarProvisionedUserDto(Guid UserId, string Name, string? Email);
