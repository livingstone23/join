using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — validation of <see cref="UploadCalendarEventDocumentCommand"/>: original name / content type are
/// required, content cannot be empty. The remaining rules (file size, allowed type, per-comment and daily
/// quotas) are enforced in the handler because they depend on the company limits.
/// </summary>
public sealed class UploadCalendarEventDocumentCommandValidator : AbstractValidator<UploadCalendarEventDocumentCommand>
{
    public UploadCalendarEventDocumentCommandValidator()
    {
        RuleFor(x => x.OriginalName)
            .NotEmpty().WithMessage("OriginalName is required.")
            .MaximumLength(Domain.Calendars.CalendarEventDocument.MaxNameLength)
            .WithMessage($"OriginalName cannot exceed {Domain.Calendars.CalendarEventDocument.MaxNameLength} characters.");

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("ContentType is required.")
            .MaximumLength(150).WithMessage("ContentType cannot exceed 150 characters.");

        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required.");
    }
}