using System;
using System.Collections.Generic;
using JOIN.Application.DTO.Calendars;

namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Flat row of the <c>PersonPendingConfirmations</c> inbox (SPEC 45 F5). The numeric <c>Status</c> matches
/// <c>JOIN.Domain.Enums.PersonPendingConfirmationStatus</c>. <c>ReviewEventStatus</c> is the global status
/// code of the review activity ("PENDING", "COMPLETED", "CANCELLED", ...) when it exists.
/// </summary>
public record PersonPendingConfirmationDto(
    Guid Id,
    Guid PersonId,
    string PersonFullName,
    string? Identification,
    Guid ChannelId,
    string ChannelName,
    string ChannelIdentifier,
    string Reason,
    DateTime RequestedUtc,
    int Status,
    Guid? ReviewEventId,
    string? ReviewEventStatus,
    Guid? CustomerId,
    Guid? ResolvedByUserId,
    DateTime? ResolvedUtc,
    string? ResolutionNotes);

/// <summary>
/// Detail of a single pending, with the person's active contacts and the activities that target them (SPEC 45 F5).
/// </summary>
public record PersonPendingConfirmationDetailDto(
    PersonPendingConfirmationDto Header,
    IReadOnlyList<PersonContactDto> Contacts,
    IReadOnlyList<IntakeEventDto> Events);

/// <summary> Outcome of <c>POST /{id}/resolve</c>. </summary>
public record ResolvePersonPendingConfirmationDto(Guid Id, int Status, Guid? ReviewEventId);

/// <summary> Outcome of <c>POST /{id}/link-customer</c>. </summary>
public record LinkCustomerToPersonPendingConfirmationDto(Guid Id, Guid CustomerId);
