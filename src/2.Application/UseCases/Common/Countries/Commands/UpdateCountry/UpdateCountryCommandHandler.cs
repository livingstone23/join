using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Handles country update commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class UpdateCountryCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCountryCommand, Response<CountryDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a country catalog item.
    /// </summary>
    /// <param name="request">The update payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the updated country.</returns>
    public async Task<Response<CountryDto>> Handle(UpdateCountryCommand request, CancellationToken cancellationToken)
    {
        var countryRepository = _unitOfWork.GetRepository<Country>();
        var countryEntity = await countryRepository.GetAsync(request.Id);

        if (countryEntity is null)
        {
            return Response<CountryDto>.Error("COUNTRY_NOT_FOUND", ["Country not found."]);
        }

        var normalizedIsoCode = request.IsoCode.Trim().ToUpperInvariant();
        var normalizedName = request.Name.Trim();

        var existingCountries = await countryRepository.GetAllAsync();
        var isoCodeAlreadyExists = existingCountries.Any(c =>
            c.Id != request.Id &&
            string.Equals(c.IsoCode, normalizedIsoCode, StringComparison.OrdinalIgnoreCase));

        if (isoCodeAlreadyExists)
        {
            return Response<CountryDto>.Error(
                "COUNTRY_ISO_CODE_IN_USE",
                ["Another active country already uses the same ISO code."]);
        }

        countryEntity.Name = normalizedName;
        countryEntity.IsoCode = normalizedIsoCode;

        await countryRepository.UpdateAsync(countryEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<CountryDto>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the country."]);
        }

        return new Response<CountryDto>
        {
            IsSuccess = true,
            Message = "Country updated successfully.",
            Data = new CountryDto
            {
                Id = countryEntity.Id,
                Name = countryEntity.Name,
                IsoCode = countryEntity.IsoCode
            }
        };
    }
}
