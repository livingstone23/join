using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands;

/// <summary>
/// SPEC 69 — validation of <see cref="CreateCalendarAttachmentSettingsCommand"/>. The bitmask
/// <c>DocumentType.None (0)</c> is allowed: it disables uploads by construction without a separate flag.
/// </summary>
public sealed class CreateCalendarAttachmentSettingsCommandValidator
    : AbstractValidator<CreateCalendarAttachmentSettingsCommand>
{
    public CreateCalendarAttachmentSettingsCommandValidator()
    {
        RuleFor(x => x.MaxFileSizeBytes)
            .GreaterThan(0).WithMessage("MaxFileSizeBytes must be greater than zero.");

        RuleFor(x => x.MaxFilesPerComment)
            .GreaterThan(0).WithMessage("MaxFilesPerComment must be greater than zero.");

        RuleFor(x => x.MaxFilesPerDay)
            .GreaterThan(0).When(x => x.MaxFilesPerDay.HasValue)
            .WithMessage("MaxFilesPerDay must be greater than zero when supplied.");
    }
}