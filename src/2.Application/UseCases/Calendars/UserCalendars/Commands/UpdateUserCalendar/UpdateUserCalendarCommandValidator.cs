using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.UpdateUserCalendar;

/// <summary>
/// Validates <see cref="UpdateUserCalendarCommand"/>.
/// </summary>
public sealed class UpdateUserCalendarCommandValidator : AbstractValidator<UpdateUserCalendarCommand>
{
    public UpdateUserCalendarCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The calendar id is required.");
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("The description cannot exceed 500 characters.");
        RuleFor(x => x.Color).ValidColor();
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");
    }
}
