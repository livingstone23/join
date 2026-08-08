namespace JOIN.Application.DTO.Security;

/// <summary>
/// Data transfer object for the ApplicationRole catalog.
/// Used by detailed GET, create, and update responses.
/// </summary>
public sealed record RoleDto(
    Guid Id,
    string Name,
    string NormalizedName,
    string? Description,
    bool IsSystemDefault,
    string? CreatedBy,
    DateTime Created);
