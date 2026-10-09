using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands;

/// <summary>
/// Handles <see cref="RestoreTicketCommand"/> (SPEC 41, Etapa 4): guards the tenant and the <c>SuperAdmin</c> role,
/// then delegates to <see cref="SoftDeleteRestorer"/>. The ticket is not restored while any catalog it references
/// (status, complexity, time unit, channel, person, project, area) or its precedent ticket is deleted. Restoring brings
/// back the attachments of the same cascade and appends a public <see cref="LogType.Restoration"/> log entry.
/// </summary>
public sealed class RestoreTicketCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer,
    TicketCascadeCoordinator cascadeCoordinator)
    : IRequestHandler<RestoreTicketCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreTicketCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var actorUserId))
        {
            return Response<Guid>.Error("USER_REQUIRED", ["The authenticated user identifier is required."]);
        }

        return await restorer.RestoreAsync<Ticket>(
            request.Id,
            request.CompanyId,
            isParentDeleted: IsAnyParentDeletedAsync,
            restoreCascade: async (ticket, stamp, _) =>
            {
                var cascadeError = await cascadeCoordinator.RestoreChildrenAsync(ticket.Id, stamp);
                if (cascadeError is null)
                {
                    ticket.AddLog(actorUserId, LogType.Restoration, "Ticket restored.");

                    // UpdateAsync marks the appended TicketLog as Added (BaseEntity assigns its Id up front,
                    // so change detection alone would treat it as an existing row).
                    await unitOfWork.GetRepository<Ticket>().UpdateAsync(ticket);
                }

                return cascadeError;
            },
            cancellationToken: cancellationToken);
    }

    private async Task<bool> IsAnyParentDeletedAsync(Ticket ticket, CancellationToken cancellationToken)
        => await restorer.IsParentDeletedAsync<TicketStatus>(ticket.TicketStatusId)
            || await restorer.IsParentDeletedAsync<TicketComplexity>(ticket.TicketComplexityId)
            || await restorer.IsParentDeletedAsync<TimeUnit>(ticket.TimeUnitId)
            || await restorer.IsParentDeletedAsync<CommunicationChannel>(ticket.ChannelId)
            || (ticket.PersonId is { } personId && await restorer.IsParentDeletedAsync<Person>(personId))
            || (ticket.ProjectId is { } projectId && await restorer.IsParentDeletedAsync<Project>(projectId))
            || (ticket.AreaId is { } areaId && await restorer.IsParentDeletedAsync<Area>(areaId))
            || (ticket.PrecedentTicketId is { } precedentId && await restorer.IsParentDeletedAsync<Ticket>(precedentId));
}
