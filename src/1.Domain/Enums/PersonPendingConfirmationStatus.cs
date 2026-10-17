namespace JOIN.Domain.Enums;

/// <summary>
/// Lifecycle of a <c>PersonPendingConfirmation</c> (SPEC 45). The numbering is stable: persisted in
/// <c>PersonPendingConfirmation.Status</c> and used in API responses. New states are appended without
/// reordering, and old states are never reused.
/// </summary>
public enum PersonPendingConfirmationStatus
{
    /// <summary> Default state for a freshly created pending confirmation. </summary>
    Pending = 1,

    /// <summary> A user reviewed the data and kept the person as-is. </summary>
    Confirmed = 2,

    /// <summary> The pending was closed by linking the person to an existing customer. </summary>
    LinkedToCustomer = 3,

    /// <summary> The pending was discarded (spam, error, duplicate). </summary>
    Discarded = 4,

    /// <summary> Converted into an open lead (SPEC 65); <c>LeadId</c> points to it. </summary>
    ConvertedToLead = 5
}
