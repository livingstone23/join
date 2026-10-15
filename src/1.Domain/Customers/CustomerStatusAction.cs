namespace JOIN.Domain.Customers;

/// <summary>
/// What happened to a customer's status, as logged in <see cref="CustomerStatusHistory"/> (SPEC 55).
/// </summary>
public enum CustomerStatusAction
{
    /// <summary>The customer row was created (active).</summary>
    Created = 1,

    /// <summary>An inactive customer was reactivated.</summary>
    Activated = 2,

    /// <summary>An event confirmed an already active customer; nothing changed on the row.</summary>
    Confirmed = 3,

    /// <summary>An active customer was deactivated.</summary>
    Deactivated = 4
}
