namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents a company linked to a user, including whether it is the active default context.
/// </summary>
public record UserCompanyDto
{
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string TaxId { get; init; } = string.Empty;
    public bool IsDefault { get; init; }
}
