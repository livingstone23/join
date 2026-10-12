using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Builds the paged responses of the calendar listings (SPEC 33 pagination).
/// </summary>
public static class CalendarPaging
{
    public static Response<PagedResult<T>> Page<T>(IReadOnlyCollection<T> items, int pageNumber, int pageSize, int totalCount, string message) =>
        new()
        {
            IsSuccess = true,
            Message = message,
            Data = new PagedResult<T>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
            }
        };
}
