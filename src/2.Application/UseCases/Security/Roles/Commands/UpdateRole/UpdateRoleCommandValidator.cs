using FluentValidation;

namespace JOIN.Application.UseCases.Security.Roles.Commands.UpdateRole;

/// <summary>
/// FluentValidation rules for <see cref="UpdateRoleCommand"/>.
/// Same constraints as create; business rules around system-default roles live in the handler.
/// </summary>
public sealed class UpdateRoleCommandValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.Description)
            .MaximumLength(500);
    }
}
