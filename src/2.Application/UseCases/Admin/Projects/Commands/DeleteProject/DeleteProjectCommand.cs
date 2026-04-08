using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Command used to delete an existing tenant-scoped project.
/// </summary>
/// <param name="Id">The unique identifier of the project to delete.</param>
/// <param name="CompanyId">The tenant identifier used to scope the delete operation.</param>
public sealed record DeleteProjectCommand(Guid Id, Guid CompanyId) : IRequest<Response<Guid>>;