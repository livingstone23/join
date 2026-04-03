using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Command to perform a soft delete for a street type.
/// </summary>
/// <param name="Id">The street type identifier to delete.</param>
public record DeleteStreetTypeCommand(Guid Id) : IRequest<Response<Guid>>;
