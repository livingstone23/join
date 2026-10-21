using System.Globalization;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Email;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Application.UseCases.Customers.Leads.Queries;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Queries;

/// <summary>
/// Handles <see cref="RenderEmailTemplateQuery"/> with Dapper: the active template, the person of the company
/// with its customer code, e-mail, the company and the sender, and the lead when one is given. The lead must be
/// a live lead of that person and visible to the user (SPEC 60); otherwise <c>LEAD_NOT_FOUND</c>.
/// </summary>
public sealed class RenderEmailTemplateQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IEmailTemplateRenderer renderer,
    LeadAccessGuard leadAccess)
    : IRequestHandler<RenderEmailTemplateQuery, Response<EmailTemplateRenderedDto>>
{
    private const string TemplateSql = """
        SELECT t.Id, t.Name, t.Subject, t.BodyHtml
        FROM admin.emailtemplates t
        WHERE t.Id = @TemplateId
          AND t.CompanyId = @CompanyId
          AND t.GcRecord = 0
          AND t.IsActive = TRUE;
        """;

    // One row with every value of the context. The e-mail prefers the primary e-mail contact, then the primary flag.
    private const string PersonSql = """
        SELECT
            p.FirstName, p.MiddleName, p.LastName, p.SecondLastName, p.CommercialName,
            (SELECT c.CustomerCode FROM customer.customers c
              WHERE c.CompanyId = @CompanyId AND c.PersonId = p.Id AND c.GcRecord = 0 AND c.IsActive = TRUE
              LIMIT 1) AS CustomerCode,
            (SELECT pc.ContactValue FROM admin.personcontacts pc
              WHERE pc.CompanyId = @CompanyId AND pc.PersonId = p.Id AND pc.GcRecord = 0 AND pc.IsActive = TRUE
                AND pc.ContactType IN (@PrimaryEmail, @AlternativeEmail)
              ORDER BY CASE WHEN pc.ContactType = @PrimaryEmail THEN 0 ELSE 1 END, pc.IsPrimary DESC
              LIMIT 1) AS Email,
            (SELECT co.Name FROM common.companies co WHERE co.Id = @CompanyId) AS CompanyName,
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

    public async Task<Response<EmailTemplateRenderedDto>> Handle(RenderEmailTemplateQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<EmailTemplateRenderedDto>();
        }

        var companyId = currentUserService.CompanyId;
        var userId = Guid.TryParse(currentUserService.UserId, out var parsed) ? parsed : Guid.Empty;
        using var connection = connectionFactory.CreateConnection();

        var template = await connection.QuerySingleOrDefaultAsync<TemplateRow>(new CommandDefinition(
            TemplateSql, new { request.TemplateId, CompanyId = companyId }, cancellationToken: cancellationToken));
        if (template is null)
        {
            return EmailTemplateErrors.NotFound<EmailTemplateRenderedDto>();
        }

        var person = await connection.QuerySingleOrDefaultAsync<PersonRow>(new CommandDefinition(
            PersonSql,
            new
            {
                request.PersonId,
                CompanyId = companyId,
                UserId = userId,
                PrimaryEmail = (int)ContactType.PrimaryEmail,
                AlternativeEmail = (int)ContactType.AlternativeEmail
            },
            cancellationToken: cancellationToken));
        if (person is null)
        {
            return Response<EmailTemplateRenderedDto>.Error(EmailTemplateErrorCodes.PersonNotFound, ["Person not found."]);
        }

        LeadRow? lead = null;
        if (request.LeadId is { } leadId && leadId != Guid.Empty)
        {
            var access = await leadAccess.ResolveAsync(companyId, userId, cancellationToken);
            if (userId != Guid.Empty && await LeadQuerySql.IsVisibleAsync(connection, leadId, companyId, access, cancellationToken))
            {
                lead = await connection.QuerySingleOrDefaultAsync<LeadRow>(new CommandDefinition(
                    LeadSql, new { LeadId = leadId, CompanyId = companyId, request.PersonId }, cancellationToken: cancellationToken));
            }

            if (lead is null)
            {
                return Response<EmailTemplateRenderedDto>.Error(EmailTemplateErrorCodes.LeadNotFound, ["Lead not found."]);
            }
        }

        var context = new EmailTemplateContext(
            PersonaNombre: string.IsNullOrWhiteSpace(person.CommercialName) ? person.FirstName : person.CommercialName,
            PersonaNombreCompleto: string.Join(" ", new[] { person.FirstName, person.MiddleName, person.LastName, person.SecondLastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))),
            PersonaCorreo: string.IsNullOrWhiteSpace(request.EmailAddress) ? person.Email ?? string.Empty : request.EmailAddress.Trim(),
            ClienteCodigo: person.CustomerCode ?? string.Empty,
            LeadCodigo: lead?.Code ?? string.Empty,
            LeadTema: lead?.Topic ?? string.Empty,
            UsuarioNombre: (person.UserName ?? string.Empty).Trim(),
            UsuarioCorreo: person.UserEmail ?? string.Empty,
            EmpresaNombre: person.CompanyName ?? string.Empty,
            FechaHoy: DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        var rendered = renderer.Render(template.Subject, template.BodyHtml, context);

        return new Response<EmailTemplateRenderedDto>
        {
            IsSuccess = true,
            Message = "Email template rendered successfully.",
            Data = new EmailTemplateRenderedDto
            {
                Id = template.Id,
                Name = template.Name,
                Subject = rendered.Subject,
                BodyHtml = rendered.BodyHtml
            }
        };
    }

    private sealed class TemplateRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string BodyHtml { get; set; } = string.Empty;
    }

    private sealed class PersonRow
    {
        public string FirstName { get; set; } = string.Empty;
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? SecondLastName { get; set; }
        public string? CommercialName { get; set; }
        public string? CustomerCode { get; set; }
        public string? Email { get; set; }
        public string? CompanyName { get; set; }
        public string? UserName { get; set; }
        public string? UserEmail { get; set; }
    }

    private sealed class LeadRow
    {
        public string Code { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
    }
}
