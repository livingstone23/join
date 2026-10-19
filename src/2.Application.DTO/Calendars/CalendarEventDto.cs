namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// Activity type as seen in an activity.
/// </summary>
public record CalendarActivityRefDto(Guid Id, string Code, string Name, string Color, bool AllowsOverlap);

/// <summary>
/// Status as seen in an activity.
/// </summary>
public record CalendarStatusRefDto(Guid Id, string Code, string Name, string Color, bool BlocksTime, bool IsFinal);

/// <summary>
/// Who requested the activity and through which channel. <see cref="Type"/> is the <c>CalendarEventOriginType</c>
/// name (User, Phone, Email, System).
/// </summary>
public record CalendarOriginDto(string ChannelCode, string Type, string? Identifier, Guid? UserId);

/// <summary>
/// Full detail of an activity (<c>GET /CalendarEvents/{id}</c> and the responses of the write endpoints). In a
/// series it is the master, with its rule and a readable description. Times are UTC; <see cref="TimeZone"/> is the
/// company zone for the frontend to convert.
/// </summary>
public record CalendarEventDto
{
    public Guid Id { get; init; }
    public Guid CalendarId { get; init; }
    public string CalendarName { get; init; } = string.Empty;
    public string CalendarColor { get; init; } = string.Empty;
    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public CalendarActivityRefDto Activity { get; init; } = new(Guid.Empty, string.Empty, string.Empty, string.Empty, false);
    public CalendarStatusRefDto Status { get; init; } = new(Guid.Empty, string.Empty, string.Empty, string.Empty, false, false);
    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public string? VideoCallUrl { get; init; }
    public bool IsAllDay { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public bool IsRecurring { get; init; }
    public string? RecurrenceRule { get; init; }
    public string? RecurrenceDescription { get; init; }
    public DateTime? RecurrenceEndUtc { get; init; }
    public Guid? SeriesMasterId { get; init; }
    public DateTime? OriginalStartUtc { get; init; }
    public CalendarOriginDto Origin { get; init; } = new(string.Empty, string.Empty, null, null);
    public Guid? RequesterPersonId { get; init; }

    /// <summary>
    /// Readable name of the requester person (SPEC 67 — unified "Persona/Cliente" lookup of FE-42). Empty when the
    /// activity has no person; the commercial name wins for legal persons, otherwise the trimmed full name.
    /// </summary>
    public string? RequesterPersonName { get; init; }

    public Guid? CustomerId { get; init; }

    /// <summary>
    /// Customer code of the linked customer (SPEC 67). <c>null</c> when the activity has no customer. The pair
    /// <see cref="RequesterPersonId"/>/<see cref="CustomerId"/> is always coherent — see <c>ValidateLinksAsync</c>.
    /// </summary>
    public string? CustomerCode { get; init; }

    /// <summary>
    /// <c>true</c> when the linked customer is active; <c>false</c> when it is inactive (SPEC 67 keeps the link
    /// for inactive customers); <c>null</c> when the activity has no customer.
    /// </summary>
    public bool? IsCustomerActive { get; init; }

    public Guid? TicketId { get; init; }
    public Guid? ProjectId { get; init; }
    public DateTime? StatusChangedUtc { get; init; }
    public Guid? StatusChangedByUserId { get; init; }
    public string TimeZone { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// One occurrence in the day/week/month views (<c>GET /CalendarEvents</c>, <c>GET /CalendarPanel/events</c>).
/// <see cref="EventId"/> is the master or one-time activity; <see cref="ExceptionId"/> is set when the occurrence is
/// an exception; <see cref="OccurrenceStartUtc"/> is the key for PUT/PATCH/DELETE with <c>scope</c>.
/// </summary>
public record CalendarOccurrenceDto
{
    public Guid EventId { get; init; }
    public Guid? ExceptionId { get; init; }
    public DateTime OccurrenceStartUtc { get; init; }
    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }
    public bool IsAllDay { get; init; }
    public bool IsRecurring { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? Location { get; init; }
    public string? VideoCallUrl { get; init; }
    public Guid CalendarId { get; init; }
    public string CalendarName { get; init; } = string.Empty;
    public string CalendarColor { get; init; } = string.Empty;
    public CalendarActivityRefDto Activity { get; init; } = new(Guid.Empty, string.Empty, string.Empty, string.Empty, false);
    public CalendarStatusRefDto Status { get; init; } = new(Guid.Empty, string.Empty, string.Empty, string.Empty, false, false);
    public Guid OwnerUserId { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public CalendarOriginDto Origin { get; init; } = new(string.Empty, string.Empty, null, null);
    public string TimeZone { get; init; } = string.Empty;
}

/// <summary>
/// A holiday or non-working period in a range response. <see cref="Kind"/> is <c>Holiday</c>,
/// <c>NonWorkingPeriod</c> (the user's own, with reason) or <c>Unavailable</c> (another user's, without reason).
/// </summary>
public record CalendarNonWorkingRangeDto(string Kind, DateOnly StartDate, DateOnly EndDate, string? Name, Guid? UserId);

/// <summary>
/// Response of the range queries: everything the day, week and month views need in one call.
/// </summary>
public record CalendarEventRangeDto
{
    public string TimeZone { get; init; } = string.Empty;
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public IReadOnlyList<CalendarOccurrenceDto> Occurrences { get; init; } = [];
    public IReadOnlyList<CalendarNonWorkingRangeDto> NonWorkingDays { get; init; } = [];
}

/// <summary>
/// A row of the activity log. <see cref="Action"/> is the <c>CalendarEventLogAction</c> name.
/// </summary>
public record CalendarEventLogDto
{
    public Guid Id { get; init; }
    public Guid CalendarEventId { get; init; }
    public string Action { get; init; } = string.Empty;
    public DateTime? PreviousStartUtc { get; init; }
    public DateTime? PreviousEndUtc { get; init; }
    public DateTime? NewStartUtc { get; init; }
    public DateTime? NewEndUtc { get; init; }
    public Guid? PreviousStatusCompanyId { get; init; }
    public string? PreviousStatusName { get; init; }
    public Guid? NewStatusCompanyId { get; init; }
    public string? NewStatusName { get; init; }
    public string ChannelCode { get; init; } = string.Empty;
    public Guid? ActorUserId { get; init; }
    public string? ActorName { get; init; }
    public string? ActorIdentifier { get; init; }
    public string? Notes { get; init; }
    public DateTime OccurredUtc { get; init; }
}
