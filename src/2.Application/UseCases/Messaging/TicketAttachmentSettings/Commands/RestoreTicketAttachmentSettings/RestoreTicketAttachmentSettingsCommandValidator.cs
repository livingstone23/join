using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Commands;

/// <summary>
/// Validates <see cref="RestoreTicketAttachmentSettingsCommand"/>.
/// </summary>
public sealed class RestoreTicketAttachmentSettingsCommandValidator : AbstractValidator<RestoreTicketAttachmentSettingsCommand>
{
    public RestoreTicketAttachmentSettingsCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
