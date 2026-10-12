using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// "Usable" rules of the company catalogs (SPEC 44, data model 2 and 4): a company row is usable when it and its
/// global row are both active and not deleted. The logic compares statuses and types by <c>Code</c>, never by name.
/// </summary>
public sealed class CalendarCatalogReader(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Returns the activity type of the company with its global type when both are usable, otherwise <c>null</c>
    /// (<c>CALENDAR_ACTIVITY_NOT_ENABLED</c>).
    /// </summary>
    public async Task<UsableActivity?> GetUsableActivityAsync(Guid companyId, Guid activityCompanyId)
    {
        var activityCompany = await unitOfWork.GetRepository<CalendarActivityCompany>().GetAsync(activityCompanyId);
        if (activityCompany is null || activityCompany.CompanyId != companyId || activityCompany.GcRecord != 0 || !activityCompany.IsActive)
        {
            return null;
        }

        var activity = await unitOfWork.GetRepository<CalendarActivity>().GetAsync(activityCompany.CalendarActivityId);
        return activity is null || activity.GcRecord != 0 || !activity.IsActive
            ? null
            : new UsableActivity(activityCompany, activity);
    }

    /// <summary>
    /// Returns the global status of <paramref name="status"/> when the company status is usable, otherwise <c>null</c>
    /// (<c>CALENDAR_STATUS_NOT_ENABLED</c>).
    /// </summary>
    public async Task<CalendarStatus?> GetUsableStatusAsync(Guid companyId, CalendarStatusCompany? status)
    {
        if (status is null || status.CompanyId != companyId || status.GcRecord != 0 || !status.IsActive)
        {
            return null;
        }

        var global = await unitOfWork.GetRepository<CalendarStatus>().GetAsync(status.CalendarStatusId);
        return global is null || global.GcRecord != 0 || !global.IsActive ? null : global;
    }

    /// <summary>
    /// Returns the global status of any company status (usable or not), or <c>null</c>.
    /// </summary>
    public async Task<CalendarStatus?> GetGlobalStatusAsync(CalendarStatusCompany status) =>
        await unitOfWork.GetRepository<CalendarStatus>().GetAsync(status.CalendarStatusId);

    /// <summary>
    /// Returns the active company status whose global status has <paramref name="code"/>, or <c>null</c>.
    /// </summary>
    public async Task<CalendarStatusCompany?> GetCompanyStatusByCodeAsync(Guid companyId, string code)
    {
        var global = (await unitOfWork.GetRepository<CalendarStatus>()
                .GetAllIncludingDeletedAsync(x => x.Code == code && x.GcRecord == 0))
            .FirstOrDefault();

        if (global is null)
        {
            return null;
        }

        var companyStatusId = (await unitOfWork.GetRepository<CalendarStatusCompany>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.CalendarStatusId == global.Id && x.GcRecord == 0))
            .Select(x => x.Id)
            .FirstOrDefault();

        return companyStatusId == Guid.Empty
            ? null
            : await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(companyStatusId);
    }
}

/// <summary>
/// A usable activity type of the company and its global type.
/// </summary>
public sealed record UsableActivity(CalendarActivityCompany ActivityCompany, CalendarActivity Activity);
