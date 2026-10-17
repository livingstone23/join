using FluentValidation;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadStages.Commands;

internal static class LeadStageInputRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : ILeadStageInput
    {
        CommercialCatalogInputRules.Apply(validator);
        validator.RuleFor(x => x.Probability).InclusiveBetween(0, 100);
        validator.RuleFor(x => x.Kind)
            .Must(v => Enum.IsDefined(typeof(LeadStageKind), v))
            .WithMessage("Kind must be 1 (Initial), 2 (Open), 3 (Won) or 4 (Lost).");
    }
}

/// <summary>Validates <see cref="CreateLeadStageCommand"/>.</summary>
public sealed class CreateLeadStageCommandValidator : AbstractValidator<CreateLeadStageCommand>
{
    public CreateLeadStageCommandValidator() => LeadStageInputRules.Apply(this);
}

/// <summary>Validates <see cref="UpdateLeadStageCommand"/>.</summary>
public sealed class UpdateLeadStageCommandValidator : AbstractValidator<UpdateLeadStageCommand>
{
    public UpdateLeadStageCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        LeadStageInputRules.Apply(this);
    }
}
