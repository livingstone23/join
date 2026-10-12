using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Access rules of the agent over an existing activity (SPEC 45): it must belong to the company and have the
/// identified person as requester, and it must not be the internal <c>PERSON_REVIEW</c> activity (otherwise
/// <c>CALENDAR_EVENT_NOT_FOUND</c>, never revealing internal activities or other people's), and it must not be in a
/// final status (<c>CALENDAR_INTAKE_EVENT_ALREADY_CLOSED</c>: the agent does
/// not reopen cancelled or completed activities).
/// </summary>
public sealed class IntakeEventGuard(IUnitOfWork unitOfWork, CalendarCatalogReader catalogReader)
{
    /// <summary> Loads the activity the agent may change. </summary>
    public async Task<Response<CalendarEvent>> OpenAsync(Guid companyId, Guid eventId, Guid requesterPersonId)
    {
        var calendarEvent = await unitOfWork.CalendarEvents.GetAsync(eventId);
        if (calendarEvent is null
            || calendarEvent.CompanyId != companyId
            || calendarEvent.GcRecord != 0
            || requesterPersonId == Guid.Empty
            || calendarEvent.RequesterPersonId != requesterPersonId)
        {
            return Response<CalendarEvent>.Error(CalendarErrorCodes.EventNotFound, ["The calendar activity was not found for the requester."]);
        }

        if (await IsPersonReviewAsync(calendarEvent))
        {
            return Response<CalendarEvent>.Error(CalendarErrorCodes.EventNotFound, ["The calendar activity was not found for the requester."]);
        }

        var status = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(calendarEvent.CalendarStatusCompanyId);
        var global = status is null ? null : await catalogReader.GetGlobalStatusAsync(status);
        if (global?.IsFinal == true)
        {
            return Response<CalendarEvent>.Error(CalendarErrorCodes.IntakeEventAlreadyClosed, ["The activity is closed and cannot be changed by the agent."]);
        }

        return new Response<CalendarEvent> { IsSuccess = true, Data = calendarEvent };
    }

    private async Task<bool> IsPersonReviewAsync(CalendarEvent calendarEvent)
    {
        var activityCompany = await unitOfWork.GetRepository<CalendarActivityCompany>().GetAsync(calendarEvent.CalendarActivityCompanyId);
        var activity = activityCompany is null ? null : await unitOfWork.GetRepository<CalendarActivity>().GetAsync(activityCompany.CalendarActivityId);
        return activity?.Code == CalendarCodes.Activities.PersonReview;
    }

    /// <summary>
    /// Scope of a change requested without one: <c>ThisOccurrence</c> for a series master (SPEC 45), none otherwise.
    /// </summary>
    public static CalendarRecurrenceEditScope? DefaultScope(CalendarEvent calendarEvent, CalendarRecurrenceEditScope? requested) =>
        requested ?? (calendarEvent.RecurrenceRule is not null && !calendarEvent.SeriesMasterId.HasValue
            ? CalendarRecurrenceEditScope.ThisOccurrence
            : null);
}
