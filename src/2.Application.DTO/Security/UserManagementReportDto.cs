namespace JOIN.Application.DTO.Security;

/// <summary>
/// Represents one row in the user-management and activity report produced by the security reporting queries.
/// Each row combines identity data, activity indicators, company assignment details, and tenant-scoped role information for reporting purposes.
/// </summary>
public record UserManagementReportDto
{
    /// <summary>
    /// Gets the unique identifier of the user represented by the report row.
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Gets the full display name composed for the user in the report output.
    /// </summary>
    public string FullName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the email address registered for the user account.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the user account is currently active.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC date/time when the user account was originally created.
    /// </summary>
    public DateTime UserCreatedDate { get; init; }

    /// <summary>
    /// Gets the most recent login date inferred from the latest refresh-token activity available for the user.
    /// </summary>
    public DateTime? LastLoginDate { get; init; }

    /// <summary>
    /// Gets the company identifier associated with the row context when the user is linked to a company.
    /// </summary>
    public Guid? CompanyId { get; init; }

    /// <summary>
    /// Gets the company name associated with the row context.
    /// This value can be <see langword="null"/> when the user does not currently have a company assignment.
    /// </summary>
    public string? CompanyName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the company represented by the row is the user's default company context.
    /// </summary>
    public bool IsDefaultCompany { get; init; }

    /// <summary>
    /// Gets the tenant-scoped roles assigned to the user through <c>UserRoleCompanies</c>.
    /// This collection can be <see langword="null"/> when the user has no effective role assignments in the scoped context.
    /// </summary>
    public IReadOnlyCollection<string>? Roles { get; init; }
}
