using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.RestoreTicketUserCompany;

/// <summary>
/// Handles <see cref="RestoreTicketUserCompanyCommand"/> (SPEC 41, Etapa 4): guards the tenant and the <c>SuperAdmin</c> role,
/// then delegates to <see cref="SoftDeleteRestorer"/>. The entry is not restored while its user is deleted or is no longer an active member of the company (<c>UserCompany</c>, same rule as <c>USER_NOT_IN_TENANT</c> on create), nor when the user already has an active entry in the company (filtered unique index on <c>(UserId, CompanyId)</c>).
/// </summary>
public sealed class RestoreTicketUserCompanyCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreTicketUserCompanyCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreTicketUserCompanyCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<TicketUserCompany>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<ApplicationUser>(entity.UserId)
                || !await restorer.AnyAsync<UserCompany>(
                    uc => uc.GcRecord == 0 && uc.UserId == entity.UserId && uc.CompanyId == entity.CompanyId),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<TicketUserCompany>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.UserId == entity.UserId),
            cancellationToken: cancellationToken);
    }
}
