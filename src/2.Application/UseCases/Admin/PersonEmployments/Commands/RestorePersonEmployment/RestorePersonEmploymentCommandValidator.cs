using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Validates <see cref="RestorePersonEmploymentCommand"/>.
/// </summary>
public sealed class RestorePersonEmploymentCommandValidator : AbstractValidator<RestorePersonEmploymentCommand>
{
    public RestorePersonEmploymentCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
