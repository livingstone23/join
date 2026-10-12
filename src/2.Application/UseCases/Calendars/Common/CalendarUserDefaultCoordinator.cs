using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Keeps the invariant "one default calendar per user and company" (SPEC 44): the single
/// <see cref="CalendarUserConfiguration"/> row of the user points to the default calendar. Same role as
/// <c>PersonAddressDefaultCoordinator</c>.
/// </summary>
public sealed class CalendarUserDefaultCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Returns the tracked configuration of the user in the company, or <c>null</c>.
    /// </summary>
    public async Task<CalendarUserConfiguration?> GetConfigurationAsync(Guid companyId, Guid userId)
    {
        var configurationId = (await unitOfWork.GetRepository<CalendarUserConfiguration>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
            .Select(x => x.Id)
            .FirstOrDefault();

        return configurationId == Guid.Empty
            ? null
            : await unitOfWork.GetRepository<CalendarUserConfiguration>().GetAsync(configurationId);
    }

    /// <summary>
    /// Whether <paramref name="calendarId"/> is the default calendar of its owner.
    /// </summary>
    public async Task<bool> IsDefaultAsync(Guid companyId, Guid userId, Guid calendarId) =>
        (await unitOfWork.GetRepository<CalendarUserConfiguration>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0 && x.DefaultCalendarId == calendarId))
        .Any();

    /// <summary>
    /// Points the user's configuration at <paramref name="calendar"/>, which must be an active calendar of the
    /// same owner and company.
    /// </summary>
    public async Task<Response<bool>> SetDefaultAsync(UserCalendar calendar)
    {
        if (!calendar.IsActive)
        {
            return Response<bool>.Error(CalendarErrorCodes.CalendarInactive, ["An inactive calendar cannot be the default calendar."]);
        }

        var configuration = await GetConfigurationAsync(calendar.CompanyId, calendar.OwnerUserId);
        if (configuration is null)
        {
            return Response<bool>.Error(CalendarErrorCodes.UserConfigurationNotFound, ["The user has no calendar configuration."]);
        }

        configuration.DefaultCalendarId = calendar.Id;
        await unitOfWork.GetRepository<CalendarUserConfiguration>().UpdateAsync(configuration);
        return new Response<bool> { IsSuccess = true, Data = true };
    }
}
