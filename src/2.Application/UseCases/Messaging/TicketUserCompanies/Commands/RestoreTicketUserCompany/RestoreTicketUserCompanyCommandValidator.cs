using FluentValidation;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.RestoreTicketUserCompany;

/// <summary>
/// Validates <see cref="RestoreTicketUserCompanyCommand"/>.
/// </summary>
public sealed class RestoreTicketUserCompanyCommandValidator : AbstractValidator<RestoreTicketUserCompanyCommand>
{
    public RestoreTicketUserCompanyCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
