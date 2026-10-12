using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Handles <see cref="RescheduleIntakeEventCommand"/> (SPEC 45 F4). Validates ownership
/// (<see cref="CalendarEvent.RequesterPersonId"/> must match), refuses to reopen final-status activities,
/// and is idempotent on a repeated <c>OriginReference</c> in the event's log.
/// </summary>
public sealed class RescheduleIntakeEventCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarEventScheduleGuard scheduleGuard,
    CalendarEventLogWriter logWriter,
    CalendarEventInitialStatusResolver statusResolver)
    : IRequestHandler<RescheduleIntakeEventCommand, Response<RescheduleIntakeEventDto>>
{
    public async Task<Response<RescheduleIntakeEventDto>> Handle(
        RescheduleIntakeEventCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId == Guid.Empty ? currentUserService.CompanyId : request.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The X-Company-Id header or the CompanyId claim is required."]);
        }

        var companyResponse = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!companyResponse.IsSuccess)
        {
            return Response<RescheduleIntakeEventDto>.Error(companyResponse.Message, companyResponse.Errors);
        }
        var company = companyResponse.Data!;

        var channel = await FindChannelAsync(unitOfWork, request.OriginChannelCode, cancellationToken);
        if (channel is null)
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"Channel '{request.OriginChannelCode}' is not registered or is inactive."]);
        }

        var calendarEvent = await unitOfWork.GetRepository<CalendarEvent>().GetAsync(request.EventId);
        if (calendarEvent is null
            || calendarEvent.CompanyId != companyId
            || calendarEvent.GcRecord != 0
            || calendarEvent.RequesterPersonId != request.RequesterPersonId)
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or does not belong to the requester."]);
        }

        var currentStatusCompany = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(calendarEvent.CalendarStatusCompanyId);
        var currentStatusGlobal = currentStatusCompany is null ? null
            : await unitOfWork.GetRepository<Domain.Calendars.CalendarStatus>().GetAsync(currentStatusCompany.CalendarStatusId);
        if (currentStatusGlobal is { IsFinal: true })
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.IntakeEventAlreadyClosed,
                ["The activity is already in a final status and cannot be rescheduled."]);
        }

        // Idempotency: a log row with Notes == originReference for the same event means the agent retried.
        var logs = await unitOfWork.GetRepository<CalendarEventLog>()
            .GetAllIncludingDeletedAsync(l => l.CalendarEventId == calendarEvent.Id && l.Notes == request.OriginReference);
        if (logs.Any())
        {
            var statusCompany = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(calendarEvent.CalendarStatusCompanyId);
            return new Response<RescheduleIntakeEventDto>
            {
                IsSuccess = true,
                Data = new RescheduleIntakeEventDto(
                    calendarEvent.Id,
                    calendarEvent.StartUtc,
                    calendarEvent.EndUtc,
                    new IntakeStatusDto(
                        statusCompany?.Id ?? Guid.Empty,
                        currentStatusGlobal?.Code ?? string.Empty,
                        statusCompany?.Name ?? string.Empty,
                        statusCompany?.Color ?? string.Empty)),
                Message = "Duplicate reschedule request; no changes applied."
            };
        }

        var activity = await unitOfWork.GetRepository<CalendarActivityCompany>().GetAsync(calendarEvent.CalendarActivityCompanyId);
        if (activity is null)
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.ActivityNotEnabled,
                ["The activity type is no longer enabled."]);
        }

        var startUtc = CalendarTime.AsUtc(request.Start.UtcDateTime);
        var endUtc = request.End.HasValue
            ? CalendarTime.AsUtc(request.End.Value.UtcDateTime)
            : startUtc.AddMinutes(activity.DefaultDurationMinutes);
        if (endUtc <= startUtc)
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.InvalidEventTimes,
                ["The end must be later than the start."]);
        }

        var blocksTime = currentStatusGlobal?.BlocksTime ?? true;
        var candidates = new[] { new CalendarOccurrence(startUtc, endUtc) };
        var scheduleCheck = new CalendarScheduleCheck(
            company,
            calendarEvent.OwnerUserId,
            ActorIsOwner: false,
            activity.AllowsOverlap,
            activity.RequiresBusinessHours,
            blocksTime,
            IsAllDay: calendarEvent.IsAllDay,
            candidates,
            new HashSet<Guid> { calendarEvent.Id },
            new HashSet<(Guid, DateTime)>());
        var validation = await scheduleGuard.ValidateAsync(scheduleCheck, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Response<RescheduleIntakeEventDto>.Error(validation.Message, validation.Errors);
        }

        var previousStartUtc = calendarEvent.StartUtc;
        var previousEndUtc = calendarEvent.EndUtc;
        var previousStatusId = calendarEvent.CalendarStatusCompanyId;

        // R6 (SPEC 44): actor is not the owner → move to the company's rescheduled status.
        var rescheduleStatusCompany = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(company.RescheduledByOthersStatusCompanyId);
        var rescheduleStatusGlobal = rescheduleStatusCompany is null ? null
            : await unitOfWork.GetRepository<Domain.Calendars.CalendarStatus>().GetAsync(rescheduleStatusCompany.CalendarStatusId);
        if (rescheduleStatusCompany is null || rescheduleStatusGlobal is null || !rescheduleStatusGlobal.IsActive || rescheduleStatusGlobal.GcRecord != 0)
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.StatusNotEnabled,
                ["The reschedule status configured in CalendarCompanies is not active."]);
        }

        var actorUserId = ParseUserIdOrNull(currentUserService.UserId);
        calendarEvent.StartUtc = startUtc;
        calendarEvent.EndUtc = endUtc;
        calendarEvent.CalendarStatusCompanyId = rescheduleStatusCompany.Id;
        calendarEvent.StatusChangedUtc = DateTime.UtcNow;
        calendarEvent.StatusChangedByUserId = actorUserId;
        await unitOfWork.GetRepository<CalendarEvent>().UpdateAsync(calendarEvent);

        var actor = new CalendarActor(channel.Id, actorUserId, request.OriginIdentifier);
        var notes = string.IsNullOrWhiteSpace(request.Notes)
            ? request.OriginReference
            : $"{request.OriginReference} | {request.Notes}";
        await logWriter.WriteAsync(calendarEvent, CalendarEventLogAction.Rescheduled, actor,
            new CalendarEventLogChange(previousStartUtc, previousEndUtc, calendarEvent.StartUtc, calendarEvent.EndUtc,
                previousStatusId, calendarEvent.CalendarStatusCompanyId),
            notes);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<RescheduleIntakeEventDto>
        {
            IsSuccess = true,
            Data = new RescheduleIntakeEventDto(
                calendarEvent.Id,
                calendarEvent.StartUtc,
                calendarEvent.EndUtc,
                new IntakeStatusDto(
                    rescheduleStatusCompany.Id,
                    rescheduleStatusGlobal.Code,
                    rescheduleStatusCompany.Name,
                    rescheduleStatusCompany.Color)),
            Message = "Calendar activity rescheduled."
        };
    }

    private static Guid? ParseUserIdOrNull(string? userId) =>
        Guid.TryParse(userId, out var parsed) && parsed != Guid.Empty ? parsed : null;

    private static async Task<ChannelRow?> FindChannelAsync(
        IUnitOfWork unitOfWork,
        string channelCode,
        CancellationToken cancellationToken)
    {
        var channels = await unitOfWork.GetRepository<Domain.Common.CommunicationChannel>()
            .GetAllIncludingDeletedAsync(c => c.Code == channelCode && c.GcRecord == 0 && c.IsActive);
        var c = channels.FirstOrDefault();
        return c is null ? null : new ChannelRow { Id = c.Id, Code = c.Code ?? string.Empty };
    }

    private sealed class ChannelRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
    }
}