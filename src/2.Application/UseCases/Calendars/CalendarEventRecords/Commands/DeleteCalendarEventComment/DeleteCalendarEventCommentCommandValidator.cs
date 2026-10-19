using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — validation of <see cref="DeleteCalendarEventCommentCommand"/>: the reason is mandatory (rule 4)
/// and capped at the same length the entity stores.
/// </summary>
public sealed class DeleteCalendarEventCommentCommandValidator : AbstractValidator<DeleteCalendarEventCommentCommand>
{
    public DeleteCalendarEventCommentCommandValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason is required.")
            .MaximumLength(Domain.Calendars.CalendarEventComment.MaxDeleteReasonLength)
            .WithMessage($"Reason cannot exceed {Domain.Calendars.CalendarEventComment.MaxDeleteReasonLength} characters.");
    }
}