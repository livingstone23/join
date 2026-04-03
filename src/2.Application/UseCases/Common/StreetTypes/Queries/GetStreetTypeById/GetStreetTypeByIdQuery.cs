using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Queries;

/// <summary>
/// Query to retrieve a street type by id.
/// </summary>
/// <param name="StreetTypeId">The street type identifier.</param>
public record GetStreetTypeByIdQuery(Guid StreetTypeId) : IRequest<Response<StreetTypeDto>>;
