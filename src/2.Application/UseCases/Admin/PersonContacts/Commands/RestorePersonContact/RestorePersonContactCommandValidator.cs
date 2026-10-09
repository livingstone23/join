using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;

/// <summary>
/// Validates <see cref="RestorePersonContactCommand"/>.
/// </summary>
public sealed class RestorePersonContactCommandValidator : AbstractValidator<RestorePersonContactCommand>
{
    public RestorePersonContactCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
