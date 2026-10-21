using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;

namespace JOIN.Application.UseCases.Common.EmailTemplateTypes.Commands;

/// <summary>Fields shared by the create and update commands of an email template type.</summary>
public interface IEmailTemplateTypeInput
{
    string Code { get; }
    string Name { get; }
    int SortOrder { get; }
    bool IsActive { get; }
}

/// <summary><c>POST /EmailTemplateTypes</c> (SPEC 71): adds a classification to the company's catalog.</summary>
public sealed record CreateEmailTemplateTypeCommand : IEmailTemplateTypeInput, ITransactionalCommand<Response<EmailTemplateTypeDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary><c>PUT /EmailTemplateTypes/{id}</c> (SPEC 71).</summary>
public sealed record UpdateEmailTemplateTypeCommand : IEmailTemplateTypeInput, ITransactionalCommand<Response<EmailTemplateTypeDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>SPEC 71 — <c>DELETE /EmailTemplateTypes/{id}</c>: blocked by <c>EMAIL_TEMPLATE_TYPE_IN_USE</c> when there are live templates.</summary>
public sealed record DeleteEmailTemplateTypeCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>SPEC 71 — <c>POST /EmailTemplateTypes/{id}/restore</c>: SuperAdmin only.</summary>
public sealed record RestoreEmailTemplateTypeCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
