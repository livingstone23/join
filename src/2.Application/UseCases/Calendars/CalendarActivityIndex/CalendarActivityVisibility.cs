using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// SPEC 68 — resolves the visibility scope of the activity index for the current tenant and user, plus the
/// <see cref="CalendarUserConfiguration.CanViewOthersActivities"/> flag the administrator toggles per user.
/// Reused by SPEC 69 (comments and documents). The Panel keeps its own role-based permission
/// (<c>CalendarOthers</c>, SPEC 44-post); this service only governs the index.
/// </summary>
public sealed class CalendarActivityVisibility(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
{
    /// <summary>
    /// Resolves the visibility for the current user. <c>HasCalendar</c> is <c>false</c> for users without a
    /// configuration row — the index still works (their own activities only) but the flag is reported as
    /// <c>false</c>.
    /// </summary>
    public async Task<CalendarActivityVisibilityContext> ResolveAsync(CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return CalendarActivityVisibilityContext.Unauthenticated();
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId) || userId == Guid.Empty)
        {
            return CalendarActivityVisibilityContext.Unauthenticated();
        }

        var configuration = (await unitOfWork.GetRepository<CalendarUserConfiguration>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.UserId == userId && x.GcRecord == 0))
            .FirstOrDefault();

        return new CalendarActivityVisibilityContext(companyId, userId, configuration?.CanViewOthersActivities ?? false);
    }

    /// <summary>
    /// Returns the company time zone configured for the calendar module, or <c>null</c> when the company has no
    /// calendar configuration. EF-only path — handy from query handlers that already have a unit of work.
    /// </summary>
    public async Task<string?> GetCompanyTimeZoneAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = (await unitOfWork.GetRepository<CalendarCompany>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();
        return company?.TimeZone;
    }
}

/// <summary>
/// Result of <see cref="CalendarActivityVisibility.ResolveAsync"/>: tenant, current user, and the
/// per-user "see others" flag. The flag is <c>false</c> when the user has no configuration row.
/// </summary>
/// <param name="CompanyId">Tenant the request is scoped to.</param>
/// <param name="CurrentUserId">User the request is attributed to.</param>
/// <param name="CanViewOthersActivities">
/// True when the current user can list the activities of every member of the company. When <c>false</c> the index
/// is restricted to the user's own activities; any <c>ownerUserIds</c> filter the request sends is ignored.
/// </param>
public sealed record CalendarActivityVisibilityContext(Guid CompanyId, Guid CurrentUserId, bool CanViewOthersActivities)
{
    /// <summary>
    /// Empty context used when the request has no tenant or no user — every caller should treat this as
    /// "no activities can be returned".
    /// </summary>
    public static CalendarActivityVisibilityContext Unauthenticated() => new(Guid.Empty, Guid.Empty, false);

    /// <summary>
    /// True when the current user can list the activities of every member of the company.
    /// </summary>
    public bool CanViewOthers() => CanViewOthersActivities;
}