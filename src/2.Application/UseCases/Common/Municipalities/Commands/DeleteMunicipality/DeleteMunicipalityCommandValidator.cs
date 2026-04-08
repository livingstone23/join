using FluentValidation;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteMunicipalityCommand"/>.
/// </summary>
public sealed class DeleteMunicipalityCommandValidator : AbstractValidator<DeleteMunicipalityCommand>
{
    public DeleteMunicipalityCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Municipality id is required.");
    }
}
