using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Reglas de validación para la creación de la configuración de adjuntos.
/// <c>AllowedDocumentTypes = 0</c> (DocumentType.None) es válido — deshabilita
/// toda subida por construcción, sin necesidad de un flag separado.
/// </summary>
public sealed class CreateTicketAttachmentSettingsCommandValidator : AbstractValidator<CreateTicketAttachmentSettingsCommand>
{
    public CreateTicketAttachmentSettingsCommandValidator()
    {
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