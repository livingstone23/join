using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.UserCalendars;

/// <summary>
/// Shared steps of the <c>UserCalendars</c> commands: tenant, current user, calendar configuration (R1) and the
/// owner check (R2: another user's calendar answers <c>CALENDAR_NOT_FOUND</c>, never 403).
/// </summary>
public sealed class UserCalendarCommandContext(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarUserDefaultCoordinator defaultCoordinator,
    ICalendarMapper mapper)
{
    /// <summary>
    /// Resolves the tenant and the current user, and requires the calendar configuration.
    /// </summary>
    public async Task<Response<(Guid CompanyId, Guid UserId)>> OpenAsync(CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<(Guid, Guid)>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId))
        {
            return Response<(Guid, Guid)>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        var company = await companyGuard.GetContextAsync(companyId, cancellationToken);
        return company.IsSuccess
            ? new Response<(Guid, Guid)> { IsSuccess = true, Data = (companyId, userId) }
            : Response<(Guid, Guid)>.Error(company.Message, company.Errors);
    }

    /// <summary>
    /// Returns a tracked calendar of the user, or <c>null</c>.
    /// </summary>
    public async Task<UserCalendar?> FindOwnAsync(Guid companyId, Guid userId, Guid calendarId)
    {
        var calendar = await unitOfWork.GetRepository<UserCalendar>().GetAsync(calendarId);
        return calendar is null || calendar.CompanyId != companyId || calendar.OwnerUserId != userId || calendar.GcRecord != 0 ? null : calendar;
    }

    /// <summary>
    /// Whether another active calendar of the owner already uses the name.
    /// </summary>
    public async Task<bool> IsNameTakenAsync(Guid companyId, Guid userId, string name, Guid? excludeId) =>
        (await unitOfWork.GetRepository<UserCalendar>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.OwnerUserId == userId && x.Name == name && x.GcRecord == 0 && x.Id != (excludeId ?? Guid.Empty)))
        .Any();

    /// <summary>
    /// Blocks deactivating or deleting the default calendar or one with upcoming non-cancelled activities.
    /// </summary>
    public async Task<Response<bool>> CanRetireAsync(UserCalendar calendar, CancellationToken cancellationToken)
    {
        if (await defaultCoordinator.IsDefaultAsync(calendar.CompanyId, calendar.OwnerUserId, calendar.Id))
        {
            return Response<bool>.Error(CalendarErrorCodes.CalendarIsDefault, ["The default calendar cannot be deactivated or deleted; set another one as default first."]);
        }

        var upcoming = await unitOfWork.CalendarEvents.CountUpcomingActiveEventsInCalendarAsync(calendar.Id, DateTime.UtcNow, cancellationToken);
        return upcoming > 0
            ? Response<bool>.Error(CalendarErrorCodes.CalendarHasActiveEvents, [$"The calendar has {upcoming} upcoming activities that are not cancelled."])
            : new Response<bool> { IsSuccess = true, Data = true };
    }

    /// <summary>
    /// Maps a calendar with its default flag.
    /// </summary>
    public async Task<UserCalendarDto> ToDtoAsync(UserCalendar calendar) =>
        mapper.ToDto(calendar) with { IsDefault = await defaultCoordinator.IsDefaultAsync(calendar.CompanyId, calendar.OwnerUserId, calendar.Id) };
}
