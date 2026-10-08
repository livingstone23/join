using FluentValidation;

namespace JOIN.Application.UseCases.Common.Provinces.Commands;

/// <summary>
/// Validates <see cref="RestoreProvinceCommand"/>.
/// </summary>
public sealed class RestoreProvinceCommandValidator : AbstractValidator<RestoreProvinceCommand>
{
    public RestoreProvinceCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
