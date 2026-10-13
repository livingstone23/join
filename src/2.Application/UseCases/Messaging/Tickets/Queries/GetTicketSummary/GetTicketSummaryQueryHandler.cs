using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries.GetTicketSummary;

/// <summary>
/// Computes the inbox summary for the active tenant (SPEC 52). One SQL pass
/// over the open tickets of the company, then the same
/// <see cref="JOIN.Domain.Messaging.TicketSlaCalculator"/> the listing does, so the
/// summary cards and the grid columns never disagree on breach/inactivity.
/// </summary>
public sealed class GetTicketSummaryQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketSummaryQuery, Response<TicketSummaryDto>>
{
    /// <summary>
    /// Aggregates the four counts the inbox summary exposes. The SLA
    /// calculator takes <c>nowUtc</c> as an explicit parameter so callers
    /// (and tests) stay in control of time.
    /// </summary>
    public async Task<Response<TicketSummaryDto>> Handle(
        GetTicketSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketSummaryDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var tenantId = TenantResolver.Resolve(currentUserService, request.CompanyId);

        // Only the inputs the calculator actually consumes. Last activity is
        // aggregated in SQL with a LEFT JOIN + GROUP BY so we don't run one
        // correlated subselect per open ticket (SPEC 52 - plan punto 3).
        const string sql = """
            SELECT
                t.Id,
                t.Created,
                t.AssignedToUserId,
                tc.ResolutionTimeUnits,
                tcu.Code AS ComplexityTimeUnitCode,
                tcd.MaxDayTicketInactivity,
                last.LastActivityAt
            FROM Messaging.Tickets t
            INNER JOIN Messaging.TicketStatuses ts ON t.TicketStatusId = ts.Id
            INNER JOIN Messaging.TicketComplexities tc ON t.TicketComplexityId = tc.Id
            INNER JOIN Messaging.TimeUnits tcu ON tc.TimeUnitId = tcu.Id
            LEFT JOIN Messaging.TicketCompanyDefaults tcd
                ON tcd.CompanyId = t.CompanyId AND tcd.GcRecord = 0
            LEFT JOIN (
                SELECT tl.TicketId, MAX(tl.Created) AS LastActivityAt
                FROM Support.TicketLogs tl
                WHERE tl.GcRecord = 0
                GROUP BY tl.TicketId
            ) last ON last.TicketId = t.Id
            WHERE t.CompanyId = @TenantId
              AND t.GcRecord = 0
              AND ts.IsFinal = FALSE;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<OpenTicketSlaInput>(
            new CommandDefinition(sql, new { TenantId = tenantId }, cancellationToken: cancellationToken)))
            .AsList();

        var nowUtc = DateTime.UtcNow;
        var openCount = 0;
        var slaBreachedCount = 0;
        var inactiveCount = 0;
        var unassignedCount = 0;

        foreach (var row in rows)
        {
            openCount++;

            if (!row.AssignedToUserId.HasValue)
            {
                unassignedCount++;
            }

            var sla = TicketSlaCalculator.Compute(
                createdUtc: row.Created,
                resolutionTimeUnits: row.ResolutionTimeUnits,
                complexityTimeUnitCode: row.ComplexityTimeUnitCode,
                lastActivityUtc: row.LastActivityAt ?? row.Created,
                finishedAtUtc: null,
                isFinalStatus: false,
                maxDayTicketInactivity: row.MaxDayTicketInactivity,
                nowUtc: nowUtc);

            if (sla.IsSlaBreached)
            {
                slaBreachedCount++;
            }

            if (sla.IsInactive)
            {
                inactiveCount++;
            }
        }

        return new Response<TicketSummaryDto>
        {
            IsSuccess = true,
            Message = "Ticket inbox summary retrieved successfully.",
            Data = new TicketSummaryDto
            {
                OpenCount = openCount,
                SlaBreachedCount = slaBreachedCount,
                InactiveCount = inactiveCount,
                UnassignedCount = unassignedCount,
                GeneratedAt = nowUtc
            }
        };
    }

    /// <summary>
    /// Minimal projection of the open tickets the calculator needs. Keeps
    /// the SELECT lean and avoids materializing the columns the listing
    /// query flattens just to drop them.
    /// </summary>
    private sealed class OpenTicketSlaInput
    {
        public Guid Id { get; init; }
        public DateTime Created { get; init; }
        public Guid? AssignedToUserId { get; init; }
        public int ResolutionTimeUnits { get; init; }
        public int ComplexityTimeUnitCode { get; init; }
        public int? MaxDayTicketInactivity { get; init; }
        public DateTime? LastActivityAt { get; init; }
    }
}