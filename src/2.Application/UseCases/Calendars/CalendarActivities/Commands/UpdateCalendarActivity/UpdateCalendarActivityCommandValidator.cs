using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.UpdateCalendarActivity;

/// <summary>
/// Validates <see cref="UpdateCalendarActivityCommand"/>.
/// </summary>
public sealed class UpdateCalendarActivityCommandValidator : AbstractValidator<UpdateCalendarActivityCommand>
{
    public UpdateCalendarActivityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The activity type id is required.");
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("The description cannot exceed 500 characters.");
        RuleFor(x => x.Color).ValidColor();
        RuleFor(x => x.Icon).MaximumLength(100).WithMessage("The icon cannot exceed 100 characters.");
        RuleFor(x => x.DefaultDurationMinutes).ValidDuration();
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");
    }
}
