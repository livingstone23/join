using FluentValidation;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.LeadSettings.RelationshipTypes.Commands;

/// <summary>Validates <see cref="CreateRelationshipTypeCommand"/>.</summary>
public sealed class CreateRelationshipTypeCommandValidator : AbstractValidator<CreateRelationshipTypeCommand>
{
    public CreateRelationshipTypeCommandValidator()
    {
        CommercialCatalogInputRules.Apply(this);
        RuleFor(x => x.OnWin).Must(v => Enum.IsDefined(typeof(RelationshipOnWin), v)).WithMessage("OnWin must be 0 (None) or 1 (Customer).");
    }
}

/// <summary>Validates <see cref="UpdateRelationshipTypeCommand"/>.</summary>
public sealed class UpdateRelationshipTypeCommandValidator : AbstractValidator<UpdateRelationshipTypeCommand>
{
    public UpdateRelationshipTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        CommercialCatalogInputRules.Apply(this);
        RuleFor(x => x.OnWin).Must(v => Enum.IsDefined(typeof(RelationshipOnWin), v)).WithMessage("OnWin must be 0 (None) or 1 (Customer).");
    }
}
