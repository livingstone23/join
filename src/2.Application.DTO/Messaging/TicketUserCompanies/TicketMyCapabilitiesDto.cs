namespace JOIN.Application.DTO.Messaging;

/// <summary>
/// Capability flags the current user holds on the active tenant's ticket roster
/// (SPEC 53). Drives the FE-18 "Finalizar" / "Reasignar" buttons, the FE-17
/// "Asignarme a mí" toggle in the create flow, and the FE-18 inactivity badge.
/// </summary>
/// <remarks>
/// All four flags are <c>false</c> when the caller is not on the roster of the
/// active company — the frontend reads <see cref="IsMember"/> first to decide
/// whether to render capability-gated actions at all.
/// </remarks>
public record TicketMyCapabilitiesDto
{
    /// <summary>
    /// Gets whether the caller has an active row in the active tenant's
    /// <c>TicketUserCompanies</c> roster (<c>GcRecord = 0</c>).
    /// </summary>
    public bool IsMember { get; init; }

    /// <summary>
    /// Gets whether the caller can redistribute (reassign) any ticket in the
    /// active tenant. SPEC 35 <c>ReassignTicket</c> requires this flag.
    /// </summary>
    public bool IsSuperAdminTicket { get; init; }

    /// <summary>
    /// Gets whether the caller can mark a ticket as finished.
    /// SPEC 35 <c>FinishTicket</c> requires this flag (or <see cref="IsSuperAdminTicket"/>).
    /// </summary>
    public bool CanFinishTicket { get; init; }

    /// <summary>
    /// Gets whether the caller is eligible to receive ticket assignments.
    /// SPEC 35 <c>CreateTicket</c> (initial assignment) and
    /// <c>ReassignTicket</c> (target) require this flag.
    /// </summary>
    public bool CanResolveTicket { get; init; }
}