using FluentValidation;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Validates the payload used to create a RoleSystemOption rule.
/// </summary>
public sealed class CreateRoleSystemOptionCommandValidator : AbstractValidator<CreateRoleSystemOptionCommand>
{
    public CreateRoleSystemOptionCommandValidator()
    {
        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");

        RuleFor(x => x.RoleId)
            .NotEmpty()
            .WithMessage("RoleId is required.");

        RuleFor(x => x.SystemOptionId)
            .NotEmpty()
            .WithMessage("SystemOptionId is required.");

        RuleFor(x => x.OrderMenu)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(10000)
            .When(x => x.OrderMenu.HasValue)
            .WithMessage("OrderMenu must be between 0 and 10000.");
    }
}
