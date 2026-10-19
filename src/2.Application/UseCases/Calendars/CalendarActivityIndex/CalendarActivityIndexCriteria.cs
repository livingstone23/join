namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// Filter shape passed from the controller/query handler to <see cref="CalendarActivityIndexReader"/>. The
/// handler resolves the visibility (own-only or company-wide) before building the criteria; the reader is a
/// plain Dapper read with no opinion on who is asking.
/// </summary>
/// <param name="CompanyId">Tenant scope.</param>
/// <param name="FromUtc">Start of the range (inclusive, UTC).</param>
/// <param name="ToUtc">End of the range (exclusive, UTC).</param>
/// <param name="OwnerUserIds">Owners to include; <c>null</c> means every owner of the company.</param>
/// <param name="CalendarIds">Calendars to include; <c>null</c> means every calendar.</param>
/// <param name="ActivityCompanyIds">Activity types to include; <c>null</c> means every type.</param>
/// <param name="StatusCompanyIds">Statuses to include; <c>null</c> means every status.</param>
/// <param name="RequesterPersonId">Person filter; <c>null</c> means every person.</param>
/// <param name="OriginChannelIds">Origin channels to include; <c>null</c> means every channel.</param>
/// <param name="Search">Free-text term matched against title, summary and location; <c>null</c> = no text filter.</param>
/// <param name="IncludeCancelled">When <c>false</c>, <c>CANCELLED</c> occurrences are filtered out after expansion.</param>
public sealed record CalendarActivityIndexCriteria(
    Guid CompanyId,
    DateTime FromUtc,
    DateTime ToUtc,
    IReadOnlyCollection<Guid>? OwnerUserIds,
    IReadOnlyCollection<Guid>? CalendarIds,
    IReadOnlyCollection<Guid>? ActivityCompanyIds,
    IReadOnlyCollection<Guid>? StatusCompanyIds,
    Guid? RequesterPersonId,
    IReadOnlyCollection<Guid>? OriginChannelIds,
    string? Search,
    bool IncludeCancelled);