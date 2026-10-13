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
    /// Two result sets in one round trip:
    /// <list type="number">
    /// <item>Names of every <c>IsBase = TRUE</c> module (drives the "complete" check).</item>
    /// <item>One row per active company that has at least one base module without an active
    /// <c>CompanyModule</c> row, OR at least one user whose role grants a permission (any flag) on an
    /// option of a module that is not enabled for the company — inactive for the company or inactive
    /// globally, the same rule the API gate applies. Companies without problems are left out.</item>
    /// </list>
    /// Aliases have no underscores because Dapper maps columns to properties by name (case-insensitive).
    /// </summary>
    private const string DiagnosticsSql = """
        SELECT name
        FROM admin.systemmodules
        WHERE isbase = TRUE AND gcrecord = 0
        ORDER BY name;

        WITH base AS (
            SELECT id, name
            FROM admin.systemmodules
            WHERE isbase = TRUE AND gcrecord = 0
        ),
        active_companies AS (
            SELECT id, name
            FROM common.companies
            WHERE isactive = TRUE AND gcrecord = 0
        ),
        missing AS (
            SELECT
                c.id   AS companyid,
                c.name AS companyname,
                COALESCE(
                    array_agg(b.name ORDER BY b.name) FILTER (WHERE b.id IS NOT NULL AND cm.id IS NULL),
                    ARRAY[]::TEXT[]
                ) AS missingbasemodules
            FROM active_companies c
            LEFT JOIN base b ON TRUE  -- keeps companies when no base module exists
            LEFT JOIN admin.companymodules cm
                ON cm.companyid = c.id
               AND cm.moduleid = b.id
               AND cm.isactive = TRUE
               AND cm.gcrecord = 0
            GROUP BY c.id, c.name
        ),
        affected_users AS (
            SELECT
                urc.companyid              AS companyid,
                COUNT(DISTINCT urc.userid) AS affectedusercount
            FROM security.userrolecompanies urc
            INNER JOIN security.rolesystemoptions rso
                ON rso.roleid    = urc.roleid
               AND rso.companyid = urc.companyid
               AND rso.gcrecord  = 0
               AND (rso.canread OR rso.cancreate OR rso.canupdate OR rso.candelete
                    OR rso.candownload OR rso.canexport OR rso.canexecute)
            INNER JOIN security.systemoptions so
                ON so.id        = rso.systemoptionid
               AND so.gcrecord  = 0
            INNER JOIN admin.systemmodules sm
                ON sm.id        = so.moduleid
               AND sm.gcrecord  = 0
            LEFT JOIN admin.companymodules cm
                ON cm.companyid = urc.companyid
               AND cm.moduleid  = sm.id
               AND cm.isactive  = TRUE
               AND cm.gcrecord  = 0
            WHERE urc.gcrecord = 0
              AND (sm.isactive = FALSE OR cm.id IS NULL)
            GROUP BY urc.companyid
        )
        SELECT
            m.companyid,
            m.companyname,
            m.missingbasemodules,
            COALESCE(a.affectedusercount, 0) AS affectedusercount
        FROM missing m
        LEFT JOIN affected_users a ON a.companyid = m.companyid
        WHERE cardinality(m.missingbasemodules) > 0
           OR COALESCE(a.affectedusercount, 0) > 0
        ORDER BY m.companyname;
        """;

    /// <inheritdoc />
    public async Task<Response<CompanyModulesDiagnosticsDto>> Handle(
        GetCompanyModulesDiagnosticsQuery request,
        CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        using var grid = await connection.QueryMultipleAsync(
            new CommandDefinition(DiagnosticsSql, cancellationToken: cancellationToken));

        var baseModuleNames = (await grid.ReadAsync<string>()).AsList();
        var rows = (await grid.ReadAsync<CompanyProblemRow>()).AsList();

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
