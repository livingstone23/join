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
    /// Gets or sets the maximum page size the API will allow for the area listing endpoint.
    /// </summary>
    public int MaxPageSize { get; set; } = 50;
}
