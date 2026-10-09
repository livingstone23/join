using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Validates <see cref="RestoreProjectCommand"/>.
/// </summary>
public sealed class RestoreProjectCommandValidator : AbstractValidator<RestoreProjectCommand>
{
    public RestoreProjectCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
