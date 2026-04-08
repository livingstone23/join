using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Handles municipality update commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class UpdateMunicipalityCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateMunicipalityCommand, Response<MunicipalityDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a municipality catalog item.
    /// </summary>
    /// <param name="request">The update payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the updated municipality.</returns>
    public async Task<Response<MunicipalityDto>> Handle(UpdateMunicipalityCommand request, CancellationToken cancellationToken)
    {
        var municipalityRepository = _unitOfWork.GetRepository<Municipality>();
        var provinceRepository = _unitOfWork.GetRepository<Province>();

        var municipalityEntity = await municipalityRepository.GetAsync(request.Id);
        if (municipalityEntity is null)
        {
            return Response<MunicipalityDto>.Error("MUNICIPALITY_NOT_FOUND", ["Municipality not found."]);
        }

        var province = await provinceRepository.GetAsync(request.ProvinceId);
        if (province is null)
        {
            return Response<MunicipalityDto>.Error("MUNICIPALITY_PROVINCE_NOT_FOUND", ["The specified ProvinceId does not exist."]);
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = string.IsNullOrWhiteSpace(request.Code)
            ? null
            : request.Code.Trim().ToUpperInvariant();

        var existingMunicipalities = await municipalityRepository.GetAllAsync();
        var nameInUse = existingMunicipalities.Any(m =>
            m.Id != request.Id
            && m.GcRecord == 0
            && m.ProvinceId == request.ProvinceId
            && string.Equals(m.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<MunicipalityDto>.Error("MUNICIPALITY_NAME_IN_USE", ["Another active municipality already uses the same name for this province."]);
        }

        var codeInUse = !string.IsNullOrWhiteSpace(normalizedCode) && existingMunicipalities.Any(m =>
            m.Id != request.Id
            && m.GcRecord == 0
            && m.ProvinceId == request.ProvinceId
            && !string.IsNullOrWhiteSpace(m.Code)
            && string.Equals(m.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));

        if (codeInUse)
        {
            return Response<MunicipalityDto>.Error("MUNICIPALITY_CODE_IN_USE", ["Another active municipality already uses the same code for this province."]);
        }

        municipalityEntity.Name = normalizedName;
        municipalityEntity.Code = normalizedCode;
        municipalityEntity.ProvinceId = request.ProvinceId;

        await municipalityRepository.UpdateAsync(municipalityEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<MunicipalityDto>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the municipality."]);
        }

        return new Response<MunicipalityDto>
        {
            IsSuccess = true,
            Message = "Municipality updated successfully.",
            Data = new MunicipalityDto
            {
                Id = municipalityEntity.Id,
                Name = municipalityEntity.Name,
                Code = municipalityEntity.Code,
                ProvinceId = municipalityEntity.ProvinceId,
                ProvinceName = province.Name,
                CreatedAt = municipalityEntity.Created
            }
        };
    }
}
