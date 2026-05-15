using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

public sealed class UpdateTaxRegimeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateTaxRegimeCommand, Response<TaxRegimeDto>>
{
    public async Task<Response<TaxRegimeDto>> Handle(UpdateTaxRegimeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<TaxRegimeDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        var companyId = currentUserService.CompanyId;
        var company = await unitOfWork.GetRepository<Company>().GetAsync(companyId);
        if (company is null)
            return Response<TaxRegimeDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);

        var repo = unitOfWork.GetRepository<TaxRegime>();
        var entity = await repo.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
            return Response<TaxRegimeDto>.Error("TAX_REGIME_NOT_FOUND", ["Tax regime not found."]);

        var code = request.Code.Trim();
        var name = request.Name.Trim();
        var all = await repo.GetAllAsync();

        if (all.Any(x => x.Id != request.Id && x.CompanyId == companyId && x.GcRecord == 0 && string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
            return Response<TaxRegimeDto>.Error("TAX_REGIME_CODE_IN_USE", ["Another active tax regime already uses the same code in this company."]);

        if (all.Any(x => x.Id != request.Id && x.CompanyId == companyId && x.GcRecord == 0 && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Response<TaxRegimeDto>.Error("TAX_REGIME_NAME_IN_USE", ["Another active tax regime already uses the same name in this company."]);

        entity.Update(code, name);
        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) entity.Reactivate();
            else entity.Deactivate();
        }

        await repo.UpdateAsync(entity);
        if (await unitOfWork.SaveChangesAsync(cancellationToken) <= 0)
            return Response<TaxRegimeDto>.Error("UPDATE_FAILED", ["No records were affected while updating the tax regime."]);

        return new Response<TaxRegimeDto>
        {
            IsSuccess = true,
            Message = "Tax regime updated successfully.",
            Data = new TaxRegimeDto
            {
                Id = entity.Id, CompanyId = entity.CompanyId, CompanyName = company.Name,
                Code = entity.Code, Name = entity.Name, IsActive = entity.IsActive, CreatedAt = entity.Created
            }
        };
    }
}
