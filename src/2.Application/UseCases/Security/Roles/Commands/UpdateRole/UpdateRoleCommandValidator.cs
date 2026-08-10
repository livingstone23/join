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
        // Name is optional: omitting it preserves the existing role name (used to edit only the description
        // of system-default roles). When supplied, it cannot be empty and must fit in 256 chars.
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(256)
            .When(x => x.Name != null);

        RuleFor(x => x.Description)
            .MaximumLength(500);
    }
}
