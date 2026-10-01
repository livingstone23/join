using FluentValidation;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands.UploadTicketDocument;

/// <summary>
/// Reglas de validación para el command de upload. La longitud y el tipo
/// del archivo se chequean además contra la configuración de la empresa
/// (<see cref="Domain.Messaging.TicketAttachmentSettings"/>) dentro del
/// handler — el validator solo enforza invariantes del shape del comando.
/// </summary>
public sealed class UploadTicketDocumentCommandValidator : AbstractValidator<UploadTicketDocumentCommand>
{
    public UploadTicketDocumentCommandValidator()
    {
        RuleFor(x => x.TicketId)
            .NotEqual(Guid.Empty).WithMessage("Ticket id is required.");

        RuleFor(x => x.File)
            .NotNull().WithMessage("File is required.");

        // Guard the inner-property rules with `When(x => x.File != null)` so
        // they don't NRE on a null File — `RuleFor(x => x.File!.FileName)` would
        // dereference null and crash before any rule fires.
        RuleFor(x => x.File!.FileName)
            .NotEmpty().WithMessage("File name is required.")
            .MaximumLength(150).WithMessage("File name cannot exceed 150 characters.")
            .When(x => x.File != null);

        RuleFor(x => x.File!.ContentType)
            .NotEmpty().WithMessage("Content type is required.")
            .MaximumLength(150).WithMessage("Content type cannot exceed 150 characters.")
            .When(x => x.File != null);

        RuleFor(x => x.File!.Length)
            .GreaterThan(0).WithMessage("File length must be greater than zero.")
            .When(x => x.File != null);
    }
}