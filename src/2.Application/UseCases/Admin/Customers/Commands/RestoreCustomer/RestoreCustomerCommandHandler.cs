using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Admin;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Handles <see cref="RestoreCustomerCommand"/>: guards the tenant and the <c>SuperAdmin</c> role, then
/// delegates to <see cref="SoftDeleteRestorer"/> with the customer's parent and active-duplicate checks (SPEC 41).
/// </summary>
public sealed class RestoreCustomerCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreCustomerCommand, Response<Guid>>
{
    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(RestoreCustomerCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Customer>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) =>
                await restorer.IsParentDeletedAsync<Person>(entity.PersonId)
                || await restorer.IsParentDeletedAsync<ApplicationUser>(entity.UserId),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Customer>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && (x.CustomerCode == entity.CustomerCode || (x.PersonId == entity.PersonId && x.UserId == entity.UserId))),
            cancellationToken: cancellationToken);
    }
}
