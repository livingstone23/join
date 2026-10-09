using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

/// <summary>
/// Validates <see cref="RestorePersonAddressCommand"/>.
/// </summary>
public sealed class RestorePersonAddressCommandValidator : AbstractValidator<RestorePersonAddressCommand>
{
    public RestorePersonAddressCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
