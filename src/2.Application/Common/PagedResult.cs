using System.Collections.Generic;



namespace JOIN.Application.Common;



/// <summary>
/// Represents a paginated result set for API responses.
/// </summary>
/// <typeparam name="T">The type of item contained in the current page.</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// Gets or sets the items returned for the current page.
    /// </summary>
    public IReadOnlyCollection<T> Items { get; set; } = [];

    /// <summary>
    /// Gets or sets the current page number.
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Gets or sets the number of items requested for each page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Gets or sets the total number of records available.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of pages available.
    /// </summary>
    public int TotalPages { get; set; }
}