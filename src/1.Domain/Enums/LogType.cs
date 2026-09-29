namespace JOIN.Domain.Enums;

/// <summary>
/// Defines the supported ticket audit log event types.
/// </summary>
public enum LogType
{
    /// <summary>
    /// Represents the initial creation of the ticket.
    /// </summary>
    Creation = 0,

    /// <summary>
    /// Represents a change in the workflow status of the ticket.
    /// </summary>
    StatusChange = 1,

    /// <summary>
    /// Represents a private note or internal audit note.
    /// </summary>
    InternalNote = 2,

    /// <summary>
    /// Represents a customer-facing or externally visible note.
    /// </summary>
    ExternalNote = 3,

    /// <summary>
    /// Represents a reassignment of the ticket to a different user.
    /// </summary>
    Reassignment = 4,

    /// <summary>
    /// Represents the finalization of the ticket (transition to a status with <c>IsFinal = true</c>).
    /// Distinct from <see cref="StatusChange"/> so a report can filter "when was this closed"
    /// without having to inspect whether the surrounding <c>StatusChange</c> landed on a final status.
    /// </summary>
    Finalization = 5,

    /// <summary>
    /// Backward-compatible alias for <see cref="StatusChange"/>.
    /// </summary>
    ChangeStatus = StatusChange,

    /// <summary>
    /// Backward-compatible alias for <see cref="Reassignment"/>.
    /// </summary>
    Reasignment = Reassignment
}
