using FluentValidation;
using Template = JOIN.Domain.Admin.EmailTemplate;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Commands;

/// <summary>Validates <see cref="CreateEmailTemplateCommand"/>.</summary>
public sealed class CreateEmailTemplateCommandValidator : AbstractValidator<CreateEmailTemplateCommand>
{
    public CreateEmailTemplateCommandValidator()
    {
        RuleFor(x => x.EmailTemplateTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Template.MaxNameLength);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(Template.MaxSubjectLength);
        RuleFor(x => x.BodyHtml).NotEmpty().MaximumLength(Template.MaxBodyLength);
    }
}

/// <summary>Validates <see cref="UpdateEmailTemplateCommand"/>.</summary>
public sealed class UpdateEmailTemplateCommandValidator : AbstractValidator<UpdateEmailTemplateCommand>
{
    public UpdateEmailTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.EmailTemplateTypeId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Template.MaxNameLength);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(Template.MaxSubjectLength);
        RuleFor(x => x.BodyHtml).NotEmpty().MaximumLength(Template.MaxBodyLength);
    }
}

/// <summary>Validates <see cref="DeleteEmailTemplateCommand"/>.</summary>
public sealed class DeleteEmailTemplateCommandValidator : AbstractValidator<DeleteEmailTemplateCommand>
{
    public DeleteEmailTemplateCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>Validates <see cref="RestoreEmailTemplateCommand"/>.</summary>
public sealed class RestoreEmailTemplateCommandValidator : AbstractValidator<RestoreEmailTemplateCommand>
{
    public RestoreEmailTemplateCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}
