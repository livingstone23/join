namespace JOIN.Domain.Customers.Leads;

/// <summary>Kind of a lead stage (SPEC 59): exactly one Initial, one Won and one Lost per company.</summary>
public enum LeadStageKind
{
    Initial = 1,
    Open = 2,
    Won = 3,
    Lost = 4
}

/// <summary>When a lead reason is used (SPEC 59): closing as won, as lost, or leaving the lead inactive.</summary>
public enum LeadReasonUsage
{
    Won = 1,
    Lost = 2,
    Inactive = 3
}

/// <summary>What winning a lead does for its relationship type (SPEC 59): create or confirm the customer, or nothing.</summary>
public enum RelationshipOnWin
{
    None = 0,
    Customer = 1
}

/// <summary>What happens when a lead is created for a person with another open lead (SPEC 59).</summary>
public enum DuplicateOpenLeadPolicy
{
    Warn = 1,
    Block = 2,
    Allow = 3
}
