using FluentValidation;

namespace JOIN.Application.UseCases.Common.Provinces.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteProvinceCommand"/>.
/// </summary>
public sealed class DeleteProvinceCommandValidator : AbstractValidator<DeleteProvinceCommand>
{
    public DeleteProvinceCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Province id is required.");
    }
}
