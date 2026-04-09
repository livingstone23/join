using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Command to perform a soft delete for a time unit catalog item.
/// </summary>
/// <param name="Id">The time unit identifier to delete.</param>
public sealed record DeleteTimeUnitCommand(Guid Id) : IRequest<Response<Guid>>;
