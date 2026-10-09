using FluentValidation;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

/// <summary>
/// Validates <see cref="RestoreSystemOptionCommand"/>.
/// </summary>
public sealed class RestoreSystemOptionCommandValidator : AbstractValidator<RestoreSystemOptionCommand>
{
    public RestoreSystemOptionCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
