using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Handles country creation commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class CreateCountryCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCountryCommand, Response<CountryDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a country catalog item.
    /// </summary>
    /// <param name="request">The creation payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the created country.</returns>
    public async Task<Response<CountryDto>> Handle(CreateCountryCommand request, CancellationToken cancellationToken)
    {
        var countryRepository = _unitOfWork.GetRepository<Country>();

        var normalizedIsoCode = request.IsoCode.Trim().ToUpperInvariant();
        var normalizedName = request.Name.Trim();

        var existingCountries = await countryRepository.GetAllAsync();
        var isoCodeAlreadyExists = existingCountries.Any(c =>
            string.Equals(c.IsoCode, normalizedIsoCode, StringComparison.OrdinalIgnoreCase));

        if (isoCodeAlreadyExists)
        {
            return Response<CountryDto>.Error(
                "COUNTRY_ISO_CODE_IN_USE",
                ["Another active country already uses the same ISO code."]);
        }

        var entity = new Country
        {
            Name = normalizedName,
            IsoCode = normalizedIsoCode
        };

        await countryRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<CountryDto>.Error(
                "CREATE_FAILED",
                ["No records were affected while creating the country."]);
        }

        return new Response<CountryDto>
        {
            IsSuccess = true,
            Message = "Country created successfully.",
            Data = new CountryDto
            {
                Id = entity.Id,
                Name = entity.Name,
                IsoCode = entity.IsoCode
            }
        };
    }
}
