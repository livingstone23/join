using FluentValidation;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Validates <see cref="RestoreMunicipalityCommand"/>.
/// </summary>
public sealed class RestoreMunicipalityCommandValidator : AbstractValidator<RestoreMunicipalityCommand>
{
    public RestoreMunicipalityCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
