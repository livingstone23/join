namespace JOIN.Application.DTO.Security.Workspaces;



/// <summary>
/// Represents the request payload used to switch the authenticated user's active company context.
/// </summary>
public sealed record SwitchCompanyRequestDto
{
    /// <summary>
    /// Gets the target company identifier requested as the new active context.
    /// </summary>
    public Guid CompanyId { get; init; }
}