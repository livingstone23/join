using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Activity input that the intake commands hand to <see cref="CalendarEventCommandProcessor"/> (SPEC 45): the agent
/// never chooses the calendar (the owner's default calendar is used) nor links a customer, ticket or project.
/// </summary>
public sealed record IntakeEventInput : ICalendarEventInput
{
    public Guid? CalendarId { get; init; }
    public Guid CalendarActivityCompanyId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public string? VideoCallUrl { get; init; }
    public bool IsAllDay { get; init; }
    public DateTimeOffset? Start { get; init; }
    public DateTimeOffset? End { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? RecurrenceRule { get; init; }
    public Guid? RequesterPersonId { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? TicketId { get; init; }
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// The current activity moved to <paramref name="start"/> (and <paramref name="end"/>, or the same duration):
    /// every other field is kept so the update does not clear them. An all-day activity keeps its number of days and
    /// moves to the local date of <paramref name="start"/> in the company zone (<paramref name="end"/> is ignored).
    /// </summary>
    public static IntakeEventInput MovedFrom(CalendarEvent current, DateTimeOffset start, DateTimeOffset? end, TimeZoneInfo zone)
    {
        var input = new IntakeEventInput
        {
            CalendarId = current.CalendarId,
            CalendarActivityCompanyId = current.CalendarActivityCompanyId,
            Title = current.Title,
            Summary = current.Summary,
            Description = current.Description,
            Location = current.Location,
            VideoCallUrl = current.VideoCallUrl,
            IsAllDay = current.IsAllDay,
            RecurrenceRule = current.RecurrenceRule,
            RequesterPersonId = current.RequesterPersonId,
            CustomerId = current.CustomerId,
            TicketId = current.TicketId,
            ProjectId = current.ProjectId
        };

        if (!current.IsAllDay)
        {
            return input with { Start = start, End = end ?? start + (current.EndUtc - current.StartUtc) };
        }

        var startDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        var days = Math.Max(1, (int)Math.Round((current.EndUtc - current.StartUtc).TotalDays));
        return input with { StartDate = startDate, EndDate = startDate.AddDays(days - 1) };
    }
}
