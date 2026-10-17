namespace JOIN.Domain.Customers.Leads;

/// <summary>State of a lead (SPEC 61). SPEC 63 sets <see cref="Won"/> and <see cref="Lost"/>.</summary>
public enum LeadStatus
{
    Open = 1,
    Inactive = 2,
    Won = 3,
    Lost = 4
}

/// <summary>Why the owner of a lead changed (SPEC 61).</summary>
public enum LeadOwnershipCause
{
    Created = 1,
    Reassigned = 2,
    Reactivated = 3
}

/// <summary>Who started a contact (SPEC 61).</summary>
public enum InteractionDirection
{
    Outbound = 1,
    Inbound = 2
}

/// <summary>Result of a contact (SPEC 61).</summary>
public enum InteractionOutcome
{
    Interested = 1,
    ProposalRequested = 2,
    NoAnswer = 3,
    NotInterested = 4,
    Other = 99
}

/// <summary>State of an offered product line (SPEC 61). Rejected lines do not add to the estimated value.</summary>
public enum LeadProductStatus
{
    Offered = 1,
    Accepted = 2,
    Rejected = 3
}
