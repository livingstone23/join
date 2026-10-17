using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Finance;

/// <summary>
/// Builds the paged responses of the finance queries (SPEC 56).
/// </summary>
internal static class FinancePaging
{
    internal static Response<PagedResult<T>> Page<T>(IReadOnlyCollection<T> items, int pageNumber, int pageSize, int totalCount, string message) =>
        new()
        {
            IsSuccess = true,
            Message = message,
            Data = new PagedResult<T>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = totalCount
            }
        };
}
