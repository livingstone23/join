using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.CreateCalendarPanelEvent;

/// <summary>
/// Validates <see cref="CreateCalendarPanelEventCommand"/>.
/// </summary>
public sealed class CreateCalendarPanelEventCommandValidator : AbstractValidator<CreateCalendarPanelEventCommand>
{
    public CreateCalendarPanelEventCommandValidator()
    {
        RuleFor(x => x.OwnerUserId).NotEmpty().WithMessage("ownerUserId is required.");
        CalendarEventInputRules.Apply(this);
    }
}
