using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.UpdateCalendarCompany;

/// <summary>
/// Validates <see cref="UpdateCalendarCompanyCommand"/>.
/// </summary>
public sealed class UpdateCalendarCompanyCommandValidator : AbstractValidator<UpdateCalendarCompanyCommand>
{
    public UpdateCalendarCompanyCommandValidator()
    {
        RuleFor(x => x.DefaultActivityUserId).NotEmpty().WithMessage("The default activity user is required.");
        RuleFor(x => x.TimeZone).NotEmpty().WithMessage("The time zone is required.")
            .MaximumLength(64).WithMessage("The time zone cannot exceed 64 characters.");
        RuleFor(x => x.DefaultCalendarName).MaximumLength(100).WithMessage("The default calendar name cannot exceed 100 characters.");
    }
}
