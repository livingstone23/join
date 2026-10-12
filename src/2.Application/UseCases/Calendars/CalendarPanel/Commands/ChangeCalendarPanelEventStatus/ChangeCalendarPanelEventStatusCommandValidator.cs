using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Commands.ChangeCalendarPanelEventStatus;

/// <summary>
/// Validates <see cref="ChangeCalendarPanelEventStatusCommand"/>.
/// </summary>
public sealed class ChangeCalendarPanelEventStatusCommandValidator : AbstractValidator<ChangeCalendarPanelEventStatusCommand>
{
    public ChangeCalendarPanelEventStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The activity id is required.");
        RuleFor(x => x.CalendarStatusCompanyId).NotEmpty().WithMessage("The status is required.");
        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage("The notes cannot exceed 1000 characters.");
        RuleFor(x => x.Scope).IsInEnum().When(x => x.Scope.HasValue).WithMessage("scope must be ThisOccurrence (1), ThisAndFollowing (2) or Series (3).");
    }
}
