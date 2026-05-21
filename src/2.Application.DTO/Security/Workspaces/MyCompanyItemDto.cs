namespace JOIN.Application.DTO.Security.Workspaces;



/// <summary>
/// Represents one company available to the authenticated user for context selection.
/// </summary>
public sealed record MyCompanyItemDto
{
    /// <summary>
    /// Gets the unique identifier of the company.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the display name of the company.
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tax identifier associated with the company.
    /// </summary>
    public string TaxId { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this company is currently selected as default.
    /// </summary>
    public bool IsDefault { get; init; }
}