using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.Tickets.RegisterInboundTicket;

/// <summary>
/// Validaciones de entrada para <see cref="RegisterInboundTicketCommand"/> — la
/// ruta sin JWT exige controles estrictos porque no hay <c>ICurrentUserService</c>
/// que descarte el request antes. La unicidad por <c>ExternalMessageId</c> la
/// resuelve el handler. Las cadenas que pueden ir vacías en condiciones reales
/// (un WhatsApp sin texto) se acotan en longitud pero NO se fuerzan no-vacías
/// — los controllers de ingesta ya aplican la validación específica de su
/// proveedor antes de armar el comando.
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

        RuleFor(x => x.MessageSubject)
            .MaximumLength(200).WithMessage("MessageSubject cannot exceed 200 characters.");

        RuleFor(x => x.MessageBody)
            .MaximumLength(8000).WithMessage("MessageBody cannot exceed 8000 characters.");
    }
}
