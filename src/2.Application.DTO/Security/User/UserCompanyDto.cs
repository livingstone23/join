namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents one company assignment associated with a user account.
/// This DTO is primarily used by security and context-switching screens to show which companies are available to the user and which one is currently marked as default.
/// </summary>
public record UserCompanyDto
{
    /// <summary>
    /// Gets the unique identifier of the linked company.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets the display name of the linked company.
    /// </summary>
    public string CompanyName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the tax identifier or fiscal registration value associated with the linked company.
    /// </summary>
    public string TaxId { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether this company is the user's current default operational context.
    /// </summary>
    public bool IsDefault { get; init; }
}
