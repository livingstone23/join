namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Status enabled in the company, plus the global values (<c>BlocksTime</c> and <c>IsFinal</c> are global only).
/// </summary>
public record CalendarStatusCompanyDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid CalendarStatusId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string GlobalName { get; init; } = string.Empty;
    public bool GlobalIsActive { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public bool BlocksTime { get; init; }
    public bool IsFinal { get; init; }
    public bool IsActive { get; init; }
    public int SortOrder { get; init; }
    public bool IsUsable => IsActive && GlobalIsActive;
    public DateTime CreatedAt { get; init; }
}
