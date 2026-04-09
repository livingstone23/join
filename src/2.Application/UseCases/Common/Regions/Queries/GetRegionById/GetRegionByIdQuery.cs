using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Queries;

/// <summary>
/// Query to retrieve a region catalog item by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the region to retrieve.</param>
public sealed record GetRegionByIdQuery(Guid Id) : IRequest<Response<RegionDto>>;
