using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Validates <see cref="RestorePersonBusinessProfileCommand"/>.
/// </summary>
public sealed class RestorePersonBusinessProfileCommandValidator : AbstractValidator<RestorePersonBusinessProfileCommand>
{
    public RestorePersonBusinessProfileCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
