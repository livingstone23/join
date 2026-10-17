using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Customers.Leads;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Customers.Leads.Queries;

/// <summary>Shared SQL of the lead reads (SPEC 61).</summary>
internal static class LeadQuerySql
{
    internal const string PersonName = "TRIM(COALESCE(NULLIF(p.CommercialName, ''), CONCAT(p.FirstName, ' ', p.LastName)))";

    internal const string ListColumns = $"""
        SELECT
            l.Id,
            l.Code,
            l.PersonId,
            {PersonName} AS PersonName,
            l.Topic,
            l.Status,
            l.LeadStageId,
            s.Code AS StageCode,
            s.Name AS StageName,
            s.Probability AS StageProbability,
            l.OwnerUserId,
            CONCAT(u.FirstName, ' ', u.LastName) AS OwnerName,
            l.SalesTeamId,
            t.Name AS SalesTeamName,
            l.LeadSourceId,
            src.Name AS SourceName,
            l.RelationshipTypeId,
            rt.Name AS RelationshipTypeName,
            l.CurrencyId,
            cur.Code AS CurrencyCode,
            l.EstimatedValue,
            l.EstimatedValueBase,
            l.EstimatedCloseDate,
            l.LastInteractionAt,
            lastct.Name AS LastContactTypeName,
            l.Created AS CreatedAt
        """;

    internal const string ListFrom = """
        FROM customer.leads l
        INNER JOIN admin.persons p ON p.Id = l.PersonId
        INNER JOIN customer.leadstages s ON s.Id = l.LeadStageId
        INNER JOIN security.users u ON u.Id = l.OwnerUserId
        LEFT JOIN customer.salesteams t ON t.Id = l.SalesTeamId
        INNER JOIN customer.leadsources src ON src.Id = l.LeadSourceId
        INNER JOIN customer.relationshiptypes rt ON rt.Id = l.RelationshipTypeId
        INNER JOIN finance.currencies cur ON cur.Id = l.CurrencyId
        LEFT JOIN LATERAL (
            SELECT ctc.Name
            FROM customer.leadinteractions i
            INNER JOIN customer.contacttypecompanies ctc ON ctc.Id = i.ContactTypeCompanyId
            WHERE i.LeadId = l.Id AND i.GcRecord = 0
            ORDER BY i.OccurredAt DESC
            LIMIT 1
        ) lastct ON TRUE
        """;

    internal const string InboxOrder = "l.LastInteractionAt DESC NULLS LAST, l.Created DESC";

    /// <summary>Status, owner and team of a live lead of the company, to apply the scope before reading more.</summary>
    internal static async Task<bool> IsVisibleAsync(System.Data.IDbConnection connection, Guid leadId, Guid companyId, LeadAccess access, CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<ScopeRow>(new CommandDefinition(
            "SELECT l.Status, l.OwnerUserId, l.SalesTeamId FROM customer.leads l WHERE l.Id = @LeadId AND l.CompanyId = @CompanyId AND l.GcRecord = 0;",
            new { LeadId = leadId, CompanyId = companyId },
            cancellationToken: cancellationToken));
        return row is not null && access.CanSee((LeadStatus)row.Status, row.OwnerUserId, row.SalesTeamId);
    }

    internal sealed class ScopeRow
    {
        public int Status { get; set; }
        public Guid OwnerUserId { get; set; }
        public Guid? SalesTeamId { get; set; }
    }
}

/// <summary>Handles <see cref="GetLeadsQuery"/>.</summary>
public sealed class GetLeadsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService,
    LeadAccessGuard guard)
    : IRequestHandler<GetLeadsQuery, Response<PagedResult<LeadListItemDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<LeadListItemDto>>> Handle(GetLeadsQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<PagedResult<LeadListItemDto>>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", actor.CompanyId);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE l.CompanyId = @CompanyId AND l.GcRecord = 0");
        var status = request.Status ?? (int)LeadStatus.Open;
        if (status != 0)
        {
            where.Append(" AND l.Status = @Status");
            parameters.Add("Status", status);
        }

        Append(where, parameters, "l.LeadStageId", "LeadStageId", request.LeadStageId);
        Append(where, parameters, "l.OwnerUserId", "OwnerUserId", request.OwnerUserId);
        Append(where, parameters, "l.SalesTeamId", "SalesTeamId", request.SalesTeamId);
        Append(where, parameters, "l.LeadSourceId", "LeadSourceId", request.LeadSourceId);
        Append(where, parameters, "l.RelationshipTypeId", "RelationshipTypeId", request.RelationshipTypeId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append($" AND (l.Code ILIKE @Search OR l.Topic ILIKE @Search OR {LeadQuerySql.PersonName} ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.FromDate is { } from)
        {
            where.Append(" AND l.Created >= @FromDate");
            parameters.Add("FromDate", from.ToDateTime(TimeOnly.MinValue));
        }

        if (request.ToDate is { } to)
        {
            where.Append(" AND l.Created < @ToDate");
            parameters.Add("ToDate", to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        }

        where.Append(' ').Append(access.SqlFilter("l", parameters));

        var sql = $"""
            {LeadQuerySql.ListColumns}
            {LeadQuerySql.ListFrom}
            {where}
            ORDER BY {LeadQuerySql.InboxOrder}
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM customer.leads l
            INNER JOIN admin.persons p ON p.Id = l.PersonId
            {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<LeadListItemDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<LeadListItemDto>>
        {
            IsSuccess = true,
            Message = "Leads retrieved successfully.",
            Data = new PagedResult<LeadListItemDto> { Items = items, PageNumber = pageNumber, PageSize = pageSize, TotalCount = total }
        };
    }

    private static void Append(StringBuilder where, DynamicParameters parameters, string column, string name, Guid? value)
    {
        if (value is { } id && id != Guid.Empty)
        {
            where.Append($" AND {column} = @{name}");
            parameters.Add(name, id);
        }
    }
}

/// <summary>Handles <see cref="GetLeadBoardQuery"/>.</summary>
public sealed class GetLeadBoardQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService, LeadAccessGuard guard)
    : IRequestHandler<GetLeadBoardQuery, Response<IReadOnlyList<LeadBoardColumnDto>>>
{
    public async Task<Response<IReadOnlyList<LeadBoardColumnDto>>> Handle(GetLeadBoardQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<IReadOnlyList<LeadBoardColumnDto>>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", actor.CompanyId);
        var filters = new StringBuilder();
        if (request.SalesTeamId is { } teamId)
        {
            filters.Append(" AND l.SalesTeamId = @SalesTeamId");
            parameters.Add("SalesTeamId", teamId);
        }

        if (request.OwnerUserId is { } ownerId)
        {
            filters.Append(" AND l.OwnerUserId = @OwnerUserId");
            parameters.Add("OwnerUserId", ownerId);
        }

        filters.Append(' ').Append(access.SqlFilter("l", parameters));

        var sql = $"""
            SELECT
                s.Id AS LeadStageId,
                s.Code,
                s.Name,
                s.Probability,
                s.SortOrder,
                COUNT(l.Id)::int AS Count,
                COALESCE(SUM(l.EstimatedValueBase), 0) AS ValueBase
            FROM customer.leadstages s
            LEFT JOIN customer.leads l ON l.LeadStageId = s.Id AND l.CompanyId = s.CompanyId AND l.GcRecord = 0 AND l.Status = 1 {filters}
            WHERE s.CompanyId = @CompanyId
              AND s.GcRecord = 0
              AND s.IsActive = TRUE
              AND s.Kind IN (1, 2)
            GROUP BY s.Id, s.Code, s.Name, s.Probability, s.SortOrder
            ORDER BY s.SortOrder, s.Code;

            SELECT x.*
            FROM (
                {LeadQuerySql.ListColumns},
                ROW_NUMBER() OVER (PARTITION BY l.LeadStageId ORDER BY {LeadQuerySql.InboxOrder}) AS CardRank
                {LeadQuerySql.ListFrom}
                WHERE l.CompanyId = @CompanyId AND l.GcRecord = 0 AND l.Status = 1 {filters}
            ) x
            WHERE x.CardRank <= 20
            ORDER BY x.CardRank;
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var columns = (await multi.ReadAsync<LeadBoardColumnDto>()).AsList();
        var cards = (await multi.ReadAsync<LeadListItemDto>()).AsList();
        var board = columns.Select(c => c with { Cards = cards.Where(card => card.LeadStageId == c.LeadStageId).ToList() }).ToList();

        return new Response<IReadOnlyList<LeadBoardColumnDto>> { IsSuccess = true, Message = "Lead board retrieved successfully.", Data = board };
    }
}

/// <summary>Handles <see cref="GetLeadByIdQuery"/>.</summary>
public sealed class GetLeadByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService, LeadAccessGuard guard)
    : IRequestHandler<GetLeadByIdQuery, Response<LeadDetailDto>>
{
    public async Task<Response<LeadDetailDto>> Handle(GetLeadByIdQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<LeadDetailDto>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        var sql = $"""
            {LeadQuerySql.ListColumns},
                p.PersonType,
                p.IsActive AS PersonIsActive,
                (SELECT pc.ContactValue FROM admin.personcontacts pc
                  WHERE pc.PersonId = p.Id AND pc.GcRecord = 0 AND pc.ContactType IN (1, 2)
                  ORDER BY pc.IsPrimary DESC, pc.ContactType LIMIT 1) AS PersonEmail,
                (SELECT pc.ContactValue FROM admin.personcontacts pc
                  WHERE pc.PersonId = p.Id AND pc.GcRecord = 0 AND pc.ContactType IN (3, 4, 5)
                  ORDER BY pc.IsPrimary DESC, pc.ContactType LIMIT 1) AS PersonPhone,
                l.ReferredByPersonId,
                TRIM(COALESCE(NULLIF(rp.CommercialName, ''), CONCAT(rp.FirstName, ' ', rp.LastName))) AS ReferredByPersonName,
                l.LeadReasonId,
                r.Name AS ReasonName,
                l.StatusNotes,
                l.ExchangeRate,
                l.ExchangeRateDate,
                COALESCE((SELECT bc.Code FROM finance.companycurrencies cc
                  INNER JOIN finance.currencies bc ON bc.Id = cc.CurrencyId
                  WHERE cc.CompanyId = l.CompanyId AND cc.IsBase = TRUE AND cc.GcRecord = 0 LIMIT 1), '') AS BaseCurrencyCode,
                l.InactivatedAt,
                l.ClosedAt,
                l.CustomerId,
                (SELECT cu.CustomerCode FROM customer.customers cu WHERE cu.Id = l.CustomerId) AS CustomerCode,
                l.WonValue,
                l.WonExchangeRate,
                l.WonValueBase,
                l.CompetitorName,
                l.ProjectId,
                l.AreaId,
                (SELECT COUNT(*)::int FROM customer.leadinteractions i WHERE i.LeadId = l.Id AND i.GcRecord = 0) AS InteractionsCount,
                (SELECT COUNT(*)::int FROM customer.leadcomments c WHERE c.LeadId = l.Id AND c.GcRecord = 0) AS CommentsCount
            {LeadQuerySql.ListFrom}
            LEFT JOIN admin.persons rp ON rp.Id = l.ReferredByPersonId
            LEFT JOIN customer.leadreasons r ON r.Id = l.LeadReasonId
            WHERE l.Id = @Id
              AND l.CompanyId = @CompanyId
              AND l.GcRecord = 0;

            SELECT
                lp.Id, lp.LeadId, lp.ProductId, pr.Code AS ProductCode, pr.Name AS ProductName, lp.Quantity, lp.UnitPrice,
                lp.DiscountPercent, lp.LineTotal, lp.Status, lp.OfferedInInteractionId, lp.Created AS CreatedAt
            FROM customer.leadproducts lp
            INNER JOIN catalog.products pr ON pr.Id = lp.ProductId
            WHERE lp.LeadId = @Id
              AND lp.CompanyId = @CompanyId
              AND lp.GcRecord = 0
            ORDER BY lp.Created;
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new { request.Id, actor.CompanyId }, cancellationToken: cancellationToken));
        var lead = await multi.ReadSingleOrDefaultAsync<LeadDetailDto>();
        var products = (await multi.ReadAsync<LeadProductDto>()).AsList();
        if (lead is null || !access.CanSee((LeadStatus)lead.Status, lead.OwnerUserId, lead.SalesTeamId))
        {
            return LeadCommandContext.NotFound<LeadDetailDto>();
        }

        return new Response<LeadDetailDto> { IsSuccess = true, Message = "Lead retrieved successfully.", Data = lead with { Products = products } };
    }
}

/// <summary>Handles <see cref="GetLeadTimelineQuery"/>.</summary>
public sealed class GetLeadTimelineQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService, LeadAccessGuard guard)
    : IRequestHandler<GetLeadTimelineQuery, Response<IReadOnlyList<LeadTimelineItemDto>>>
{
    private const string StatusName = "CASE {0} WHEN 1 THEN 'Open' WHEN 2 THEN 'Inactive' WHEN 3 THEN 'Won' WHEN 4 THEN 'Lost' ELSE '' END";

    public async Task<Response<IReadOnlyList<LeadTimelineItemDto>>> Handle(GetLeadTimelineQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<IReadOnlyList<LeadTimelineItemDto>>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        if (!await LeadQuerySql.IsVisibleAsync(connection, request.Id, actor.CompanyId, access, cancellationToken))
        {
            return LeadCommandContext.NotFound<IReadOnlyList<LeadTimelineItemDto>>();
        }

        var fromStatus = string.Format(StatusName, "h.FromStatus");
        var toStatus = string.Format(StatusName, "h.ToStatus");
        var sql = $"""
            SELECT x.*
            FROM (
                SELECT 'Interaction' AS Kind, i.Id, i.OccurredAt AS At, i.UserId, CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
                       ctc.Name AS Title, i.Summary AS Detail, i.ContactTypeCompanyId AS RefId
                FROM customer.leadinteractions i
                INNER JOIN customer.contacttypecompanies ctc ON ctc.Id = i.ContactTypeCompanyId
                INNER JOIN security.users u ON u.Id = i.UserId
                WHERE i.LeadId = @LeadId AND i.GcRecord = 0
                UNION ALL
                SELECT 'Comment', c.Id, c.Created, c.UserId, CONCAT(u.FirstName, ' ', u.LastName), 'Comment', c.Body, c.LeadInteractionId
                FROM customer.leadcomments c
                INNER JOIN security.users u ON u.Id = c.UserId
                WHERE c.LeadId = @LeadId AND c.GcRecord = 0
                UNION ALL
                SELECT 'Event', e.Id, e.StartUtc, e.OwnerUserId, CONCAT(u.FirstName, ' ', u.LastName), e.Title, st.Code, e.CalendarActivityCompanyId
                FROM calendar.events e
                INNER JOIN calendar.statuscompanies sc ON sc.Id = e.CalendarStatusCompanyId
                INNER JOIN calendar.statuses st ON st.Id = sc.CalendarStatusId
                INNER JOIN security.users u ON u.Id = e.OwnerUserId
                WHERE e.LeadId = @LeadId AND e.CompanyId = @CompanyId AND e.GcRecord = 0
                UNION ALL
                SELECT 'Document', d.Id, d.Created, d.UploadedByUserId, CONCAT(u.FirstName, ' ', u.LastName), d.OriginalName, d.ContentType, d.LeadInteractionId
                FROM customer.leaddocuments d
                INNER JOIN security.users u ON u.Id = d.UploadedByUserId
                WHERE d.LeadId = @LeadId AND d.GcRecord = 0
                UNION ALL
                SELECT 'Stage', h.Id, h.ChangedAt, h.UserId, CONCAT(u.FirstName, ' ', u.LastName),
                       CONCAT(COALESCE(fs.Name, ''), ' → ', ts.Name), NULL, h.ToStageId
                FROM customer.leadstagehistory h
                LEFT JOIN customer.leadstages fs ON fs.Id = h.FromStageId
                INNER JOIN customer.leadstages ts ON ts.Id = h.ToStageId
                INNER JOIN security.users u ON u.Id = h.UserId
                WHERE h.LeadId = @LeadId AND h.GcRecord = 0
                UNION ALL
                SELECT 'Ownership', h.Id, h.At, h.UserId, CONCAT(u.FirstName, ' ', u.LastName),
                       CONCAT(COALESCE(CONCAT(fu.FirstName, ' ', fu.LastName), ''), ' → ', CONCAT(tu.FirstName, ' ', tu.LastName)), h.Notes, h.ToUserId
                FROM customer.leadownershiphistory h
                LEFT JOIN security.users fu ON fu.Id = h.FromUserId
                INNER JOIN security.users tu ON tu.Id = h.ToUserId
                INNER JOIN security.users u ON u.Id = h.UserId
                WHERE h.LeadId = @LeadId AND h.GcRecord = 0
                UNION ALL
                SELECT 'Status', h.Id, h.At, h.UserId, CONCAT(u.FirstName, ' ', u.LastName),
                       CONCAT({fromStatus}, ' → ', {toStatus}), CONCAT(COALESCE(r.Name, ''), CASE WHEN r.Name IS NOT NULL AND h.Notes IS NOT NULL THEN ': ' ELSE '' END, COALESCE(h.Notes, '')), h.LeadReasonId
                FROM customer.leadstatushistory h
                LEFT JOIN customer.leadreasons r ON r.Id = h.LeadReasonId
                INNER JOIN security.users u ON u.Id = h.UserId
                WHERE h.LeadId = @LeadId AND h.GcRecord = 0
            ) x
            ORDER BY x.At DESC, x.Kind
            LIMIT @Limit;
            """;

        var items = (await connection.QueryAsync<LeadTimelineItemDto>(new CommandDefinition(
                sql, new { LeadId = request.Id, actor.CompanyId, Limit = Math.Clamp(request.Limit ?? 200, 1, 500) }, cancellationToken: cancellationToken)))
            .ToList();
        return new Response<IReadOnlyList<LeadTimelineItemDto>> { IsSuccess = true, Message = "Lead timeline retrieved successfully.", Data = items };
    }
}

/// <summary>Handles <see cref="GetLeadInteractionsQuery"/>.</summary>
public sealed class GetLeadInteractionsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService, LeadAccessGuard guard)
    : IRequestHandler<GetLeadInteractionsQuery, Response<IReadOnlyList<LeadInteractionDto>>>
{
    public async Task<Response<IReadOnlyList<LeadInteractionDto>>> Handle(GetLeadInteractionsQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<IReadOnlyList<LeadInteractionDto>>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        if (!await LeadQuerySql.IsVisibleAsync(connection, request.Id, actor.CompanyId, access, cancellationToken))
        {
            return LeadCommandContext.NotFound<IReadOnlyList<LeadInteractionDto>>();
        }

        const string sql = """
            SELECT
                i.Id, i.LeadId, i.ContactTypeCompanyId, ct.Code AS ContactTypeCode, ctc.Name AS ContactTypeName, ct.Icon,
                i.Direction, i.OccurredAt, i.DurationMinutes, i.UserId, CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
                i.Outcome, i.Summary, i.NextActionEventId, i.Created AS CreatedAt
            FROM customer.leadinteractions i
            INNER JOIN customer.contacttypecompanies ctc ON ctc.Id = i.ContactTypeCompanyId
            INNER JOIN customer.contacttypes ct ON ct.Id = ctc.ContactTypeId
            INNER JOIN security.users u ON u.Id = i.UserId
            WHERE i.LeadId = @LeadId
              AND i.GcRecord = 0
            ORDER BY i.OccurredAt DESC;
            """;

        var items = (await connection.QueryAsync<LeadInteractionDto>(new CommandDefinition(sql, new { LeadId = request.Id }, cancellationToken: cancellationToken))).ToList();
        return new Response<IReadOnlyList<LeadInteractionDto>> { IsSuccess = true, Message = "Lead contacts retrieved successfully.", Data = items };
    }
}

/// <summary>Handles <see cref="GetLeadDuplicatesQuery"/>.</summary>
public sealed class GetLeadDuplicatesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadDuplicatesQuery, Response<IReadOnlyList<LeadDuplicateDto>>>
{
    public async Task<Response<IReadOnlyList<LeadDuplicateDto>>> Handle(GetLeadDuplicatesQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<IReadOnlyList<LeadDuplicateDto>>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        const string sql = """
            SELECT l.Id, l.Code, l.Topic, s.Name AS StageName, l.OwnerUserId, CONCAT(u.FirstName, ' ', u.LastName) AS OwnerName, l.Created AS CreatedAt
            FROM customer.leads l
            INNER JOIN customer.leadstages s ON s.Id = l.LeadStageId
            INNER JOIN security.users u ON u.Id = l.OwnerUserId
            WHERE l.CompanyId = @CompanyId
              AND l.PersonId = @PersonId
              AND l.Status = 1
              AND l.GcRecord = 0
            ORDER BY l.Created DESC;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<LeadDuplicateDto>(new CommandDefinition(
                sql, new { actor.CompanyId, request.PersonId }, cancellationToken: cancellationToken)))
            .ToList();
        return new Response<IReadOnlyList<LeadDuplicateDto>> { IsSuccess = true, Message = "Open leads of the person retrieved successfully.", Data = items };
    }
}

/// <summary>Handles <see cref="GetLeadCatalogsQuery"/>.</summary>
public sealed class GetLeadCatalogsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadCatalogsQuery, Response<LeadCatalogsDto>>
{
    private const string Columns = "t.Id, t.GcRecord, t.CompanyId, t.Code, t.Name, t.SortOrder, t.IsActive, t.Created AS CreatedAt";
    private const string Live = "WHERE t.CompanyId = @CompanyId AND t.GcRecord = 0 AND t.IsActive = TRUE ORDER BY t.SortOrder, t.Name";

    public async Task<Response<LeadCatalogsDto>> Handle(GetLeadCatalogsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<LeadCatalogsDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var sql = $"""
            SELECT {Columns}, t.Probability, t.Kind FROM customer.leadstages t {Live};
            SELECT {Columns}, t.Description FROM customer.leadsources t {Live};
            SELECT {Columns}, t.UsedFor FROM customer.leadreasons t {Live};
            SELECT {Columns}, t.OnWin FROM customer.relationshiptypes t {Live};
            SELECT c.Id, c.CompanyId, c.ContactTypeId, ct.Code, c.Name, ct.Name AS MasterName, ct.Icon, ct.CommunicationChannelCode,
                   ct.SchedulesCalendarEvent, ct.AsksDuration, c.SortOrder, c.IsActive, c.CalendarActivityCompanyId
            FROM customer.contacttypecompanies c
            INNER JOIN customer.contacttypes ct ON ct.Id = c.ContactTypeId AND ct.GcRecord = 0 AND ct.IsActive = TRUE
            WHERE c.CompanyId = @CompanyId AND c.GcRecord = 0 AND c.IsActive = TRUE
            ORDER BY c.SortOrder, c.Name;
            SELECT cur.Id AS CurrencyId, cur.Code, cur.Name, cur.Symbol, cc.IsBase
            FROM finance.companycurrencies cc
            INNER JOIN finance.currencies cur ON cur.Id = cc.CurrencyId
            WHERE cc.CompanyId = @CompanyId AND cc.GcRecord = 0 AND cc.IsActive = TRUE
            ORDER BY cc.IsBase DESC, cur.Code;
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, new { currentUserService.CompanyId }, cancellationToken: cancellationToken));
        var catalogs = new LeadCatalogsDto
        {
            Stages = (await multi.ReadAsync<LeadStageDto>()).AsList(),
            Sources = (await multi.ReadAsync<LeadSourceDto>()).AsList(),
            Reasons = (await multi.ReadAsync<LeadReasonDto>()).AsList(),
            RelationshipTypes = (await multi.ReadAsync<RelationshipTypeDto>()).AsList(),
            ContactTypes = (await multi.ReadAsync<ContactTypeCompanyDto>()).AsList(),
            Currencies = (await multi.ReadAsync<LeadCurrencyOptionDto>()).AsList()
        };

        return new Response<LeadCatalogsDto> { IsSuccess = true, Message = "Lead catalogs retrieved successfully.", Data = catalogs };
    }
}

/// <summary>Handles <see cref="GetLeadEventsQuery"/>.</summary>
public sealed class GetLeadEventsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService, LeadAccessGuard guard)
    : IRequestHandler<GetLeadEventsQuery, Response<IReadOnlyList<LeadEventDto>>>
{
    public async Task<Response<IReadOnlyList<LeadEventDto>>> Handle(GetLeadEventsQuery request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<IReadOnlyList<LeadEventDto>>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        var access = await guard.ResolveAsync(actor.CompanyId, actor.UserId, cancellationToken);
        using var connection = connectionFactory.CreateConnection();
        if (!await LeadQuerySql.IsVisibleAsync(connection, request.Id, actor.CompanyId, access, cancellationToken))
        {
            return LeadCommandContext.NotFound<IReadOnlyList<LeadEventDto>>();
        }

        const string sql = """
            SELECT
                e.Id, e.Title, e.StartUtc, e.EndUtc, e.OwnerUserId, CONCAT(u.FirstName, ' ', u.LastName) AS OwnerName,
                e.CalendarActivityCompanyId, ac.Name AS ActivityName, st.Code AS StatusCode, sc.Name AS StatusName, e.Location, e.VideoCallUrl
            FROM calendar.events e
            INNER JOIN calendar.activitycompanies ac ON ac.Id = e.CalendarActivityCompanyId
            INNER JOIN calendar.statuscompanies sc ON sc.Id = e.CalendarStatusCompanyId
            INNER JOIN calendar.statuses st ON st.Id = sc.CalendarStatusId
            INNER JOIN security.users u ON u.Id = e.OwnerUserId
            WHERE e.LeadId = @LeadId
              AND e.CompanyId = @CompanyId
              AND e.GcRecord = 0
            ORDER BY e.StartUtc DESC;
            """;

        var items = (await connection.QueryAsync<LeadEventDto>(new CommandDefinition(sql, new { LeadId = request.Id, actor.CompanyId }, cancellationToken: cancellationToken))).ToList();
        return new Response<IReadOnlyList<LeadEventDto>> { IsSuccess = true, Message = "Lead events retrieved successfully.", Data = items };
    }
}
