using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

/// <summary>
/// Validates <see cref="RestoreTicketComplexityCommand"/>.
/// </summary>
public sealed class RestoreTicketComplexityCommandValidator : AbstractValidator<RestoreTicketComplexityCommand>
{
    public RestoreTicketComplexityCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
