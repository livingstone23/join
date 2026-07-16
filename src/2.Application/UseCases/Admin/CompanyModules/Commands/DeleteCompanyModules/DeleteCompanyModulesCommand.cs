using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Command used to soft-delete a tenant-scoped company module assignment.
/// </summary>
/// <param name="Id">The unique identifier of the assignment to delete.</param>
public sealed record DeleteCompanyModulesCommand(Guid Id)
    : ITransactionalCommand<Response<Guid>>;
