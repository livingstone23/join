using System.Data;
using System.Text;
using Dapper;
using FluentValidation.Results;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;

namespace JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;

/// <summary>
/// Shared reader for the user-management report. Backed by Dapper on purpose: the
/// EF global query filter would hide inactive users from this report. Two
/// entry-points — <see cref="ReadAsync"/> for the unbounded
/// <c>/reports/system</c> endpoint, and <see cref="ReadPagedAsync"/> for the
/// paginated <c>/reports/my-company</c> endpoint introduced by SPEC 28 / F7.
/// Both share the same filter semantics and the same per-pair grouping in C# so
/// the response shape stays identical across endpoints.
/// </summary>
internal static class UserManagementReportQueryHelper
{
    /// <summary>Hard cap on page size for the paginated report.</summary>
    public const int MaxPageSize = 50;

    /// <summary>Default page size when the caller omits <c>pageSize</c>.</summary>
    public const int DefaultPageSize = 10;

    // Filters shared between ReadAsync and ReadPagedAsync. Built once in C# and
    // interpolated into both query strings so a future change cannot land in only
    // one place (the spec's documented risk about WHERE duplication).
    private const string SharedFilters = """
            u.GcRecord = 0
          AND (@ScopedCompanyId IS NULL OR uc.CompanyId = @ScopedCompanyId)
          AND (@TargetCompanyId IS NULL OR uc.CompanyId = @TargetCompanyId)
          AND (uc.UserId IS NOT NULL OR (@ScopedCompanyId IS NULL AND @TargetCompanyId IS NULL))
          AND (@FromDate IS NULL OR COALESCE(lastLogin.LastLoginDate, u.Created) >= @FromDate)
          AND (@ToDateExclusive IS NULL OR COALESCE(lastLogin.LastLoginDate, u.Created) < @ToDateExclusive)
          AND (
                @ApplyRoleFilter = 0
                OR EXISTS (
                    SELECT 1
                    FROM Security.UserRoleCompanies urcf
                    INNER JOIN Security.Roles rf ON rf.Id = urcf.RoleId
                    WHERE urcf.GcRecord = 0
                      AND rf.GcRecord = 0
                      AND urcf.UserId = uc.UserId
                      AND urcf.CompanyId = uc.CompanyId
                      AND UPPER(rf.Name) IN @RoleNames
                )
          )
        """;

    private const string DetailSelectFrom = """
        SELECT
            u.Id AS UserId,
            u.FirstName,
            u.LastName,
            u.Email,
            u.IsActive,
            u.Created AS UserCreatedDate,
            uc.CompanyId,
            c.Name AS CompanyName,
            uc.IsDefault AS IsDefaultCompany,
            lastLogin.LastLoginDate,
            r.Name AS RoleName
        FROM Security.Users u
        LEFT JOIN Security.UserCompanies uc
            ON uc.UserId = u.Id
           AND uc.GcRecord = 0
        LEFT JOIN Common.Companies c
            ON c.Id = uc.CompanyId
           AND c.GcRecord = 0
        LEFT JOIN Security.UserRoleCompanies urc
            ON urc.UserId = u.Id
           AND urc.CompanyId = uc.CompanyId
           AND urc.GcRecord = 0
        LEFT JOIN Security.Roles r
            ON r.Id = urc.RoleId
           AND r.GcRecord = 0
        LEFT JOIN (
            SELECT
                rt.UserId,
                MAX(rt.Created) AS LastLoginDate
            FROM Security.UserRefreshTokens rt
            WHERE rt.GcRecord = 0
            GROUP BY rt.UserId
        ) lastLogin ON lastLogin.UserId = u.Id
        WHERE
        """;

    private const string ReadAsyncSql = DetailSelectFrom + SharedFilters + """
        ORDER BY c.Name, u.FirstName, u.LastName, u.Email;
        """;

    // CTE strategy: distinct (UserId, CompanyId) pairs are paginated first, then the
    // detail JOIN re-attaches the role rows for each pair. A raw OFFSET on the
    // detail query would split a user with 3 roles across two pages because the
    // detail emits one row per (UserId, CompanyId, Role).
    private const string ReadPagedSqlTemplate = """
        WITH filtered AS (
            SELECT DISTINCT
                u.Id        AS UserId,
                uc.CompanyId AS CompanyId,
                c.Name      AS CompanyName,
                u.FirstName AS FirstName,
                u.LastName  AS LastName,
                u.Email     AS Email,
                u.IsActive  AS IsActive
            FROM Security.Users u
            LEFT JOIN Security.UserCompanies uc
                ON uc.UserId = u.Id
               AND uc.GcRecord = 0
            LEFT JOIN Common.Companies c
                ON c.Id = uc.CompanyId
               AND c.GcRecord = 0
            LEFT JOIN (
                SELECT rt.UserId, MAX(rt.Created) AS LastLoginDate
                FROM Security.UserRefreshTokens rt
                WHERE rt.GcRecord = 0
                GROUP BY rt.UserId
            ) lastLogin ON lastLogin.UserId = u.Id
            WHERE
        """ + SharedFilters + """
              AND (@IsActive IS NULL OR u.IsActive = @IsActive)
              AND (
                    @Search IS NULL
                    OR u.Email LIKE @Search
                    OR CONCAT(u.FirstName, ' ', u.LastName) LIKE @Search
              )
        ),
        page AS (
            SELECT UserId, CompanyId
            FROM filtered
            ORDER BY CompanyName, FirstName, LastName, Email
            {PAGINATION}
        )
        SELECT
            u.Id          AS UserId,
            u.FirstName   AS FirstName,
            u.LastName    AS LastName,
            u.Email       AS Email,
            u.IsActive    AS IsActive,
            u.Created     AS UserCreatedDate,
            uc.CompanyId  AS CompanyId,
            c.Name        AS CompanyName,
            uc.IsDefault  AS IsDefaultCompany,
            lastLogin.LastLoginDate,
            r.Name        AS RoleName
        FROM page p
        INNER JOIN Security.Users u
            ON u.Id = p.UserId
        LEFT JOIN Security.UserCompanies uc
            ON uc.UserId = u.Id
           AND uc.CompanyId = p.CompanyId
           AND uc.GcRecord = 0
        LEFT JOIN Common.Companies c
            ON c.Id = uc.CompanyId
           AND c.GcRecord = 0
        LEFT JOIN Security.UserRoleCompanies urc
            ON urc.UserId = u.Id
           AND urc.CompanyId = p.CompanyId
           AND urc.GcRecord = 0
        LEFT JOIN Security.Roles r
            ON r.Id = urc.RoleId
           AND r.GcRecord = 0
        LEFT JOIN (
            SELECT rt.UserId, MAX(rt.Created) AS LastLoginDate
            FROM Security.UserRefreshTokens rt
            WHERE rt.GcRecord = 0
            GROUP BY rt.UserId
        ) lastLogin ON lastLogin.UserId = u.Id
        ORDER BY c.Name, u.FirstName, u.LastName, u.Email;
        """;

    // Count query references the same predicates as the detail so totalCount and
    // page rows agree. Run as a separate command — CTEs do NOT persist across
    // semicolon-separated batches, which is why an inline
    // `SELECT COUNT(*) FROM filtered;` after the detail SELECT fails with
    // "Invalid object name 'filtered'". Two reads on the same connection,
    // inside an explicit REPEATABLE READ transaction, keep them consistent
    // without depending on CTE visibility rules.
    private const string ReadPagedCountSqlTemplate = """
        SELECT COUNT(*) FROM (
            SELECT DISTINCT u.Id AS UserId, uc.CompanyId AS CompanyId
            FROM Security.Users u
            LEFT JOIN Security.UserCompanies uc
                ON uc.UserId = u.Id
               AND uc.GcRecord = 0
            LEFT JOIN (
                SELECT rt.UserId, MAX(rt.Created) AS LastLoginDate
                FROM Security.UserRefreshTokens rt
                WHERE rt.GcRecord = 0
                GROUP BY rt.UserId
            ) lastLogin ON lastLogin.UserId = u.Id
            WHERE
        """ + SharedFilters + """
              AND (@IsActive IS NULL OR u.IsActive = @IsActive)
              AND (
                    @Search IS NULL
                    OR u.Email LIKE @Search
                    OR CONCAT(u.FirstName, ' ', u.LastName) LIKE @Search
              )
        ) AS pairs;
        """;

    public static async Task<IReadOnlyCollection<UserManagementReportDto>> ReadAsync(
        ISqlConnectionFactory connectionFactory,
        Guid? scopedCompanyId,
        Guid? targetCompanyId,
        DateTime? fromDate,
        DateTime? toDate,
        IReadOnlyCollection<string>? roleNames,
        CancellationToken cancellationToken)
    {
        var (normalizedFromDate, normalizedToDateExclusive) = NormalizeDateRange(fromDate, toDate);
        var normalizedRoleNames = NormalizeRoleNames(roleNames);

        using var connection = connectionFactory.CreateConnection();

        var parameters = BuildBaseParameters(
            scopedCompanyId,
            targetCompanyId,
            normalizedFromDate,
            normalizedToDateExclusive,
            normalizedRoleNames);

        var rows = (await connection.QueryAsync<UserManagementReportSqlRow>(
            new CommandDefinition(ReadAsyncSql, parameters, cancellationToken: cancellationToken))).AsList();

        return MapRows(rows);
    }

    /// <summary>
    /// Paginated variant of <see cref="ReadAsync"/>. Returns the items on the
    /// requested page plus the total count of distinct <c>(UserId, CompanyId)</c>
    /// pairs that pass the filters — not the row count, so callers can render an
    /// accurate page count.
    /// </summary>
    public static async Task<(IReadOnlyCollection<UserManagementReportDto> Items, int TotalCount)>
        ReadPagedAsync(
            ISqlConnectionFactory connectionFactory,
            Guid? scopedCompanyId,
            Guid? targetCompanyId,
            DateTime? fromDate,
            DateTime? toDate,
            IReadOnlyCollection<string>? roleNames,
            int pageNumber,
            int pageSize,
            string? search,
            bool? isActive,
            CancellationToken cancellationToken)
    {
        var sanitizedPageNumber = pageNumber < 1 ? 1 : pageNumber;
        var sanitizedPageSize = pageSize < 1
            ? DefaultPageSize
            : Math.Min(pageSize, MaxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        var (normalizedFromDate, normalizedToDateExclusive) = NormalizeDateRange(fromDate, toDate);
        var normalizedRoleNames = NormalizeRoleNames(roleNames);

        // Search wildcard is built in C# so the SQL stays portable across providers
        // (the spec mandates no '+' concatenation or '||' in the SQL).
        var searchPattern = string.IsNullOrWhiteSpace(search)
            ? null
            : $"%{search.Trim()}%";

        using var connection = connectionFactory.CreateConnection();
        connection.Open();

        var parameters = BuildBaseParameters(
            scopedCompanyId,
            targetCompanyId,
            normalizedFromDate,
            normalizedToDateExclusive,
            normalizedRoleNames);
        parameters.Add("IsActive", isActive);
        parameters.Add("Search", searchPattern);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);
        // Re-fix the DBNull issue: Dapper rejects DBNull.Value for nullable typed
        // parameters. Passing null directly lets Dapper emit the SQL NULL it needs.
        // (See SPEC 28 / F7 test for Search / IsActive propagation.)

        // Count first (cheap, single row) then the detail — both inside a
        // REPEATABLE READ transaction so the page slice and the total reflect
        // the same snapshot, no CTE-name reuse across batches.
        using var tx = connection.BeginTransaction(System.Data.IsolationLevel.RepeatableRead);

        var totalCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(ReadPagedCountSqlTemplate, parameters, transaction: tx, cancellationToken: cancellationToken));

        var detailSql = ReadPagedSqlTemplate.Replace("{PAGINATION}", GetPaginationClause(connection));
        var rows = (await connection.QueryAsync<UserManagementReportSqlRow>(
            new CommandDefinition(detailSql, parameters, transaction: tx, cancellationToken: cancellationToken))).AsList();

        tx.Commit();

        return (MapRows(rows), totalCount);
    }

    /// <summary>
    /// Resolves a provider-compatible pagination clause. Branching on the runtime
    /// type name is the project's established pattern (see
    /// <c>GetPersonsPagedQueryHandler</c>).
    /// </summary>
    private static string GetPaginationClause(IDbConnection connection)
        => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

    private static DynamicParameters BuildBaseParameters(
        Guid? scopedCompanyId,
        Guid? targetCompanyId,
        DateTime? normalizedFromDate,
        DateTime? normalizedToDateExclusive,
        string[] normalizedRoleNames)
    {
        var parameters = new DynamicParameters();
        parameters.Add("ScopedCompanyId", NormalizeGuid(scopedCompanyId));
        parameters.Add("TargetCompanyId", NormalizeGuid(targetCompanyId));
        parameters.Add("FromDate", normalizedFromDate);
        parameters.Add("ToDateExclusive", normalizedToDateExclusive);
        parameters.Add("ApplyRoleFilter", normalizedRoleNames.Length > 0 ? 1 : 0);
        parameters.Add("RoleNames", normalizedRoleNames.Length > 0 ? normalizedRoleNames : new[] { string.Empty });
        return parameters;
    }

    /// <summary>Flattens detail rows (one per role) into the per-pair DTOs.</summary>
    private static IReadOnlyCollection<UserManagementReportDto> MapRows(
        IList<UserManagementReportSqlRow> rows)
    {
        return rows
            .GroupBy(row => new
            {
                row.UserId,
                row.FirstName,
                row.LastName,
                row.Email,
                row.IsActive,
                row.UserCreatedDate,
                row.LastLoginDate,
                row.CompanyId,
                row.CompanyName,
                row.IsDefaultCompany
            })
            .Select(group => new UserManagementReportDto
            {
                UserId = group.Key.UserId,
                FullName = BuildFullName(group.Key.FirstName, group.Key.LastName, group.Key.Email),
                Email = group.Key.Email ?? string.Empty,
                IsActive = group.Key.IsActive,
                UserCreatedDate = group.Key.UserCreatedDate,
                LastLoginDate = group.Key.LastLoginDate,
                CompanyId = group.Key.CompanyId,
                CompanyName = group.Key.CompanyName,
                IsDefaultCompany = group.Key.IsDefaultCompany ?? false,
                Roles = group
                    .Select(item => item.RoleName)
                    .Where(role => !string.IsNullOrWhiteSpace(role))
                    .Select(role => role!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                    .ToArray() is { Length: > 0 } resolvedRoles
                        ? resolvedRoles
                        : null
            })
            .OrderBy(item => item.CompanyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.FullName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static (DateTime? FromDate, DateTime? ToDateExclusive) NormalizeDateRange(
        DateTime? fromDate, DateTime? toDate)
    {
        var normalizedFromDate = fromDate?.Date;
        var normalizedToDateExclusive = toDate?.Date.AddDays(1);

        if (normalizedFromDate.HasValue
            && normalizedToDateExclusive.HasValue
            && normalizedFromDate.Value >= normalizedToDateExclusive.Value)
        {
            throw new ValidationException(new[]
            {
                new ValidationFailure("ToDate", "'ToDate' must be greater than or equal to 'FromDate'.")
            });
        }

        return (normalizedFromDate, normalizedToDateExclusive);
    }

    private static Guid? NormalizeGuid(Guid? value)
        => value.HasValue && value.Value != Guid.Empty ? value : null;

    private static string[] NormalizeRoleNames(IReadOnlyCollection<string>? roleNames)
        => roleNames?
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

    private static string BuildFullName(string? firstName, string? lastName, string? email)
    {
        var parts = new[] { firstName?.Trim(), lastName?.Trim() }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length > 0 ? string.Join(" ", parts) : (email?.Trim() ?? string.Empty);
    }

    private sealed class UserManagementReportSqlRow
    {
        public Guid UserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public bool IsActive { get; set; }
        public DateTime UserCreatedDate { get; set; }
        public DateTime? LastLoginDate { get; set; }
        public Guid? CompanyId { get; set; }
        public string? CompanyName { get; set; }
        public bool? IsDefaultCompany { get; set; }
        public string? RoleName { get; set; }
    }
}