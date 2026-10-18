using JOIN.Domain.Audit;

namespace JOIN.Domain.Customers.Leads;

/// <summary>
/// Global master of contact types (table <c>Customer.ContactTypes</c>, SPEC 59): call, online meeting, visit…
/// Seeded by the system; each company activates, renames and orders its copy (<see cref="ContactTypeCompany"/>).
/// </summary>
public class ContactType : BaseAuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    /// <summary>Communication channel matched by <c>CommunicationChannel.Code</c> (SPEC 00), if any.</summary>
    public string? CommunicationChannelCode { get; set; }
    public bool SchedulesCalendarEvent { get; set; }
    public bool AsksDuration { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
