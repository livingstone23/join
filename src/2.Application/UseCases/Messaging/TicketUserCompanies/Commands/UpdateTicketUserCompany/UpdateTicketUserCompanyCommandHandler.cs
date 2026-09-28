using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;

/// <summary>
/// Handles <see cref="UpdateTicketUserCompanyCommand"/>: enforces the
/// "at least one active IsSuperAdminTicket" invariant via
/// <see cref="TicketUserCompanySuperAdminCoordinator"/> before applying the new flags.
/// </summary>
public sealed class UpdateTicketUserCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    TicketUserCompanySuperAdminCoordinator coordinator)
    : IRequestHandler<UpdateTicketUserCompanyCommand, Response<TicketUserCompanyDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly TicketUserCompanySuperAdminCoordinator _coordinator = coordinator;

    public async Task<Response<TicketUserCompanyDto>> Handle(
        UpdateTicketUserCompanyCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<TicketUserCompanyDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketUserCompany>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null || entity.CompanyId != tenantId)
        {
            return Response<TicketUserCompanyDto>.Error("TICKET_USER_COMPANY_NOT_FOUND", ["Ticket roster entry not found for the current company."]);
        }

        if (entity.IsSuperAdminTicket && !request.IsSuperAdminTicket)
        {
            var anotherExists = await _coordinator.AnotherActiveSuperAdminExistsAsync(tenantId, entity.Id, cancellationToken);
            if (!anotherExists)
            {
                return Response<TicketUserCompanyDto>.Error("LAST_SUPERADMIN_TICKET", ["At least one active IsSuperAdminTicket must remain for the company."]);
            }
        }

        entity.IsSuperAdminTicket = request.IsSuperAdminTicket;
        entity.CanFinishTicket = request.CanFinishTicket;
        entity.CanResolveTicket = request.CanResolveTicket;
        entity.LastModified = DateTime.UtcNow;
        entity.LastModifiedBy = _currentUserService.UserId;

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketUserCompanyDto>.Error("UPDATE_FAILED", ["No records were affected while updating the ticket roster entry."]);
        }

        var user = await _unitOfWork.GetRepository<ApplicationUser>().GetAsync(entity.UserId);
        var company = await _unitOfWork.GetRepository<JOIN.Domain.Common.Company>().GetAsync(tenantId);

        return new Response<TicketUserCompanyDto>
        {
            IsSuccess = true,
            Message = "Ticket roster entry updated successfully.",
            Data = new TicketUserCompanyDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company?.Name,
                UserId = entity.UserId,
                UserName = user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim(),
                UserEmail = user?.Email,
                IsSuperAdminTicket = entity.IsSuperAdminTicket,
                CanFinishTicket = entity.CanFinishTicket,
                CanResolveTicket = entity.CanResolveTicket,
                CreatedAt = entity.Created
            }
        };
    }
}
