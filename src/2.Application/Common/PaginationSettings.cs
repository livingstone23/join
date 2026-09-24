namespace JOIN.Application.Common;



/// <summary>
/// Represents configurable pagination defaults that can be reused across paged endpoints.
/// </summary>
public class PaginationSettings
{
    /// <summary>
    /// Gets or sets the default page number used when the client does not provide one.
    /// </summary>
    public int DefaultPageNumber { get; set; } = 1;

    /// <summary>
    /// Gets or sets the default page size used when the client does not provide one.
    /// </summary>
    public int DefaultPageSize { get; set; } = 10;

    /// <summary>
    /// Gets or sets the maximum page size the API will allow.
    /// </summary>
    public int MaxPageSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the minimum page size the API will allow.
    /// </summary>
    public int MinPageSize { get; set; } = 1;

    /// <summary>
    /// Sanitizes a requested page number/page size pair against these limits: a missing or
    /// invalid <paramref name="pageNumber"/> (&lt; 1) falls back to <see cref="DefaultPageNumber"/>;
    /// a missing or invalid <paramref name="pageSize"/> (&lt; <see cref="MinPageSize"/>) falls back
    /// to <see cref="DefaultPageSize"/>, and any requested page size is capped at
    /// <see cref="MaxPageSize"/>. Inconsistent configuration (e.g. <see cref="MaxPageSize"/> below
    /// <see cref="MinPageSize"/>, or <see cref="DefaultPageSize"/> outside that range) is
    /// self-corrected before being applied, instead of producing an invalid result.
    /// </summary>
    public (int PageNumber, int PageSize) Sanitize(int? pageNumber, int? pageSize)
    {
        var minPageSize = MinPageSize < 1 ? 1 : MinPageSize;
        var maxPageSize = MaxPageSize < minPageSize ? minPageSize : MaxPageSize;
        var defaultPageSize = DefaultPageSize < minPageSize
            ? minPageSize
            : Math.Min(DefaultPageSize, maxPageSize);
        var defaultPageNumber = DefaultPageNumber < 1 ? 1 : DefaultPageNumber;

        var sanitizedPageNumber = pageNumber.GetValueOrDefault(defaultPageNumber);
        sanitizedPageNumber = sanitizedPageNumber < 1 ? defaultPageNumber : sanitizedPageNumber;

        var requestedPageSize = pageSize.GetValueOrDefault(defaultPageSize);
        var sanitizedPageSize = requestedPageSize < minPageSize
            ? defaultPageSize
            : Math.Min(requestedPageSize, maxPageSize);

        return (sanitizedPageNumber, sanitizedPageSize);
    }
}
