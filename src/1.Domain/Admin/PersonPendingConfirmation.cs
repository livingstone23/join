using JOIN.Domain.Audit;
using JOIN.Domain.Calendars;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;

namespace JOIN.Domain.Admin;

/// <summary>
/// Record of a person created by a channel agent that still needs human review (SPEC 45). Lives in the
/// <c>Admin</c> schema alongside <see cref="Person"/>. Resolution closes the related review activity in
/// the calendar; discarding the pending does NOT deactivate the person (decision 2026-10-01).
/// </summary>
public class PersonPendingConfirmation : BaseTenantEntity
{
    /// <summary> Person created by the agent. </summary>
    public Guid PersonId { get; set; }

    /// <summary> Reason reported by the agent ("Quiere una cita para revisar su contrato"). </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary> Channel the request came through (WhatsApp, e-mail, etc.). </summary>
    public Guid CommunicationChannelId { get; set; }

    /// <summary> E.164 phone or e-mail the requester wrote from. </summary>
    public string ChannelIdentifier { get; set; } = string.Empty;

    /// <summary> When the agent registered the request. </summary>
    public DateTime RequestedUtc { get; set; }

    /// <summary> Current lifecycle state. </summary>
    public PersonPendingConfirmationStatus Status { get; set; } = PersonPendingConfirmationStatus.Pending;

    /// <summary> Review activity generated for the default user. Null until the activity is created. </summary>
    public Guid? ReviewEventId { get; set; }

    /// <summary> Customer linked to the person when <see cref="Status"/> becomes <c>LinkedToCustomer</c>. </summary>
    public Guid? CustomerId { get; set; }

    /// <summary> User who resolved the pending. </summary>
    public Guid? ResolvedByUserId { get; set; }

    public DateTime? ResolvedUtc { get; set; }

    /// <summary> Notes added when resolving (mandatory on Discarded). </summary>
    public string? ResolutionNotes { get; set; }

    // --- Navigation ---
    public virtual Person Person { get; set; } = null!;
    public virtual CommunicationChannel CommunicationChannel { get; set; } = null!;
    public virtual CalendarEvent? ReviewEvent { get; set; }
    public virtual Customer? Customer { get; set; }
    public virtual ApplicationUser? ResolvedByUser { get; set; }
}
