using System.Globalization;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Customers.Leads.Queries;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Common.EmailTemplates;

/// <summary>
/// SPEC 71 — reads with Dapper the values of the closed list of variables for one recipient: the person of the
/// company (with its customer code and e-mail), the lead when one is given (a live lead of that person, visible to
/// the user per SPEC 60), the sender and the company. Shared by <c>render</c> and by the sending of SPEC 72, which
/// replaces the variables left in the text with the same context.
/// </summary>
public sealed class EmailTemplateContextReader(ISqlConnectionFactory connectionFactory, LeadAccessGuard leadAccess)
{
    // One row with every value. The e-mail prefers the primary e-mail contact, then the primary flag; the date uses
    // the time zone of the company's calendar settings (SPEC 44-post) when it has them.
    private const string PersonSql = """
        SELECT
            p.PersonType, p.FirstName, p.MiddleName, p.LastName, p.SecondLastName, p.CommercialName,
            (SELECT c.CustomerCode FROM customer.customers c
              WHERE c.CompanyId = @CompanyId AND c.PersonId = p.Id AND c.GcRecord = 0 AND c.IsActive = TRUE
              LIMIT 1) AS CustomerCode,
            (SELECT pc.ContactValue FROM admin.personcontacts pc
              WHERE pc.CompanyId = @CompanyId AND pc.PersonId = p.Id AND pc.GcRecord = 0 AND pc.IsActive = TRUE
                AND pc.ContactType IN (@PrimaryEmail, @AlternativeEmail)
              ORDER BY CASE WHEN pc.ContactType = @PrimaryEmail THEN 0 ELSE 1 END, pc.IsPrimary DESC
              LIMIT 1) AS Email,
            (SELECT co.Name FROM common.companies co WHERE co.Id = @CompanyId) AS CompanyName,
            (SELECT cc.TimeZone FROM calendar.calendarcompanies cc WHERE cc.CompanyId = @CompanyId AND cc.GcRecord = 0 LIMIT 1) AS TimeZone,
            (SELECT CONCAT(u.FirstName, ' ', u.LastName) FROM security.users u WHERE u.Id = @UserId) AS UserName,
            (SELECT u.Email FROM security.users u WHERE u.Id = @UserId) AS UserEmail
        FROM admin.persons p
        WHERE p.Id = @PersonId
          AND p.CompanyId = @CompanyId
          AND p.GcRecord = 0;
        """;

    private const string LeadSql = """
        SELECT l.Code, l.Topic
        FROM customer.leads l
        WHERE l.Id = @LeadId
          AND l.CompanyId = @CompanyId
          AND l.PersonId = @PersonId
          AND l.GcRecord = 0;
        """;

    /// <summary>
    /// The context of the variables, or <c>PERSON_NOT_FOUND</c> / <c>LEAD_NOT_FOUND</c>. Without
    /// <paramref name="emailAddress"/>, <c>{{Persona.Correo}}</c> is the primary e-mail of the person.
    /// </summary>
    public async Task<Response<EmailTemplateContext>> ReadAsync(
        Guid companyId,
        Guid userId,
        Guid personId,
        string? emailAddress,
        Guid? leadId,
        CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();
        var person = await connection.QuerySingleOrDefaultAsync<PersonRow>(new CommandDefinition(
            PersonSql,
            new
            {
                PersonId = personId,
                CompanyId = companyId,
                UserId = userId,
                PrimaryEmail = (int)ContactType.PrimaryEmail,
                AlternativeEmail = (int)ContactType.AlternativeEmail
            },
            cancellationToken: cancellationToken));
        if (person is null)
        {
            return Response<EmailTemplateContext>.Error(EmailTemplateErrorCodes.PersonNotFound, ["Person not found."]);
        }

        LeadRow? lead = null;
        if (leadId is { } id && id != Guid.Empty)
        {
            var access = await leadAccess.ResolveAsync(companyId, userId, cancellationToken);
            if (userId != Guid.Empty && await LeadQuerySql.IsVisibleAsync(connection, id, companyId, access, cancellationToken))
            {
                lead = await connection.QuerySingleOrDefaultAsync<LeadRow>(new CommandDefinition(
                    LeadSql, new { LeadId = id, CompanyId = companyId, PersonId = personId }, cancellationToken: cancellationToken));
            }

            if (lead is null)
            {
                return Response<EmailTemplateContext>.Error(EmailTemplateErrorCodes.LeadNotFound, ["Lead not found."]);
            }
        }

        var isLegal = person.PersonType == (int)PersonType.Legal;
        var fullName = string.Join(" ", new[] { person.FirstName, person.MiddleName, person.LastName, person.SecondLastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        // Rule 3: the name of a natural person, the business name (CommercialName) of a legal one; a legal person
        // without it falls back to the names it has.
        var businessName = string.IsNullOrWhiteSpace(person.CommercialName) ? null : person.CommercialName.Trim();
        var zone = CalendarTime.FindTimeZone(person.TimeZone);
        var today = zone is null ? DateTime.Now : TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone);

        return new Response<EmailTemplateContext>
        {
            IsSuccess = true,
            Data = new EmailTemplateContext(
                PersonaNombre: isLegal ? businessName ?? person.FirstName : person.FirstName,
                PersonaNombreCompleto: isLegal ? businessName ?? fullName : fullName,
                PersonaCorreo: string.IsNullOrWhiteSpace(emailAddress) ? person.Email ?? string.Empty : emailAddress.Trim(),
                ClienteCodigo: person.CustomerCode ?? string.Empty,
                LeadCodigo: lead?.Code ?? string.Empty,
                LeadTema: lead?.Topic ?? string.Empty,
                UsuarioNombre: (person.UserName ?? string.Empty).Trim(),
                UsuarioCorreo: person.UserEmail ?? string.Empty,
                EmpresaNombre: person.CompanyName ?? string.Empty,
                FechaHoy: today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))
        };
    }

    private sealed class PersonRow
    {
        public int PersonType { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? SecondLastName { get; set; }
        public string? CommercialName { get; set; }
        public string? CustomerCode { get; set; }
        public string? Email { get; set; }
        public string? CompanyName { get; set; }
        public string? TimeZone { get; set; }
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
    }

    private sealed class LeadRow
    {
        public string Code { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
    }
}
