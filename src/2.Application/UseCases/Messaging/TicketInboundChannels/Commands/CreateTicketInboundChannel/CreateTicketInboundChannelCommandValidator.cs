using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;

/// <summary>
/// Validaciones de entrada para <see cref="CreateTicketInboundChannelCommand"/>.
/// La unicidad del par <c>(CompanyId, ChannelId)</c> se valida en el handler porque
/// requiere un lookup contra la BD.
/// </summary>
public sealed class CreateTicketInboundChannelCommandValidator : AbstractValidator<CreateTicketInboundChannelCommand>
{
    /// <inheritdoc />
    public CreateTicketInboundChannelCommandValidator()
    {
        RuleFor(x => x.ChannelId)
            .NotEqual(Guid.Empty).WithMessage("ChannelId is required.");

        RuleFor(x => x.Description)
            .MaximumLength(500).When(x => x.Description is not null)
            .WithMessage("Description cannot exceed 500 characters.");
    }
}
