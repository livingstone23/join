namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data Transfer Object representing a tenant-scoped tax regime catalog entry.
/// </summary>
public sealed record TaxRegimeDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
}
