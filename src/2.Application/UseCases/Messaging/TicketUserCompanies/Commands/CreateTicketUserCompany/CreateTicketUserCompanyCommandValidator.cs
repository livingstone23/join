using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;

/// <summary>
/// Defines validation rules for <see cref="CreateTicketUserCompanyCommand"/>.
/// </summary>
public sealed class CreateTicketUserCompanyCommandValidator : AbstractValidator<CreateTicketUserCompanyCommand>
{
    public CreateTicketUserCompanyCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEqual(Guid.Empty)
            .WithMessage("UserId is required.");
    }
}
