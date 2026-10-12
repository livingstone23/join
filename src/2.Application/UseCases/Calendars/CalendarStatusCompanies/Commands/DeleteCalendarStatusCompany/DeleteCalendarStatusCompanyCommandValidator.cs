using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Commands.DeleteCalendarStatusCompany;

/// <summary>
/// Validates <see cref="DeleteCalendarStatusCompanyCommand"/>.
/// </summary>
public sealed class DeleteCalendarStatusCompanyCommandValidator : AbstractValidator<DeleteCalendarStatusCompanyCommand>
{
    public DeleteCalendarStatusCompanyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("The status id is required.");
    }
}
