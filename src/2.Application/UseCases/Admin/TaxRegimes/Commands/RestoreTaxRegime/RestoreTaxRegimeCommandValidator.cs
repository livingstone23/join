using FluentValidation;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

/// <summary>
/// Validates <see cref="RestoreTaxRegimeCommand"/>.
/// </summary>
public sealed class RestoreTaxRegimeCommandValidator : AbstractValidator<RestoreTaxRegimeCommand>
{
    public RestoreTaxRegimeCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
