using FluentValidation;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.UpdateCalendarStatusCompany;

/// <summary>
/// Validates <see cref="UpdateCalendarStatusCompanyCommand"/>.
/// </summary>
public sealed class UpdateCalendarStatusCompanyCommandValidator : AbstractValidator<UpdateCalendarStatusCompanyCommand>
{
    public UpdateCalendarStatusCompanyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The status id is required.");
        RuleFor(x => x.Name).ValidName(100);
        RuleFor(x => x.Color).ValidColor();
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0).WithMessage("The sort order cannot be negative.");
    }
}
