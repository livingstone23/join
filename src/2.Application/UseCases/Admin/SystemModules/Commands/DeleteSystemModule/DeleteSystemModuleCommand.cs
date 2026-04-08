using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;

/// <summary>
/// Command used to delete an existing system module.
/// </summary>
/// <param name="Id">The unique identifier of the system module to delete.</param>
public sealed record DeleteSystemModuleCommand(Guid Id) : IRequest<Response<Guid>>;