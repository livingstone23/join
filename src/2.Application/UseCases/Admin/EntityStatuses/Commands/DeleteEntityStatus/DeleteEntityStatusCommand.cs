using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Command used to perform a soft delete over an administrative entity status.
/// </summary>
/// <param name="Id">The unique identifier of the entity status to delete.</param>
/// <param name="CompanyId">The tenant identifier used to validate the request scope.</param>
public sealed record DeleteEntityStatusCommand(Guid Id, Guid CompanyId)
    : IRequest<Response<Guid>>;
