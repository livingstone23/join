using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarEventRecords.Commands;

/// <summary>
/// SPEC 69 — validation of <see cref="CreateCalendarEventCommentCommand"/>: the body must be non-empty and
/// capped at <see cref="Domain.Calendars.CalendarEventComment.MaxBodyLength"/> (the cap is shared with the
/// entity to keep the API in sync with the DB).
/// </summary>
public sealed class CreateCalendarEventCommentCommandValidator : AbstractValidator<CreateCalendarEventCommentCommand>
{
    public CreateCalendarEventCommentCommandValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Body is required.")
            .MaximumLength(Domain.Calendars.CalendarEventComment.MaxBodyLength)
            .WithMessage($"Body cannot exceed {Domain.Calendars.CalendarEventComment.MaxBodyLength} characters.");
    }
}