using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Industries.Commands;

/// <summary>
/// Handles industry update commands using the transactional write stack.
/// </summary>
public sealed class UpdateIndustryCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateIndustryCommand, Response<IndustryDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <inheritdoc />
    public async Task<Response<IndustryDto>> Handle(UpdateIndustryCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IndustryDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var companyRepository = _unitOfWork.GetRepository<Company>();
        var industryRepository = _unitOfWork.GetRepository<Industry>();

        var company = await companyRepository.GetAsync(companyId);
        if (company is null)
        {
            return Response<IndustryDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var entity = await industryRepository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<IndustryDto>.Error("INDUSTRY_NOT_FOUND", ["Industry not found."]);
        }

        var normalizedCode = request.Code.Trim();
        var normalizedName = request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var industries = await industryRepository.GetAllAsync();
        var codeInUse = industries.Any(industry =>
            industry.Id != request.Id
            && industry.CompanyId == companyId
            && industry.GcRecord == 0
            && string.Equals(industry.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));

        if (codeInUse)
        {
            return Response<IndustryDto>.Error("INDUSTRY_CODE_IN_USE", ["Another active industry already uses the same code in this company."]);
        }

        var nameInUse = industries.Any(industry =>
            industry.Id != request.Id
            && industry.CompanyId == companyId
            && industry.GcRecord == 0
            && string.Equals(industry.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<IndustryDto>.Error("INDUSTRY_NAME_IN_USE", ["Another active industry already uses the same name in this company."]);
        }

        entity.Update(normalizedCode, normalizedName, normalizedDescription);

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value)
            {
                entity.Reactivate();
            }
            else
            {
                entity.Deactivate();
            }
        }

        await industryRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<IndustryDto>.Error("UPDATE_FAILED", ["No records were affected while updating the industry."]);
        }

        return new Response<IndustryDto>
        {
            IsSuccess = true,
            Message = "Industry updated successfully.",
            Data = new IndustryDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company.Name,
                Code = entity.Code,
                Name = entity.Name,
                Description = entity.Description,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}
