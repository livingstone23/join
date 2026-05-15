using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

public sealed class UpdateIncomeRangeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateIncomeRangeCommand, Response<IncomeRangeDto>>
{
    public async Task<Response<IncomeRangeDto>> Handle(UpdateIncomeRangeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<IncomeRangeDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        var companyId = currentUserService.CompanyId;
        var company = await unitOfWork.GetRepository<Company>().GetAsync(companyId);
        if (company is null)
            return Response<IncomeRangeDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);

        var repo = unitOfWork.GetRepository<IncomeRange>();
        var entity = await repo.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
            return Response<IncomeRangeDto>.Error("INCOME_RANGE_NOT_FOUND", ["Income range not found."]);

        var displayName = request.DisplayName.Trim();
        var currencyCode = request.CurrencyCode.Trim();
        var all = await repo.GetAllAsync();

        if (all.Any(x => x.Id != request.Id && x.CompanyId == companyId && x.GcRecord == 0 && string.Equals(x.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
            return Response<IncomeRangeDto>.Error("INCOME_RANGE_DISPLAY_NAME_IN_USE", ["Another active income range already uses the same display name in this company."]);

        try
        {
            entity.Update(displayName, request.MinimumValue, request.MaximumValue, currencyCode);
        }
        catch (ArgumentException ex)
        {
            return Response<IncomeRangeDto>.Error("INVALID_INCOME_RANGE_VALUES", [ex.Message]);
        }

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value) entity.Reactivate();
            else entity.Deactivate();
        }

        await repo.UpdateAsync(entity);
        if (await unitOfWork.SaveChangesAsync(cancellationToken) <= 0)
            return Response<IncomeRangeDto>.Error("UPDATE_FAILED", ["No records were affected while updating the income range."]);

        return new Response<IncomeRangeDto>
        {
            IsSuccess = true,
            Message = "Income range updated successfully.",
            Data = new IncomeRangeDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company.Name,
                DisplayName = entity.DisplayName,
                MinimumValue = entity.MinimumValue,
                MaximumValue = entity.MaximumValue,
                CurrencyCode = entity.CurrencyCode,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}
