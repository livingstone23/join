using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

public sealed class DeleteIncomeRangeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteIncomeRangeCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteIncomeRangeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        var companyId = currentUserService.CompanyId;
        var repo = unitOfWork.GetRepository<IncomeRange>();
        var entity = await repo.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
            return Response<Guid>.Error("INCOME_RANGE_NOT_FOUND", ["Income range not found."]);

        var profiles = await unitOfWork.GetRepository<PersonFinancialProfile>().GetAllAsync();
        if (profiles.Any(p => p.CompanyId == companyId && p.GcRecord == 0 && p.IncomeRangeId == request.Id))
            return Response<Guid>.Error("INCOME_RANGE_IN_USE", ["The income range is currently assigned to one or more financial profiles and cannot be deleted."]);

        entity.MarkAsDeleted();
        await repo.UpdateAsync(entity);
        if (await unitOfWork.SaveChangesAsync(cancellationToken) <= 0)
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the income range."]);

        return new Response<Guid> { IsSuccess = true, Message = "Income range deleted successfully.", Data = entity.Id };
    }
}
