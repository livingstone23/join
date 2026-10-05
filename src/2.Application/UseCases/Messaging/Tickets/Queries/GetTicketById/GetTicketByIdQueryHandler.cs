using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Handles ticket detail queries using Dapper for high-performance reads.
/// Log rows marked with <c>IsOnlyForCreatedAndAssigned = 1</c> are filtered
/// server-side: only the ticket's creator, current assignee, or a tenant
/// <c>IsSuperAdminTicket</c> can read them (SPEC 35 F9).
/// SLA / inactivity are computed in C# via <see cref="TicketSlaCalculator"/>
/// to keep the math portable across DB providers (SPEC 37 F6).
/// </summary>
public sealed class GetTicketByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketByIdQuery, Response<TicketDto>>
{
    /// <summary>
    /// Retrieves a flattened ticket projection by identifier, constrained by tenant.
    /// </summary>
    public async Task<Response<TicketDto>> Handle(GetTicketByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var viewerId))
        {
            // SPEC 35 F9 — the log-visibility filter requires a parseable viewer id
            // because every privacy branch (createdBy/assignedTo/superAdmin) compares
            // against @ViewerId. Without one, the handler cannot reason about who is
            // asking and must refuse the read entirely.
            return Response<TicketDto>.Error("USER_REQUIRED", ["The authenticated user identifier is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                t.Id,
                t.CompanyId,
                co.Name AS CompanyName,
                t.Code,
                t.Name,
                t.Description,
                t.EstimatedTime,
                t.ConsumedTime,
                t.IsVisibleToExternals,
                t.TicketStatusId,
                ts.Name AS TicketStatusName,
                t.TicketComplexityId,
                tc.Name AS TicketComplexityName,
                t.TimeUnitId,
                tu.Name AS TimeUnitName,
                t.PersonId,
                CASE
                    WHEN c.Id IS NULL THEN NULL
                    WHEN c.CommercialName IS NOT NULL AND c.CommercialName <> '' THEN c.CommercialName
                    ELSE CONCAT(c.FirstName, ' ', COALESCE(c.MiddleName, ''), ' ', COALESCE(c.LastName, ''), ' ', COALESCE(c.SecondLastName, ''))
                END AS PersonName,
                t.ProjectId,
                p.Name AS ProjectName,
                t.AreaId,
                a.Name AS AreaName,
                t.ChannelId,
                ch.Name AS ChannelName,
                t.CreatedByUserId,
                CONCAT(cu.FirstName, ' ', cu.LastName) AS CreatedByUserName,
                t.AssignedToUserId,
                CASE
                    WHEN au.Id IS NULL THEN NULL
                    ELSE CONCAT(au.FirstName, ' ', au.LastName)
                END AS AssignedToUserName,
                t.PrecedentTicketId,
                pt.Code AS PrecedentTicketCode,
                t.Created AS CreatedAt,
                ts.IsFinal AS IsFinalStatus,
                tc.ResolutionTimeUnits,
                tcu.Code AS ComplexityTimeUnitCode,
                tcd.MaxDayTicketInactivity,
                (SELECT MAX(tl.Created) FROM Support.TicketLogs tl
                    WHERE tl.TicketId = t.Id AND tl.GcRecord = 0) AS LastActivityAt,
                (SELECT MAX(tl2.Created) FROM Support.TicketLogs tl2
                    WHERE tl2.TicketId = t.Id AND tl2.LogType = 5 AND tl2.GcRecord = 0) AS FinishedAt
            FROM Messaging.Tickets t
            LEFT JOIN Common.Companies co ON t.CompanyId = co.Id
            INNER JOIN Messaging.TicketStatuses ts ON t.TicketStatusId = ts.Id
            INNER JOIN Messaging.TicketComplexities tc ON t.TicketComplexityId = tc.Id
            INNER JOIN Messaging.TimeUnits tu ON t.TimeUnitId = tu.Id
            INNER JOIN Messaging.TimeUnits tcu ON tc.TimeUnitId = tcu.Id
            LEFT JOIN Messaging.TicketCompanyDefaults tcd ON tcd.CompanyId = t.CompanyId AND tcd.GcRecord = 0
            LEFT JOIN Admin.Persons c ON t.PersonId = c.Id
            LEFT JOIN Admin.Projects p ON t.ProjectId = p.Id
            LEFT JOIN Admin.Areas a ON t.AreaId = a.Id
            INNER JOIN Common.CommunicationChannels ch ON t.ChannelId = ch.Id
            INNER JOIN Security.Users cu ON t.CreatedByUserId = cu.Id
            LEFT JOIN Security.Users au ON t.AssignedToUserId = au.Id
            LEFT JOIN Messaging.Tickets pt ON t.PrecedentTicketId = pt.Id
            WHERE t.Id = @Id
              AND t.CompanyId = @TenantId
              AND t.GcRecord = 0;

            SELECT
                tl.Id,
                CASE tl.LogType
                    WHEN 0 THEN 'Creation'
                    WHEN 1 THEN 'StatusChange'
                    WHEN 2 THEN 'InternalNote'
                    WHEN 3 THEN 'ExternalNote'
                    WHEN 4 THEN 'Reassignment'
                    WHEN 5 THEN 'Finalization'
                    ELSE CONCAT('Unknown(', tl.LogType, ')')
                END AS LogType,
                tl.Summary,
                tl.Created AS CreatedAt,
                CONCAT(usr.FirstName, ' ', usr.LastName) AS UserRegisteredName,
                ps.Name AS PreviousStatusName,
                ns.Name AS NewStatusName,
                tl.ConsumedTime
            FROM Support.TicketLogs tl
            INNER JOIN Messaging.Tickets t ON tl.TicketId = t.Id
            LEFT JOIN Security.Users usr ON tl.UserRegisterLogId = usr.Id
            LEFT JOIN Messaging.TicketStatuses ps ON tl.PreviousStatusId = ps.Id
            LEFT JOIN Messaging.TicketStatuses ns ON tl.TicketStatusId = ns.Id
            WHERE tl.TicketId = @Id
              AND tl.CompanyId = @TenantId
              AND tl.GcRecord = 0
              AND (
                    tl.IsOnlyForCreatedAndAssigned = 0
                    OR t.CreatedByUserId = @ViewerId
                    OR t.AssignedToUserId = @ViewerId
                    OR EXISTS (
                        SELECT 1
                        FROM Messaging.TicketUserCompanies tuc
                        WHERE tuc.UserId = @ViewerId
                          AND tuc.CompanyId = @TenantId
                          AND tuc.GcRecord = 0
                          AND tuc.IsSuperAdminTicket = 1
                    )
                  )
            ORDER BY tl.Created DESC;
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                new { request.Id, TenantId = currentUserService.CompanyId, ViewerId = viewerId },
                cancellationToken: cancellationToken));

        var row = await multi.ReadFirstOrDefaultAsync<TicketSlaRow>();

        if (row is null)
        {
            return Response<TicketDto>.Error("TICKET_NOT_FOUND", ["Ticket not found for the current company."]);
        }

        var sla = TicketSlaCalculator.Compute(
            row.CreatedAt,
            row.ResolutionTimeUnits,
            row.ComplexityTimeUnitCode,
            row.LastActivityAt ?? row.CreatedAt,
            row.FinishedAt,
            row.IsFinalStatus,
            row.MaxDayTicketInactivity,
            nowUtc: DateTime.UtcNow);

        var ticket = new TicketDto
        {
            Id = row.Id,
            CompanyId = row.CompanyId,
            CompanyName = row.CompanyName,
            Code = row.Code,
            Name = row.Name,
            Description = row.Description,
            EstimatedTime = row.EstimatedTime,
            ConsumedTime = row.ConsumedTime,
            IsVisibleToExternals = row.IsVisibleToExternals,
            TicketStatusId = row.TicketStatusId,
            TicketStatusName = row.TicketStatusName,
            TicketComplexityId = row.TicketComplexityId,
            TicketComplexityName = row.TicketComplexityName,
            TimeUnitId = row.TimeUnitId,
            TimeUnitName = row.TimeUnitName,
            PersonId = row.PersonId,
            PersonName = row.PersonName,
            ProjectId = row.ProjectId,
            ProjectName = row.ProjectName,
            AreaId = row.AreaId,
            AreaName = row.AreaName,
            ChannelId = row.ChannelId,
            ChannelName = row.ChannelName,
            CreatedByUserId = row.CreatedByUserId,
            CreatedByUserName = row.CreatedByUserName,
            AssignedToUserId = row.AssignedToUserId,
            AssignedToUserName = row.AssignedToUserName,
            PrecedentTicketId = row.PrecedentTicketId,
            PrecedentTicketCode = row.PrecedentTicketCode,
            CreatedAt = row.CreatedAt,
            SlaDueAt = sla.SlaDueAt,
            IsSlaBreached = sla.IsSlaBreached,
            LastActivityAt = sla.LastActivityAt,
            IsInactive = sla.IsInactive
        };

        ticket.Logs = (await multi.ReadAsync<TicketLogDto>()).AsList();

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = "Ticket retrieved successfully.",
            Data = ticket
        };
    }

    /// <summary>
    /// Flat row that combines every <see cref="TicketDto"/> column with the
    /// raw SLA inputs that have no destination on the DTO and feed
    /// <see cref="TicketSlaCalculator.Compute"/> instead.
    /// </summary>
    private sealed class TicketSlaRow
    {
        public Guid Id { get; init; }
        public Guid CompanyId { get; init; }
        public string? CompanyName { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal EstimatedTime { get; init; }
        public decimal ConsumedTime { get; init; }
        public bool IsVisibleToExternals { get; init; }
        public Guid TicketStatusId { get; init; }
        public string TicketStatusName { get; init; } = string.Empty;
        public Guid TicketComplexityId { get; init; }
        public string TicketComplexityName { get; init; } = string.Empty;
        public Guid TimeUnitId { get; init; }
        public string TimeUnitName { get; init; } = string.Empty;
        public Guid? PersonId { get; init; }
        public string? PersonName { get; init; }
        public Guid? ProjectId { get; init; }
        public string? ProjectName { get; init; }
        public Guid? AreaId { get; init; }
        public string? AreaName { get; init; }
        public Guid ChannelId { get; init; }
        public string ChannelName { get; init; } = string.Empty;
        public Guid CreatedByUserId { get; init; }
        public string CreatedByUserName { get; init; } = string.Empty;
        public Guid? AssignedToUserId { get; init; }
        public string? AssignedToUserName { get; init; }
        public Guid? PrecedentTicketId { get; init; }
        public string? PrecedentTicketCode { get; init; }
        public DateTime CreatedAt { get; init; }

        // --- SLA raw inputs (SPEC 37 F6) ---
        public bool IsFinalStatus { get; init; }
        public int ResolutionTimeUnits { get; init; }
        public int ComplexityTimeUnitCode { get; init; }
        public int? MaxDayTicketInactivity { get; init; }
        public DateTime? LastActivityAt { get; init; }
        public DateTime? FinishedAt { get; init; }
    }
}