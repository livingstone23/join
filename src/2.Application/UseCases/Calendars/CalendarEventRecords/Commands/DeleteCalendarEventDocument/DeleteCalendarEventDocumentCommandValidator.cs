using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — validation of <see cref="DeleteCalendarEventDocumentCommand"/>: the reason is mandatory (rule 4).
/// </summary>
public sealed class DeleteCalendarEventDocumentCommandValidator : AbstractValidator<DeleteCalendarEventDocumentCommand>
{
    public DeleteCalendarEventDocumentCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(Domain.Calendars.CalendarEventComment.MaxDeleteReasonLength)
            .WithMessage($"Reason cannot exceed {Domain.Calendars.CalendarEventComment.MaxDeleteReasonLength} characters.");
    }
}