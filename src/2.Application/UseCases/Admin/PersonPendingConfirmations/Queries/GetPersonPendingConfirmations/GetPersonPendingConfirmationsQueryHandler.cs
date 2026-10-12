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
/// Handles <see cref="GetPersonPendingConfirmationsQuery"/> with Dapper: tenant and soft-delete filters, status
/// (default <c>Pending</c>), channel, requested range and text search; <c>ReviewEventStatus</c> is the global status
/// code of the review activity.
/// </summary>
public sealed class GetPersonPendingConfirmationsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonPendingConfirmationsQuery, Response<PagedResult<PersonPendingConfirmationDto>>>
{
    /// <summary> Columns and joins shared by the inbox and the detail. </summary>
    internal const string SelectSql = """
        SELECT ppc.id AS Id,
               ppc.personid AS PersonId,
               TRIM(CONCAT(p.firstname, ' ', COALESCE(p.lastname, ''))) AS PersonFullName,
               p.identificationnumber AS Identification,
               ppc.communicationchannelid AS ChannelId,
               cc.name AS ChannelName,
               ppc.channelidentifier AS ChannelIdentifier,
               ppc.reason AS Reason,
               ppc.requestedutc AS RequestedUtc,
               ppc.status AS Status,
               ppc.revieweventid AS ReviewEventId,
               s.code AS ReviewEventStatus,
               ppc.customerid AS CustomerId,
               ppc.resolvedbyuserid AS ResolvedByUserId,
               ppc.resolvedutc AS ResolvedUtc,
               ppc.resolutionnotes AS ResolutionNotes
          FROM admin.personpendingconfirmations ppc
          INNER JOIN admin.persons p ON p.id = ppc.personid
          INNER JOIN common.communicationchannels cc ON cc.id = ppc.communicationchannelid
          LEFT JOIN calendar.events e ON e.id = ppc.revieweventid AND e.gcrecord = 0
          LEFT JOIN calendar.statuscompanies sc ON sc.id = e.calendarstatuscompanyid
          LEFT JOIN calendar.statuses s ON s.id = sc.calendarstatusid
        """;

    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new PaginationSettings();

    public async Task<Response<PagedResult<PersonPendingConfirmationDto>>> Handle(GetPersonPendingConfirmationsQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PagedResult<PersonPendingConfirmationDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var status = request.Status ?? (int)PersonPendingConfirmationStatus.Pending;
        if (!Enum.IsDefined(typeof(PersonPendingConfirmationStatus), status))
        {
            return Response<PagedResult<PersonPendingConfirmationDto>>.Error(CalendarErrorCodes.PersonPendingInvalidStatus,
                ["status must be Pending (1), Confirmed (2), LinkedToCustomer (3) or Discarded (4)."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.Page, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("TenantId", companyId);
        parameters.Add("Status", status);
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("""
             WHERE ppc.companyid = @TenantId AND ppc.gcrecord = 0 AND ppc.status = @Status
               AND p.companyid = @TenantId AND p.gcrecord = 0
            """);

        if (request.ChannelId is { } channelId)
        {
            where.Append(" AND ppc.communicationchannelid = @ChannelId");
            parameters.Add("ChannelId", channelId);
        }

        if (request.From is { } from)
        {
            where.Append(" AND ppc.requestedutc >= @From");
            parameters.Add("From", DateTime.SpecifyKind(from.ToUniversalTime(), DateTimeKind.Utc));
        }

        if (request.To is { } to)
        {
            where.Append(" AND ppc.requestedutc <= @To");
            parameters.Add("To", DateTime.SpecifyKind(to.ToUniversalTime(), DateTimeKind.Utc));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append("""
                 AND (CONCAT(p.firstname, ' ', COALESCE(p.lastname, '')) ILIKE @Search
                      OR p.identificationnumber ILIKE @Search
                      OR EXISTS (SELECT 1 FROM admin.personcontacts pc
                                  WHERE pc.personid = p.id AND pc.companyid = @TenantId
                                    AND pc.gcrecord = 0 AND pc.isactive = TRUE
                                    AND pc.contactvalue ILIKE @Search))
                """);
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        var sql = $"""
            {SelectSql}
            {where}
             ORDER BY ppc.requestedutc DESC
             LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*)
              FROM admin.personpendingconfirmations ppc
              INNER JOIN admin.persons p ON p.id = ppc.personid
            {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<PendingRow>()).Select(r => r.ToDto()).ToList();
        var total = await multi.ReadSingleAsync<int>();

        return CalendarPaging.Page(items, pageNumber, pageSize, total, "Person pending confirmations retrieved successfully.");
    }
}

/// <summary> Row of <see cref="GetPersonPendingConfirmationsQueryHandler.SelectSql"/>. </summary>
internal sealed class PendingRow
{
    public Guid Id { get; init; }
    public Guid PersonId { get; init; }
    public string PersonFullName { get; init; } = string.Empty;
    public string? Identification { get; init; }
    public Guid ChannelId { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string ChannelIdentifier { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public DateTime RequestedUtc { get; init; }
    public int Status { get; init; }
    public Guid? ReviewEventId { get; init; }
    public string? ReviewEventStatus { get; init; }
    public Guid? CustomerId { get; init; }
    public Guid? ResolvedByUserId { get; init; }
    public DateTime? ResolvedUtc { get; init; }
    public string? ResolutionNotes { get; init; }

    public PersonPendingConfirmationDto ToDto() => new(
        Id, PersonId, PersonFullName, Identification, ChannelId, ChannelName, ChannelIdentifier, Reason,
        DateTime.SpecifyKind(RequestedUtc, DateTimeKind.Utc), Status, ReviewEventId, ReviewEventStatus, CustomerId, ResolvedByUserId,
        ResolvedUtc is null ? null : DateTime.SpecifyKind(ResolvedUtc.Value, DateTimeKind.Utc), ResolutionNotes);
}
