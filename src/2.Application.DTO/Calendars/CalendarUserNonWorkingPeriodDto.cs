namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Non-working period of a user (vacation, leave, absence…), whole days, both ends inclusive.
/// </summary>
public record CalendarUserNonWorkingPeriodDto
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string? UserName { get; init; }
    public DateOnly StartDate { get; init; }
    public DateOnly EndDate { get; init; }

    /// <summary> <c>CalendarNonWorkingReason</c>: Vacation = 1, Leave = 2, Absence = 3, Other = 99. </summary>
    public int Reason { get; init; }

    public string? Notes { get; init; }
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Response of registering a non-working period: the period and the activities that fall in it. They are not
/// touched; the user decides whether to reschedule or cancel them.
/// </summary>
public record CalendarNonWorkingPeriodResultDto
{
    public CalendarUserNonWorkingPeriodDto Period { get; init; } = new();
    public IReadOnlyList<CalendarAffectedEventDto> AffectedEvents { get; init; } = [];
}

/// <summary>
/// An activity occurrence affected by a non-working period.
/// </summary>
public record CalendarAffectedEventDto(Guid EventId, Guid? ExceptionId, string Title, DateTime StartUtc, DateTime EndUtc);
