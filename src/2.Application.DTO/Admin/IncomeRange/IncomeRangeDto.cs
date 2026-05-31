namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object representing a tenant-scoped income range catalog entry.
/// </summary>
public sealed record IncomeRangeDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public decimal MinimumValue { get; init; }
    public decimal? MaximumValue { get; init; }
    public string CurrencyCode { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }
    public DateTime CreatedAt { get; init; }
}
