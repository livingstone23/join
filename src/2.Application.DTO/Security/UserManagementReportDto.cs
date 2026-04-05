namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents one user-management and activity row scoped to a specific company.
/// </summary>
public record UserManagementReportDto
{
    /// <summary>
    /// Gets the unique identifier of the user.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the display name of the user.
    /// </summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the email address of the user.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the user account is active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the date when the user account was created.
    /// </summary>
    public DateTime UserCreatedDate { get; init; }

    /// <summary>
    /// Gets the most recent login date inferred from refresh token creation records.
    /// </summary>
    public DateTime? LastLoginDate { get; init; }

    /// <summary>
    /// Gets the company identifier for the row context when the user is assigned to a company.
    /// </summary>
    public Guid? CompanyId { get; init; }

    /// <summary>
    /// Gets the company name for the row context.
    /// Returns <see langword="null"/> when the user has no company assignment.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Gets a value indicating whether this company is the user's default company.
    /// </summary>
    public bool IsDefaultCompany { get; init; }

    /// <summary>
    /// Gets the tenant-scoped roles assigned through <c>UserRoleCompanies</c>.
    /// Returns <see langword="null"/> when the user has no role assignments.
    /// </summary>
    public IReadOnlyCollection<string>? Roles { get; init; }
}
