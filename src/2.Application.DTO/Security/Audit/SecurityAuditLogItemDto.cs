namespace JOIN.Application.DTO.Security.Audit;

/// <summary>
/// One row of <c>Security.AuditLogs</c> projected for the read endpoint.
/// <c>Changes</c> is the union of keys in old/new JSON with null on the missing side,
/// not the raw JSON the handler receives from the repository.
/// </summary>
public sealed record SecurityAuditLogItemDto(
    Guid Id,
    string EntityName,
    Guid EntityId,
    string? EntityLabel,
    string Action,
    string ChangedBy,
    string? ChangedByName,
    DateTime ChangedAtUtc,
    string? IpAddress,
    IReadOnlyList<AuditFieldChangeDto> Changes,
    string? Metadata);
