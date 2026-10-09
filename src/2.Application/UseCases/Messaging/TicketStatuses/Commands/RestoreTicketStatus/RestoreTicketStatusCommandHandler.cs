using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatuses.Commands;

/// <summary>
/// Handles <see cref="RestoreTicketStatusCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the ticket status's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreTicketStatusCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreTicketStatusCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreTicketStatusCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<TicketStatus>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<TicketStatus>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && ((entity.IsInitial && x.IsInitial) || (entity.IsPaused && x.IsPaused) || (entity.IsFinal && x.IsFinal))),
            cancellationToken: cancellationToken);
    }
}
