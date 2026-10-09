// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Audit;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;

namespace JOIN.Application.UseCases.Messaging.Tickets;

/// <summary>
/// Soft-delete cascade of a <see cref="Ticket"/> (SPEC 41, Etapa 4, decision 2026-10-08).
/// <list type="bullet">
/// <item><b>Composition</b>: the ticket's attachments (<see cref="TicketDocument"/>) are deleted with the ticket,
/// with the same <c>GcRecord</c> stamp, and restored with it.</item>
/// <item><b>References</b>: active follow-up tickets (<c>PrecedentTicketId</c>) block the delete.</item>
/// </list>
/// <see cref="TicketLog"/> and <see cref="TicketNotification"/> are history and are never touched.
/// </summary>
public sealed class TicketCascadeCoordinator(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Returns one detail per active reference type that blocks deleting the ticket, e.g. <c>"Active follow-up tickets: 2"</c>.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetBlockingReferencesAsync(Guid ticketId)
    {
        var references = new ActiveDependentsCheck(unitOfWork);
        await references.CountAsync<Ticket>(t => t.GcRecord == 0 && t.PrecedentTicketId == ticketId, "follow-up tickets");
        return references.Details;
    }

    /// <summary>
    /// Marks the active attachments of the ticket as deleted with the same stamp as the ticket.
    /// </summary>
    public async Task MarkChildrenAsDeletedAsync(Guid ticketId, DateTime deletedAtUtc)
    {
        var repository = unitOfWork.GetRepository<TicketDocument>();
        foreach (var document in await repository.GetAllIncludingDeletedAsync(d => d.GcRecord == 0 && d.TicketId == ticketId))
        {
            document.MarkAsDeleted(deletedAtUtc);
            await repository.UpdateAsync(document);
        }
    }

    /// <summary>
    /// Restores the attachments deleted in the same cascade as the ticket (<c>GcRecord == stamp</c>). Attachments
    /// deleted one by one on another day stay deleted. No quota check: they were active together with the ticket
    /// and a deleted ticket cannot receive new uploads.
    /// </summary>
    /// <returns>Always <c>null</c>; the signature matches the <c>restoreCascade</c> hook of <see cref="SoftDeleteRestorer"/>.</returns>
    public async Task<Response<Guid>?> RestoreChildrenAsync(Guid ticketId, int stamp)
    {
        var repository = unitOfWork.GetRepository<TicketDocument>();
        foreach (var document in await repository.GetAllIncludingDeletedAsync(d => d.TicketId == ticketId && d.GcRecord == stamp))
        {
            document.Restore();
            await repository.UpdateAsync(document);
        }

        return null;
    }
}
