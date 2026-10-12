using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateMyCalendarConfiguration;

/// <summary>
/// Validates <see cref="UpdateMyCalendarConfigurationCommand"/>.
/// </summary>
public sealed class UpdateMyCalendarConfigurationCommandValidator : AbstractValidator<UpdateMyCalendarConfigurationCommand>
{
    public UpdateMyCalendarConfigurationCommandValidator()
    {
        RuleFor(x => x.DefaultView).IsInEnum().WithMessage("defaultView must be Day (1), Week (2), Month (3) or Agenda (4).");
        RuleFor(x => x.WeekStartsOn).Must(d => d is DayOfWeek.Monday or DayOfWeek.Sunday).WithMessage("weekStartsOn must be Monday (1) or Sunday (0).");
    }
}
