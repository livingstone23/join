using FluentValidation;

namespace JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;

/// <summary>
/// FluentValidation rules for <see cref="CreateRoleCommand"/>.
/// Name is trimmed and upper-cased into NormalizedName by the handler before persistence.
/// </summary>
public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.Description)
            .MaximumLength(500);
    }
}
