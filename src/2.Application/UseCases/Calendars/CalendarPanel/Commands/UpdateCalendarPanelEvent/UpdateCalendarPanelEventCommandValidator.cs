using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.UpdateCalendarPanelEvent;

/// <summary>
/// Validates <see cref="UpdateCalendarPanelEventCommand"/>.
/// </summary>
public sealed class UpdateCalendarPanelEventCommandValidator : AbstractValidator<UpdateCalendarPanelEventCommand>
{
    public UpdateCalendarPanelEventCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The activity id is required.");
        RuleFor(x => x.Scope).IsInEnum().When(x => x.Scope.HasValue).WithMessage("scope must be ThisOccurrence (1), ThisAndFollowing (2) or Series (3).");
        CalendarEventInputRules.Apply(this);
    }
}
