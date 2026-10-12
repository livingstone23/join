using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarEvents.Commands.DeleteCalendarEvent;

/// <summary>
/// Validates <see cref="DeleteCalendarEventCommand"/>.
/// </summary>
public sealed class DeleteCalendarEventCommandValidator : AbstractValidator<DeleteCalendarEventCommand>
{
    public DeleteCalendarEventCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The activity id is required.");
        RuleFor(x => x.Scope).IsInEnum().When(x => x.Scope.HasValue).WithMessage("scope must be ThisOccurrence (1), ThisAndFollowing (2) or Series (3).");
    }
}
