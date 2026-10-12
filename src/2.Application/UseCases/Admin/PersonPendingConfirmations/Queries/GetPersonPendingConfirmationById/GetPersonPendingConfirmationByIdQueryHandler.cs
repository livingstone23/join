using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmationById;

/// <summary>
/// Loads the header, the person's active contacts and the events created with the person as
/// <c>RequesterPersonId</c>. Written for the PostgreSQL fork; the calendar status code is read
/// from <c>calendar.calendarstatuses</c> through the per-company mapping.
/// </summary>
public sealed class GetPersonPendingConfirmationByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonPendingConfirmationByIdQuery, Response<PersonPendingConfirmationDetailDto>>
{
    public async Task<Response<PersonPendingConfirmationDetailDto>> Handle(
        GetPersonPendingConfirmationByIdQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PersonPendingConfirmationDetailDto>.Error(
                CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        var headerSql = """
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
            WHERE ppc."id" = @Id
              AND ppc."companyid" = @TenantId
              AND ppc."gcrecord" = 0;
            """;

        var header = await connection.QuerySingleOrDefaultAsync<PersonPendingConfirmationDto>(
            new CommandDefinition(headerSql, new { Id = request.PendingConfirmationId, TenantId = companyId }, cancellationToken: cancellationToken));

        if (header is null)
        {
            return Response<PersonPendingConfirmationDetailDto>.Error(
                CalendarErrorCodes.PersonPendingNotFound,
                ["The pending confirmation was not found in the current company."]);
        }

        var contactsSql = """
            SELECT
                pc."id" AS Id,
                pc."contacttype" AS ContactType,
                pc."contacttype" AS ContactName,
                pc."contactvalue" AS ContactValue,
                pc."isprimary" AS IsPrimary,
                pc."comments" AS Comments,
                to_char(pc."created" AT TIME ZONE 'UTC', 'YYYY-MM-DD HH24:MI') AS CreatedAt
            FROM admin.personcontacts pc
            INNER JOIN admin.persons p ON p."id" = pc."personid"
            WHERE p."id" = @PersonId
              AND p."companyid" = @TenantId
              AND p."gcrecord" = 0
              AND pc."companyid" = @TenantId
              AND pc."gcrecord" = 0
              AND pc."isactive" = TRUE
            ORDER BY pc."isprimary" DESC, pc."created" DESC;
            """;

        var contacts = (await connection.QueryAsync<PersonContactDto>(
            new CommandDefinition(contactsSql, new { PersonId = header.PersonId, TenantId = companyId }, cancellationToken: cancellationToken))).ToList();

        var eventsSql = """
            SELECT
                ce."id" AS EventId,
                ce."startutc" AS OccurrenceStartUtc,
                ce."title" AS Title,
                ce."startutc" AS StartUtc,
                ce."endutc" AS EndUtc,
                csc."id" AS Id,
                cs."code" AS Code,
                csc."name" AS Name,
                csc."color" AS Color,
                CONCAT(au."firstname", ' ', COALESCE(au."lastname", '')) AS OwnerFullName
            FROM calendar.calendarevents ce
            INNER JOIN security.users au ON au."id" = ce."owneruserid"
            INNER JOIN calendar.calendarstatuscompanies csc ON csc."id" = ce."calendarstatuscompanyid"
            INNER JOIN calendar.calendarstatuses cs ON cs."id" = csc."calendarstatusid"
            WHERE ce."companyid" = @TenantId
              AND ce."gcrecord" = 0
              AND ce."requesterpersonid" = @PersonId
              AND cs."code" <> 'CANCELLED'
            ORDER BY ce."startutc" ASC;
            """;

        var events = (await connection.QueryAsync<IntakeEventDto>(
            new CommandDefinition(eventsSql, new { PersonId = header.PersonId, TenantId = companyId }, cancellationToken: cancellationToken))).ToList();

        return new Response<PersonPendingConfirmationDetailDto>
        {
            IsSuccess = true,
            Message = "Pending confirmation detail retrieved successfully.",
            Data = new PersonPendingConfirmationDetailDto(header, contacts, events)
        };
    }
}
