using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Reglas de validación para la actualización de la configuración de adjuntos.
/// </summary>
public sealed class UpdateTicketAttachmentSettingsCommandValidator : AbstractValidator<UpdateTicketAttachmentSettingsCommand>
{
    public UpdateTicketAttachmentSettingsCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty).WithMessage("The configuration identifier is required.");

        RuleFor(x => x.AllowedDocumentTypes)
            .GreaterThanOrEqualTo(0).WithMessage("AllowedDocumentTypes cannot be negative.");

        RuleFor(x => x.MaxFileSizeBytes)
            .GreaterThan(0).WithMessage("MaxFileSizeBytes must be greater than zero.");

        RuleFor(x => x.MaxFilesPerTicket)
            .GreaterThan(0).WithMessage("MaxFilesPerTicket must be greater than zero.");

        RuleFor(x => x.MaxFilesPerDay)
            .GreaterThan(0).When(x => x.MaxFilesPerDay.HasValue)
            .WithMessage("MaxFilesPerDay must be greater than zero when supplied.");
    }
}