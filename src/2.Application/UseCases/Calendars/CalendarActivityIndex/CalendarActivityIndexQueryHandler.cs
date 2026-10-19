using System.Data;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// SPEC 68 — handles the activity index query. Validates the 92-day range cap, applies the visibility scope
/// (own activities only, or company-wide when the per-user flag is on), fetches candidate rows through the
/// <see cref="CalendarActivityIndexReader"/>, expands series via <see cref="CalendarOccurrenceExpansion"/>,
/// re-applies every filter per occurrence (including <c>CANCELLED</c> and the search text), sorts by
/// <see cref="ExpandedOccurrence{T}.StartUtc"/>, paginates and projects to
/// <see cref="CalendarActivityIndexItemDto"/>. Export = first page of 10 000 rows.
/// </summary>
public sealed class CalendarActivityIndexQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICalendarActivityIndexReader reader,
    CalendarActivityVisibility visibility,
    ICalendarRecurrenceExpander expander)
    : IRequestHandler<CalendarActivityIndexQuery, Response<PagedResult<CalendarActivityIndexItemDto>>>
{
    /// <summary> Maximum range allowed by the index (a quarter plus edges, SPEC 68 rule 6). </summary>
    public const int MaxRangeDays = 92;

    /// <summary> Maximum page size when the request asks for export (SPEC 68 rule 8). </summary>
    public const int ExportPageSize = 10_000;

    public async Task<Response<PagedResult<CalendarActivityIndexItemDto>>> Handle(
        CalendarActivityIndexQuery request,
        CancellationToken cancellationToken)
    {
        var context = await visibility.ResolveAsync(cancellationToken);
        if (context.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<CalendarActivityIndexItemDto>>.Error(CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var fromUtc = CalendarTime.AsUtc(request.FromUtc);
        var toUtc = CalendarTime.AsUtc(request.ToUtc);
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(MaxRangeDays))
        {
            return Response<PagedResult<CalendarActivityIndexItemDto>>.Error(CalendarErrorCodes.RangeTooLarge,
                [$"'to' must be later than 'from' and the range cannot exceed {MaxRangeDays} days."]);
        }

        // Visibility: own-only when the user has no flag, ignoring the ownerUserIds filter.
        IReadOnlyCollection<Guid>? ownerFilter = context.CanViewOthersActivities ? request.OwnerUserIds : new[] { context.CurrentUserId };
        IReadOnlyCollection<Guid>? calendarFilter = context.CanViewOthersActivities ? request.CalendarIds : null;

        var criteria = new CalendarActivityIndexCriteria(
            CompanyId: context.CompanyId,
            FromUtc: fromUtc,
            ToUtc: toUtc,
            OwnerUserIds: ownerFilter,
            CalendarIds: calendarFilter,
            ActivityCompanyIds: request.ActivityCompanyIds,
            StatusCompanyIds: request.StatusCompanyIds,
            RequesterPersonId: request.RequesterPersonId,
            OriginChannelIds: request.OriginChannelIds,
            Search: request.Search,
            IncludeCancelled: request.IncludeCancelled);

        var timeZone = await visibility.GetCompanyTimeZoneAsync(context.CompanyId, cancellationToken);
        if (timeZone is null)
        {
            return Response<PagedResult<CalendarActivityIndexItemDto>>.Error(CalendarErrorCodes.CompanyNotConfigured,
                ["The company has no calendar configuration."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var rows = await reader.ReadAsync(connection, criteria, cancellationToken);
        var occurrences = CalendarOccurrenceExpansion.Expand(rows, fromUtc, toUtc, timeZone, expander);

        // Every filter is re-applied per occurrence: the reader returns series masters and exceptions unfiltered so
        // an exception that fails a filter still replaces (hides) the master occurrence it keys.
        var term = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        var filtered = occurrences.Where(o =>
            Matches(criteria.OwnerUserIds, o.Source.OwnerUserId)
            && Matches(criteria.CalendarIds, o.Source.CalendarId)
            && Matches(criteria.ActivityCompanyIds, o.Source.ActivityCompanyId)
            && Matches(criteria.StatusCompanyIds, o.Source.StatusCompanyId)
            && Matches(criteria.OriginChannelIds, o.Source.OriginChannelId)
            && (criteria.RequesterPersonId is null || o.Source.RequesterPersonId == criteria.RequesterPersonId)
            && (request.IncludeCancelled || o.Source.StatusCode != CalendarCodes.Statuses.Cancelled)
            && (term is null
                || o.Source.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (o.Source.Summary?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (o.Source.Location?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)));

        var sorted = filtered
            .OrderBy(o => o.StartUtc)
            .ThenBy(o => o.EndUtc)
            .ToList();

        var pageSize = request.Export ? ExportPageSize : Math.Clamp(request.PageSize, 1, 500);
        var pageNumber = Math.Max(1, request.PageNumber);
        var totalCount = sorted.Count;
        var totalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
        var page = sorted
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(o => ToDto(o))
            .ToList();

        return new Response<PagedResult<CalendarActivityIndexItemDto>>
        {
            IsSuccess = true,
            Message = "Calendar activities retrieved successfully.",
            Data = new PagedResult<CalendarActivityIndexItemDto>
            {
                Items = page,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages
            }
        };
    }

    private static bool Matches(IReadOnlyCollection<Guid>? filter, Guid value) =>
        filter is not { Count: > 0 } || filter.Contains(value);

    private static CalendarActivityIndexItemDto ToDto(ExpandedOccurrence<CalendarActivityIndexRow> occurrence) => new()
    {
        EventId = occurrence.EventId,
        ExceptionId = occurrence.ExceptionId,
        OccurrenceStartUtc = CalendarTime.AsUtc(occurrence.StartUtc),
        OccurrenceEndUtc = CalendarTime.AsUtc(occurrence.EndUtc),
        IsAllDay = occurrence.Source.IsAllDay,
        IsRecurring = occurrence.IsRecurring,
        Title = occurrence.Source.Title,
        ActivityCompanyId = occurrence.Source.ActivityCompanyId,
        ActivityName = occurrence.Source.ActivityName,
        ActivityColor = occurrence.Source.ActivityColor,
        StatusCompanyId = occurrence.Source.StatusCompanyId,
        StatusName = occurrence.Source.StatusName,
        OwnerUserId = occurrence.Source.OwnerUserId,
        OwnerName = occurrence.Source.OwnerName.Trim(),
        CalendarName = occurrence.Source.CalendarName,
        RequesterPersonId = occurrence.Source.RequesterPersonId,
        RequesterPersonName = occurrence.Source.RequesterPersonName,
        CustomerCode = occurrence.Source.CustomerCode,
        OriginChannelCode = occurrence.Source.OriginChannelCode,
        Location = occurrence.Source.Location
    };
}