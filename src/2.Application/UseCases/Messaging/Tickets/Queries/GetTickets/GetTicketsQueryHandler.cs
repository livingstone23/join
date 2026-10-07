using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Domain.Messaging;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Messaging.Tickets.Queries;

/// <summary>
/// Handles paginated ticket queries using Dapper for high-performance reads.
/// SLA / inactivity are computed in C# via <see cref="JOIN.Domain.Messaging.TicketSlaCalculator"/>
/// to keep the math portable across DB providers (SPEC 37 F6).
/// </summary>
public sealed class GetTicketsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetTicketsQuery, Response<PagedResult<TicketListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <summary>
    /// Retrieves a tenant-scoped paginated ticket list with optional filters.
    /// </summary>
    public async Task<Response<PagedResult<TicketListItemDto>>> Handle(GetTicketsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<TicketListItemDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (sanitizedPageNumber, sanitizedPageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("TenantId", currentUserService.CompanyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE t.CompanyId = @TenantId AND t.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            whereBuilder.Append(" AND (t.Code LIKE @Search OR t.Name LIKE @Search OR t.Description LIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.TicketStatusId.HasValue && request.TicketStatusId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.TicketStatusId = @TicketStatusId");
            parameters.Add("TicketStatusId", request.TicketStatusId.Value);
        }

        if (request.TicketComplexityId.HasValue && request.TicketComplexityId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.TicketComplexityId = @TicketComplexityId");
            parameters.Add("TicketComplexityId", request.TicketComplexityId.Value);
        }

        if (request.AssignedToUserId.HasValue && request.AssignedToUserId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.AssignedToUserId = @AssignedToUserId");
            parameters.Add("AssignedToUserId", request.AssignedToUserId.Value);
        }

        if (request.PersonId.HasValue && request.PersonId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.PersonId = @PersonId");
            parameters.Add("PersonId", request.PersonId.Value);
        }

        if (request.ProjectId.HasValue && request.ProjectId.Value != Guid.Empty)
        {
            whereBuilder.Append(" AND t.ProjectId = @ProjectId");
            parameters.Add("ProjectId", request.ProjectId.Value);
        }

        if (request.IsVisibleToExternals.HasValue)
        {
            whereBuilder.Append(" AND t.IsVisibleToExternals = @IsVisibleToExternals");
            parameters.Add("IsVisibleToExternals", request.IsVisibleToExternals.Value);
        }

        if (request.FromDate.HasValue)
        {
            whereBuilder.Append(" AND t.Created >= @FromDate");
            parameters.Add("FromDate", request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            whereBuilder.Append(" AND t.Created <= @ToDate");
            parameters.Add("ToDate", request.ToDate.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                t.Id,
                t.CompanyId,
                co.Name AS CompanyName,
                t.Code,
                t.Name,
                t.TicketStatusId,
                ts.Name AS TicketStatusName,
                t.TicketComplexityId,
                tc.Name AS TicketComplexityName,
                t.PersonId,
                CASE
                    WHEN c.Id IS NULL THEN NULL
                    WHEN c.CommercialName IS NOT NULL AND c.CommercialName <> '' THEN c.CommercialName
                    ELSE CONCAT(c.FirstName, ' ', COALESCE(c.MiddleName, ''), ' ', COALESCE(c.LastName, ''), ' ', COALESCE(c.SecondLastName, ''))
                END AS PersonName,
                t.AssignedToUserId,
                CASE
                    WHEN au.Id IS NULL THEN NULL
                    ELSE CONCAT(au.FirstName, ' ', au.LastName)
                END AS AssignedToUserName,
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
            INNER JOIN Messaging.TimeUnits tcu ON tc.TimeUnitId = tcu.Id
            LEFT JOIN Messaging.TicketCompanyDefaults tcd ON tcd.CompanyId = t.CompanyId AND tcd.GcRecord = 0
            LEFT JOIN Admin.Persons c ON t.PersonId = c.Id
            LEFT JOIN Security.Users au ON t.AssignedToUserId = au.Id
            {whereClause}
            ORDER BY t.Created DESC, t.Code DESC
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM Messaging.Tickets t
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var rows = (await multi.ReadAsync<TicketSlaRow>()).AsList();

        var nowUtc = DateTime.UtcNow;
        var items = new List<TicketListItemDto>(rows.Count);
        foreach (var row in rows)
        {
            var sla = TicketSlaCalculator.Compute(
                row.CreatedAt,
                row.ResolutionTimeUnits,
                row.ComplexityTimeUnitCode,
                row.LastActivityAt ?? row.CreatedAt,
                row.FinishedAt,
                row.IsFinalStatus,
                row.MaxDayTicketInactivity,
                nowUtc);

            items.Add(new TicketListItemDto
            {
                Id = row.Id,
                CompanyId = row.CompanyId,
                CompanyName = row.CompanyName,
                Code = row.Code,
                Name = row.Name,
                TicketStatusId = row.TicketStatusId,
                TicketStatusName = row.TicketStatusName,
                TicketComplexityId = row.TicketComplexityId,
                TicketComplexityName = row.TicketComplexityName,
                PersonId = row.PersonId,
                PersonName = row.PersonName,
                AssignedToUserId = row.AssignedToUserId,
                AssignedToUserName = row.AssignedToUserName,
                CreatedAt = row.CreatedAt,
                SlaDueAt = sla.SlaDueAt,
                IsSlaBreached = sla.IsSlaBreached,
                LastActivityAt = sla.LastActivityAt,
                IsInactive = sla.IsInactive
            });
        }

        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<TicketListItemDto>>
        {
            IsSuccess = true,
            Message = "Tickets retrieved successfully.",
            Data = new PagedResult<TicketListItemDto>
            {
                Items = items,
                PageNumber = sanitizedPageNumber,
                PageSize = sanitizedPageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)sanitizedPageSize)
            }
        };
    }

    /// <summary>
    /// Flat row that combines every <see cref="TicketListItemDto"/> column with the
    /// raw SLA inputs that have no destination on the DTO and feed
    /// <see cref="TicketSlaCalculator.Compute"/> instead. Dapper materializes a single
    /// object per row, so the multi-mapping overload is unnecessary here.
    /// </summary>
    private sealed class TicketSlaRow
    {
        public Guid Id { get; init; }
        public Guid CompanyId { get; init; }
        public string? CompanyName { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public Guid TicketStatusId { get; init; }
        public string TicketStatusName { get; init; } = string.Empty;
        public Guid TicketComplexityId { get; init; }
        public string TicketComplexityName { get; init; } = string.Empty;
        public Guid? PersonId { get; init; }
        public string? PersonName { get; init; }
        public Guid? AssignedToUserId { get; init; }
        public string? AssignedToUserName { get; init; }
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
