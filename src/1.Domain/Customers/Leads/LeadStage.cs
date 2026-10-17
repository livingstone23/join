using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Leads;

/// <summary>
/// Stage of the company's lead funnel (table <c>Customer.LeadStages</c>, SPEC 59). Exactly one live Initial, Won and
/// Lost stage per company; Won and Lost are system stages (renamed, never deleted or deactivated).
/// </summary>
public class LeadStage : BaseTenantEntity, ICommercialCatalogItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>Win probability 0–100, used for the weighted pipeline value.</summary>
    public int Probability { get; set; }
    public int SortOrder { get; set; }
    public LeadStageKind Kind { get; set; } = LeadStageKind.Open;
    public bool IsActive { get; set; } = true;

    /// <summary>Won and Lost are system stages.</summary>
    public bool IsSystem => Kind is LeadStageKind.Won or LeadStageKind.Lost;

    /// <summary>Initial, Won and Lost exist exactly once per company.</summary>
    public bool IsUniqueKind => Kind != LeadStageKind.Open;
}
