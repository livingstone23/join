using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Commands;

/// <summary>Fields shared by the create and update commands of an email template.</summary>
public interface IEmailTemplateInput
{
    Guid EmailTemplateTypeId { get; }
    string Name { get; }
    string Subject { get; }
    string BodyHtml { get; }
    bool IsActive { get; }
}

/// <summary><c>POST /EmailTemplates</c> (SPEC 71): a template of the company, grouped by type.</summary>
public sealed record CreateEmailTemplateCommand : IEmailTemplateInput, ITransactionalCommand<Response<EmailTemplateDto>>
{
    public Guid EmailTemplateTypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
}

/// <summary><c>PUT /EmailTemplates/{id}</c> (SPEC 71).</summary>
public sealed record UpdateEmailTemplateCommand : IEmailTemplateInput, ITransactionalCommand<Response<EmailTemplateDto>>
{
    public Guid Id { get; init; }
    public Guid EmailTemplateTypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public bool IsActive { get; init; } = true;
}

/// <summary>SPEC 71 — <c>DELETE /EmailTemplates/{id}</c>: logical delete, the file is not involved (there is none).</summary>
public sealed record DeleteEmailTemplateCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>SPEC 71 — <c>POST /EmailTemplates/{id}/restore</c>: SuperAdmin only.</summary>
public sealed record RestoreEmailTemplateCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
