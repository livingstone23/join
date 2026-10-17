using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Catalog;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>Input of a contact (create lead, <c>POST /Leads/{id}/interactions</c>).</summary>
public sealed record LeadInteractionInput
{
    public Guid ContactTypeCompanyId { get; init; }
    /// <summary>1 Outbound, 2 Inbound.</summary>
    public int Direction { get; init; } = 1;
    /// <summary>Defaults to now (UTC).</summary>
    public DateTime? OccurredAt { get; init; }
    public int? DurationMinutes { get; init; }
    /// <summary>1 Interested, 2 ProposalRequested, 3 NoAnswer, 4 NotInterested, 99 Other.</summary>
    public int Outcome { get; init; } = 99;
    public string Summary { get; init; } = string.Empty;
}

/// <summary>Input of an offered product line (create lead, <c>POST /Leads/{id}/products</c>).</summary>
public sealed record LeadProductInput
{
    public Guid ProductId { get; init; }
    public decimal Quantity { get; init; } = 1m;
    /// <summary>In the lead currency; defaults to the product reference price converted from base currency.</summary>
    public decimal? UnitPrice { get; init; }
    public decimal DiscountPercent { get; init; }
    /// <summary>1 Offered, 2 Accepted, 3 Rejected.</summary>
    public int Status { get; init; } = 1;
    public Guid? OfferedInInteractionId { get; init; }
}

/// <summary>
/// Contacts and product lines of a lead (SPEC 61 B, items 29 and 31): active company contact type (with its duration
/// rule) and active product with the default price in the lead currency.
/// </summary>
public sealed class LeadWorkRules(IUnitOfWork unitOfWork, LeadReferenceValidator references)
{
    /// <summary>Validates the contact type and duration; returns the entity ready to insert.</summary>
    public async Task<(LeadInteraction? Interaction, Response<T>? Error)> BuildInteractionAsync<T>(Lead lead, LeadInteractionInput input, Guid userId)
    {
        var copy = (await unitOfWork.GetRepository<ContactTypeCompany>().GetAllIncludingDeletedAsync(c =>
                c.Id == input.ContactTypeCompanyId && c.CompanyId == lead.CompanyId && c.GcRecord == 0 && c.IsActive))
            .FirstOrDefault();
        var master = copy is null
            ? null
            : (await unitOfWork.GetRepository<ContactType>().GetAllIncludingDeletedAsync(t => t.Id == copy.ContactTypeId && t.GcRecord == 0 && t.IsActive)).FirstOrDefault();
        if (copy is null || master is null)
        {
            return (null, Response<T>.Error(LeadErrorCodes.InvalidContactType, ["The contact type is inactive or does not exist in the company."]));
        }

        if (master.AsksDuration && input.DurationMinutes is null)
        {
            return (null, Response<T>.Error(LeadErrorCodes.InteractionDurationRequired, [$"The contact type '{copy.Name}' needs a duration."]));
        }

        return (new LeadInteraction
        {
            CompanyId = lead.CompanyId,
            LeadId = lead.Id,
            ContactTypeCompanyId = copy.Id,
            Direction = (InteractionDirection)input.Direction,
            OccurredAt = input.OccurredAt?.ToUniversalTime() ?? DateTime.UtcNow,
            DurationMinutes = input.DurationMinutes,
            UserId = userId,
            Outcome = (InteractionOutcome)input.Outcome,
            Summary = input.Summary.Trim()
        }, null);
    }

    /// <summary>Validates the product and builds the line in the lead currency (rate already set on the lead).</summary>
    public async Task<(LeadProduct? Line, Response<T>? Error)> BuildLineAsync<T>(Lead lead, LeadProductInput input)
    {
        var product = await references.ProductAsync(lead.CompanyId, input.ProductId);
        if (product is null)
        {
            return (null, Response<T>.Error(LeadErrorCodes.InvalidProduct, ["The product is inactive or does not exist in the company."]));
        }

        var line = new LeadProduct
        {
            CompanyId = lead.CompanyId,
            LeadId = lead.Id,
            ProductId = product.Id,
            Quantity = input.Quantity,
            UnitPrice = input.UnitPrice ?? DefaultPrice(product, lead.ExchangeRate),
            DiscountPercent = input.DiscountPercent,
            Status = (LeadProductStatus)input.Status,
            OfferedInInteractionId = input.OfferedInInteractionId
        };
        line.Recalculate();
        return (line, null);
    }

    /// <summary>Reference price (base currency) in the lead currency: price ÷ rate, rounded to 2 decimals.</summary>
    public static decimal DefaultPrice(Product product, decimal rate) =>
        rate <= 0 ? product.ReferencePrice : Math.Round(product.ReferencePrice / rate, 2, MidpointRounding.AwayFromZero);

    /// <summary>Moves <c>LastInteractionAt</c> forward when the contact is newer.</summary>
    public static void Touch(Lead lead, DateTime occurredAt)
    {
        if (lead.LastInteractionAt is null || occurredAt > lead.LastInteractionAt)
        {
            lead.LastInteractionAt = occurredAt;
        }
    }
}
