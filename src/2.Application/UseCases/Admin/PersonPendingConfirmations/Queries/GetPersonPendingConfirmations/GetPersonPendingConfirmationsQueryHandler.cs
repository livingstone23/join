using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmations;

/// <summary>
/// Dapper-backed paged query. Filters by tenant, status (default <c>Pending</c>), channel, requested
/// range, and a search against the person's name or any of their active contacts. All raw SQL is
/// written for the PostgreSQL fork: no vendor functions, comparisons use <c>ILIKE</c>.
/// </summary>
public sealed class GetPersonPendingConfirmationsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonPendingConfirmationsQuery, Response<PagedResult<PersonPendingConfirmationDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new PaginationSettings();

    public async Task<Response<PagedResult<PersonPendingConfirmationDto>>> Handle(
        GetPersonPendingConfirmationsQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PagedResult<PersonPendingConfirmationDto>>.Error(
                CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var status = (PersonPendingConfirmationStatus)(request.Status ?? (int)PersonPendingConfirmationStatus.Pending);
        if (!Enum.IsDefined(typeof(PersonPendingConfirmationStatus), status))
        {
            return Response<PagedResult<PersonPendingConfirmationDto>>.Error(
                "INVALID_PENDING_STATUS",
                ["The status filter is not a valid PersonPendingConfirmationStatus value."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.Page, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", companyId);
        parameters.Add("Status", (int)status);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder(
            "WHERE ppc.\"companyid\" = @TenantId AND ppc.\"gcrecord\" = 0 AND ppc.\"status\" = @Status " +
            "AND p.\"gcrecord\" = 0 AND p.\"companyid\" = @TenantId " +
            "AND cc.\"gcrecord\" = 0");

        if (request.ChannelId.HasValue)
        {
            where.Append(" AND ppc.\"communicationchannelid\" = @ChannelId");
            parameters.Add("ChannelId", request.ChannelId.Value);
        }

        if (request.From.HasValue)
        {
            where.Append(" AND ppc.\"requestedutc\" >= @From");
            parameters.Add("From", request.From.Value);
        }

        if (request.To.HasValue)
        {
            where.Append(" AND ppc.\"requestedutc\" <= @To");
            parameters.Add("To", request.To.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (");
            where.Append("CONCAT(p.\"firstname\", ' ', COALESCE(p.\"lastname\", '')) ILIKE @Search ");
            where.Append("OR p.\"identificationnumber\" ILIKE @Search ");
            where.Append("OR EXISTS (SELECT 1 FROM admin.personcontacts pc ");
            where.Append("            WHERE pc.\"personid\" = p.\"id\" AND pc.\"companyid\" = @TenantId ");
            where.Append("              AND pc.\"gcrecord\" = 0 AND pc.\"isactive\" = TRUE ");
            where.Append("              AND pc.\"contactvalue\" ILIKE @Search)");
            where.Append(")");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        var sql = $"""
            SELECT
                ppc."id" AS Id,
                ppc."personid" AS PersonId,
                CONCAT(p."firstname", ' ', COALESCE(p."lastname", '')) AS PersonFullName,
                p."identificationnumber" AS Identification,
                ppc."communicationchannelid" AS ChannelId,
                cc."name" AS ChannelName,
                ppc."channelidentifier" AS ChannelIdentifier,
                ppc."reason" AS Reason,
                ppc."requestedutc" AS RequestedUtc,
                ppc."status" AS Status,
                ppc."revieweventid" AS ReviewEventId,
                (SELECT cs."code" FROM calendar.calendarstatuses cs WHERE cs."id" = csc."calendarstatusid" AND cs."gcrecord" = 0) AS ReviewEventStatus,
                ppc."customerid" AS CustomerId,
                ppc."resolvedbyuserid" AS ResolvedByUserId,
                ppc."resolvedutc" AS ResolvedUtc,
                ppc."resolutionnotes" AS ResolutionNotes
            FROM admin.personpendingconfirmations ppc
            INNER JOIN admin.persons p ON p."id" = ppc."personid"
            INNER JOIN common.communicationchannels cc ON cc."id" = ppc."communicationchannelid"
            LEFT JOIN calendar.calendarevents ce ON ce."id" = ppc."revieweventid" AND ce."gcrecord" = 0
            LEFT JOIN calendar.calendarstatuscompanies csc ON csc."id" = ce."calendarstatuscompanyid"
            {where}
            ORDER BY ppc."requestedutc" DESC
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
            FROM admin.personpendingconfirmations ppc
            INNER JOIN admin.persons p ON p."id" = ppc."personid"
            INNER JOIN common.communicationchannels cc ON cc."id" = ppc."communicationchannelid"
            {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<PersonPendingConfirmationDto>()).ToList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(
            items, pageNumber, pageSize, total,
            "Person pending confirmations retrieved successfully.");
    }
}
