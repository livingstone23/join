using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Command to perform a soft delete for a region catalog item.
/// </summary>
/// <param name="Id">The region identifier to delete.</param>
public sealed record DeleteRegionCommand(Guid Id) : IRequest<Response<Guid>>;
