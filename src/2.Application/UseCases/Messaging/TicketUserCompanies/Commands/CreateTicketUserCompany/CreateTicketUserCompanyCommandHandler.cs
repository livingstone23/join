using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;

/// <summary>
/// Handles <see cref="CreateTicketUserCompanyCommand"/>: validates the user belongs to the
/// tenant, ensures no duplicate active row exists, then inserts and maps the DTO.
/// </summary>
public sealed class CreateTicketUserCompanyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateTicketUserCompanyCommand, Response<TicketUserCompanyDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    public async Task<Response<TicketUserCompanyDto>> Handle(
        CreateTicketUserCompanyCommand request,
        CancellationToken cancellationToken)
    {
        var tenantId = _currentUserService.CompanyId;
        if (tenantId == Guid.Empty)
        {
            return Response<TicketUserCompanyDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var userRepository = _unitOfWork.GetRepository<ApplicationUser>();
        var user = await userRepository.GetAsync(request.UserId);
        if (user is null)
        {
            return Response<TicketUserCompanyDto>.Error("USER_NOT_FOUND", ["The provided user does not exist."]);
        }

        var userCompanyRepository = _unitOfWork.GetRepository<UserCompany>();
        var userCompanies = await userCompanyRepository.GetAllAsync();
        var userHasTenant = userCompanies.Any(link =>
            link.GcRecord == 0
            && link.CompanyId == tenantId
            && link.UserId == request.UserId);

        if (!userHasTenant)
        {
            return Response<TicketUserCompanyDto>.Error("USER_NOT_IN_TENANT", ["The user is not an active member of the current company."]);
        }

        var ticketUserCompanyRepository = _unitOfWork.GetRepository<TicketUserCompany>();
        var existing = await ticketUserCompanyRepository.GetAllAsync();
        var duplicate = existing.Any(x =>
            x.GcRecord == 0
            && x.CompanyId == tenantId
            && x.UserId == request.UserId);

        if (duplicate)
        {
            return Response<TicketUserCompanyDto>.Error("TICKET_USER_COMPANY_DUPLICATE", ["The user already has an active entry in this company's ticket roster."]);
        }

        var entity = new TicketUserCompany
        {
            CompanyId = tenantId,
            UserId = request.UserId,
            IsSuperAdminTicket = request.IsSuperAdminTicket,
            CanFinishTicket = request.CanFinishTicket,
            CanResolveTicket = request.CanResolveTicket
        };

        await ticketUserCompanyRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketUserCompanyDto>.Error("CREATE_FAILED", ["No records were affected while creating the ticket roster entry."]);
        }

        var company = await _unitOfWork.GetRepository<JOIN.Domain.Common.Company>().GetAsync(tenantId);

        return new Response<TicketUserCompanyDto>
        {
            IsSuccess = true,
            Message = "Ticket roster entry created successfully.",
            Data = new TicketUserCompanyDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company?.Name,
                UserId = entity.UserId,
                UserName = $"{user.FirstName} {user.LastName}".Trim(),
                UserEmail = user.Email,
                IsSuperAdminTicket = entity.IsSuperAdminTicket,
                CanFinishTicket = entity.CanFinishTicket,
                CanResolveTicket = entity.CanResolveTicket,
                CreatedAt = entity.Created
            }
        };
    }
}
