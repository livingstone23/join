using FluentValidation;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadSources.Commands;

/// <summary>Validates <see cref="CreateLeadSourceCommand"/>.</summary>
public sealed class CreateLeadSourceCommandValidator : AbstractValidator<CreateLeadSourceCommand>
{
    public CreateLeadSourceCommandValidator()
    {
        CommercialCatalogInputRules.Apply(this);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}

/// <summary>Validates <see cref="UpdateLeadSourceCommand"/>.</summary>
public sealed class UpdateLeadSourceCommandValidator : AbstractValidator<UpdateLeadSourceCommand>
{
    public UpdateLeadSourceCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        CommercialCatalogInputRules.Apply(this);
        RuleFor(x => x.Description).MaximumLength(500);
    }
}
