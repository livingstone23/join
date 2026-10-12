using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.CreateCalendarStatus;

/// <summary>
/// Validates <see cref="CreateCalendarStatusCommand"/>.
/// </summary>
public sealed class CreateCalendarStatusCommandValidator : AbstractValidator<CreateCalendarStatusCommand>
{
    public CreateCalendarStatusCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("The code is required.")
            .MaximumLength(30).WithMessage("The code cannot exceed 30 characters.")
            .Matches(CalendarCatalogRules.CodePattern).WithMessage("The code only accepts letters, digits and underscore.");
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Description).MaximumLength(500).WithMessage("The description cannot exceed 500 characters.");
        RuleFor(x => x.Color).ValidColor();
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");
    }
}
