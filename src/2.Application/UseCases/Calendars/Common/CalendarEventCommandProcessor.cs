using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Customers;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Write logic of the activities, shared by <c>CalendarEvents</c> (the owner is the current user, R2) and
/// <c>CalendarPanel</c> (any user of the company). Rules R3–R9 of SPEC 44: times in the company zone, overlap and
/// business hours (<see cref="CalendarEventScheduleGuard"/>), initial status, reschedule status when the actor is
/// not the owner, recurrence scopes and the log. Changes are saved first and validated after, inside the command
/// transaction: a failed validation returns an error response and <c>TransactionBehavior</c> rolls everything
/// back, so the validation always sees the real state of the series.
/// </summary>
public sealed class CalendarEventCommandProcessor(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarCatalogReader catalogReader,
    CalendarEventInitialStatusResolver initialStatusResolver,
    CalendarEventScheduleGuard scheduleGuard,
    CalendarEventLogWriter logWriter,
    ICalendarRecurrenceExpander expander)
{
    /// <summary> Maximum length of one occurrence (SPEC 44, data model 10). </summary>
    public const int MaxOccurrenceDays = 14;

    private static readonly IReadOnlySet<Guid> NoIds = new HashSet<Guid>();
    private static readonly IReadOnlySet<(Guid, DateTime)> NoOccurrences = new HashSet<(Guid, DateTime)>();

    // ------------------------------------------------------------------ create

    /// <summary>
    /// Creates an activity. <paramref name="panelOwnerUserId"/> is <c>null</c> for <c>CalendarEvents</c> (the owner
    /// is the current user) and the target user for <c>CalendarPanel</c> and the channel intake (SPEC 45).
    /// <paramref name="origin"/> replaces the <c>WEB</c> channel of the actor when the activity is written on behalf
    /// of an external requester.
    /// </summary>
    public async Task<Response<CalendarEventDto>> CreateAsync(
        ICalendarEventInput input,
        Guid? panelOwnerUserId,
        CancellationToken cancellationToken,
        CalendarExternalOrigin? origin = null)
    {
        var sessionResponse = await OpenSessionAsync(cancellationToken, origin);
        if (!sessionResponse.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(sessionResponse.Message, sessionResponse.Errors);
        }

        var session = sessionResponse.Data!;
        var ownerUserId = panelOwnerUserId ?? session.ActorUserId;
        if (panelOwnerUserId.HasValue && !await IsCompanyMemberAsync(session.CompanyId, ownerUserId))
        {
            return Response<CalendarEventDto>.Error(CalendarErrorCodes.UserNotInCompany, ["The owner is not an active user of the current company."]);
        }

        var prepared = await PrepareAsync(session, ownerUserId, input, null);
        if (!prepared.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(prepared.Message, prepared.Errors);
        }

        var data = prepared.Data!;
        var rule = ValidateRecurrence(input.RecurrenceRule, input.IsAllDay);
        if (!rule.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(rule.Message, rule.Errors);
        }

        var initialStatus = await initialStatusResolver.ResolveAsync(data.Activity.ActivityCompany);
        if (!initialStatus.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(initialStatus.Message, initialStatus.Errors);
        }

        var calendarEvent = new CalendarEvent
        {
            CompanyId = session.CompanyId,
            OwnerUserId = ownerUserId,
            CalendarStatusCompanyId = initialStatus.Data!.Id,
            OriginChannelId = session.Actor.ChannelId,
            OriginType = origin?.OriginType ?? CalendarEventOriginType.User,
            OriginIdentifier = origin?.Identifier,
            OriginReference = origin?.Reference,
            OriginUserId = session.ActorUserId,
            StatusChangedUtc = DateTime.UtcNow,
            StatusChangedByUserId = session.ActorUserId
        };
        Apply(calendarEvent, input, data);
        SetRecurrence(calendarEvent, rule.Data, session.Company);

        await unitOfWork.CalendarEvents.InsertAsync(calendarEvent);
        await logWriter.WriteAsync(calendarEvent, CalendarEventLogAction.Created, session.Actor,
            new CalendarEventLogChange(NewStartUtc: calendarEvent.StartUtc, NewEndUtc: calendarEvent.EndUtc, NewStatusCompanyId: calendarEvent.CalendarStatusCompanyId));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var validation = await ValidateEventAsync(session, calendarEvent, data.Activity.ActivityCompany, cancellationToken);
        if (!validation.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(validation.Message, validation.Errors);
        }

        return await BuildResultAsync(calendarEvent, session.Company, "Calendar activity created successfully.");
    }

    // ------------------------------------------------------------------ update

    /// <summary>
    /// Updates an activity. For a series, <paramref name="scope"/> and <paramref name="occurrenceStartUtc"/> are
    /// required (R7). <paramref name="origin"/> and <paramref name="notes"/> come from the channel intake (SPEC 45):
    /// the log rows are written with the requester's channel and identifier and carry the notes.
    /// </summary>
    public async Task<Response<CalendarEventDto>> UpdateAsync(
        Guid eventId,
        CalendarRecurrenceEditScope? scope,
        DateTime? occurrenceStartUtc,
        ICalendarEventInput input,
        bool panel,
        CancellationToken cancellationToken,
        CalendarExternalOrigin? origin = null,
        string? notes = null)
    {
        var context = await OpenTargetAsync(eventId, scope, occurrenceStartUtc, panel, cancellationToken, origin);
        if (!context.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(context.Message, context.Errors);
        }

        var (session, calendarEvent, target) = context.Data!;
        var prepared = await PrepareAsync(session, calendarEvent.OwnerUserId, input, calendarEvent.CalendarId);
        if (!prepared.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(prepared.Message, prepared.Errors);
        }

        var data = prepared.Data!;
        var actorIsOwner = session.ActorUserId == calendarEvent.OwnerUserId;

        switch (target.Kind)
        {
            case CalendarEditTarget.Single:
            {
                var rule = calendarEvent.SeriesMasterId.HasValue
                    ? Ok<CalendarRecurrenceRule?>(null)
                    : ValidateRecurrence(input.RecurrenceRule, input.IsAllDay);
                if (!rule.IsSuccess)
                {
                    return Response<CalendarEventDto>.Error(rule.Message, rule.Errors);
                }

                var changed = await ApplyUpdateAsync(session, calendarEvent, input, data, actorIsOwner, calendarEvent.StartUtc, calendarEvent.EndUtc,
                    calendarEvent.SeriesMasterId.HasValue ? CalendarEventLogAction.OccurrenceModified : null, notes: notes);
                if (!changed.IsSuccess)
                {
                    return Response<CalendarEventDto>.Error(changed.Message, changed.Errors);
                }

                if (!calendarEvent.SeriesMasterId.HasValue)
                {
                    SetRecurrence(calendarEvent, rule.Data, session.Company);
                }

                return await SaveValidateAndBuildAsync(session, calendarEvent, data.Activity.ActivityCompany, "Calendar activity updated successfully.", cancellationToken);
            }

            case CalendarEditTarget.Occurrence:
            {
                var exception = await GetOrCreateExceptionAsync(calendarEvent, target.OccurrenceStartUtc, target.OccurrenceEndUtc);
                var changed = await ApplyUpdateAsync(session, exception, input, data, actorIsOwner, target.OccurrenceStartUtc, target.OccurrenceEndUtc, CalendarEventLogAction.OccurrenceModified, notes: notes);
                if (!changed.IsSuccess)
                {
                    return Response<CalendarEventDto>.Error(changed.Message, changed.Errors);
                }

                return await SaveValidateAndBuildAsync(session, exception, data.Activity.ActivityCompany, "Calendar occurrence updated successfully.", cancellationToken);
            }

            case CalendarEditTarget.Following:
            {
                var rule = ValidateRecurrence(input.RecurrenceRule, input.IsAllDay);
                if (!rule.IsSuccess)
                {
                    return Response<CalendarEventDto>.Error(rule.Message, rule.Errors);
                }

                var split = await SplitSeriesAsync(session, calendarEvent, target.OccurrenceStartUtc, data.StartUtc - target.OccurrenceStartUtc, rule.Data, notes);
                if (!split.IsSuccess)
                {
                    return Response<CalendarEventDto>.Error(split.Message, split.Errors);
                }

                var newMaster = split.Data!;
                var changed = await ApplyUpdateAsync(session, newMaster, input, data, actorIsOwner, target.OccurrenceStartUtc, target.OccurrenceEndUtc, CalendarEventLogAction.SeriesSplit, logNew: false);
                if (!changed.IsSuccess)
                {
                    return Response<CalendarEventDto>.Error(changed.Message, changed.Errors);
                }

                SetRecurrence(newMaster, CalendarRecurrenceRule.Parse(newMaster.RecurrenceRule).Data, session.Company);
                return await SaveValidateAndBuildAsync(session, newMaster, data.Activity.ActivityCompany, "Calendar series split successfully.", cancellationToken);
            }

            default:
            {
                var rule = ValidateRecurrence(input.RecurrenceRule ?? calendarEvent.RecurrenceRule, input.IsAllDay);
                if (!rule.IsSuccess)
                {
                    return Response<CalendarEventDto>.Error(rule.Message, rule.Errors);
                }

                var delta = data.StartUtc - target.OccurrenceStartUtc;
                var previousStart = calendarEvent.StartUtc;
                var previousEnd = calendarEvent.EndUtc;
                var shifted = data with
                {
                    StartUtc = calendarEvent.StartUtc + delta,
                    EndUtc = calendarEvent.StartUtc + delta + (data.EndUtc - data.StartUtc)
                };

                var changed = await ApplyUpdateAsync(session, calendarEvent, input, shifted, actorIsOwner, previousStart, previousEnd, null, notes: notes);
                if (!changed.IsSuccess)
                {
                    return Response<CalendarEventDto>.Error(changed.Message, changed.Errors);
                }

                SetRecurrence(calendarEvent, rule.Data, session.Company);
                if (delta != TimeSpan.Zero)
                {
                    await ShiftExceptionKeysAsync(calendarEvent.Id, delta, cancellationToken);
                }

                return await SaveValidateAndBuildAsync(session, calendarEvent, data.Activity.ActivityCompany, "Calendar series updated successfully.", cancellationToken);
            }
        }
    }

    // ------------------------------------------------------------------ status

    /// <summary>
    /// Changes the status of an activity (R6): any usable status, no transition rules; cancelling requires notes;
    /// moving from a status that does not block time to one that does runs R4 again.
    /// </summary>
    public async Task<Response<CalendarEventDto>> ChangeStatusAsync(
        Guid eventId,
        CalendarRecurrenceEditScope? scope,
        DateTime? occurrenceStartUtc,
        Guid statusCompanyId,
        string? notes,
        bool panel,
        CancellationToken cancellationToken,
        CalendarExternalOrigin? origin = null)
    {
        var context = await OpenTargetAsync(eventId, scope, occurrenceStartUtc, panel, cancellationToken, origin);
        if (!context.IsSuccess)
        {
            return Response<CalendarEventDto>.Error(context.Message, context.Errors);
        }

        var (session, calendarEvent, target) = context.Data!;
        var newStatus = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(statusCompanyId);
        var newGlobal = await catalogReader.GetUsableStatusAsync(session.CompanyId, newStatus);
        if (newGlobal is null)
        {
            return Response<CalendarEventDto>.Error(CalendarErrorCodes.StatusNotEnabled, ["The status is not active in the company."]);
        }

        if (newGlobal.Code == CalendarCodes.Statuses.Cancelled && string.IsNullOrWhiteSpace(notes))
        {
            return Response<CalendarEventDto>.Error(CalendarErrorCodes.CancellationReasonRequired, ["A reason (notes) is required to cancel an activity."]);
        }

        var subject = target.Kind switch
        {
            CalendarEditTarget.Occurrence => await GetOrCreateExceptionAsync(calendarEvent, target.OccurrenceStartUtc, target.OccurrenceEndUtc),
            CalendarEditTarget.Following => (await SplitSeriesAsync(session, calendarEvent, target.OccurrenceStartUtc, TimeSpan.Zero, null)).Data!,
            _ => calendarEvent
        };

        var previousStatus = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(subject.CalendarStatusCompanyId);
        var previousBlocks = previousStatus is not null && (await catalogReader.GetGlobalStatusAsync(previousStatus))?.BlocksTime == true;
        var previousStatusId = subject.CalendarStatusCompanyId;

        subject.CalendarStatusCompanyId = newStatus!.Id;
        subject.StatusChangedUtc = DateTime.UtcNow;
        subject.StatusChangedByUserId = session.ActorUserId;
        await unitOfWork.CalendarEvents.UpdateAsync(subject);
        await logWriter.WriteAsync(subject, CalendarEventLogAction.StatusChanged, session.Actor,
            new CalendarEventLogChange(PreviousStatusCompanyId: previousStatusId, NewStatusCompanyId: newStatus.Id), notes);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!previousBlocks && newGlobal.BlocksTime)
        {
            var activity = await unitOfWork.GetRepository<CalendarActivityCompany>().GetAsync(subject.CalendarActivityCompanyId);
            var validation = await ValidateEventAsync(session, subject, activity!, cancellationToken, overlapOnly: true);
            if (!validation.IsSuccess)
            {
                return Response<CalendarEventDto>.Error(validation.Message, validation.Errors);
            }
        }

        return await BuildResultAsync(subject, session.Company, "Calendar activity status changed successfully.");
    }

    // ------------------------------------------------------------------ delete

    /// <summary>
    /// Logically deletes an activity (R8). A master takes its exceptions with it. For one occurrence of a series the
    /// occurrence is removed from the views through an exception in the company's <c>CANCELLED</c> status; for
    /// "this and following" the series is cut before the occurrence.
    /// </summary>
    public async Task<Response<bool>> DeleteAsync(
        Guid eventId,
        CalendarRecurrenceEditScope? scope,
        DateTime? occurrenceStartUtc,
        bool panel,
        CancellationToken cancellationToken)
    {
        var context = await OpenTargetAsync(eventId, scope, occurrenceStartUtc, panel, cancellationToken);
        if (!context.IsSuccess)
        {
            return Response<bool>.Error(context.Message, context.Errors);
        }

        var (session, calendarEvent, target) = context.Data!;
        var now = DateTime.UtcNow;

        switch (target.Kind)
        {
            case CalendarEditTarget.Occurrence:
            {
                var cancelled = await catalogReader.GetCompanyStatusByCodeAsync(session.CompanyId, CalendarCodes.Statuses.Cancelled);
                if (cancelled is null)
                {
                    return Response<bool>.Error(CalendarErrorCodes.StatusNotEnabled, ["The company has no CANCELLED status to remove an occurrence."]);
                }

                var exception = await GetOrCreateExceptionAsync(calendarEvent, target.OccurrenceStartUtc, target.OccurrenceEndUtc);
                var previousStatusId = exception.CalendarStatusCompanyId;
                exception.CalendarStatusCompanyId = cancelled.Id;
                exception.StatusChangedUtc = now;
                exception.StatusChangedByUserId = session.ActorUserId;
                await unitOfWork.CalendarEvents.UpdateAsync(exception);
                await logWriter.WriteAsync(exception, CalendarEventLogAction.Deleted, session.Actor,
                    new CalendarEventLogChange(PreviousStatusCompanyId: previousStatusId, NewStatusCompanyId: cancelled.Id), "Occurrence removed from the series.");
                break;
            }

            case CalendarEditTarget.Following:
            {
                var rule = CalendarRecurrenceRule.Parse(calendarEvent.RecurrenceRule).Data!;
                calendarEvent.RecurrenceRule = rule.WithUntil(target.OccurrenceStartUtc.AddSeconds(-1)).ToRuleString();
                calendarEvent.RecurrenceEndUtc = expander.GetSeriesEndUtc(AsSeries(calendarEvent), session.Company.TimeZoneId);
                await unitOfWork.CalendarEvents.UpdateAsync(calendarEvent);

                foreach (var exception in (await unitOfWork.CalendarEvents.GetExceptionsAsync(calendarEvent.Id, cancellationToken))
                             .Where(e => e.OriginalStartUtc >= target.OccurrenceStartUtc))
                {
                    exception.MarkAsDeleted(now);
                    await unitOfWork.CalendarEvents.UpdateAsync(exception);
                    await logWriter.WriteAsync(exception, CalendarEventLogAction.Deleted, session.Actor);
                }

                await logWriter.WriteAsync(calendarEvent, CalendarEventLogAction.SeriesSplit, session.Actor, null,
                    $"Occurrences from {target.OccurrenceStartUtc:O} deleted.");
                break;
            }

            default:
            {
                if (calendarEvent.RecurrenceRule is not null && !calendarEvent.SeriesMasterId.HasValue)
                {
                    foreach (var exception in await unitOfWork.CalendarEvents.GetExceptionsAsync(calendarEvent.Id, cancellationToken))
                    {
                        exception.MarkAsDeleted(now);
                        await unitOfWork.CalendarEvents.UpdateAsync(exception);
                        await logWriter.WriteAsync(exception, CalendarEventLogAction.Deleted, session.Actor);
                    }
                }

                calendarEvent.MarkAsDeleted(now);
                await unitOfWork.CalendarEvents.UpdateAsync(calendarEvent);
                await logWriter.WriteAsync(calendarEvent, CalendarEventLogAction.Deleted, session.Actor);
                break;
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<bool> { IsSuccess = true, Data = true, Message = "Calendar activity deleted successfully." };
    }

    // ------------------------------------------------------------------ session and target

    /// <summary>
    /// Resolves the tenant, the actor, the calendar configuration (R1) and the <c>WEB</c> channel of the actor, or
    /// the requester's channel and identifier when <paramref name="origin"/> is given (SPEC 45).
    /// </summary>
    public async Task<Response<CalendarSession>> OpenSessionAsync(CancellationToken cancellationToken, CalendarExternalOrigin? origin = null)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarSession>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var actorUserId) || actorUserId == Guid.Empty)
        {
            return Response<CalendarSession>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        var company = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<CalendarSession>.Error(company.Message, company.Errors);
        }

        if (origin is not null)
        {
            return Ok(new CalendarSession(companyId, actorUserId, company.Data!, new CalendarActor(origin.ChannelId, actorUserId, origin.Identifier)));
        }

        var webChannel = (await unitOfWork.GetRepository<CommunicationChannel>()
                .GetAllIncludingDeletedAsync(x => x.Code == CommunicationChannelCodes.Web && x.GcRecord == 0))
            .FirstOrDefault();
        if (webChannel is null)
        {
            return Response<CalendarSession>.Error(CalendarErrorCodes.WebChannelNotFound, ["The WEB communication channel is missing (SPEC 00)."]);
        }

        return Ok(new CalendarSession(companyId, actorUserId, company.Data!, new CalendarActor(webChannel.Id, actorUserId)));
    }

    private async Task<Response<CalendarTargetContext>> OpenTargetAsync(
        Guid eventId,
        CalendarRecurrenceEditScope? scope,
        DateTime? occurrenceStartUtc,
        bool panel,
        CancellationToken cancellationToken,
        CalendarExternalOrigin? origin = null)
    {
        var sessionResponse = await OpenSessionAsync(cancellationToken, origin);
        if (!sessionResponse.IsSuccess)
        {
            return Response<CalendarTargetContext>.Error(sessionResponse.Message, sessionResponse.Errors);
        }

        var session = sessionResponse.Data!;
        var calendarEvent = await unitOfWork.CalendarEvents.GetAsync(eventId);

        // R2: another user's activity answers 404 in the own calendar, never 403.
        if (calendarEvent is null
            || calendarEvent.CompanyId != session.CompanyId
            || calendarEvent.GcRecord != 0
            || (!panel && calendarEvent.OwnerUserId != session.ActorUserId))
        {
            return Response<CalendarTargetContext>.Error(CalendarErrorCodes.EventNotFound, ["The calendar activity was not found."]);
        }

        if (calendarEvent.RecurrenceRule is null || calendarEvent.SeriesMasterId.HasValue)
        {
            return Ok(new CalendarTargetContext(session, calendarEvent, new CalendarTarget(CalendarEditTarget.Single, calendarEvent.StartUtc, calendarEvent.EndUtc)));
        }

        if (scope is null)
        {
            return Response<CalendarTargetContext>.Error(CalendarErrorCodes.RecurrenceScopeRequired,
                ["scope (ThisOccurrence = 1, ThisAndFollowing = 2, Series = 3) and occurrenceStartUtc are required for a recurring activity."]);
        }

        var key = occurrenceStartUtc.HasValue ? CalendarTime.AsUtc(occurrenceStartUtc.Value.ToUniversalTime()) : calendarEvent.StartUtc;
        var occurrence = expander.Expand(AsSeries(calendarEvent), key, key.AddSeconds(1), session.Company.TimeZoneId)
            .FirstOrDefault(o => o.StartUtc == key);
        if (occurrence is null)
        {
            return Response<CalendarTargetContext>.Error(CalendarErrorCodes.OccurrenceNotFound, ["occurrenceStartUtc does not match an occurrence of the series."]);
        }

        var kind = scope switch
        {
            CalendarRecurrenceEditScope.ThisOccurrence => CalendarEditTarget.Occurrence,
            CalendarRecurrenceEditScope.ThisAndFollowing when key > calendarEvent.StartUtc => CalendarEditTarget.Following,
            _ => CalendarEditTarget.Series
        };

        return Ok(new CalendarTargetContext(session, calendarEvent, new CalendarTarget(kind, occurrence.StartUtc, occurrence.EndUtc)));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Response<PreparedInput>> PrepareAsync(CalendarSession session, Guid ownerUserId, ICalendarEventInput input, Guid? currentCalendarId)
    {
        var calendarId = input.CalendarId ?? currentCalendarId;
        if (calendarId is null)
        {
            calendarId = (await unitOfWork.GetRepository<CalendarUserConfiguration>()
                    .GetAllIncludingDeletedAsync(x => x.CompanyId == session.CompanyId && x.UserId == ownerUserId && x.GcRecord == 0))
                .Select(x => (Guid?)x.DefaultCalendarId)
                .FirstOrDefault();

            if (calendarId is null)
            {
                return Response<PreparedInput>.Error(CalendarErrorCodes.UserHasNoCalendar, ["The user has no calendar in this company yet."]);
            }
        }

        var calendar = await unitOfWork.GetRepository<UserCalendar>().GetAsync(calendarId.Value);
        if (calendar is null || calendar.GcRecord != 0 || calendar.CompanyId != session.CompanyId || calendar.OwnerUserId != ownerUserId)
        {
            return Response<PreparedInput>.Error(CalendarErrorCodes.CalendarNotFound, ["The calendar was not found for the owner of the activity."]);
        }

        if (!calendar.IsActive && calendar.Id != currentCalendarId)
        {
            return Response<PreparedInput>.Error(CalendarErrorCodes.CalendarInactive, ["An inactive calendar does not receive new activities."]);
        }

        var activity = await catalogReader.GetUsableActivityAsync(session.CompanyId, input.CalendarActivityCompanyId);
        if (activity is null)
        {
            return Response<PreparedInput>.Error(CalendarErrorCodes.ActivityNotEnabled, ["The activity type is not enabled in the company."]);
        }

        var times = ResolveTimes(input, session.Company, activity.ActivityCompany.DefaultDurationMinutes);
        if (!times.IsSuccess)
        {
            return Response<PreparedInput>.Error(times.Message, times.Errors);
        }

        var links = await ValidateLinksAsync(session.CompanyId, input);
        if (!links.IsSuccess)
        {
            return Response<PreparedInput>.Error(links.Message, links.Errors);
        }

        return Ok(new PreparedInput(calendar, activity, times.Data.StartUtc, times.Data.EndUtc, links.Data!));
    }

    /// <summary>
    /// Resolves the UTC times of the input (R3): offsets are converted with <c>UtcDateTime</c>; an all-day
    /// activity goes from local midnight of the first day to local midnight after the last; a missing end uses the
    /// type's duration.
    /// </summary>
    public static Response<(DateTime StartUtc, DateTime EndUtc)> ResolveTimes(ICalendarEventInput input, CalendarCompanyContext company, int defaultDurationMinutes)
    {
        DateTime startUtc;
        DateTime endUtc;

        if (input.IsAllDay)
        {
            if (input.StartDate is not { } startDate)
            {
                return Response<(DateTime, DateTime)>.Error(CalendarErrorCodes.InvalidEventTimes, ["startDate is required for an all-day activity."]);
            }

            var endDate = input.EndDate ?? startDate;
            if (endDate < startDate)
            {
                return Response<(DateTime, DateTime)>.Error(CalendarErrorCodes.InvalidEventTimes, ["endDate cannot be earlier than startDate."]);
            }

            startUtc = CalendarTime.StartOfDayUtc(startDate, company.TimeZone);
            endUtc = CalendarTime.StartOfDayUtc(endDate.AddDays(1), company.TimeZone);
        }
        else
        {
            if (input.Start is not { } start)
            {
                return Response<(DateTime, DateTime)>.Error(CalendarErrorCodes.InvalidEventTimes, ["start is required."]);
            }

            startUtc = start.UtcDateTime;
            endUtc = input.End?.UtcDateTime ?? startUtc.AddMinutes(defaultDurationMinutes);
        }

        if (endUtc <= startUtc)
        {
            return Response<(DateTime, DateTime)>.Error(CalendarErrorCodes.InvalidEventTimes, ["The end must be later than the start."]);
        }

        if (endUtc - startUtc > TimeSpan.FromDays(MaxOccurrenceDays))
        {
            return Response<(DateTime, DateTime)>.Error(CalendarErrorCodes.InvalidEventTimes, [$"An occurrence cannot last more than {MaxOccurrenceDays} days."]);
        }

        return new Response<(DateTime, DateTime)> { IsSuccess = true, Data = (CalendarTime.AsUtc(startUtc), CalendarTime.AsUtc(endUtc)) };
    }

    private Response<CalendarRecurrenceRule?> ValidateRecurrence(string? recurrenceRule, bool isAllDay)
    {
        if (string.IsNullOrWhiteSpace(recurrenceRule))
        {
            return Ok<CalendarRecurrenceRule?>(null);
        }

        var validation = expander.ValidateRule(recurrenceRule);
        if (!validation.IsSuccess)
        {
            return Response<CalendarRecurrenceRule?>.Error(validation.Message, validation.Errors);
        }

        var rule = validation.Data!;
        if (isAllDay && rule.Frequency == "DAILY" && !rule.HasEnd)
        {
            return Response<CalendarRecurrenceRule?>.Error(CalendarErrorCodes.InvalidRecurrenceRule,
                ["An all-day series cannot repeat daily without an end (COUNT or UNTIL)."]);
        }

        return Ok<CalendarRecurrenceRule?>(rule);
    }

    /// <summary>
    /// R9 + SPEC 67: validates each linked record exists in the company and resolves the pair
    /// <c>(requesterPersonId, customerId)</c> so the two columns always point at the same relationship. Rules 2 and 3
    /// of SPEC 67 fill in the missing side; rule 4 rejects a mismatch with <c>CALENDAR_PERSON_CUSTOMER_MISMATCH</c>.
    /// Rule 6 keeps both nulls when both arrive null (the entity is cleared).
    /// </summary>
    private async Task<Response<ResolvedLinks>> ValidateLinksAsync(Guid companyId, ICalendarEventInput input)
    {
        Person? person = null;
        Customer? customer = null;

        if (input.RequesterPersonId is { } personId)
        {
            person = (await unitOfWork.GetRepository<Person>()
                .GetAllIncludingDeletedAsync(x => x.Id == personId && x.CompanyId == companyId && x.GcRecord == 0))
                .FirstOrDefault();
            if (person is null)
            {
                return InvalidLink<ResolvedLinks>("requesterPersonId");
            }
        }

        if (input.CustomerId is { } customerId)
        {
            customer = await unitOfWork.GetRepository<Customer>().GetAsync(customerId);
            if (customer is null || customer.CompanyId != companyId || customer.GcRecord != 0)
            {
                return InvalidLink<ResolvedLinks>("customerId");
            }
        }

        // SPEC 67 rule 2: person → customer (the customer's customer of that person in the company, active or not).
        if (person is not null && customer is null)
        {
            customer = (await unitOfWork.GetRepository<Customer>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.PersonId == person.Id && x.GcRecord == 0))
                .FirstOrDefault();
        }

        // SPEC 67 rule 3: customer → person (the customer must point at a live person of the company).
        if (customer is not null && person is null)
        {
            person = (await unitOfWork.GetRepository<Person>()
                .GetAllIncludingDeletedAsync(x => x.Id == customer.PersonId && x.CompanyId == companyId && x.GcRecord == 0))
                .FirstOrDefault();
        }

        // SPEC 67 rule 4: when both arrive, they must agree; otherwise the pair is incoherent.
        if (person is not null && customer is not null && customer.PersonId != person.Id)
        {
            return Response<ResolvedLinks>.Error(CalendarErrorCodes.PersonCustomerMismatch,
                ["The customer does not belong to the requester person."]);
        }

        if (input.TicketId is { } ticketId && !await IsLinkValidAsync<Ticket>(ticketId, companyId))
        {
            return InvalidLink<ResolvedLinks>("ticketId");
        }

        if (input.ProjectId is { } projectId && !await IsLinkValidAsync<Project>(projectId, companyId))
        {
            return InvalidLink<ResolvedLinks>("projectId");
        }

        // SPEC 67 rule 6: when both are null the pair is cleared (entity keeps nulls).
        return Ok(new ResolvedLinks(person?.Id, customer?.Id));
    }

    /// <summary>
    /// R9: the linked record exists, is not deleted and belongs to the company (read only).
    /// </summary>
    private async Task<bool> IsLinkValidAsync<T>(Guid id, Guid companyId)
        where T : Domain.Audit.BaseTenantEntity =>
        (await unitOfWork.GetRepository<T>()
            .GetAllIncludingDeletedAsync(x => x.Id == id && x.CompanyId == companyId && x.GcRecord == 0))
        .Any();

    private static Response<T> InvalidLink<T>(string field) =>
        Response<T>.Error(CalendarErrorCodes.InvalidLink, [$"{field} does not exist in the current company."]);

    private async Task<bool> IsCompanyMemberAsync(Guid companyId, Guid userId) =>
        (await unitOfWork.GetRepository<UserCompany>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
        .Any();

    private static void Apply(CalendarEvent calendarEvent, ICalendarEventInput input, PreparedInput data)
    {
        calendarEvent.CalendarId = data.Calendar.Id;
        calendarEvent.CalendarActivityCompanyId = data.Activity.ActivityCompany.Id;
        calendarEvent.Title = input.Title.Trim();
        calendarEvent.Summary = Clean(input.Summary);
        calendarEvent.Description = Clean(input.Description);
        calendarEvent.Location = Clean(input.Location);
        calendarEvent.VideoCallUrl = Clean(input.VideoCallUrl);
        calendarEvent.IsAllDay = input.IsAllDay;
        calendarEvent.StartUtc = data.StartUtc;
        calendarEvent.EndUtc = data.EndUtc;
        calendarEvent.RequesterPersonId = data.Links.PersonId;
        calendarEvent.CustomerId = data.Links.CustomerId;
        calendarEvent.TicketId = input.TicketId;
        calendarEvent.ProjectId = input.ProjectId;
    }

    private void SetRecurrence(CalendarEvent calendarEvent, CalendarRecurrenceRule? rule, CalendarCompanyContext company)
    {
        calendarEvent.RecurrenceRule = rule?.ToRuleString();
        calendarEvent.RecurrenceEndUtc = rule is null ? null : expander.GetSeriesEndUtc(AsSeries(calendarEvent), company.TimeZoneId);
    }

    /// <summary>
    /// Applies the body to an activity row and writes the log: <c>Rescheduled</c> when the times change (with the
    /// reschedule status of the company when the actor is not the owner, R6), <c>Updated</c> otherwise, and
    /// <c>MovedToCalendar</c> when the calendar changes.
    /// </summary>
    private async Task<Response<bool>> ApplyUpdateAsync(
        CalendarSession session,
        CalendarEvent calendarEvent,
        ICalendarEventInput input,
        PreparedInput data,
        bool actorIsOwner,
        DateTime previousStartUtc,
        DateTime previousEndUtc,
        CalendarEventLogAction? forcedAction,
        bool logNew = true,
        string? notes = null)
    {
        var previousCalendarId = calendarEvent.CalendarId;
        var previousStatusId = calendarEvent.CalendarStatusCompanyId;
        var rescheduled = data.StartUtc != previousStartUtc || data.EndUtc != previousEndUtc;

        Apply(calendarEvent, input, data);

        if (rescheduled && !actorIsOwner)
        {
            var rescheduleStatus = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(session.Company.RescheduledByOthersStatusCompanyId);
            if (await catalogReader.GetUsableStatusAsync(session.CompanyId, rescheduleStatus) is null)
            {
                return Response<bool>.Error(CalendarErrorCodes.StatusNotEnabled,
                    ["The reschedule status configured in CalendarCompanies is not active."]);
            }

            calendarEvent.CalendarStatusCompanyId = rescheduleStatus!.Id;
            calendarEvent.StatusChangedUtc = DateTime.UtcNow;
            calendarEvent.StatusChangedByUserId = session.ActorUserId;
        }

        await unitOfWork.CalendarEvents.UpdateAsync(calendarEvent);

        if (logNew)
        {
            var action = forcedAction ?? (rescheduled ? CalendarEventLogAction.Rescheduled : CalendarEventLogAction.Updated);
            var change = rescheduled || previousStatusId != calendarEvent.CalendarStatusCompanyId
                ? new CalendarEventLogChange(previousStartUtc, previousEndUtc, calendarEvent.StartUtc, calendarEvent.EndUtc,
                    previousStatusId, calendarEvent.CalendarStatusCompanyId)
                : null;
            await logWriter.WriteAsync(calendarEvent, action, session.Actor, change, notes);
        }

        if (previousCalendarId != calendarEvent.CalendarId)
        {
            await logWriter.WriteAsync(calendarEvent, CalendarEventLogAction.MovedToCalendar, session.Actor, null,
                $"Moved from calendar {previousCalendarId}.");
        }

        return Ok(true);
    }

    /// <summary>
    /// Returns the exception that replaces an occurrence, creating it from the master (an exception inherits every
    /// field it does not change, R7).
    /// </summary>
    private async Task<CalendarEvent> GetOrCreateExceptionAsync(CalendarEvent master, DateTime occurrenceStartUtc, DateTime occurrenceEndUtc)
    {
        var existing = (await unitOfWork.CalendarEvents.GetExceptionsAsync(master.Id))
            .FirstOrDefault(e => e.OriginalStartUtc == occurrenceStartUtc);
        if (existing is not null)
        {
            return existing;
        }

        var exception = CopyOf(master);
        exception.SeriesMasterId = master.Id;
        exception.OriginalStartUtc = occurrenceStartUtc;
        exception.StartUtc = occurrenceStartUtc;
        exception.EndUtc = occurrenceEndUtc;
        exception.RecurrenceRule = null;
        exception.RecurrenceEndUtc = null;
        await unitOfWork.CalendarEvents.InsertAsync(exception);
        return exception;
    }

    /// <summary>
    /// "This and following" (R7): the original series ends right before the occurrence (<c>UNTIL</c>), a new master
    /// starts at the occurrence and the later exceptions move to it (keys shifted by <paramref name="delta"/>).
    /// Both masters get a <c>SeriesSplit</c> log row.
    /// </summary>
    private async Task<Response<CalendarEvent>> SplitSeriesAsync(
        CalendarSession session,
        CalendarEvent master,
        DateTime occurrenceStartUtc,
        TimeSpan delta,
        CalendarRecurrenceRule? newRule,
        string? notes = null)
    {
        var originalRule = CalendarRecurrenceRule.Parse(master.RecurrenceRule).Data!;
        var occurrenceEnd = occurrenceStartUtc + (master.EndUtc - master.StartUtc);

        if (newRule is null)
        {
            newRule = originalRule;
            if (originalRule.Count.HasValue)
            {
                var before = expander.Expand(AsSeries(master), master.StartUtc, occurrenceStartUtc, session.Company.TimeZoneId)
                    .Count(o => o.StartUtc < occurrenceStartUtc);
                newRule = originalRule.WithCount(Math.Max(1, originalRule.Count.Value - before));
            }
        }

        var newMaster = CopyOf(master);
        newMaster.StartUtc = occurrenceStartUtc;
        newMaster.EndUtc = occurrenceEnd;
        newMaster.RecurrenceRule = newRule.ToRuleString();
        newMaster.RecurrenceEndUtc = expander.GetSeriesEndUtc(AsSeries(newMaster), session.Company.TimeZoneId);

        master.RecurrenceRule = originalRule.WithUntil(occurrenceStartUtc.AddSeconds(-1)).ToRuleString();
        master.RecurrenceEndUtc = expander.GetSeriesEndUtc(AsSeries(master), session.Company.TimeZoneId);
        await unitOfWork.CalendarEvents.UpdateAsync(master);
        await unitOfWork.CalendarEvents.InsertAsync(newMaster);

        foreach (var exception in (await unitOfWork.CalendarEvents.GetExceptionsAsync(master.Id))
                     .Where(e => e.OriginalStartUtc >= occurrenceStartUtc))
        {
            exception.SeriesMasterId = newMaster.Id;
            exception.OriginalStartUtc = exception.OriginalStartUtc!.Value + delta;
            await unitOfWork.CalendarEvents.UpdateAsync(exception);
        }

        var note = notes is null ? $"Series split at {occurrenceStartUtc:O}." : $"{notes} Series split at {occurrenceStartUtc:O}.";
        await logWriter.WriteAsync(master, CalendarEventLogAction.SeriesSplit, session.Actor, null, note);
        await logWriter.WriteAsync(newMaster, CalendarEventLogAction.SeriesSplit, session.Actor,
            new CalendarEventLogChange(NewStartUtc: newMaster.StartUtc, NewEndUtc: newMaster.EndUtc, NewStatusCompanyId: newMaster.CalendarStatusCompanyId), note);
        return Ok(newMaster);
    }

    /// <summary>
    /// Moves the keys of the exceptions with the series (the series kept its exceptions, R7). Two saves inside the
    /// transaction so the unique index <c>(SeriesMasterId, OriginalStartUtc)</c> never sees two rows with the same
    /// key while they move.
    /// </summary>
    private async Task ShiftExceptionKeysAsync(Guid masterId, TimeSpan delta, CancellationToken cancellationToken)
    {
        var exceptions = await unitOfWork.CalendarEvents.GetExceptionsAsync(masterId, cancellationToken);
        if (exceptions.Count == 0)
        {
            return;
        }

        var parking = TimeSpan.FromDays(365 * 500);
        foreach (var exception in exceptions)
        {
            exception.OriginalStartUtc = exception.OriginalStartUtc!.Value - parking;
            await unitOfWork.CalendarEvents.UpdateAsync(exception);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var exception in exceptions)
        {
            exception.OriginalStartUtc = exception.OriginalStartUtc!.Value + parking + delta;
            await unitOfWork.CalendarEvents.UpdateAsync(exception);
        }
    }

    private async Task<Response<CalendarEventDto>> SaveValidateAndBuildAsync(
        CalendarSession session,
        CalendarEvent calendarEvent,
        CalendarActivityCompany activityCompany,
        string message,
        CancellationToken cancellationToken)
    {
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var validation = await ValidateEventAsync(session, calendarEvent, activityCompany, cancellationToken);
        return validation.IsSuccess
            ? await BuildResultAsync(calendarEvent, session.Company, message)
            : Response<CalendarEventDto>.Error(validation.Message, validation.Errors);
    }

    /// <summary>
    /// Runs the schedule guard over the saved activity: its own row is excluded; the occurrences replaced by
    /// exceptions are skipped (the exceptions compete as they are stored).
    /// </summary>
    private async Task<Response<bool>> ValidateEventAsync(
        CalendarSession session,
        CalendarEvent calendarEvent,
        CalendarActivityCompany activityCompany,
        CancellationToken cancellationToken,
        bool overlapOnly = false)
    {
        var status = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(calendarEvent.CalendarStatusCompanyId);
        var blocksTime = status is not null && (await catalogReader.GetGlobalStatusAsync(status))?.BlocksTime == true;

        IReadOnlySet<DateTime>? skipped = null;
        if (calendarEvent.RecurrenceRule is not null)
        {
            skipped = (await unitOfWork.CalendarEvents.GetExceptionsAsync(calendarEvent.Id, cancellationToken))
                .Where(e => e.OriginalStartUtc.HasValue)
                .Select(e => CalendarTime.AsUtc(e.OriginalStartUtc!.Value))
                .ToHashSet();
        }

        var candidates = scheduleGuard.BuildCandidates(
            calendarEvent.StartUtc, calendarEvent.EndUtc, calendarEvent.RecurrenceRule, calendarEvent.IsAllDay, session.Company.TimeZoneId, skipped);

        var check = new CalendarScheduleCheck(
            session.Company,
            calendarEvent.OwnerUserId,
            ActorIsOwner: overlapOnly || session.ActorUserId == calendarEvent.OwnerUserId,
            activityCompany.AllowsOverlap,
            activityCompany.RequiresBusinessHours,
            blocksTime,
            calendarEvent.IsAllDay,
            candidates,
            new HashSet<Guid> { calendarEvent.Id },
            NoOccurrences);

        return await scheduleGuard.ValidateAsync(check, cancellationToken);
    }

    /// <summary>
    /// Builds the DTO of an activity as the commands return it (also used by the channel intake to answer a
    /// duplicate or idempotent request with the current state).
    /// </summary>
    public async Task<Response<CalendarEventDto>> BuildResultAsync(CalendarEvent calendarEvent, CalendarCompanyContext company, string message)
    {
        var activityCompany = await unitOfWork.GetRepository<CalendarActivityCompany>().GetAsync(calendarEvent.CalendarActivityCompanyId);
        var activity = activityCompany is null ? null : await unitOfWork.GetRepository<CalendarActivity>().GetAsync(activityCompany.CalendarActivityId);
        var statusCompany = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(calendarEvent.CalendarStatusCompanyId);
        var status = statusCompany is null ? null : await catalogReader.GetGlobalStatusAsync(statusCompany);
        var calendar = await unitOfWork.GetRepository<UserCalendar>().GetAsync(calendarEvent.CalendarId);
        var owner = await unitOfWork.GetRepository<ApplicationUser>().GetAsync(calendarEvent.OwnerUserId);
        var channel = await unitOfWork.GetRepository<CommunicationChannel>().GetAsync(calendarEvent.OriginChannelId);
        var person = calendarEvent.RequesterPersonId is { } pid
            ? (await unitOfWork.GetRepository<Person>()
                .GetAllIncludingDeletedAsync(p => p.Id == pid && p.CompanyId == calendarEvent.CompanyId && p.GcRecord == 0))
                .FirstOrDefault()
            : null;
        var customer = calendarEvent.CustomerId is { } cid
            ? (await unitOfWork.GetRepository<Customer>()
                .GetAllIncludingDeletedAsync(c => c.Id == cid && c.CompanyId == calendarEvent.CompanyId && c.GcRecord == 0))
                .FirstOrDefault()
            : null;

        var dto = new CalendarEventDto
        {
            Id = calendarEvent.Id,
            CalendarId = calendarEvent.CalendarId,
            CalendarName = calendar?.Name ?? string.Empty,
            CalendarColor = calendar?.Color ?? string.Empty,
            OwnerUserId = calendarEvent.OwnerUserId,
            OwnerName = owner is null ? string.Empty : $"{owner.FirstName} {owner.LastName}".Trim(),
            Activity = new CalendarActivityRefDto(
                calendarEvent.CalendarActivityCompanyId,
                activity?.Code ?? string.Empty,
                activityCompany?.Name ?? string.Empty,
                activityCompany?.Color ?? string.Empty,
                activityCompany?.AllowsOverlap ?? false),
            Status = new CalendarStatusRefDto(
                calendarEvent.CalendarStatusCompanyId,
                status?.Code ?? string.Empty,
                statusCompany?.Name ?? string.Empty,
                statusCompany?.Color ?? string.Empty,
                status?.BlocksTime ?? false,
                status?.IsFinal ?? false),
            Title = calendarEvent.Title,
            Summary = calendarEvent.Summary,
            Description = calendarEvent.Description,
            Location = calendarEvent.Location,
            VideoCallUrl = calendarEvent.VideoCallUrl,
            IsAllDay = calendarEvent.IsAllDay,
            StartUtc = CalendarTime.AsUtc(calendarEvent.StartUtc),
            EndUtc = CalendarTime.AsUtc(calendarEvent.EndUtc),
            IsRecurring = calendarEvent.RecurrenceRule is not null || calendarEvent.SeriesMasterId.HasValue,
            RecurrenceRule = calendarEvent.RecurrenceRule,
            RecurrenceDescription = calendarEvent.RecurrenceRule is null ? null : CalendarRecurrenceRule.Parse(calendarEvent.RecurrenceRule).Data?.Describe(),
            RecurrenceEndUtc = calendarEvent.RecurrenceEndUtc,
            SeriesMasterId = calendarEvent.SeriesMasterId,
            OriginalStartUtc = calendarEvent.OriginalStartUtc,
            Origin = new CalendarOriginDto(channel?.Code ?? string.Empty, calendarEvent.OriginType.ToString(), calendarEvent.OriginIdentifier, calendarEvent.OriginUserId),
            RequesterPersonId = calendarEvent.RequesterPersonId,
            RequesterPersonName = person is null ? null : PersonDisplayName(person),
            CustomerId = calendarEvent.CustomerId,
            CustomerCode = customer?.CustomerCode,
            IsCustomerActive = customer?.IsActive,
            TicketId = calendarEvent.TicketId,
            ProjectId = calendarEvent.ProjectId,
            StatusChangedUtc = calendarEvent.StatusChangedUtc,
            StatusChangedByUserId = calendarEvent.StatusChangedByUserId,
            TimeZone = company.TimeZoneId,
            CreatedAt = calendarEvent.Created
        };

        return new Response<CalendarEventDto> { IsSuccess = true, Data = dto, Message = message };
    }

    /// <summary>
    /// SPEC 67 — mirrors the <c>ByIdSql</c> projection: legal persons get their commercial name, physical persons
    /// get the trimmed CONCAT_WS of their name parts.
    /// </summary>
    private static string PersonDisplayName(Person person) =>
        person.PersonType == PersonType.Legal && !string.IsNullOrWhiteSpace(person.CommercialName)
            ? person.CommercialName
            : string.Join(' ',
                new[] { person.FirstName, person.MiddleName, person.LastName, person.SecondLastName }
                    .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

    private static CalendarEvent CopyOf(CalendarEvent source) => new()
    {
        CompanyId = source.CompanyId,
        CalendarId = source.CalendarId,
        OwnerUserId = source.OwnerUserId,
        CalendarActivityCompanyId = source.CalendarActivityCompanyId,
        CalendarStatusCompanyId = source.CalendarStatusCompanyId,
        Title = source.Title,
        Summary = source.Summary,
        Description = source.Description,
        Location = source.Location,
        VideoCallUrl = source.VideoCallUrl,
        IsAllDay = source.IsAllDay,
        StartUtc = source.StartUtc,
        EndUtc = source.EndUtc,
        RecurrenceRule = source.RecurrenceRule,
        RecurrenceEndUtc = source.RecurrenceEndUtc,
        OriginChannelId = source.OriginChannelId,
        OriginType = source.OriginType,
        OriginUserId = source.OriginUserId,
        OriginIdentifier = source.OriginIdentifier,
        RequesterPersonId = source.RequesterPersonId,
        CustomerId = source.CustomerId,
        TicketId = source.TicketId,
        ProjectId = source.ProjectId,
        StatusChangedUtc = source.StatusChangedUtc,
        StatusChangedByUserId = source.StatusChangedByUserId
    };

    private static CalendarRecurrenceSeries AsSeries(CalendarEvent calendarEvent) =>
        new(calendarEvent.StartUtc, calendarEvent.EndUtc, calendarEvent.RecurrenceRule!, calendarEvent.IsAllDay);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Response<T> Ok<T>(T data) => new() { IsSuccess = true, Data = data };

}

/// <summary>
/// Origin of an activity written on behalf of an external requester (SPEC 45): the channel and identifier replace the
/// <c>WEB</c> channel of the actor in the activity and its log, and <see cref="Reference"/> is the external message id
/// used to deduplicate retries.
/// </summary>
public sealed record CalendarExternalOrigin(Guid ChannelId, CalendarEventOriginType OriginType, string Identifier, string? Reference);

/// <summary>
/// Tenant, actor and calendar configuration of a calendar command.
/// </summary>
public sealed record CalendarSession(Guid CompanyId, Guid ActorUserId, CalendarCompanyContext Company, CalendarActor Actor);

internal sealed record PreparedInput(UserCalendar Calendar, UsableActivity Activity, DateTime StartUtc, DateTime EndUtc, ResolvedLinks Links);

/// <summary>
/// Pair of ids resolved by <see cref="CalendarEventCommandProcessor.ValidateLinksAsync"/> (SPEC 67): the two columns
/// are kept consistent on every write — both null, both pointing at the same person/customer pair, or a single
/// side that fills in the other.
/// </summary>
internal sealed record ResolvedLinks(Guid? PersonId, Guid? CustomerId);

internal enum CalendarEditTarget
{
    Single,
    Occurrence,
    Following,
    Series
}

internal sealed record CalendarTarget(CalendarEditTarget Kind, DateTime OccurrenceStartUtc, DateTime OccurrenceEndUtc);

internal sealed record CalendarTargetContext(CalendarSession Session, CalendarEvent Event, CalendarTarget Target);
