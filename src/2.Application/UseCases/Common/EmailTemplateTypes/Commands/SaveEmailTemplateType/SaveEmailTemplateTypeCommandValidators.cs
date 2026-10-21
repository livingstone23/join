using FluentValidation;
using Settings = JOIN.Domain.Admin.EmailTemplateType;

namespace JOIN.Application.UseCases.Common.EmailTemplateTypes.Commands;

/// <summary>Validates <see cref="CreateEmailTemplateTypeCommand"/>.</summary>
public sealed class CreateEmailTemplateTypeCommandValidator : AbstractValidator<CreateEmailTemplateTypeCommand>
{
    public CreateEmailTemplateTypeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Settings.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Settings.MaxNameLength);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Validates <see cref="UpdateEmailTemplateTypeCommand"/>.</summary>
public sealed class UpdateEmailTemplateTypeCommandValidator : AbstractValidator<UpdateEmailTemplateTypeCommand>
{
    public UpdateEmailTemplateTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Settings.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Settings.MaxNameLength);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Validates <see cref="DeleteEmailTemplateTypeCommand"/>.</summary>
public sealed class DeleteEmailTemplateTypeCommandValidator : AbstractValidator<DeleteEmailTemplateTypeCommand>
{
    public DeleteEmailTemplateTypeCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>Validates <see cref="RestoreEmailTemplateTypeCommand"/>.</summary>
public sealed class RestoreEmailTemplateTypeCommandValidator : AbstractValidator<RestoreEmailTemplateTypeCommand>
{
    public RestoreEmailTemplateTypeCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}
