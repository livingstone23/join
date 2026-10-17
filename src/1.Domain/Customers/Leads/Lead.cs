using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Leads;

/// <summary>
/// A prospect on a person (table <c>Customer.Leads</c>, SPEC 61). The customer row is only created when the lead is won
/// (SPEC 63). The lead keeps its team (for reports), its currency, the rate of the day and the amount in base currency.
/// </summary>
public class Lead : BaseTenantEntity
{
    public string Code { get; set; } = string.Empty;
    public Guid PersonId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public Guid OwnerUserId { get; set; }
    public Guid? SalesTeamId { get; set; }
    public Guid LeadStageId { get; set; }
    public Guid LeadSourceId { get; set; }
    public Guid RelationshipTypeId { get; set; }
    public Guid? ReferredByPersonId { get; set; }
    public LeadStatus Status { get; set; } = LeadStatus.Open;
    /// <summary>Reason of the last close or inactivation.</summary>
    public Guid? LeadReasonId { get; set; }
    public string? StatusNotes { get; set; }
    public Guid CurrencyId { get; set; }
    /// <summary>1 lead currency = <see cref="ExchangeRate"/> base, fixed when the amounts were entered.</summary>
    public decimal ExchangeRate { get; set; } = 1m;
    public DateOnly ExchangeRateDate { get; set; }
    public decimal EstimatedValue { get; set; }
    public decimal EstimatedValueBase { get; set; }
    public DateOnly? EstimatedCloseDate { get; set; }
    public DateTime? InactivatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    /// <summary>Filled by SPEC 63 (win or migration).</summary>
    public Guid? CustomerId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? AreaId { get; set; }
    /// <summary>Denormalized for the inbox order.</summary>
    public DateTime? LastInteractionAt { get; set; }
}

/// <summary>A stage change of a lead (table <c>Customer.LeadStageHistory</c>); the first row has no origin.</summary>
public class LeadStageHistory : BaseTenantEntity
{
    public Guid LeadId { get; set; }
    public Guid? FromStageId { get; set; }
    public Guid ToStageId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>An owner or team change of a lead (table <c>Customer.LeadOwnershipHistory</c>).</summary>
public class LeadOwnershipHistory : BaseTenantEntity
{
    public Guid LeadId { get; set; }
    public Guid? FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public Guid? FromTeamId { get; set; }
    public Guid? ToTeamId { get; set; }
    public LeadOwnershipCause Cause { get; set; }
    public Guid UserId { get; set; }
    public DateTime At { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A status change of a lead (table <c>Customer.LeadStatusHistory</c>).</summary>
public class LeadStatusHistory : BaseTenantEntity
{
    public Guid LeadId { get; set; }
    public LeadStatus? FromStatus { get; set; }
    public LeadStatus ToStatus { get; set; }
    public Guid? LeadReasonId { get; set; }
    public Guid UserId { get; set; }
    public DateTime At { get; set; }
    public string? Notes { get; set; }
}

/// <summary>A contact with the person of a lead (table <c>Customer.LeadInteractions</c>).</summary>
public class LeadInteraction : BaseTenantEntity
{
    public Guid LeadId { get; set; }
    public Guid ContactTypeCompanyId { get; set; }
    public InteractionDirection Direction { get; set; } = InteractionDirection.Outbound;
    public DateTime OccurredAt { get; set; }
    public int? DurationMinutes { get; set; }
    public Guid UserId { get; set; }
    public InteractionOutcome Outcome { get; set; } = InteractionOutcome.Other;
    public string Summary { get; set; } = string.Empty;
}

/// <summary>A comment on a lead, optionally on one of its contacts (table <c>Customer.LeadComments</c>).</summary>
public class LeadComment : BaseTenantEntity
{
    public Guid LeadId { get; set; }
    public Guid? LeadInteractionId { get; set; }
    public string Body { get; set; } = string.Empty;
    public Guid UserId { get; set; }
}

/// <summary>A product offered in a lead, in the lead currency (table <c>Customer.LeadProducts</c>).</summary>
public class LeadProduct : BaseTenantEntity
{
    public Guid LeadId { get; set; }
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; } = 1m;
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal LineTotal { get; set; }
    public LeadProductStatus Status { get; set; } = LeadProductStatus.Offered;
    public Guid? OfferedInInteractionId { get; set; }

    /// <summary>Quantity × price × (1 − discount), rounded to 2 decimals (midpoint away from zero).</summary>
    public static decimal Total(decimal quantity, decimal unitPrice, decimal discountPercent) =>
        Math.Round(quantity * unitPrice * (1m - (discountPercent / 100m)), 2, MidpointRounding.AwayFromZero);

    public void Recalculate() => LineTotal = Total(Quantity, UnitPrice, DiscountPercent);
}
