using FluentValidation;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Validates <see cref="RestoreEntityStatusCommand"/>.
/// </summary>
public sealed class RestoreEntityStatusCommandValidator : AbstractValidator<RestoreEntityStatusCommand>
{
    public RestoreEntityStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
