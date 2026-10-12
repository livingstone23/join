using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Commands.CreateUserCalendar;

/// <summary>
/// Validates <see cref="CreateUserCalendarCommand"/>.
/// </summary>
public sealed class CreateUserCalendarCommandValidator : AbstractValidator<CreateUserCalendarCommand>
{
    public CreateUserCalendarCommandValidator()
    {
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("The description cannot exceed 500 characters.");
        RuleFor(x => x.Color).ValidColor();
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");
    }
}
