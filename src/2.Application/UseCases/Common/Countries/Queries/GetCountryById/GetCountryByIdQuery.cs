using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Queries;

/// <summary>
/// Query to retrieve a country catalog item by id.
/// </summary>
/// <param name="CountryId">The country identifier.</param>
public record GetCountryByIdQuery(Guid CountryId) : IRequest<Response<CountryDto>>;
