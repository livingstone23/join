using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Industries.Commands;

/// <summary>
/// Handles soft delete operations for tenant-scoped industries.
/// </summary>
public sealed class DeleteIndustryCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteIndustryCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(DeleteIndustryCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var industryRepository = _unitOfWork.GetRepository<Industry>();

        var entity = await industryRepository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error("INDUSTRY_NOT_FOUND", ["Industry not found."]);
        }

        var dependents = await new ActiveDependentsCheck(_unitOfWork)
            .CountAsync<PersonBusinessProfile>(p => p.GcRecord == 0 && p.IndustryId == request.Id, "business profiles");

        if (dependents.HasDependents)
        {
            return Response<Guid>.Error("INDUSTRY_IN_USE", dependents.Details);
        }

        entity.MarkAsDeleted();

        await industryRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the industry."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Industry deleted successfully.",
            Data = entity.Id
        };
    }
}
