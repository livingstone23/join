using FluentValidation;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadReasons.Commands;

/// <summary>Validates <see cref="CreateLeadReasonCommand"/>.</summary>
public sealed class CreateLeadReasonCommandValidator : AbstractValidator<CreateLeadReasonCommand>
{
    public CreateLeadReasonCommandValidator()
    {
        CommercialCatalogInputRules.Apply(this);
        RuleFor(x => x.UsedFor).Must(v => Enum.IsDefined(typeof(LeadReasonUsage), v)).WithMessage("UsedFor must be 1 (Won), 2 (Lost) or 3 (Inactive).");
    }
}

/// <summary>Validates <see cref="UpdateLeadReasonCommand"/>.</summary>
public sealed class UpdateLeadReasonCommandValidator : AbstractValidator<UpdateLeadReasonCommand>
{
    public UpdateLeadReasonCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        CommercialCatalogInputRules.Apply(this);
        RuleFor(x => x.UsedFor).Must(v => Enum.IsDefined(typeof(LeadReasonUsage), v)).WithMessage("UsedFor must be 1 (Won), 2 (Lost) or 3 (Inactive).");
    }
}
