using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.RegisterInboundTicket;

/// <summary>
/// Input validation for <see cref="RegisterInboundTicketCommand"/>. Subject and body are not limited here: the
/// handler truncates them (150 / 2000 characters), because rejecting a long e-mail would make the provider retry it
/// forever.
/// </summary>
public sealed class RegisterInboundTicketCommandValidator : AbstractValidator<RegisterInboundTicketCommand>
{
    /// <inheritdoc />
    public RegisterInboundTicketCommandValidator()
    {
        RuleFor(x => x.CompanyId)
            .NotEqual(Guid.Empty).WithMessage("CompanyId is required.");

        RuleFor(x => x.ChannelId)
            .NotEqual(Guid.Empty).WithMessage("ChannelId is required.");

        RuleFor(x => x.ExternalMessageId)
            .NotEmpty().WithMessage("ExternalMessageId is required.")
            .MaximumLength(200).WithMessage("ExternalMessageId cannot exceed 200 characters.");

        RuleFor(x => x.SenderIdentifier)
            .NotEmpty().WithMessage("SenderIdentifier is required.")
            .MaximumLength(200).WithMessage("SenderIdentifier cannot exceed 200 characters.");

        RuleFor(x => x.Attachments)
            .NotNull().WithMessage("Attachments cannot be null.");
    }
}
