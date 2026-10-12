using System;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;

/// <summary>
/// Closes a pending as <c>Confirmed</c> or <c>Discarded</c> (SPEC 45 F5). Notes are required on
/// <c>Discarded</c>. Resolving from a non-<c>Pending</c> state returns
/// <c>PERSON_PENDING_ALREADY_RESOLVED</c>.
/// </summary>
public sealed record ResolvePersonPendingConfirmationCommand(
    Guid CompanyId,
    Guid PendingConfirmationId,
    PersonPendingConfirmationStatus Status,
    string? Notes) : IRequest<Response<ResolvePersonPendingConfirmationDto>>, ITransactionalCommand<ResolvePersonPendingConfirmationDto>;
