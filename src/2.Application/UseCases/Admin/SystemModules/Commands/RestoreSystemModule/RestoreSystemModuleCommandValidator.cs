using FluentValidation;

namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;

/// <summary>
/// Validates <see cref="RestoreSystemModuleCommand"/>.
/// </summary>
public sealed class RestoreSystemModuleCommandValidator : AbstractValidator<RestoreSystemModuleCommand>
{
    public RestoreSystemModuleCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
