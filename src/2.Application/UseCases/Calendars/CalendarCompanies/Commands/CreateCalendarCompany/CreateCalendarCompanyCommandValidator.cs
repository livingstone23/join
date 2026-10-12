using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.CreateCalendarCompany;

/// <summary>
/// Validates <see cref="CreateCalendarCompanyCommand"/>.
/// </summary>
public sealed class CreateCalendarCompanyCommandValidator : AbstractValidator<CreateCalendarCompanyCommand>
{
    public CreateCalendarCompanyCommandValidator()
    {
        RuleFor(x => x.DefaultActivityUserId).NotEmpty().WithMessage("The default activity user is required.");
        RuleFor(x => x.TimeZone).NotEmpty().WithMessage("The time zone is required.")
            .MaximumLength(64).WithMessage("The time zone cannot exceed 64 characters.");
        RuleFor(x => x.DefaultCalendarName).MaximumLength(100).WithMessage("The default calendar name cannot exceed 100 characters.");
    }
}
