using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Creates the default calendar of a user in a company (SPEC 44, decision 20). Only adds rows to
/// <c>Calendar.UserCalendars</c> and <c>Calendar.UserConfigurations</c>; never changes the user or its membership.
/// It does NOT call <c>SaveAsync</c>: the rows join the caller's unit of work and transaction. Reads bypass the
/// tenant filter on purpose — <c>AddUserCompany</c> provisions a company other than the caller's.
/// </summary>
public sealed class CalendarUserProvisioner(IUnitOfWork unitOfWork)
{
    /// <summary> Name of the Calendar <see cref="SystemModule"/>. </summary>
    public const string CalendarModuleName = "Calendar";

    /// <summary> Color of the calendars created automatically. </summary>
    public const string DefaultCalendarColor = "#1E88E5";

    /// <summary>
    /// Creates the user's default calendar and configuration when the Calendar module is active for the company, the
    /// company has a <see cref="CalendarCompany"/> and the user has no configuration yet. Idempotent.
    /// </summary>
    /// <returns><c>true</c> when the calendar was added to the unit of work.</returns>
    public async Task<bool> EnsureUserCalendarAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await IsModuleActiveAsync(companyId))
        {
            return false;
        }

        var calendarCompany = (await unitOfWork.GetRepository<CalendarCompany>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();

        if (calendarCompany is null)
        {
            return false;
        }

        var alreadyProvisioned = (await unitOfWork.GetRepository<CalendarUserConfiguration>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
            .Any();

        if (alreadyProvisioned)
        {
            return false;
        }

        var calendar = new UserCalendar
        {
            CompanyId = companyId,
            OwnerUserId = userId,
            Name = calendarCompany.DefaultCalendarName,
            Color = DefaultCalendarColor,
            IsActive = true,
            SortOrder = 1
        };

        // Business hours stay null: the effective hours are the company's (SPEC 44, data model 7).
        var configuration = new CalendarUserConfiguration
        {
            CompanyId = companyId,
            UserId = userId,
            DefaultCalendarId = calendar.Id,
            WorkDays = CalendarWorkDays.Weekdays,
            DefaultView = CalendarViewType.Week,
            WeekStartsOn = DayOfWeek.Monday
        };

        await unitOfWork.GetRepository<UserCalendar>().InsertAsync(calendar);
        await unitOfWork.GetRepository<CalendarUserConfiguration>().InsertAsync(configuration);
        return true;
    }

    /// <summary>
    /// Whether the company has the Calendar module active (<see cref="CompanyModule"/>).
    /// </summary>
    public async Task<bool> IsModuleActiveAsync(Guid companyId)
    {
        var module = (await unitOfWork.GetRepository<SystemModule>()
                .GetAllIncludingDeletedAsync(x => x.Name == CalendarModuleName && x.GcRecord == 0 && x.IsActive))
            .FirstOrDefault();

        if (module is null)
        {
            return false;
        }

        return (await unitOfWork.GetRepository<CompanyModule>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.ModuleId == module.Id && x.GcRecord == 0 && x.IsActive))
            .Any();
    }
}
