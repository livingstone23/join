using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Commands.DeleteCalendarActivityCompany;

/// <summary>
/// Validates <see cref="DeleteCalendarActivityCompanyCommand"/>.
/// </summary>
public sealed class DeleteCalendarActivityCompanyCommandValidator : AbstractValidator<DeleteCalendarActivityCompanyCommand>
{
    public DeleteCalendarActivityCompanyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The activity type id is required.");
    }
}
