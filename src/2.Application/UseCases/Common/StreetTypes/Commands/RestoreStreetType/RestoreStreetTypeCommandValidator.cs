using FluentValidation;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Validates <see cref="RestoreStreetTypeCommand"/>.
/// </summary>
public sealed class RestoreStreetTypeCommandValidator : AbstractValidator<RestoreStreetTypeCommand>
{
    public RestoreStreetTypeCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
