using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Command to perform a soft delete for a municipality catalog item.
/// </summary>
/// <param name="Id">The municipality identifier to delete.</param>
public sealed record DeleteMunicipalityCommand(Guid Id) : IRequest<Response<Guid>>;
