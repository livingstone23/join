using FluentValidation;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.Leads.Commands;

/// <summary>Validators of the nested lead inputs.</summary>
public sealed class LeadInteractionInputValidator : AbstractValidator<LeadInteractionInput>
{
    public LeadInteractionInputValidator()
    {
        RuleFor(x => x.ContactTypeCompanyId).NotEmpty();
        RuleFor(x => x.Direction).Must(v => Enum.IsDefined(typeof(InteractionDirection), v)).WithMessage("Direction must be 1 (Outbound) or 2 (Inbound).");
        RuleFor(x => x.Outcome).Must(v => Enum.IsDefined(typeof(InteractionOutcome), v)).WithMessage("Outcome must be 1, 2, 3, 4 or 99.");
        RuleFor(x => x.DurationMinutes).GreaterThan(0).When(x => x.DurationMinutes.HasValue);
        RuleFor(x => x.Summary).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.NextAction!).SetValidator(new LeadNextActionInputValidator()).When(x => x.NextAction is not null);
    }
}

/// <summary>Validates a next action (SPEC 65).</summary>
public sealed class LeadNextActionInputValidator : AbstractValidator<LeadNextActionInput>
{
    public LeadNextActionInputValidator()
    {
        RuleFor(x => x.ContactTypeCompanyId).NotEmpty();
        RuleFor(x => x.StartUtc).NotEmpty();
        RuleFor(x => x.EndUtc).GreaterThan(x => x.StartUtc).WithMessage("EndUtc must be later than StartUtc.");
        RuleFor(x => x.Title).MaximumLength(200);
        RuleFor(x => x.Location).MaximumLength(300);
        RuleFor(x => x.VideoCallUrl).MaximumLength(500);
    }
}

/// <summary>Validates a product line input.</summary>
public sealed class LeadProductInputValidator : AbstractValidator<LeadProductInput>
{
    public LeadProductInputValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).PrecisionScale(18, 2, ignoreTrailingZeros: true).When(x => x.UnitPrice.HasValue);
        RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
        RuleFor(x => x.Status).Must(v => Enum.IsDefined(typeof(LeadProductStatus), v)).WithMessage("Status must be 1 (Offered), 2 (Accepted) or 3 (Rejected).");
    }
}
