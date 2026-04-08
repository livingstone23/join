using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Provinces.Commands;

/// <summary>
/// Handles province update commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class UpdateProvinceCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateProvinceCommand, Response<ProvinceDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a province catalog item.
    /// </summary>
    /// <param name="request">The update payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the updated province.</returns>
    public async Task<Response<ProvinceDto>> Handle(UpdateProvinceCommand request, CancellationToken cancellationToken)
    {
        var provinceRepository = _unitOfWork.GetRepository<Province>();
        var countryRepository = _unitOfWork.GetRepository<Country>();
        var regionRepository = _unitOfWork.GetRepository<Region>();

        var provinceEntity = await provinceRepository.GetAsync(request.Id);
        if (provinceEntity is null)
        {
            return Response<ProvinceDto>.Error("PROVINCE_NOT_FOUND", ["Province not found."]);
        }

        var country = await countryRepository.GetAsync(request.CountryId);
        if (country is null)
        {
            return Response<ProvinceDto>.Error("PROVINCE_COUNTRY_NOT_FOUND", ["The specified CountryId does not exist."]);
        }

        Region? region = null;
        if (request.RegionId.HasValue && request.RegionId.Value != Guid.Empty)
        {
            region = await regionRepository.GetAsync(request.RegionId.Value);
            if (region is null)
            {
                return Response<ProvinceDto>.Error("PROVINCE_REGION_NOT_FOUND", ["The specified RegionId does not exist."]);
            }

            if (region.CountryId != request.CountryId)
            {
                return Response<ProvinceDto>.Error("PROVINCE_REGION_COUNTRY_MISMATCH", ["The specified region does not belong to the selected country."]);
            }
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = request.Code.Trim().ToUpperInvariant();

        var existingProvinces = await provinceRepository.GetAllAsync();
        var codeInUse = existingProvinces.Any(p =>
            p.Id != request.Id
            && p.GcRecord == 0
            && p.CountryId == request.CountryId
            && string.Equals(p.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));

        if (codeInUse)
        {
            return Response<ProvinceDto>.Error("PROVINCE_CODE_IN_USE", ["Another active province already uses the same code for this country."]);
        }

        var nameInUse = existingProvinces.Any(p =>
            p.Id != request.Id
            && p.GcRecord == 0
            && p.CountryId == request.CountryId
            && string.Equals(p.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<ProvinceDto>.Error("PROVINCE_NAME_IN_USE", ["Another active province already uses the same name for this country."]);
        }

        provinceEntity.Name = normalizedName;
        provinceEntity.Code = normalizedCode;
        provinceEntity.CountryId = request.CountryId;
        provinceEntity.RegionId = request.RegionId;

        await provinceRepository.UpdateAsync(provinceEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<ProvinceDto>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the province."]);
        }

        return new Response<ProvinceDto>
        {
            IsSuccess = true,
            Message = "Province updated successfully.",
            Data = new ProvinceDto
            {
                Id = provinceEntity.Id,
                Name = provinceEntity.Name,
                Code = provinceEntity.Code,
                CountryId = provinceEntity.CountryId,
                CountryName = country.Name,
                RegionId = provinceEntity.RegionId,
                RegionName = region?.Name,
                CreatedAt = provinceEntity.Created
            }
        };
    }
}