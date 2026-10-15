namespace JOIN.Domain.Customers;

/// <summary>
/// Event that caused a change in a customer's status (SPEC 55). Each new event adds its value in the spec that connects it.
/// </summary>
public enum CustomerStatusSource
{
    /// <summary>A user acted from the API (POST /Customers, activate, deactivate).</summary>
    Manual = 1,

    /// <summary>The database seeder.</summary>
    Seed = 2,

    /// <summary>The SPEC 55 migration, for customers that existed before the history.</summary>
    Migration = 3,

    /// <summary>The customer's person was deactivated (rule 7).</summary>
    PersonDeactivated = 4
}
