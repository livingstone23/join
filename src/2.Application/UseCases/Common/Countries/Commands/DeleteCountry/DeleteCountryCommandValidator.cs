using FluentValidation;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteCountryCommand"/>.
/// </summary>
public sealed class DeleteCountryCommandValidator : AbstractValidator<DeleteCountryCommand>
{
    public DeleteCountryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Country id is required.");
    }
}
