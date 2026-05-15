using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

public sealed class DeleteTaxRegimeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteTaxRegimeCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteTaxRegimeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        var companyId = currentUserService.CompanyId;
        var repo = unitOfWork.GetRepository<TaxRegime>();
        var entity = await repo.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
            return Response<Guid>.Error("TAX_REGIME_NOT_FOUND", ["Tax regime not found."]);

        var profiles = await unitOfWork.GetRepository<PersonBusinessProfile>().GetAllAsync();
        if (profiles.Any(p => p.CompanyId == companyId && p.GcRecord == 0 && p.TaxRegimeId == request.Id))
            return Response<Guid>.Error("TAX_REGIME_IN_USE", ["The tax regime is currently assigned to one or more business profiles and cannot be deleted."]);

        entity.MarkAsDeleted();
        await repo.UpdateAsync(entity);
        if (await unitOfWork.SaveChangesAsync(cancellationToken) <= 0)
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the tax regime."]);

        return new Response<Guid> { IsSuccess = true, Message = "Tax regime deleted successfully.", Data = entity.Id };
    }
}
