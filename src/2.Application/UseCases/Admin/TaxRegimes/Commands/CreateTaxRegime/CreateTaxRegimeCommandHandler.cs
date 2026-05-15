using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Commands;

public sealed class CreateTaxRegimeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateTaxRegimeCommand, Response<TaxRegimeDto>>
{
    public async Task<Response<TaxRegimeDto>> Handle(CreateTaxRegimeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<TaxRegimeDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        var companyId = currentUserService.CompanyId;
        var company = await unitOfWork.GetRepository<Company>().GetAsync(companyId);
        if (company is null)
            return Response<TaxRegimeDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);

        var code = request.Code.Trim();
        var name = request.Name.Trim();
        var repo = unitOfWork.GetRepository<TaxRegime>();
        var all = await repo.GetAllAsync();

        if (all.Any(x => x.CompanyId == companyId && x.GcRecord == 0 && string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase)))
            return Response<TaxRegimeDto>.Error("TAX_REGIME_CODE_IN_USE", ["Another active tax regime already uses the same code in this company."]);

        if (all.Any(x => x.CompanyId == companyId && x.GcRecord == 0 && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Response<TaxRegimeDto>.Error("TAX_REGIME_NAME_IN_USE", ["Another active tax regime already uses the same name in this company."]);

        var entity = TaxRegime.Create(companyId, code, name);
        await repo.InsertAsync(entity);
        if (await unitOfWork.SaveChangesAsync(cancellationToken) <= 0)
            return Response<TaxRegimeDto>.Error("CREATE_FAILED", ["No records were affected while creating the tax regime."]);

        return new Response<TaxRegimeDto>
        {
            IsSuccess = true,
            Message = "Tax regime created successfully.",
            Data = Map(entity, company.Name)
        };
    }

    private static TaxRegimeDto Map(TaxRegime e, string companyName) => new()
    {
        Id = e.Id, CompanyId = e.CompanyId, CompanyName = companyName,
        Code = e.Code, Name = e.Name, IsActive = e.IsActive, CreatedAt = e.Created
    };
}
