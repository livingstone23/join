using System.Globalization;
using System.Text.Json.Serialization;

namespace JOIN.Application.DTO.Security;

/// <summary>
/// Data transfer object for the ApplicationRole catalog.
/// Used by detailed GET, create, and update responses.
/// </summary>
/// <param name="PermissionsCount">
/// Number of active <c>RoleSystemOption</c> rows this role currently has in the caller's tenant.
/// Always tenant-scoped: a role with permissions in another <c>CompanyId</c> returns <c>0</c> here.
/// Projected via correlated subquery on <c>GetByIdAsync</c> / <c>GetPagedAsync</c> — never materialized in C#.
/// </param>
public sealed record RoleDto(
    Guid Id,
    string Name,
    string NormalizedName,
    string? Description,
    bool IsSystemDefault,
    string? CreatedBy,
    DateTime Created,
    int PermissionsCount = 0,
    [property: JsonIgnore] int GcRecord = 0)
{
    /// <summary>
    /// Gets whether the role is logically deleted (SPEC 41). Positional record, so it cannot derive from
    /// <see cref="JOIN.Application.DTO.Common.SoftDeletableDto"/>; same contract.
    /// </summary>
    public bool IsDeleted => GcRecord != 0;

    /// <summary>
    /// Gets the deletion date, or <c>null</c> for an active role.
    /// </summary>
    public DateOnly? DeletedOn =>
        GcRecord != 0
        && DateOnly.TryParseExact(GcRecord.ToString(CultureInfo.InvariantCulture), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var deletedOn)
            ? deletedOn
            : null;
}
