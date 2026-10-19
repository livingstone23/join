namespace JOIN.Application.DTO.Customers;

/// <summary>
/// One status event of a customer (SPEC 55 part C) or a stage change (SPEC 66).
/// </summary>
public sealed record CustomerStatusHistoryDto
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    /// <summary><c>CustomerStatusAction</c>: 1 Created, 2 Activated, 3 Confirmed, 4 Deactivated, 5 StageChanged.</summary>
    public int Action { get; init; }
    public string ActionName { get; init; } = string.Empty;
    /// <summary><c>CustomerStatusSource</c>: 1 Manual, 2 Seed, 3 Migration, 4 PersonDeactivated, 5 Lead.</summary>
    public int Source { get; init; }
    public string SourceName { get; init; } = string.Empty;
    public Guid? SourceReferenceId { get; init; }
    /// <summary>Code of the referenced lead when the event comes from a lead (SPEC 63).</summary>
    public string? SourceReferenceCode { get; init; }
    public string? Notes { get; init; }
    public Guid UserId { get; init; }
    public string UserEmail { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
    /// <summary>Current name of the stage before the change; only on <c>StageChanged</c> rows (SPEC 66).</summary>
    public string? FromStageName { get; init; }
    /// <summary>Current name of the stage after the change; only on <c>StageChanged</c> rows (SPEC 66).</summary>
    public string? ToStageName { get; init; }
}
