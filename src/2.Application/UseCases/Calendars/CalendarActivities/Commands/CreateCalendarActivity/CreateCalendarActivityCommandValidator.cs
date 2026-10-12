using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.CreateCalendarActivity;

/// <summary>
/// Validates <see cref="CreateCalendarActivityCommand"/>.
/// </summary>
public sealed class CreateCalendarActivityCommandValidator : AbstractValidator<CreateCalendarActivityCommand>
{
    public CreateCalendarActivityCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("The code is required.")
            .MaximumLength(30).WithMessage("The code cannot exceed 30 characters.")
            .Matches(CalendarCatalogRules.CodePattern).WithMessage("The code only accepts letters, digits and underscore.");
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("The description cannot exceed 500 characters.");
        RuleFor(x => x.Color).ValidColor();
        RuleFor(x => x.Icon).MaximumLength(100).WithMessage("The icon cannot exceed 100 characters.");
        RuleFor(x => x.DefaultDurationMinutes).ValidDuration();
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");
    }
}
