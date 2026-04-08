using FluentValidation;

namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteSystemModuleCommand"/>.
/// </summary>
public sealed class DeleteSystemModuleCommandValidator : AbstractValidator<DeleteSystemModuleCommand>
{
    public DeleteSystemModuleCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("System module id is required.");
    }
}
