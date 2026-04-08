using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Command used to soft-delete a tenant-scoped company module assignment.
/// </summary>
/// <param name="Id">The unique identifier of the assignment to delete.</param>
/// <param name="CompanyId">The tenant identifier used to scope the delete operation.</param>
public sealed record DeleteCompanyModulesCommand(Guid Id, Guid CompanyId)
    : IRequest<Response<Guid>>;
