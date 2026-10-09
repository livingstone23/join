using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

/// <summary>
/// Validates <see cref="RestoreTicketCompanyDefaultCommand"/>.
/// </summary>
public sealed class RestoreTicketCompanyDefaultCommandValidator : AbstractValidator<RestoreTicketCompanyDefaultCommand>
{
    public RestoreTicketCompanyDefaultCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
