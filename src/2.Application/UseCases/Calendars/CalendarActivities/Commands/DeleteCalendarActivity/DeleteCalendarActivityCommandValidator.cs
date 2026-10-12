using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Commands.DeleteCalendarActivity;

/// <summary>
/// Validates <see cref="DeleteCalendarActivityCommand"/>.
/// </summary>
public sealed class DeleteCalendarActivityCommandValidator : AbstractValidator<DeleteCalendarActivityCommand>
{
    public DeleteCalendarActivityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The activity type id is required.");
    }
}
