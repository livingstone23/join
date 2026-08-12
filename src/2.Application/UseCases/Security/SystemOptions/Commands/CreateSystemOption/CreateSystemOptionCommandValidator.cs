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
            .MaximumLength(250)
            .When(x => !string.IsNullOrWhiteSpace(x.ControllerName));
        RuleFor(x => x.Icon)
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.Icon));
        RuleFor(x => x.OrderMenu)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(10000)
            .When(x => x.OrderMenu.HasValue);
    }
}
