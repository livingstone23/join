using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;

/// <summary>
/// Defines validation rules for <see cref="UpdateTicketUserCompanyCommand"/>.
/// </summary>
public sealed class UpdateTicketUserCompanyCommandValidator : AbstractValidator<UpdateTicketUserCompanyCommand>
{
    public UpdateTicketUserCompanyCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEqual(Guid.Empty)
            .WithMessage("Id is required.");
    }
}
