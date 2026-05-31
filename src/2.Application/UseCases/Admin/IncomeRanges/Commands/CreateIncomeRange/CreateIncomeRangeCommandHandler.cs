using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

public sealed class CreateIncomeRangeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateIncomeRangeCommand, Response<IncomeRangeDto>>
{
    public async Task<Response<IncomeRangeDto>> Handle(CreateIncomeRangeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<IncomeRangeDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        var companyId = currentUserService.CompanyId;
        var company = await unitOfWork.GetRepository<Company>().GetAsync(companyId);
        if (company is null)
            return Response<IncomeRangeDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);

        var displayName = request.DisplayName.Trim();
        var currencyCode = request.CurrencyCode.Trim();
        var repo = unitOfWork.GetRepository<IncomeRange>();
        var all = await repo.GetAllAsync();

        if (all.Any(x => x.CompanyId == companyId && x.GcRecord == 0 && string.Equals(x.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
            return Response<IncomeRangeDto>.Error("INCOME_RANGE_DISPLAY_NAME_IN_USE", ["Another active income range already uses the same display name in this company."]);

        if (all.Any(x => x.CompanyId == companyId && x.GcRecord == 0 && x.DisplayOrder == request.DisplayOrder))
            return Response<IncomeRangeDto>.Error("INCOME_RANGE_DISPLAY_ORDER_IN_USE", ["Another active income range already uses the same display order in this company."]);

        IncomeRange entity;
        try
        {
            entity = IncomeRange.Create(companyId, displayName, request.MinimumValue, request.MaximumValue, currencyCode, request.DisplayOrder);
        }
        catch (ArgumentException ex)
        {
            return Response<IncomeRangeDto>.Error("INVALID_INCOME_RANGE_VALUES", [ex.Message]);
        }

        await repo.InsertAsync(entity);
        if (await unitOfWork.SaveChangesAsync(cancellationToken) <= 0)
            return Response<IncomeRangeDto>.Error("CREATE_FAILED", ["No records were affected while creating the income range."]);

        return new Response<IncomeRangeDto>
        {
            IsSuccess = true,
            Message = "Income range created successfully.",
            Data = Map(entity, company.Name)
        };
    }

    private static IncomeRangeDto Map(IncomeRange e, string companyName) => new()
    {
        Id = e.Id,
        CompanyId = e.CompanyId,
        CompanyName = companyName,
        DisplayName = e.DisplayName,
        MinimumValue = e.MinimumValue,
        MaximumValue = e.MaximumValue,
        CurrencyCode = e.CurrencyCode,
        IsActive = e.IsActive,
        DisplayOrder = e.DisplayOrder,
        CreatedAt = e.Created
    };
}
