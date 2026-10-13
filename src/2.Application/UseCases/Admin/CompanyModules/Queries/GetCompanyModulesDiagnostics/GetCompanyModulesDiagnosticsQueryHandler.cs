using Dapper;
using JOIN.Application.Common;
using JOIN.Application.Common.Settings;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Queries;

/// <summary>
/// SPEC 48 F: SuperAdmin-only diagnostics that show, for each active company, which base modules are
/// missing in <c>Admin.CompanyModules</c> and how many users would be impacted by enabling the
/// <c>EnforceCompanyModules</c> switch. The query is read-only; it does not flip the switch.
///
/// The query is implemented in PostgreSQL syntax (lowercase identifiers, <c>TRUE/FALSE</c> literals,
/// <c>COUNT(DISTINCT ...)</c>) and uses the same engine-agnostic Dapper connection factory that
/// query handlers across the codebase use. SQL Server compatibility is not a goal on the
/// <c>main_postgresql</c> fork.
/// </summary>
/// <param name="connectionFactory">Engine-agnostic connection factory.</param>
/// <param name="moduleSettings">Reports the current value of the switch in the payload.</param>
public sealed class GetCompanyModulesDiagnosticsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<ModuleEnforcementSettings> moduleSettings)
    : IRequestHandler<GetCompanyModulesDiagnosticsQuery, Response<CompanyModulesDiagnosticsDto>>
{
    /// <summary>
    /// Names of every active <c>IsBase = TRUE</c> module. Drives the "complete" check below.
    /// </summary>
    private const string BaseModulesSql = """
        SELECT name
        FROM admin.systemmodules
        WHERE isbase = TRUE AND gcrecord = 0
        ORDER BY name;
        """;

    /// <summary>
    /// One row per active company that has at least one base module without an active
    /// <c>CompanyModule</c> row, OR at least one user whose role grants permissions on options of a
    /// module not active for the company. The list is pre-filtered by the WHERE clause so the API
    /// returns only companies that need attention before enabling the switch.
    /// </summary>
    private const string CompanyProblemsSql = """
        WITH base AS (
            SELECT id, name
            FROM admin.systemmodules
            WHERE isbase = TRUE AND gcrecord = 0
        ),
        active_companies AS (
            SELECT id, name
            FROM admin.companies
            WHERE gcrecord = 0
        ),
        missing AS (
            SELECT
                c.id   AS companyid,
                c.name AS companyname,
                COALESCE(
                    array_agg(b.name) FILTER (WHERE cm.id IS NULL),
                    ARRAY[]::TEXT[]
                ) AS missing_base_modules
            FROM active_companies c
            CROSS JOIN base b
            LEFT JOIN admin.companymodules cm
                ON cm.companyid = c.id
               AND cm.moduleid = b.id
               AND cm.isactive = TRUE
               AND cm.gcrecord = 0
            GROUP BY c.id, c.name
        ),
        affected_users AS (
            SELECT
                urc.companyid                                        AS companyid,
                COUNT(DISTINCT urc.userid)                           AS affected_user_count
            FROM security.userrolecompanies urc
            INNER JOIN security.rolesystemoptions rso
                ON rso.roleid    = urc.roleid
               AND rso.companyid = urc.companyid
               AND rso.gcrecord  = 0
            INNER JOIN security.systemoptions so
                ON so.id        = rso.systemoptionid
               AND so.gcrecord  = 0
            INNER JOIN admin.systemmodules sm
                ON sm.id        = so.moduleid
               AND sm.isactive  = TRUE
               AND sm.gcrecord  = 0
            LEFT JOIN admin.companymodules cm
                ON cm.companyid = urc.companyid
               AND cm.moduleid  = sm.id
               AND cm.isactive  = TRUE
               AND cm.gcrecord  = 0
            WHERE urc.gcrecord = 0
              AND cm.id IS NULL
            GROUP BY urc.companyid
        )
        SELECT
            m.companyid,
            m.companyname,
            m.missing_base_modules,
            COALESCE(a.affected_user_count, 0) AS affected_user_count
        FROM missing m
        LEFT JOIN affected_users a ON a.companyid = m.companyid
        WHERE cardinality(m.missing_base_modules) > 0
           OR COALESCE(a.affected_user_count, 0) > 0
        ORDER BY m.companyname;
        """;

    /// <inheritdoc />
    public async Task<Response<CompanyModulesDiagnosticsDto>> Handle(
        GetCompanyModulesDiagnosticsQuery request,
        CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        var baseModuleNames = (await connection.QueryAsync<string>(
            new CommandDefinition(BaseModulesSql, cancellationToken: cancellationToken))).AsList();

        var rows = (await connection.QueryAsync<CompanyProblemRow>(
            new CommandDefinition(CompanyProblemsSql, cancellationToken: cancellationToken))).AsList();

        var entries = rows
            .Select(row => new CompanyDiagnosticsEntry
            {
                CompanyId = row.CompanyId,
                CompanyName = row.CompanyName,
                MissingBaseModules = row.MissingBaseModules ?? Array.Empty<string>(),
                UsersWithPermissionsInDisabledModules = (int)row.AffectedUserCount
            })
            .ToList();

        return new Response<CompanyModulesDiagnosticsDto>
        {
            IsSuccess = true,
            Message = "Diagnostics retrieved successfully.",
            Data = new CompanyModulesDiagnosticsDto
            {
                EnforceCompanyModules = moduleSettings.Value.EnforceCompanyModules,
                BaseModules = baseModuleNames,
                Companies = entries
            }
        };
    }

    /// <summary>
    /// Dapper projection of <see cref="CompanyProblemsSql"/>.
    /// <see cref="MissingBaseModules"/> is bound to a PostgreSQL <c>text[]</c> column
    /// (<c>array_agg(...)</c>), which Dapper returns as a <c>string[]</c> on Npgsql.
    /// </summary>
    private sealed class CompanyProblemRow
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string[]? MissingBaseModules { get; set; }
        public long AffectedUserCount { get; set; }
    }
}