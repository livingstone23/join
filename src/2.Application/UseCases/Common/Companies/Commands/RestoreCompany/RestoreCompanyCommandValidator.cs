using FluentValidation;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Validates <see cref="RestoreCompanyCommand"/>.
/// </summary>
public sealed class RestoreCompanyCommandValidator : AbstractValidator<RestoreCompanyCommand>
{
    public RestoreCompanyCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
