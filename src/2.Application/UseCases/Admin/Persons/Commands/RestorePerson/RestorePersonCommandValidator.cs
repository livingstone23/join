using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Persons.Commands;

/// <summary>
/// Validates <see cref="RestorePersonCommand"/>.
/// </summary>
public sealed class RestorePersonCommandValidator : AbstractValidator<RestorePersonCommand>
{
    public RestorePersonCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
