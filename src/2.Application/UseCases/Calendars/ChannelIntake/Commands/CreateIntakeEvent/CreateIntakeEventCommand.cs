using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CreateIntakeEvent;

/// <summary>
/// <c>POST /CalendarChannelIntake/events</c> (SPEC 45): the activity the requester asked for. It goes to the user
/// whose e-mail is <see cref="TargetUserEmail"/> or, without it or without a match, to the company's default user.
/// <see cref="OriginReference"/> (the message id) deduplicates retries. <see cref="OriginType"/> (<c>Phone</c> |
/// <c>Email</c>) is optional: it follows from the channel. The status is never accepted: the type's initial status
/// applies. The company is the one of the token.
/// </summary>
public sealed record CreateIntakeEventCommand(
    string ActivityCode,
    string Title,
    string? Summary,
    string? Description,
    string? Location,
    string? VideoCallUrl,
    bool IsAllDay,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? RecurrenceRule,
    string? TargetUserEmail,
    Guid RequesterPersonId,
    string OriginChannelCode,
    string? OriginType,
    string OriginIdentifier,
    string OriginReference)
    : ITransactionalCommand<Response<CreateIntakeEventDto>>;
