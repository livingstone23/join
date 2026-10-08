using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Validates <see cref="RestoreAreaCommand"/>.
/// </summary>
public sealed class RestoreAreaCommandValidator : AbstractValidator<RestoreAreaCommand>
{
    public RestoreAreaCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
