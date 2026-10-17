namespace JOIN.Domain.Customers.Teams;

/// <summary>Which leads the holder of a management role sees (SPEC 60): own, the team's, or the company's.</summary>
public enum LeadScope
{
    Own = 1,
    Team = 2,
    Company = 3
}
