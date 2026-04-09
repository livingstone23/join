using FluentValidation;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteRegionCommand"/>.
/// </summary>
public sealed class DeleteRegionCommandValidator : AbstractValidator<DeleteRegionCommand>
{
    public DeleteRegionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Region id is required.");
    }
}
