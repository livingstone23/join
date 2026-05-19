namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Customer read model including related person and user display fields.
/// </summary>
public sealed record CustomerResponseDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid PersonId { get; init; }
    public Guid UserId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public int PersonLifecycleStage { get; init; }
    public string PersonLifecycleStageName { get; init; } = string.Empty;
    public string PersonName { get; init; } = string.Empty;
    public string UserEmail { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime ActivatedAt { get; init; }
    public DateTime? DeactivatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
