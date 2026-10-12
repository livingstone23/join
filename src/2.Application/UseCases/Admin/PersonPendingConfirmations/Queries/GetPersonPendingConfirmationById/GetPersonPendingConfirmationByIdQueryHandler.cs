using System.Globalization;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmations;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmationById;

/// <summary>
/// Handles <see cref="GetPersonPendingConfirmationByIdQuery"/> with Dapper: the header row, the active contacts of the
/// person and its non-cancelled activities (series rows as stored, without expanding occurrences).
/// </summary>
public sealed class GetPersonPendingConfirmationByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonPendingConfirmationByIdQuery, Response<PersonPendingConfirmationDetailDto>>
{
    private const string HeaderSql = GetPersonPendingConfirmationsQueryHandler.SelectSql + """

         WHERE ppc.id = @Id AND ppc.companyid = @TenantId AND ppc.gcrecord = 0;
        """;

    private const string ContactsSql = """
        SELECT pc.id AS Id, pc.contacttype AS ContactType, pc.contactvalue AS ContactValue,
               pc.isprimary AS IsPrimary, pc.comments AS Comments, pc.created AS Created
          FROM admin.personcontacts pc
         WHERE pc.personid = @PersonId AND pc.companyid = @TenantId
           AND pc.gcrecord = 0 AND pc.isactive = TRUE
         ORDER BY pc.isprimary DESC, pc.created DESC;
        """;

    private const string EventsSql = """
        SELECT e.id AS EventId, e.startutc AS StartUtc, e.endutc AS EndUtc, e.title AS Title,
               sc.id AS StatusId, s.code AS StatusCode, sc.name AS StatusName, sc.color AS StatusColor,
               TRIM(CONCAT(u.firstname, ' ', COALESCE(u.lastname, ''))) AS OwnerFullName
          FROM calendar.events e
          INNER JOIN calendar.statuscompanies sc ON sc.id = e.calendarstatuscompanyid
          INNER JOIN calendar.statuses s ON s.id = sc.calendarstatusid
          INNER JOIN security.users u ON u.id = e.owneruserid
         WHERE e.companyid = @TenantId AND e.gcrecord = 0
           AND e.requesterpersonid = @PersonId
           AND s.code <> @Cancelled
         ORDER BY e.startutc;
        """;

    public async Task<Response<PersonPendingConfirmationDetailDto>> Handle(GetPersonPendingConfirmationByIdQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PersonPendingConfirmationDetailDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var header = (await connection.QueryFirstOrDefaultAsync<PendingRow>(
            new CommandDefinition(HeaderSql, new { Id = request.PendingConfirmationId, TenantId = companyId }, cancellationToken: cancellationToken)))?.ToDto();
        if (header is null)
        {
            return Response<PersonPendingConfirmationDetailDto>.Error(CalendarErrorCodes.PersonPendingNotFound, ["The pending confirmation was not found in the current company."]);
        }

        var contacts = (await connection.QueryAsync<ContactRow>(
                new CommandDefinition(ContactsSql, new { header.PersonId, TenantId = companyId }, cancellationToken: cancellationToken)))
            .Select(c => new PersonContactDto
            {
                Id = c.Id,
                ContactType = c.ContactType.ToString(CultureInfo.InvariantCulture),
                ContactName = Enum.IsDefined(typeof(ContactType), c.ContactType) ? ((ContactType)c.ContactType).GetDisplayName() : string.Empty,
                ContactValue = c.ContactValue,
                IsPrimary = c.IsPrimary,
                Comments = c.Comments,
                CreatedAt = c.Created?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty
            })
            .ToList();

        var events = (await connection.QueryAsync<EventRow>(
                new CommandDefinition(EventsSql, new { header.PersonId, TenantId = companyId, Cancelled = CalendarCodes.Statuses.Cancelled }, cancellationToken: cancellationToken)))
            .Select(e => new IntakeEventDto(
                e.EventId,
                CalendarTime.AsUtc(e.StartUtc),
                e.Title,
                CalendarTime.AsUtc(e.StartUtc),
                CalendarTime.AsUtc(e.EndUtc),
                new IntakeStatusDto(e.StatusId, e.StatusCode, e.StatusName, e.StatusColor),
                e.OwnerFullName))
            .ToList();

        return new Response<PersonPendingConfirmationDetailDto>
        {
            IsSuccess = true,
            Data = new PersonPendingConfirmationDetailDto(header, contacts, events),
            Message = "Person pending confirmation retrieved successfully."
        };
    }

    private sealed class ContactRow
    {
        public Guid Id { get; init; }
        public int ContactType { get; init; }
        public string ContactValue { get; init; } = string.Empty;
        public bool IsPrimary { get; init; }
        public string? Comments { get; init; }
        public DateTime? Created { get; init; }
    }

    private sealed class EventRow
    {
        public Guid EventId { get; init; }
        public DateTime StartUtc { get; init; }
        public DateTime EndUtc { get; init; }
        public string Title { get; init; } = string.Empty;
        public Guid StatusId { get; init; }
        public string StatusCode { get; init; } = string.Empty;
        public string StatusName { get; init; } = string.Empty;
        public string StatusColor { get; init; } = string.Empty;
        public string OwnerFullName { get; init; } = string.Empty;
    }
}
