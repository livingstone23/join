using FluentValidation;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Validates <see cref="RestoreIdentificationTypeCommand"/>.
/// </summary>
public sealed class RestoreIdentificationTypeCommandValidator : AbstractValidator<RestoreIdentificationTypeCommand>
{
    public RestoreIdentificationTypeCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
