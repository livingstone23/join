using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Industries.Commands;

/// <summary>
/// Validates <see cref="RestoreIndustryCommand"/>.
/// </summary>
public sealed class RestoreIndustryCommandValidator : AbstractValidator<RestoreIndustryCommand>
{
    public RestoreIndustryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
