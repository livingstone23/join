using System.Globalization;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Email;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Security;
using MediatR;
using PersonContactType = JOIN.Domain.Enums.ContactType;
using Template = JOIN.Domain.Admin.EmailTemplate;

namespace JOIN.Application.UseCases.Common.EmailTemplates;

/// <summary>
/// SPEC 71 — <c>GET /EmailTemplates/lookup</c>: the active templates of the company for the picker of
/// the sending modal. Open to any authenticated user of the company (the seller sends the mail, but
/// does not administer the templates).
/// </summary>
public sealed record LookupEmailTemplatesQuery(Guid? EmailTemplateTypeId = null, string? Search = null)
    : IRequest<Response<IReadOnlyList<EmailTemplateLookupItemDto>>>;

/// <summary>
/// SPEC 71 — <c>GET /EmailTemplates/variables</c>: the closed list of variables with its description,
/// for the editor of the template.
/// </summary>
public sealed record GetEmailTemplateVariablesQuery : IRequest<Response<IReadOnlyList<EmailTemplateVariableDto>>>;

/// <summary>
/// SPEC 71 — <c>POST /EmailTemplates/{id}/render</c>: the subject and the body of a template with the
/// variables replaced by the values of the person, the lead, the user and the company.
/// </summary>
public sealed record RenderEmailTemplateQuery(Guid TemplateId, Guid PersonId, string? EmailAddress = null, Guid? LeadId = null)
    : IRequest<Response<EmailTemplateRenderedDto>>;

/// <summary>Handles <see cref="LookupEmailTemplatesQuery"/> with Dapper.</summary>
public sealed class LookupEmailTemplatesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<LookupEmailTemplatesQuery, Response<IReadOnlyList<EmailTemplateLookupItemDto>>>
{
    /// <summary>Maximum number of templates the picker returns (SPEC 71, scope C.11).</summary>
    private const int MaxResults = 50;

    public async Task<Response<IReadOnlyList<EmailTemplateLookupItemDto>>> Handle(LookupEmailTemplatesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<IReadOnlyList<EmailTemplateLookupItemDto>>();
        }

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", currentUserService.CompanyId);
        parameters.Add("MaxResults", MaxResults);

        var filters = "WHERE t.CompanyId = @CompanyId AND t.GcRecord = 0 AND t.IsActive = TRUE";

        if (request.EmailTemplateTypeId is { } typeId && typeId != Guid.Empty)
        {
            filters += " AND t.EmailTemplateTypeId = @EmailTemplateTypeId";
            parameters.Add("EmailTemplateTypeId", typeId);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            filters += " AND (t.Name ILIKE @Search OR t.Subject ILIKE @Search)";
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        var sql = $"""
            SELECT
                t.Id,
                t.Name,
                t.Subject,
                t.EmailTemplateTypeId,
                tt.Name AS EmailTemplateTypeName,
                tt.Code AS EmailTemplateTypeCode
            FROM admin.emailtemplates t
            JOIN admin.emailtemplatetypes tt ON tt.Id = t.EmailTemplateTypeId
            {filters}
            ORDER BY tt.SortOrder, tt.Name, t.Name
            LIMIT @MaxResults;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<EmailTemplateLookupItemDto>(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));

        return new Response<IReadOnlyList<EmailTemplateLookupItemDto>>
        {
            IsSuccess = true,
            Message = "Email templates retrieved successfully.",
            Data = rows.AsList()
        };
    }
}

/// <summary>Handles <see cref="GetEmailTemplateVariablesQuery"/>.</summary>
public sealed class GetEmailTemplateVariablesQueryHandler
    : IRequestHandler<GetEmailTemplateVariablesQuery, Response<IReadOnlyList<EmailTemplateVariableDto>>>
{
    public Task<Response<IReadOnlyList<EmailTemplateVariableDto>>> Handle(GetEmailTemplateVariablesQuery request, CancellationToken cancellationToken)
    {
        var variables = EmailTemplateVariables.All
            .Select(v => new EmailTemplateVariableDto { Key = v.Key, Description = v.Description })
            .ToList();

        return Task.FromResult(new Response<IReadOnlyList<EmailTemplateVariableDto>>
        {
            IsSuccess = true,
            Message = "Email template variables retrieved successfully.",
            Data = variables
        });
    }
}

/// <summary>
/// Handles <see cref="RenderEmailTemplateQuery"/>: builds the context of the template (person, customer,
/// lead, user and company) and renders the subject and the body.
/// </summary>
public sealed class RenderEmailTemplateQueryHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IEmailTemplateRenderer renderer,
    LeadCommandContext leadContext)
    : IRequestHandler<RenderEmailTemplateQuery, Response<EmailTemplateRenderedDto>>
{
    public async Task<Response<EmailTemplateRenderedDto>> Handle(RenderEmailTemplateQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<EmailTemplateRenderedDto>();
        }

        var companyId = currentUserService.CompanyId;

        var template = await unitOfWork.GetRepository<Template>().GetAsync(request.TemplateId);
        if (template is null || template.CompanyId != companyId || template.GcRecord != 0 || !template.IsActive)
        {
            return EmailTemplateErrors.NotFound<EmailTemplateRenderedDto>();
        }

        // GetAllIncludingDeletedAsync (EF) and not GetAsync: the persons repository resolves with Dapper,
        // which cannot run inside the tenant filter of this request.
        var person = (await unitOfWork.GetRepository<Person>().GetAllIncludingDeletedAsync(
            x => x.Id == request.PersonId && x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();
        if (person is null)
        {
            return Response<EmailTemplateRenderedDto>.Error(
                EmailTemplateErrorCodes.PersonNotFound, ["Person not found."]);
        }

        var lead = await ResolveLeadAsync(request, person, companyId, cancellationToken);
        if (lead is null && request.LeadId is { } requestedLeadId)
        {
            return Response<EmailTemplateRenderedDto>.Error(
                EmailTemplateErrorCodes.LeadNotFound, ["Lead not found."]);
        }

        var context = await BuildContextAsync(companyId, person, lead, request.EmailAddress, cancellationToken);
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

    /// <summary>
    /// The lead of the person, when the request carries one. A lead of another person, of another
    /// company or outside the scope of the user (SPEC 60) answers <c>LEAD_NOT_FOUND</c>.
    /// </summary>
    private async Task<Lead?> ResolveLeadAsync(RenderEmailTemplateQuery request, Person person, Guid companyId, CancellationToken cancellationToken)
    {
        if (request.LeadId is not { } leadId || leadId == Guid.Empty)
        {
            return null;
        }

        var actorError = LeadCommandContext.ActorOf<EmailTemplateRenderedDto>(currentUserService, out var actor);
        if (actorError is not null)
        {
            return null;
        }

        var (load, error) = await leadContext.LoadAsync<EmailTemplateRenderedDto>(actor, leadId, cancellationToken);
        if (error is not null || load is null)
        {
            return null;
        }

        return load.Lead.PersonId == person.Id ? load.Lead : null;
    }

    /// <summary>The values of the closed list of variables for this render.</summary>
    private async Task<EmailTemplateContext> BuildContextAsync(
        Guid companyId,
        Person person,
        Lead? lead,
        string? emailAddress,
        CancellationToken cancellationToken)
    {
        var customer = (await unitOfWork.GetRepository<Customer>().GetAllIncludingDeletedAsync(
            x => x.CompanyId == companyId && x.PersonId == person.Id && x.GcRecord == 0))
            .FirstOrDefault(c => c.IsActive);

        var company = await unitOfWork.GetRepository<Domain.Common.Company>().GetAsync(companyId);

        ApplicationUser? user = null;
        if (Guid.TryParse(currentUserService.UserId, out var userId))
        {
            user = await unitOfWork.GetRepository<ApplicationUser>().GetIncludingDeletedAsync(userId);
        }

        var contacts = (await unitOfWork.GetRepository<PersonContact>().GetAllIncludingDeletedAsync(
            x => x.CompanyId == companyId && x.PersonId == person.Id && x.GcRecord == 0 && x.IsActive))
            .Where(c => c.ContactType is PersonContactType.PrimaryEmail or PersonContactType.AlternativeEmail)
            .OrderByDescending(c => c.IsPrimary)
            .ToList();

        var personEmail = emailAddress
            ?? contacts.FirstOrDefault(c => c.ContactType == PersonContactType.PrimaryEmail)?.ContactValue
            ?? contacts.FirstOrDefault()?.ContactValue
            ?? string.Empty;

        return new EmailTemplateContext(
            PersonaNombre: string.IsNullOrWhiteSpace(person.CommercialName) ? person.FirstName : person.CommercialName,
            PersonaNombreCompleto: FullName(person),
            PersonaCorreo: personEmail,
            ClienteCodigo: customer?.CustomerCode ?? string.Empty,
            LeadCodigo: lead?.Code ?? string.Empty,
            LeadTema: lead?.Topic ?? string.Empty,
            UsuarioNombre: user is null ? string.Empty : FullName(user),
            UsuarioCorreo: user?.Email ?? string.Empty,
            EmpresaNombre: company?.Name ?? string.Empty,
            FechaHoy: DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
    }

    private static string FullName(Person person) =>
        string.Join(" ", new[] { person.FirstName, person.MiddleName, person.LastName, person.SecondLastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string FullName(ApplicationUser user) =>
        string.Join(" ", new[] { user.FirstName, user.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
}
