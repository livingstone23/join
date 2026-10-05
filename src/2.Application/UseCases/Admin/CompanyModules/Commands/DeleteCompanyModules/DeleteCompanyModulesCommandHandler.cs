using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Handles soft delete operations for tenant-scoped company module assignments.
/// SPEC 38: only SuperAdmin can delete; the target CompanyId is resolved via TenantResolver
/// and scoped explicitly to defend against cross-tenant manipulation.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
/// <param name="currentUserService">Resolves the JWT-driven tenant and role context.</param>
public sealed class DeleteCompanyModulesCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<DeleteCompanyModulesCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// Performs a logical delete by marking the company module assignment as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted assignment identifier.</returns>
    public async Task<Response<Guid>> Handle(DeleteCompanyModulesCommand request, CancellationToken cancellationToken)
    {
        // SPEC 38: tenant scope = explicit request.CompanyId only for SuperAdmin, otherwise the token's tenant.
        var tenantId = TenantResolver.Resolve(_currentUserService, request.CompanyId);

        // SPEC 38: named repo with IgnoreQueryFilters + explicit tenant filter so SuperAdmin can
        // delete a CompanyModule belonging to any tenant via TenantResolver. Also closes the
        // pre-existing cross-tenant bug where Delete picked any CompanyModule by Id.
        var entity = await _unitOfWork.CompanyModules
            .GetByIdForUpdateAsync(request.Id, tenantId, cancellationToken);

        if (entity is null)
        {
            return Response<Guid>.Error("COMPANY_MODULE_NOT_FOUND", ["Company module not found."]);
        }

        entity.MarkAsDeleted();

        await _unitOfWork.GetRepository<CompanyModule>().UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the company module assignment."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Company module deleted successfully.",
            Data = entity.Id
        };
    }
}
