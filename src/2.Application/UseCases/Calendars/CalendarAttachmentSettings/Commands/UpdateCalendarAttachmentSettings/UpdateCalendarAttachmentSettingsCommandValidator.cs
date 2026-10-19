using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands;

/// <summary>
/// SPEC 69 — validation of <see cref="UpdateCalendarAttachmentSettingsCommand"/>: the limits must stay
/// strictly positive; <c>DocumentType.None</c> is allowed (it disables uploads by construction).
/// </summary>
public sealed class UpdateCalendarAttachmentSettingsCommandValidator
    : AbstractValidator<UpdateCalendarAttachmentSettingsCommand>
{
    public UpdateCalendarAttachmentSettingsCommandValidator()
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