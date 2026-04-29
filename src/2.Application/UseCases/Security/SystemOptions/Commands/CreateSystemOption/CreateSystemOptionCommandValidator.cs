using FluentValidation;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

public sealed class CreateSystemOptionCommandValidator : AbstractValidator<CreateSystemOptionCommand>
{
    public CreateSystemOptionCommandValidator()
    {
        RuleFor(x => x.ModuleId)
            .NotEmpty();
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(150);
        RuleFor(x => x.Route)
            .NotEmpty()
            .MaximumLength(250);
        RuleFor(x => x.ControllerName)
            .MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.ControllerName));
        RuleFor(x => x.Icon)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.Icon));
    }
}
