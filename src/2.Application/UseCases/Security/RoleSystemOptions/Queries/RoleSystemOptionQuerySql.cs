using System.Text;
using Dapper;
using JOIN.Application.Common;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Shared T-SQL projection and filter building for RoleSystemOption read queries.
/// </summary>
internal static class RoleSystemOptionQuerySql
{
    internal const string SelectListProjection = """
        SELECT
            rso.Id,
            rso.CompanyId,
            c.Name AS CompanyName,
            rso.RoleId,
            ar.Name AS RoleName,
            rso.SystemOptionId,
            so.Name AS SystemOptionName,
            rso.CanRead,
            rso.CanCreate,
            rso.CanUpdate,
            rso.CanDelete,
            rso.Created
        """;

    internal const string FromWithJoins = """
        FROM Security.RoleSystemOptions rso
        INNER JOIN Security.Roles ar ON ar.Id = rso.RoleId AND ar.GcRecord = 0
        INNER JOIN Security.SystemOptions so ON so.Id = rso.SystemOptionId AND so.GcRecord = 0
        LEFT JOIN Common.Companies c ON c.Id = rso.CompanyId AND c.GcRecord = 0
        """;

    internal static string BuildPagedSql(string whereClause) =>
        $"""
        {SelectListProjection}
        {FromWithJoins}
        {whereClause}
        ORDER BY c.Name ASC, ar.Name ASC, so.Name ASC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

        SELECT COUNT(*)
        {FromWithJoins}
        {whereClause};
        """;

    internal static string BuildByIdSql(string whereClause) =>
        $"""
        {SelectListProjection}
        {FromWithJoins}
        {whereClause};
        """;

    internal static (string WhereClause, DynamicParameters Parameters) BuildWhereClause(RoleSystemOptionQueryFilters filters)
    {
        var parameters = new DynamicParameters();
        var whereBuilder = new StringBuilder("WHERE rso.GcRecord = 0");

        if (filters.RequireCompanyFilter || filters.CompanyId.HasValue)
        {
            whereBuilder.Append(" AND rso.CompanyId = @CompanyId");
            parameters.Add("CompanyId", filters.CompanyId);
        }

        if (filters.RoleId.HasValue)
        {
            whereBuilder.Append(" AND rso.RoleId = @RoleId");
            parameters.Add("RoleId", filters.RoleId.Value);
        }

        if (filters.SystemOptionId.HasValue)
        {
            whereBuilder.Append(" AND rso.SystemOptionId = @SystemOptionId");
            parameters.Add("SystemOptionId", filters.SystemOptionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filters.RoleName))
        {
            whereBuilder.Append(" AND ar.Name LIKE @RoleNamePattern");
            parameters.Add("RoleNamePattern", $"%{filters.RoleName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(filters.SystemOptionName))
        {
            whereBuilder.Append(" AND so.Name LIKE @SystemOptionNamePattern");
            parameters.Add("SystemOptionNamePattern", $"%{filters.SystemOptionName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(filters.CompanyName))
        {
            whereBuilder.Append(" AND c.Name LIKE @CompanyNamePattern");
            parameters.Add("CompanyNamePattern", $"%{filters.CompanyName.Trim()}%");
        }

        if (filters.CanRead.HasValue)
        {
            whereBuilder.Append(" AND rso.CanRead = @CanRead");
            parameters.Add("CanRead", filters.CanRead.Value);
        }

        if (filters.CanCreate.HasValue)
        {
            whereBuilder.Append(" AND rso.CanCreate = @CanCreate");
            parameters.Add("CanCreate", filters.CanCreate.Value);
        }

        if (filters.CanUpdate.HasValue)
        {
            whereBuilder.Append(" AND rso.CanUpdate = @CanUpdate");
            parameters.Add("CanUpdate", filters.CanUpdate.Value);
        }

        if (filters.CanDelete.HasValue)
        {
            whereBuilder.Append(" AND rso.CanDelete = @CanDelete");
            parameters.Add("CanDelete", filters.CanDelete.Value);
        }

        return (whereBuilder.ToString(), parameters);
    }

    internal static (int PageNumber, int PageSize, int Offset) SanitizePagination(
        int? pageNumber,
        int? pageSize,
        PaginationSettings paginationSettings)
    {
        var defaultPageNumber = paginationSettings.DefaultPageNumber < 1 ? 1 : paginationSettings.DefaultPageNumber;
        var defaultPageSize = paginationSettings.DefaultPageSize < 1 ? 10 : paginationSettings.DefaultPageSize;
        var maxPageSize = paginationSettings.MaxPageSize < defaultPageSize ? defaultPageSize : paginationSettings.MaxPageSize;

        var sanitizedPageNumber = pageNumber.GetValueOrDefault(defaultPageNumber);
        sanitizedPageNumber = sanitizedPageNumber < 1 ? defaultPageNumber : sanitizedPageNumber;

        var requestedPageSize = pageSize.GetValueOrDefault(defaultPageSize);
        var sanitizedPageSize = requestedPageSize < 1 ? defaultPageSize : Math.Min(requestedPageSize, maxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        return (sanitizedPageNumber, sanitizedPageSize, offset);
    }
}

/// <summary>
/// Optional filter parameters for RoleSystemOption read queries.
/// </summary>
internal sealed record RoleSystemOptionQueryFilters(
    Guid? CompanyId,
    bool RequireCompanyFilter,
    Guid? RoleId = null,
    Guid? SystemOptionId = null,
    string? RoleName = null,
    string? SystemOptionName = null,
    string? CompanyName = null,
    bool? CanRead = null,
    bool? CanCreate = null,
    bool? CanUpdate = null,
    bool? CanDelete = null);
