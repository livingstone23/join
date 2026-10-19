using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Builds and queues the <see cref="CalendarEventLog"/> row of each operation (SPEC 44 R6 + SPEC 69: every
/// operation is logged with its channel and actor; comment / document rows also carry the denormalized id).
/// Does not save.
/// </summary>
public sealed class CalendarEventLogWriter(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Queues a log row for <paramref name="calendarEvent"/>.
    /// </summary>
    public Task<CalendarEventLog> WriteAsync(
        CalendarEvent calendarEvent,
        CalendarEventLogAction action,
        CalendarActor actor,
        CalendarEventLogChange? change = null,
        string? notes = null,
        Guid? commentId = null,
        Guid? documentId = null)
    {
        var log = new CalendarEventLog
        {
            CompanyId = calendarEvent.CompanyId,
            CalendarEventId = calendarEvent.Id,
            Action = action,
            PreviousStartUtc = change?.PreviousStartUtc,
            PreviousEndUtc = change?.PreviousEndUtc,
            NewStartUtc = change?.NewStartUtc,
            NewEndUtc = change?.NewEndUtc,
            PreviousStatusCompanyId = change?.PreviousStatusCompanyId,
            NewStatusCompanyId = change?.NewStatusCompanyId,
            ChannelId = actor.ChannelId,
            ActorUserId = actor.UserId,
            ActorIdentifier = actor.Identifier,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CalendarEventCommentId = commentId,
            CalendarEventDocumentId = documentId,
            OccurredUtc = DateTime.UtcNow
        };

        return InsertAsync(log);
    }

    private async Task<CalendarEventLog> InsertAsync(CalendarEventLog log)
    {
        await unitOfWork.GetRepository<CalendarEventLog>().InsertAsync(log);
        return log;
    }
}

/// <summary>
/// Who made a change and through which channel.
/// </summary>
public sealed record CalendarActor(Guid ChannelId, Guid? UserId, string? Identifier = null);

/// <summary>
/// Times and statuses before and after a change, for the log.
/// </summary>
public sealed record CalendarEventLogChange(
    DateTime? PreviousStartUtc = null,
    DateTime? PreviousEndUtc = null,
    DateTime? NewStartUtc = null,
    DateTime? NewEndUtc = null,
    Guid? PreviousStatusCompanyId = null,
    Guid? NewStatusCompanyId = null);
