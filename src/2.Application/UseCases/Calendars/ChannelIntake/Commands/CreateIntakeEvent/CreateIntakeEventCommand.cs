using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// <c>POST /events</c>: registers the activity the requester asked for (SPEC 45 F4). The recipient user is
/// resolved from <c>TargetUserEmail</c> when present, falling back to the company's
/// <see cref="JOIN.Domain.Calendars.CalendarCompany.DefaultActivityUserId"/>. Duplicates by
/// <c>(OriginChannelId, OriginReference)</c> are answered idempotently (<c>IsDuplicate = true</c>), including
/// the unique-index race.
/// </summary>
public record CreateIntakeEventCommand(
    Guid CompanyId,
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
    string OriginType,
    string OriginIdentifier,
    string OriginReference)
    : IRequest<Response<CreateIntakeEventDto>>, ITransactionalCommand<CreateIntakeEventDto>;