using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex;

/// <summary>
/// SPEC 68 — query parameters for <c>GET /CalendarActivityIndex</c>. <see cref="FromUtc"/> and
/// <see cref="ToUtc"/> are mandatory; the rest narrow the result without breaking the visibility scope.
/// </summary>
public sealed record CalendarActivityIndexQuery : IRequest<Response<PagedResult<CalendarActivityIndexItemDto>>>
{
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }
    public IReadOnlyCollection<Guid>? ActivityCompanyIds { get; init; }
    public IReadOnlyCollection<Guid>? StatusCompanyIds { get; init; }

    /// <summary> Owners to include. Without the per-user flag the handler ignores this and pins the current user. </summary>
    public IReadOnlyCollection<Guid>? OwnerUserIds { get; init; }

    /// <summary> Calendars to include. Without the per-user flag the handler ignores this. </summary>
    public IReadOnlyCollection<Guid>? CalendarIds { get; init; }

    public Guid? RequesterPersonId { get; init; }
    public IReadOnlyCollection<Guid>? OriginChannelIds { get; init; }

    /// <summary> Free-text term matched against title, summary and location (case-insensitive). </summary>
    public string? Search { get; init; }

    /// <summary> When <c>false</c>, <c>CANCELLED</c> occurrences are excluded. </summary>
    public bool IncludeCancelled { get; init; }

    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;

    [JsonIgnore]
    public bool Export { get; init; }
}