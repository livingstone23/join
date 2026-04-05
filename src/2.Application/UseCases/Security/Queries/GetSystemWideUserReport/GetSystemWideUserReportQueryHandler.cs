using Dapper;
using FluentValidation.Results;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport
{
/// <summary>
/// Handles system-wide user management and activity report queries.
/// </summary>
/// <param name="connectionFactory">Factory used to create engine-agnostic read connections.</param>
public class GetSystemWideUserReportQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetSystemWideUserReportQuery, Response<IReadOnlyCollection<UserManagementReportDto>>>
{
    /// <summary>
    /// Retrieves the global report across all companies, optionally filtered by company, date range, and roles.
    /// </summary>
    public async Task<Response<IReadOnlyCollection<UserManagementReportDto>>> Handle(
        GetSystemWideUserReportQuery request,
        CancellationToken cancellationToken)
    {
        var items = await UserManagementReportQueryHelper.ReadAsync(
            connectionFactory,
            scopedCompanyId: null,
            targetCompanyId: request.TargetCompanyId,
            fromDate: request.FromDate,
            toDate: request.ToDate,
            roleNames: request.RoleNames,
            cancellationToken: cancellationToken);

        return new Response<IReadOnlyCollection<UserManagementReportDto>>
        {
            IsSuccess = true,
            Message = "System-wide user report retrieved successfully.",
            Data = items
        };
    }
}

/// <summary>
/// Shared reader used by user-management report queries.
/// </summary>
internal static class UserManagementReportQueryHelper
{
    private const string Sql = """
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
        WHERE u.GcRecord = 0
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
        ORDER BY c.Name, u.FirstName, u.LastName, u.Email;
        """;

    /// <summary>
    /// Executes the shared report query and groups the tenant-scoped role assignments into one row per user-company pair.
    /// </summary>
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
        var normalizedRoleNames = roleNames?
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("ScopedCompanyId", NormalizeGuid(scopedCompanyId));
        parameters.Add("TargetCompanyId", NormalizeGuid(targetCompanyId));
        parameters.Add("FromDate", normalizedFromDate);
        parameters.Add("ToDateExclusive", normalizedToDateExclusive);
        parameters.Add("ApplyRoleFilter", normalizedRoleNames.Length > 0 ? 1 : 0);
        parameters.Add("RoleNames", normalizedRoleNames.Length > 0 ? normalizedRoleNames : new[] { string.Empty });

        var rows = (await connection.QueryAsync<UserManagementReportSqlRow>(
            new CommandDefinition(Sql, parameters, cancellationToken: cancellationToken))).AsList();

        var items = rows
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

        return items;
    }

    private static (DateTime? FromDate, DateTime? ToDateExclusive) NormalizeDateRange(DateTime? fromDate, DateTime? toDate)
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
}
