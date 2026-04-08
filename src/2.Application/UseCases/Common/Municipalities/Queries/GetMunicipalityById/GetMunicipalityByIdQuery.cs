using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Municipalities.Queries;

/// <summary>
/// Query to retrieve a municipality catalog item by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the municipality to retrieve.</param>
public sealed record GetMunicipalityByIdQuery(Guid Id) : IRequest<Response<MunicipalityDto>>;
