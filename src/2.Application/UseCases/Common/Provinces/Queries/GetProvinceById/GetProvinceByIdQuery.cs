using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Provinces.Queries;

/// <summary>
/// Query to retrieve a province catalog item by its identifier.
/// </summary>
/// <param name="Id">The province identifier.</param>
public record GetProvinceByIdQuery(Guid Id) : IRequest<Response<ProvinceDto>>;