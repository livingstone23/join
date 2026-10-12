using FluentValidation;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations.Commands.UpdateUserCalendarConfiguration;

/// <summary>
/// Validates <see cref="UpdateUserCalendarConfigurationCommand"/>.
/// </summary>
public sealed class UpdateUserCalendarConfigurationCommandValidator : AbstractValidator<UpdateUserCalendarConfigurationCommand>
{
    public UpdateUserCalendarConfigurationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("The user id is required.");
        RuleFor(x => x).Must(x => x.BusinessHoursStart.HasValue == x.BusinessHoursEnd.HasValue)
            .WithMessage("businessHoursStart and businessHoursEnd go together: send both or neither.");
        RuleFor(x => x.BusinessHoursEnd).GreaterThan(x => x.BusinessHoursStart)
            .When(x => x.BusinessHoursStart.HasValue && x.BusinessHoursEnd.HasValue)
            .WithMessage("businessHoursEnd must be later than businessHoursStart.");
        RuleFor(x => x.WorkDays).Must(d => d != CalendarWorkDays.None && (d & ~CalendarWorkDays.All) == 0)
            .WithMessage("workDays must contain at least one day (flags Sunday = 1 … Saturday = 64).");
        RuleFor(x => x.DefaultView).IsInEnum().WithMessage("defaultView must be Day (1), Week (2), Month (3) or Agenda (4).");
        RuleFor(x => x.WeekStartsOn).Must(d => d is DayOfWeek.Monday or DayOfWeek.Sunday).WithMessage("weekStartsOn must be Monday (1) or Sunday (0).");
    }
}
