using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;

/// <summary>
/// <c>POST /PersonPendingConfirmations/{id}/resolve</c> (SPEC 45): closes a pending as <c>Confirmed</c> (data reviewed)
/// or <c>Discarded</c> (notes required). Only from <c>Pending</c>. Discarding does not change the person.
/// </summary>
public sealed record ResolvePersonPendingConfirmationCommand(
    Guid PendingConfirmationId,
    PersonPendingConfirmationStatus Status,
    string? Notes) : ITransactionalCommand<Response<ResolvePersonPendingConfirmationDto>>;
