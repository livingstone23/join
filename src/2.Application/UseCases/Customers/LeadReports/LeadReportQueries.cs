using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadReports;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Customers.Leads.Common;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Customers.LeadReports;

/// <summary>Filters shared by every commercial report (SPEC 64): period (inclusive, UTC), team and owner.</summary>
public interface ILeadReportFilter
{
    DateOnly? From { get; }
    DateOnly? To { get; }
    Guid? SalesTeamId { get; }
    Guid? OwnerUserId { get; }
}

/// <summary><c>GET /LeadReports/summary</c>.</summary>
public sealed record GetLeadReportSummaryQuery(DateOnly? From = null, DateOnly? To = null, Guid? SalesTeamId = null, Guid? OwnerUserId = null)
    : ILeadReportFilter, IRequest<Response<LeadReportSummaryDto>>;

/// <summary><c>GET /LeadReports/funnel</c> (current snapshot of the open stages).</summary>
public sealed record GetLeadReportFunnelQuery(DateOnly? From = null, DateOnly? To = null, Guid? SalesTeamId = null, Guid? OwnerUserId = null)
    : ILeadReportFilter, IRequest<Response<IReadOnlyList<LeadReportFunnelStageDto>>>;

/// <summary><c>GET /LeadReports/by-team</c>.</summary>
public sealed record GetLeadReportByTeamQuery(DateOnly? From = null, DateOnly? To = null, Guid? SalesTeamId = null, Guid? OwnerUserId = null)
    : ILeadReportFilter, IRequest<Response<IReadOnlyList<LeadReportTeamDto>>>;

/// <summary><c>GET /LeadReports/ranking</c>.</summary>
public sealed record GetLeadReportRankingQuery(DateOnly? From = null, DateOnly? To = null, Guid? SalesTeamId = null, Guid? OwnerUserId = null)
    : ILeadReportFilter, IRequest<Response<IReadOnlyList<LeadReportRankingDto>>>;

/// <summary><c>GET /LeadReports/by-product</c>.</summary>
public sealed record GetLeadReportByProductQuery(DateOnly? From = null, DateOnly? To = null, Guid? SalesTeamId = null, Guid? OwnerUserId = null)
    : ILeadReportFilter, IRequest<Response<IReadOnlyList<LeadReportProductDto>>>;

/// <summary><c>GET /LeadReports/by-source</c>.</summary>
public sealed record GetLeadReportBySourceQuery(DateOnly? From = null, DateOnly? To = null, Guid? SalesTeamId = null, Guid? OwnerUserId = null)
    : ILeadReportFilter, IRequest<Response<IReadOnlyList<LeadReportSourceDto>>>;

/// <summary><c>GET /LeadReports/by-contact-type</c>.</summary>
public sealed record GetLeadReportByContactTypeQuery(DateOnly? From = null, DateOnly? To = null, Guid? SalesTeamId = null, Guid? OwnerUserId = null)
    : ILeadReportFilter, IRequest<Response<LeadReportContactTypesDto>>;

/// <summary>
/// <c>GET /LeadReports/won-leads</c>: paged detail of the won leads of the period; <c>Export</c> returns every row (max
/// 10 000) and needs <c>CanExport</c> (checked by the controller).
/// </summary>
public sealed record GetLeadReportWonLeadsQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? SalesTeamId = null,
    Guid? OwnerUserId = null,
    bool Export = false,
    int? PageNumber = null,
    int? PageSize = null) : ILeadReportFilter, IRequest<Response<PagedResult<LeadReportWonLeadDto>>>;

/// <summary>A validated report request: tenant, scope, period bounds and the common WHERE.</summary>
public sealed record LeadReportScope(Guid CompanyId, string Where, DynamicParameters Parameters);

/// <summary>
/// Validates the filters (<c>REPORT_PERIOD_INVALID</c>, <c>INVALID_SALES_TEAM</c>) and builds the common WHERE over
/// <c>customer.leads l</c>: tenant, live rows, report scope (SPEC 60) and the team/owner filters. <c>@From</c>/<c>@To</c>
/// are the period bounds (<c>@To</c> exclusive). Without dates the period is the current month up to today.
/// </summary>
public sealed class LeadReportContext(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService, LeadAccessGuard guard)
{
    public const string ReportPeriodInvalid = "REPORT_PERIOD_INVALID";
    public const string InvalidSalesTeam = "INVALID_SALES_TEAM";

    internal const string Created = "l.Created >= @From AND l.Created < @To";
    internal const string Won = "l.Status = 3 AND l.ClosedAt >= @From AND l.ClosedAt < @To";
    internal const string Days = "EXTRACT(EPOCH FROM (l.ClosedAt - l.Created)) / 86400.0";

    public async Task<(LeadReportScope? Scope, Response<T>? Error)> ResolveAsync<T>(ILeadReportFilter filter, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<T>(currentUserService, out var actor) is { } noActor)
        {
            return (null, noActor);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = filter.From ?? new DateOnly(today.Year, today.Month, 1);
        var to = filter.To ?? today;
        if (to < from || to.DayNumber - from.DayNumber > 366)
        {
            return (null, Response<T>.Error(ReportPeriodInvalid, ["'to' must not be earlier than 'from', and the period cannot exceed 366 days."]));
        }

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", actor.CompanyId);
        parameters.Add("From", from.ToDateTime(TimeOnly.MinValue));
        parameters.Add("To", to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        parameters.Add("Today", today.ToDateTime(TimeOnly.MinValue));

        var where = new StringBuilder("l.CompanyId = @CompanyId AND l.GcRecord = 0");
        if (filter.SalesTeamId is { } teamId)
        {
            using var connection = connectionFactory.CreateConnection();
            var exists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM customer.salesteams t WHERE t.Id = @Id AND t.CompanyId = @CompanyId AND t.GcRecord = 0);",
                new { Id = teamId, actor.CompanyId }, cancellationToken: cancellationToken));
            if (!exists)
            {
                return (null, Response<T>.Error(InvalidSalesTeam, ["The team does not exist in the company."]));
            }

            where.Append(" AND l.SalesTeamId = @SalesTeamId");
            parameters.Add("SalesTeamId", teamId);
        }

        if (filter.OwnerUserId is { } ownerId)
        {
            where.Append(" AND l.OwnerUserId = @OwnerUserId");
            parameters.Add("OwnerUserId", ownerId);
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        where.Append(' ').Append(access.ReportFilter("l", parameters));
        return (new LeadReportScope(actor.CompanyId, where.ToString(), parameters), null);
    }

    internal static Response<T> Ok<T>(T data, string message) => new() { IsSuccess = true, Message = message, Data = data };
}

/// <summary>Raw funnel row (weighted value computed in C#).</summary>
internal sealed class FunnelRow
{
    public Guid LeadStageId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int Probability { get; set; }
    public int Count { get; set; }
    public decimal ValueBase { get; set; }

    internal static string Sql(string where) => $"""
        SELECT s.Id AS LeadStageId, s.Code, s.Name, s.SortOrder, s.Probability,
               COUNT(l.Id)::int AS Count, COALESCE(SUM(l.EstimatedValueBase), 0) AS ValueBase
        FROM customer.leadstages s
        LEFT JOIN customer.leads l ON l.LeadStageId = s.Id AND l.Status = 1 AND {where}
        WHERE s.CompanyId = @CompanyId AND s.GcRecord = 0 AND s.IsActive = TRUE AND s.Kind IN (1, 2)
        GROUP BY s.Id, s.Code, s.Name, s.SortOrder, s.Probability
        ORDER BY s.SortOrder, s.Code;
        """;

    internal LeadReportFunnelStageDto ToDto() => new()
    {
        LeadStageId = LeadStageId, Code = Code, Name = Name, SortOrder = SortOrder, Probability = Probability, Count = Count, ValueBase = ValueBase,
        WeightedValueBase = LeadReportMath.Weighted(ValueBase, Probability)
    };
}

/// <summary>Handles <see cref="GetLeadReportSummaryQuery"/>.</summary>
public sealed class GetLeadReportSummaryQueryHandler(ISqlConnectionFactory connectionFactory, LeadReportContext context)
    : IRequestHandler<GetLeadReportSummaryQuery, Response<LeadReportSummaryDto>>
{
    public async Task<Response<LeadReportSummaryDto>> Handle(GetLeadReportSummaryQuery request, CancellationToken cancellationToken)
    {
        var (scope, error) = await context.ResolveAsync<LeadReportSummaryDto>(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var sql = $"""
            SELECT
                COUNT(*) FILTER (WHERE {LeadReportContext.Created})::int AS CreatedCount,
                COUNT(*) FILTER (WHERE {LeadReportContext.Won})::int AS WonCount,
                COALESCE(SUM(l.WonValueBase) FILTER (WHERE {LeadReportContext.Won}), 0) AS WonValueBase,
                AVG({LeadReportContext.Days}) FILTER (WHERE {LeadReportContext.Won})::float8 AS AvgDays,
                COALESCE((SELECT c.Code FROM finance.companycurrencies cc INNER JOIN finance.currencies c ON c.Id = cc.CurrencyId
                          WHERE cc.CompanyId = @CompanyId AND cc.IsBase = TRUE AND cc.GcRecord = 0 LIMIT 1), '') AS BaseCurrencyCode
            FROM customer.leads l
            WHERE {scope!.Where};

            {FunnelRow.Sql(scope.Where)}
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, scope.Parameters, cancellationToken: cancellationToken));
        var totals = await multi.ReadSingleAsync<SummaryRow>();
        var funnel = (await multi.ReadAsync<FunnelRow>()).Select(r => r.ToDto()).ToList();

        return LeadReportContext.Ok(new LeadReportSummaryDto
        {
            OpenCount = funnel.Sum(f => f.Count),
            CreatedCount = totals.CreatedCount,
            WonCount = totals.WonCount,
            ConversionPercent = LeadReportMath.Conversion(totals.WonCount, totals.CreatedCount),
            PipelineValueBase = funnel.Sum(f => f.ValueBase),
            WeightedPipelineValueBase = funnel.Sum(f => f.WeightedValueBase),
            WonValueBase = totals.WonValueBase,
            AvgDaysToClose = LeadReportMath.Days(totals.AvgDays),
            BaseCurrencyCode = totals.BaseCurrencyCode
        }, "Commercial summary retrieved successfully.");
    }

    private sealed class SummaryRow
    {
        public int CreatedCount { get; set; }
        public int WonCount { get; set; }
        public decimal WonValueBase { get; set; }
        public double? AvgDays { get; set; }
        public string BaseCurrencyCode { get; set; } = string.Empty;
    }
}

/// <summary>Handles <see cref="GetLeadReportFunnelQuery"/>.</summary>
public sealed class GetLeadReportFunnelQueryHandler(ISqlConnectionFactory connectionFactory, LeadReportContext context)
    : IRequestHandler<GetLeadReportFunnelQuery, Response<IReadOnlyList<LeadReportFunnelStageDto>>>
{
    public async Task<Response<IReadOnlyList<LeadReportFunnelStageDto>>> Handle(GetLeadReportFunnelQuery request, CancellationToken cancellationToken)
    {
        var (scope, error) = await context.ResolveAsync<IReadOnlyList<LeadReportFunnelStageDto>>(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<FunnelRow>(new CommandDefinition(FunnelRow.Sql(scope!.Where), scope.Parameters, cancellationToken: cancellationToken)))
            .Select(r => r.ToDto())
            .ToList();
        return LeadReportContext.Ok<IReadOnlyList<LeadReportFunnelStageDto>>(rows, "Funnel retrieved successfully.");
    }
}

/// <summary>Handles <see cref="GetLeadReportByTeamQuery"/>.</summary>
public sealed class GetLeadReportByTeamQueryHandler(ISqlConnectionFactory connectionFactory, LeadReportContext context)
    : IRequestHandler<GetLeadReportByTeamQuery, Response<IReadOnlyList<LeadReportTeamDto>>>
{
    public async Task<Response<IReadOnlyList<LeadReportTeamDto>>> Handle(GetLeadReportByTeamQuery request, CancellationToken cancellationToken)
    {
        var (scope, error) = await context.ResolveAsync<IReadOnlyList<LeadReportTeamDto>>(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var sql = $"""
            SELECT
                l.SalesTeamId,
                COALESCE(t.Name, 'Sin grupo') AS SalesTeamName,
                COUNT(*) FILTER (WHERE {LeadReportContext.Created})::int AS CreatedCount,
                COUNT(*) FILTER (WHERE {LeadReportContext.Won})::int AS WonCount,
                COALESCE(SUM(l.WonValueBase) FILTER (WHERE {LeadReportContext.Won}), 0) AS WonValueBase,
                AVG({LeadReportContext.Days}) FILTER (WHERE {LeadReportContext.Won})::float8 AS AvgDays
            FROM customer.leads l
            LEFT JOIN customer.salesteams t ON t.Id = l.SalesTeamId
            WHERE {scope!.Where}
              AND (({LeadReportContext.Created}) OR ({LeadReportContext.Won}))
            GROUP BY l.SalesTeamId, t.Name
            ORDER BY WonCount DESC, CreatedCount DESC, SalesTeamName;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<TeamRow>(new CommandDefinition(sql, scope.Parameters, cancellationToken: cancellationToken)))
            .Select(r => new LeadReportTeamDto
            {
                SalesTeamId = r.SalesTeamId, SalesTeamName = r.SalesTeamName, CreatedCount = r.CreatedCount, WonCount = r.WonCount,
                ConversionPercent = LeadReportMath.Conversion(r.WonCount, r.CreatedCount), WonValueBase = r.WonValueBase, AvgDaysToClose = LeadReportMath.Days(r.AvgDays)
            })
            .ToList();
        return LeadReportContext.Ok<IReadOnlyList<LeadReportTeamDto>>(rows, "Team report retrieved successfully.");
    }

    private sealed class TeamRow
    {
        public Guid? SalesTeamId { get; set; }
        public string SalesTeamName { get; set; } = string.Empty;
        public int CreatedCount { get; set; }
        public int WonCount { get; set; }
        public decimal WonValueBase { get; set; }
        public double? AvgDays { get; set; }
    }
}

/// <summary>Handles <see cref="GetLeadReportRankingQuery"/>.</summary>
public sealed class GetLeadReportRankingQueryHandler(ISqlConnectionFactory connectionFactory, LeadReportContext context)
    : IRequestHandler<GetLeadReportRankingQuery, Response<IReadOnlyList<LeadReportRankingDto>>>
{
    public async Task<Response<IReadOnlyList<LeadReportRankingDto>>> Handle(GetLeadReportRankingQuery request, CancellationToken cancellationToken)
    {
        var (scope, error) = await context.ResolveAsync<IReadOnlyList<LeadReportRankingDto>>(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var sql = $"""
            WITH period AS (
                SELECT l.Id, l.OwnerUserId, l.SalesTeamId, l.WonValueBase, l.Created, l.ClosedAt,
                       ({LeadReportContext.Created}) AS IsCreated,
                       ({LeadReportContext.Won}) AS IsWon,
                       {LeadReportContext.Days} AS Days
                FROM customer.leads l
                WHERE {scope!.Where}
                  AND (({LeadReportContext.Created}) OR ({LeadReportContext.Won}))
            ),
            agg AS (
                SELECT p.OwnerUserId,
                       COUNT(*) FILTER (WHERE p.IsCreated)::int AS CreatedCount,
                       COUNT(*) FILTER (WHERE p.IsWon)::int AS WonCount,
                       COALESCE(SUM(p.WonValueBase) FILTER (WHERE p.IsWon), 0) AS WonValueBase,
                       AVG(p.Days) FILTER (WHERE p.IsWon)::float8 AS AvgDays,
                       STRING_AGG(DISTINCT t.Name, ', ') FILTER (WHERE p.IsWon AND t.Name IS NOT NULL) AS WonTeams
                FROM period p
                LEFT JOIN customer.salesteams t ON t.Id = p.SalesTeamId
                GROUP BY p.OwnerUserId
            )
            SELECT
                a.OwnerUserId AS UserId,
                CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
                a.WonTeams,
                (SELECT r.Name FROM customer.salesteammembers m INNER JOIN customer.teamroles r ON r.Id = m.TeamRoleId
                  WHERE m.CompanyId = @CompanyId AND m.UserId = a.OwnerUserId AND m.GcRecord = 0
                    AND m.ValidFrom <= @Today AND (m.ValidTo IS NULL OR m.ValidTo >= @Today)
                  ORDER BY m.IsPrimary DESC, m.ValidFrom LIMIT 1) AS CurrentRole,
                a.CreatedCount, a.WonCount, a.WonValueBase, a.AvgDays,
                (SELECT ctc.Name FROM customer.leadinteractions i
                  INNER JOIN period w ON w.Id = i.LeadId AND w.IsWon AND w.OwnerUserId = a.OwnerUserId
                  INNER JOIN customer.contacttypecompanies ctc ON ctc.Id = i.ContactTypeCompanyId
                  WHERE i.GcRecord = 0
                  GROUP BY ctc.Name ORDER BY COUNT(*) DESC, ctc.Name LIMIT 1) AS TopContactType,
                (SELECT pr.Name FROM customer.leadproducts lp
                  INNER JOIN period w ON w.Id = lp.LeadId AND w.IsWon AND w.OwnerUserId = a.OwnerUserId
                  INNER JOIN catalog.products pr ON pr.Id = lp.ProductId
                  WHERE lp.GcRecord = 0 AND lp.Status = 2
                  GROUP BY pr.Name ORDER BY COUNT(*) DESC, pr.Name LIMIT 1) AS TopProduct
            FROM agg a
            INNER JOIN security.users u ON u.Id = a.OwnerUserId
            ORDER BY a.WonCount DESC, a.WonValueBase DESC, UserName;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<RankingRow>(new CommandDefinition(sql, scope.Parameters, cancellationToken: cancellationToken)))
            .Select(r => new LeadReportRankingDto
            {
                UserId = r.UserId, UserName = r.UserName, WonTeams = r.WonTeams, CurrentRole = r.CurrentRole, CreatedCount = r.CreatedCount, WonCount = r.WonCount,
                ConversionPercent = LeadReportMath.Conversion(r.WonCount, r.CreatedCount), WonValueBase = r.WonValueBase, AvgDaysToClose = LeadReportMath.Days(r.AvgDays),
                TopContactType = r.TopContactType, TopProduct = r.TopProduct
            })
            .ToList();
        return LeadReportContext.Ok<IReadOnlyList<LeadReportRankingDto>>(rows, "Ranking retrieved successfully.");
    }

    private sealed class RankingRow
    {
        public Guid UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string? WonTeams { get; set; }
        public string? CurrentRole { get; set; }
        public int CreatedCount { get; set; }
        public int WonCount { get; set; }
        public decimal WonValueBase { get; set; }
        public double? AvgDays { get; set; }
        public string? TopContactType { get; set; }
        public string? TopProduct { get; set; }
    }
}

/// <summary>Handles <see cref="GetLeadReportByProductQuery"/>.</summary>
public sealed class GetLeadReportByProductQueryHandler(ISqlConnectionFactory connectionFactory, LeadReportContext context)
    : IRequestHandler<GetLeadReportByProductQuery, Response<IReadOnlyList<LeadReportProductDto>>>
{
    public async Task<Response<IReadOnlyList<LeadReportProductDto>>> Handle(GetLeadReportByProductQuery request, CancellationToken cancellationToken)
    {
        var (scope, error) = await context.ResolveAsync<IReadOnlyList<LeadReportProductDto>>(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var sql = $"""
            SELECT pr.Id AS ProductId, pr.Code AS ProductCode, pr.Name AS ProductName,
                   COUNT(DISTINCT l.Id)::int AS LeadsCount,
                   COALESCE(SUM(lp.Quantity), 0) AS Quantity,
                   COALESCE(SUM(ROUND(lp.LineTotal * COALESCE(l.WonExchangeRate, l.ExchangeRate), 2)), 0) AS ValueBase
            FROM customer.leads l
            INNER JOIN customer.leadproducts lp ON lp.LeadId = l.Id AND lp.GcRecord = 0 AND lp.Status = 2
            INNER JOIN catalog.products pr ON pr.Id = lp.ProductId
            WHERE {scope!.Where}
              AND {LeadReportContext.Won}
            GROUP BY pr.Id, pr.Code, pr.Name
            ORDER BY ValueBase DESC, ProductName;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<LeadReportProductDto>(new CommandDefinition(sql, scope.Parameters, cancellationToken: cancellationToken))).ToList();
        return LeadReportContext.Ok<IReadOnlyList<LeadReportProductDto>>(rows, "Product report retrieved successfully.");
    }
}

/// <summary>Handles <see cref="GetLeadReportBySourceQuery"/>.</summary>
public sealed class GetLeadReportBySourceQueryHandler(ISqlConnectionFactory connectionFactory, LeadReportContext context)
    : IRequestHandler<GetLeadReportBySourceQuery, Response<IReadOnlyList<LeadReportSourceDto>>>
{
    public async Task<Response<IReadOnlyList<LeadReportSourceDto>>> Handle(GetLeadReportBySourceQuery request, CancellationToken cancellationToken)
    {
        var (scope, error) = await context.ResolveAsync<IReadOnlyList<LeadReportSourceDto>>(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var sql = $"""
            SELECT src.Id AS LeadSourceId, src.Name AS SourceName,
                   COUNT(*) FILTER (WHERE {LeadReportContext.Created})::int AS CreatedCount,
                   COUNT(*) FILTER (WHERE {LeadReportContext.Won})::int AS WonCount
            FROM customer.leads l
            INNER JOIN customer.leadsources src ON src.Id = l.LeadSourceId
            WHERE {scope!.Where}
              AND (({LeadReportContext.Created}) OR ({LeadReportContext.Won}))
            GROUP BY src.Id, src.Name
            ORDER BY WonCount DESC, CreatedCount DESC, SourceName;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<LeadReportSourceDto>(new CommandDefinition(sql, scope.Parameters, cancellationToken: cancellationToken))).ToList();
        return LeadReportContext.Ok<IReadOnlyList<LeadReportSourceDto>>(rows, "Source report retrieved successfully.");
    }
}

/// <summary>Handles <see cref="GetLeadReportByContactTypeQuery"/>.</summary>
public sealed class GetLeadReportByContactTypeQueryHandler(ISqlConnectionFactory connectionFactory, LeadReportContext context)
    : IRequestHandler<GetLeadReportByContactTypeQuery, Response<LeadReportContactTypesDto>>
{
    public async Task<Response<LeadReportContactTypesDto>> Handle(GetLeadReportByContactTypeQuery request, CancellationToken cancellationToken)
    {
        var (scope, error) = await context.ResolveAsync<LeadReportContactTypesDto>(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var sql = $"""
            SELECT COUNT(*)::int FROM customer.leads l WHERE {scope!.Where} AND {LeadReportContext.Won};

            SELECT ct.Code AS ContactTypeCode, ctc.Name AS ContactTypeName, COUNT(*)::int AS Count
            FROM customer.leads l
            INNER JOIN customer.leadinteractions i ON i.LeadId = l.Id AND i.GcRecord = 0
            INNER JOIN customer.contacttypecompanies ctc ON ctc.Id = i.ContactTypeCompanyId
            INNER JOIN customer.contacttypes ct ON ct.Id = ctc.ContactTypeId
            WHERE {scope.Where}
              AND {LeadReportContext.Won}
            GROUP BY ct.Code, ctc.Name
            ORDER BY Count DESC, ContactTypeName;
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, scope.Parameters, cancellationToken: cancellationToken));
        var won = await multi.ReadSingleAsync<int>();
        var items = (await multi.ReadAsync<LeadReportContactTypeDto>()).ToList();
        var total = items.Sum(i => i.Count);

        return LeadReportContext.Ok(new LeadReportContactTypesDto
        {
            WonLeadsCount = won,
            InteractionsCount = total,
            AveragePerWonLead = won == 0 ? 0 : Math.Round((decimal)total / won, 1, MidpointRounding.AwayFromZero),
            Items = items
        }, "Contact type report retrieved successfully.");
    }
}

/// <summary>Handles <see cref="GetLeadReportWonLeadsQuery"/>.</summary>
public sealed class GetLeadReportWonLeadsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    LeadReportContext context,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetLeadReportWonLeadsQuery, Response<PagedResult<LeadReportWonLeadDto>>>
{
    /// <summary>Rows returned by an export (SPEC 64 item 9).</summary>
    public const int ExportLimit = 10_000;

    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<LeadReportWonLeadDto>>> Handle(GetLeadReportWonLeadsQuery request, CancellationToken cancellationToken)
    {
        var (scope, error) = await context.ResolveAsync<PagedResult<LeadReportWonLeadDto>>(request, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var (pageNumber, pageSize) = request.Export ? (1, ExportLimit) : _pagination.Sanitize(request.PageNumber, request.PageSize);
        scope!.Parameters.Add("Offset", (pageNumber - 1) * pageSize);
        scope.Parameters.Add("PageSize", pageSize);
        var sql = $"""
            SELECT
                l.Id, l.Code,
                TRIM(COALESCE(NULLIF(p.CommercialName, ''), CONCAT(p.FirstName, ' ', p.LastName))) AS PersonName,
                cu.CustomerCode,
                l.OwnerUserId, CONCAT(u.FirstName, ' ', u.LastName) AS OwnerName,
                COALESCE(t.Name, 'Sin grupo') AS SalesTeamName,
                src.Name AS SourceName,
                l.ClosedAt,
                COALESCE(l.WonValueBase, 0) AS WonValueBase,
                (SELECT STRING_AGG(pr.Name, ', ' ORDER BY pr.Name) FROM customer.leadproducts lp
                  INNER JOIN catalog.products pr ON pr.Id = lp.ProductId
                  WHERE lp.LeadId = l.Id AND lp.GcRecord = 0 AND lp.Status = 2) AS AcceptedProducts
            FROM customer.leads l
            INNER JOIN admin.persons p ON p.Id = l.PersonId
            INNER JOIN security.users u ON u.Id = l.OwnerUserId
            INNER JOIN customer.leadsources src ON src.Id = l.LeadSourceId
            LEFT JOIN customer.salesteams t ON t.Id = l.SalesTeamId
            LEFT JOIN customer.customers cu ON cu.Id = l.CustomerId
            WHERE {scope.Where}
              AND {LeadReportContext.Won}
            ORDER BY l.ClosedAt DESC, l.Code
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM customer.leads l WHERE {scope.Where} AND {LeadReportContext.Won};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, scope.Parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<LeadReportWonLeadDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return LeadReportContext.Ok(
            new PagedResult<LeadReportWonLeadDto> { Items = items, PageNumber = pageNumber, PageSize = pageSize, TotalCount = total },
            "Won leads retrieved successfully.");
    }
}
