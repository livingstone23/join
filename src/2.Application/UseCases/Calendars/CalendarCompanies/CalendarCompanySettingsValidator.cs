using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies;

/// <summary>
/// Shared rules of <c>POST</c> and <c>PUT /CalendarCompanies</c> (SPEC 44, data model 5): IANA zone, business hours
/// (both, end after start) or 24 hours, default user of the company, usable reschedule status, and no user with
/// own hours outside the company range.
/// </summary>
public sealed class CalendarCompanySettingsValidator(IUnitOfWork unitOfWork, CalendarCatalogReader catalogReader)
{
    public static Response<bool> ValidateZoneAndHours(string timeZone, bool isOpen24Hours, TimeOnly? start, TimeOnly? end)
    {
        if (CalendarTime.FindTimeZone(timeZone) is null)
        {
            return Response<bool>.Error(CalendarErrorCodes.InvalidTimeZone, [$"'{timeZone}' is not a valid IANA time zone."]);
        }

        if (!isOpen24Hours && (start is null || end is null || end <= start))
        {
            return Response<bool>.Error(CalendarErrorCodes.InvalidBusinessHours,
                ["businessHoursStart and businessHoursEnd are required and the end must be later than the start (no night shifts)."]);
        }

        return new Response<bool> { IsSuccess = true, Data = true };
    }

    public async Task<bool> IsCompanyMemberAsync(Guid companyId, Guid userId) =>
        (await unitOfWork.GetRepository<UserCompany>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
        .Any();

    /// <summary>
    /// Returns the reschedule status: the requested one when usable, otherwise (when none is requested) the
    /// company's <c>RESCHEDULED</c> status.
    /// </summary>
    public async Task<CalendarStatusCompany?> ResolveRescheduleStatusAsync(Guid companyId, Guid? requestedStatusId)
    {
        var status = requestedStatusId is { } id
            ? await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(id)
            : await catalogReader.GetCompanyStatusByCodeAsync(companyId, CalendarCodes.Statuses.Rescheduled);
        return await catalogReader.GetUsableStatusAsync(companyId, status) is null ? null : status;
    }

    /// <summary>
    /// Names of the users whose own hours fall outside the new company hours (empty for a 24-hour company).
    /// </summary>
    public async Task<IReadOnlyList<string>> GetUsersOutOfRangeAsync(Guid companyId, bool isOpen24Hours, TimeOnly? start, TimeOnly? end)
    {
        if (isOpen24Hours || start is null || end is null)
        {
            return [];
        }

        var outOfRange = (await unitOfWork.GetRepository<CalendarUserConfiguration>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId
                    && x.GcRecord == 0
                    && x.BusinessHoursStart != null
                    && x.BusinessHoursEnd != null
                    && (x.BusinessHoursStart < start || x.BusinessHoursEnd > end)))
            .Select(x => x.UserId)
            .ToList();

        if (outOfRange.Count == 0)
        {
            return [];
        }

        return (await unitOfWork.GetRepository<ApplicationUser>().GetAllIncludingDeletedAsync(u => outOfRange.Contains(u.Id)))
            .Select(u => $"{u.FirstName} {u.LastName}".Trim() + (u.Email is null ? string.Empty : $" <{u.Email}>"))
            .ToList();
    }
}
