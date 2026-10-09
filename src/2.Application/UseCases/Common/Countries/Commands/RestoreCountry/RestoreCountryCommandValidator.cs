using FluentValidation;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Validates <see cref="RestoreCountryCommand"/>.
/// </summary>
public sealed class RestoreCountryCommandValidator : AbstractValidator<RestoreCountryCommand>
{
    public RestoreCountryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
