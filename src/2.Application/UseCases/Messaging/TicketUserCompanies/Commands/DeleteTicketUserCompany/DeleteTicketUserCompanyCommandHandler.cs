using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.DeleteTicketUserCompany;

/// <summary>
/// Handles soft delete of a <see cref="TicketUserCompany"/> row, blocking the operation
/// when the row is the last active <c>IsSuperAdminTicket</c> of the tenant.
/// </summary>
public sealed class DeleteTicketUserCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    TicketUserCompanySuperAdminCoordinator coordinator)
    : IRequestHandler<DeleteTicketUserCompanyCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly TicketUserCompanySuperAdminCoordinator _coordinator = coordinator;

    public async Task<Response<Guid>> Handle(
        DeleteTicketUserCompanyCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketUserCompany>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null || entity.CompanyId != tenantId)
        {
            return Response<Guid>.Error("TICKET_USER_COMPANY_NOT_FOUND", ["Ticket roster entry not found for the current company."]);
        }

        if (entity.IsSuperAdminTicket)
        {
            var anotherExists = await _coordinator.AnotherActiveSuperAdminExistsAsync(tenantId, entity.Id, cancellationToken);
            if (!anotherExists)
            {
                return Response<Guid>.Error("LAST_SUPERADMIN_TICKET", ["At least one active IsSuperAdminTicket must remain for the company."]);
            }
        }

        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("TICKET_USER_COMPANY_NOT_FOUND", ["No records were affected while deleting the ticket roster entry."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Ticket roster entry deleted successfully.",
            Data = entity.Id
        };
    }
}
