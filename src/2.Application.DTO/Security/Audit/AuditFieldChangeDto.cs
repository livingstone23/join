namespace JOIN.Application.DTO.Security.Audit;

/// <summary>
/// One changed field of a bitácora row, derived by crossing <c>OldValuesJson</c>
/// against <c>NewValuesJson</c> on the read side so the front never parses raw JSON.
/// </summary>
public sealed record AuditFieldChangeDto(
    string Field,
    string? OldValue,
    string? NewValue);
