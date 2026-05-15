using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Handles gender creation commands using the transactional write stack.
/// </summary>
public sealed class CreateGenderCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateGenderCommand, Response<GenderDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <inheritdoc />
    public async Task<Response<GenderDto>> Handle(CreateGenderCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<GenderDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var companyRepository = _unitOfWork.GetRepository<Company>();
        var genderRepository = _unitOfWork.GetRepository<Gender>();

        var company = await companyRepository.GetAsync(companyId);
        if (company is null)
        {
            return Response<GenderDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var normalizedCode = request.Code.Trim();
        var normalizedName = request.Name.Trim();

        var genders = await genderRepository.GetAllAsync();
        var codeInUse = genders.Any(gender =>
            gender.CompanyId == companyId
            && gender.GcRecord == 0
            && string.Equals(gender.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));

        if (codeInUse)
        {
            return Response<GenderDto>.Error("GENDER_CODE_IN_USE", ["Another active gender already uses the same code in this company."]);
        }

        var nameInUse = genders.Any(gender =>
            gender.CompanyId == companyId
            && gender.GcRecord == 0
            && string.Equals(gender.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<GenderDto>.Error("GENDER_NAME_IN_USE", ["Another active gender already uses the same name in this company."]);
        }

        var entity = Gender.Create(companyId, normalizedCode, normalizedName);

        await genderRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<GenderDto>.Error("CREATE_FAILED", ["No records were affected while creating the gender."]);
        }

        return new Response<GenderDto>
        {
            IsSuccess = true,
            Message = "Gender created successfully.",
            Data = new GenderDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                CompanyName = company.Name,
                Code = entity.Code,
                Name = entity.Name,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}
