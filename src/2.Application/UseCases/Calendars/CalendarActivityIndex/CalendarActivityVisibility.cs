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

    /// <summary>
    /// SPEC 69 — can the current user see the activity (rule 5)? Owner always; otherwise the
    /// <c>CanViewOthersActivities</c> flag. The Panel's <c>CalendarOthers.CanRead</c> rule is enforced by the
    /// controller / handler layer separately when relevant.
    /// </summary>
    public async Task<CalendarActivityAccessResult> ResolveEventAccessAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var context = await ResolveAsync(cancellationToken);
        if (context.CompanyId == Guid.Empty)
        {
            return CalendarActivityAccessResult.Unauthenticated();
        }

        var calendarEvent = await unitOfWork.CalendarEvents.GetAsync(eventId);
        if (calendarEvent is null
            || calendarEvent.CompanyId != context.CompanyId
            || calendarEvent.GcRecord != 0)
        {
            return CalendarActivityAccessResult.NotFound();
        }

        var visible = calendarEvent.OwnerUserId == context.CurrentUserId || context.CanViewOthersActivities;
        return visible
            ? CalendarActivityAccessResult.Visible(calendarEvent, context.CurrentUserId)
            : CalendarActivityAccessResult.NotFound();
    }
}

/// <summary>
/// SPEC 69 — visibility verdict of <see cref="CalendarActivityVisibility.ResolveEventAccessAsync"/>: either the
/// event was found and the current user can see it, or it is hidden (404). The visibility helper returns the
/// not-found verdict both when the event does not exist in the tenant and when it exists but the user lacks
/// the right to read it (so a foreign-tenant probe cannot distinguish the two cases).
/// </summary>
/// <param name="Found"><c>true</c> when the activity exists and is live in the current tenant.</param>
/// <param name="Visible"><c>true</c> when the user can read the activity.</param>
/// <param name="CalendarEvent">Loaded activity when <paramref name="Found"/>; <c>null</c> otherwise.</param>
/// <param name="CurrentUserId">User the visibility was resolved for.</param>
public sealed record CalendarActivityAccessResult(bool Found, bool CanRead, CalendarEvent? CalendarEvent, Guid CurrentUserId)
{
    public static CalendarActivityAccessResult Unauthenticated() => new(false, false, null, Guid.Empty);
    public static CalendarActivityAccessResult NotFound() => new(false, false, null, Guid.Empty);
    public static CalendarActivityAccessResult Visible(CalendarEvent calendarEvent, Guid currentUserId) =>
        new(true, true, calendarEvent, currentUserId);

    public bool IsFoundAndVisible => Found && CanRead && CalendarEvent is not null;
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