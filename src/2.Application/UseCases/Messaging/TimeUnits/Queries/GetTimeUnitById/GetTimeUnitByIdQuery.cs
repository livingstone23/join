using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Queries;

/// <summary>
/// Query to retrieve a time unit catalog item by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the time unit to retrieve.</param>
public sealed record GetTimeUnitByIdQuery(Guid Id) : IRequest<Response<TimeUnitDto>>;
