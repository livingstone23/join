using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.RestoreTicketStatusTransition;

/// <summary>
/// Handles <see cref="RestoreTicketStatusTransitionCommand"/> (SPEC 41, Etapa 4): guards the tenant and the <c>SuperAdmin</c> role,
/// then delegates to <see cref="SoftDeleteRestorer"/>. The transition is not restored while its origin or destination status is deleted, nor when the same active rule already exists (filtered unique index on <c>(CompanyId, FromStatusId, ToStatusId)</c>).
/// </summary>
public sealed class RestoreTicketStatusTransitionCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreTicketStatusTransitionCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreTicketStatusTransitionCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<TicketStatusTransition>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<TicketStatus>(entity.FromStatusId)
                || await restorer.IsParentDeletedAsync<TicketStatus>(entity.ToStatusId),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<TicketStatusTransition>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId
                    && x.FromStatusId == entity.FromStatusId && x.ToStatusId == entity.ToStatusId),
            cancellationToken: cancellationToken);
    }
}
