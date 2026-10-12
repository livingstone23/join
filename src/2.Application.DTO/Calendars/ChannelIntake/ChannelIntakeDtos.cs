namespace JOIN.Application.DTO.Calendars;

/// <summary>
/// One company the channel agent can operate in (SPEC 45, <c>GET /companies</c>): the agent holds an active
/// <c>UserCompany</c> there, the Calendar module is enabled and a <c>CalendarCompany</c> exists.
/// </summary>
public record IntakeCompanyDto(Guid CompanyId, string Name, string TaxId);

/// <summary>
/// One identification type the agent may ask for (SPEC 45, <c>GET /identification-types</c>): active global
/// rows, no tenant filter.
/// </summary>
public record IntakeIdentificationTypeDto(Guid Id, string Name);

/// <summary>
/// Result of looking up a person from a channel identifier or an identification pair (SPEC 45,
/// <c>GET /persons/lookup</c>). Only id and full name are returned — never contact data.
/// </summary>
public record IntakePersonLookupDto(
    bool Found,
    Guid? PersonId,
    string? FullName,
    bool HasPendingConfirmation,
    bool Ambiguous = false);

/// <summary>
/// Result of looking up a user by exact e-mail (SPEC 45, <c>GET /users/lookup</c>). When <see cref="Found"/> is
/// <c>false</c>, <see cref="UserId"/> and <see cref="FullName"/> are <c>null</c>; the absence of a match is NOT an
/// error and falls through to the default user of the company.
/// </summary>
public record IntakeUserLookupDto(bool Found, Guid? UserId, string? FullName);

/// <summary>
/// A free slot offered to the agent in UTC, in blocks of the activity type's default duration.
/// </summary>
public record IntakeAvailabilitySlotDto(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// Availability response of <c>GET /availability</c> (SPEC 45). <see cref="TimeZone"/> is the IANA zone of the
/// company so the agent can format the slots for the requester.
/// </summary>
public record IntakeAvailabilityDto(string TimeZone, IReadOnlyList<IntakeAvailabilitySlotDto> Slots);

/// <summary>
/// Lightweight status reference for the person events list (SPEC 45, <c>GET /events</c>).
/// </summary>
public record IntakeStatusDto(Guid Id, string Code, string Name, string Color);

/// <summary>
/// One activity of the requester in a range (SPEC 45, <c>GET /events</c>): non-cancelled, in chronological
/// order. <see cref="EventId"/> is the activity (or master for series) and <see cref="OccurrenceStartUtc"/>
/// identifies the occurrence to target on <c>PUT /reschedule</c> or <c>POST /cancel</c>.
/// </summary>
public record IntakeEventDto(
    Guid EventId,
    DateTime OccurrenceStartUtc,
    string Title,
    DateTime StartUtc,
    DateTime EndUtc,
    IntakeStatusDto Status,
    string OwnerFullName);

/// <summary>
/// Result of <c>POST /persons</c> (SPEC 45 F4). When <see cref="Created"/> is <c>false</c> the person already
/// existed by identification and no rows were added; <see cref="HasPendingConfirmation"/> reports whether an
/// open <c>PersonPendingConfirmation</c> exists for the same person.
/// </summary>
public record RegisterIntakePersonDto(
    Guid PersonId,
    bool Created,
    Guid? PendingConfirmationId = null,
    Guid? ReviewEventId = null,
    bool HasPendingConfirmation = false);

/// <summary>
/// Result of <c>POST /events</c> (SPEC 45 F4). <see cref="IsDuplicate"/> is <c>true</c> when the
/// (OriginChannelId, OriginReference) pair already pointed at an event, including the unique-index race;
/// <see cref="RoutedToDefaultUser"/> is <c>true</c> when no matching <c>targetUserEmail</c> was supplied or
/// found.
/// </summary>
public record CreateIntakeEventDto(
    Guid EventId,
    bool IsDuplicate,
    IntakeStatusDto Status,
    string OwnerFullName,
    DateTime StartUtc,
    DateTime EndUtc,
    string TimeZone,
    bool RoutedToDefaultUser);

/// <summary> Result of <c>PUT /events/{id}/reschedule</c> (SPEC 45 F4). </summary>
public record RescheduleIntakeEventDto(
    Guid EventId,
    DateTime StartUtc,
    DateTime EndUtc,
    IntakeStatusDto Status);

/// <summary> Result of <c>POST /events/{id}/cancel</c> (SPEC 45 F4). </summary>
public record CancelIntakeEventDto(
    Guid EventId,
    IntakeStatusDto Status);