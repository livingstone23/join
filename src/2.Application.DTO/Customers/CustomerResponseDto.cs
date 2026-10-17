using JOIN.Application.DTO.Common;

namespace JOIN.Application.DTO.Customers;

/// <summary>
/// Customer read model including related person, user, stage and last status event (SPEC 55).
/// </summary>
public sealed record CustomerResponseDto : SoftDeletableDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public Guid PersonId { get; init; }
    /// <summary>User who registered the customer.</summary>
    public Guid UserId { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public Guid CustomerStageId { get; init; }
    public string CustomerStageCode { get; init; } = string.Empty;
    public string CustomerStageName { get; init; } = string.Empty;
    public string PersonName { get; init; } = string.Empty;
    public string UserEmail { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime ActivatedAt { get; init; }
    public DateTime? DeactivatedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    /// <summary>Last status event (<c>CustomerStatusAction</c>): why the customer is in its current state.</summary>
    public int? LastStatusAction { get; init; }
    /// <summary>Source of the last status event (<c>CustomerStatusSource</c>).</summary>
    public int? LastStatusSource { get; init; }
    public DateTime? LastStatusAt { get; init; }

    /// <summary>Lead the customer came from: first <c>Created</c>/<c>Activated</c> event with origin <c>Lead</c> (SPEC 63).</summary>
    public Guid? OriginLeadId { get; init; }
    public string? OriginLeadCode { get; init; }
}
