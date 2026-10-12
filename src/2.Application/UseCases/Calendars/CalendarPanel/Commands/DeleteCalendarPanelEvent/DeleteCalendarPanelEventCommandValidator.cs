using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.DeleteCalendarPanelEvent;

/// <summary>
/// Validates <see cref="DeleteCalendarPanelEventCommand"/>.
/// </summary>
public sealed class DeleteCalendarPanelEventCommandValidator : AbstractValidator<DeleteCalendarPanelEventCommand>
{
    public DeleteCalendarPanelEventCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The activity id is required.");
        RuleFor(x => x.Scope).IsInEnum().When(x => x.Scope.HasValue).WithMessage("scope must be ThisOccurrence (1), ThisAndFollowing (2) or Series (3).");
    }
}
