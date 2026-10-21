using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Email;
using MediatR;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Queries;

/// <summary>
/// Handles <see cref="RenderEmailTemplateQuery"/> with Dapper: the active template of the company, then the values of
/// the variables from <see cref="EmailTemplateContextReader"/> (person, customer, lead in scope, sender and company).
/// </summary>
public sealed class RenderEmailTemplateQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService,
    IEmailTemplateRenderer renderer,
    EmailTemplateContextReader contextReader)
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

    public async Task<Response<EmailTemplateRenderedDto>> Handle(RenderEmailTemplateQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailTemplateErrors.CompanyRequired<EmailTemplateRenderedDto>();
        }

        var companyId = currentUserService.CompanyId;
        TemplateRow? template;
        using (var connection = connectionFactory.CreateConnection())
        {
            template = await connection.QuerySingleOrDefaultAsync<TemplateRow>(new CommandDefinition(
                TemplateSql, new { request.TemplateId, CompanyId = companyId }, cancellationToken: cancellationToken));
        }

        if (template is null)
        {
            return EmailTemplateErrors.NotFound<EmailTemplateRenderedDto>();
        }

        var userId = Guid.TryParse(currentUserService.UserId, out var parsed) ? parsed : Guid.Empty;
        var context = await contextReader.ReadAsync(companyId, userId, request.PersonId, request.EmailAddress, request.LeadId, cancellationToken);
        if (!context.IsSuccess)
        {
            return Response<EmailTemplateRenderedDto>.Error(context.Message, context.Errors);
        }

        var rendered = renderer.Render(template.Subject, template.BodyHtml, context.Data!);
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
}
