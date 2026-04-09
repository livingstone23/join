using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Handles region creation commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateRegionCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateRegionCommand, Response<RegionDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a region catalog item.
    /// </summary>
    /// <param name="request">The creation payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the created region.</returns>
    public async Task<Response<RegionDto>> Handle(CreateRegionCommand request, CancellationToken cancellationToken)
    {
        var regionRepository = _unitOfWork.GetRepository<Region>();
        var countryRepository = _unitOfWork.GetRepository<Country>();

        var country = await countryRepository.GetAsync(request.CountryId);
        if (country is null)
        {
            return Response<RegionDto>.Error("REGION_COUNTRY_NOT_FOUND", ["The specified CountryId does not exist."]);
        }

        var normalizedName = request.Name.Trim();
        var normalizedCode = string.IsNullOrWhiteSpace(request.Code)
            ? null
            : request.Code.Trim().ToUpperInvariant();

        var existingRegions = await regionRepository.GetAllAsync();
        var nameInUse = existingRegions.Any(r =>
            r.GcRecord == 0
            && r.CountryId == request.CountryId
            && string.Equals(r.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<RegionDto>.Error("REGION_NAME_IN_USE", ["Another active region already uses the same name for this country."]);
        }

        var codeInUse = !string.IsNullOrWhiteSpace(normalizedCode) && existingRegions.Any(r =>
            r.GcRecord == 0
            && r.CountryId == request.CountryId
            && !string.IsNullOrWhiteSpace(r.Code)
            && string.Equals(r.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));

        if (codeInUse)
        {
            return Response<RegionDto>.Error("REGION_CODE_IN_USE", ["Another active region already uses the same code for this country."]);
        }

        var entity = new Region
        {
            Name = normalizedName,
            Code = normalizedCode,
            CountryId = request.CountryId
        };

        await regionRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<RegionDto>.Error(
                "CREATE_FAILED",
                ["No records were affected while creating the region."]);
        }

        return new Response<RegionDto>
        {
            IsSuccess = true,
            Message = "Region created successfully.",
            Data = new RegionDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Code = entity.Code,
                CountryId = entity.CountryId,
                CountryName = country.Name,
                CreatedAt = entity.Created
            }
        };
    }
}
