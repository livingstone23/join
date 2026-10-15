using JOIN.Application.DTO.Common;

namespace JOIN.Application.DTO.Customers;

/// <summary>
/// A stage of the company's customer funnel (SPEC 55 part F).
/// </summary>
public sealed record CustomerStageDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int SortOrder { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    /// <summary>Customers that currently have this stage.</summary>
    public int CustomersCount { get; init; }
    public DateTime CreatedAt { get; init; }
}
