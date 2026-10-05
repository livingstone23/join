using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Command used to soft-delete a tenant-scoped company module assignment.
/// </summary>
/// <param name="Id">The unique identifier of the assignment to delete.</param>
/// <param name="CompanyId">Optional target tenant — only honored for real SuperAdmin via TenantResolver.</param>
public sealed record DeleteCompanyModulesCommand(Guid Id, Guid? CompanyId = null)
    : ITransactionalCommand<Response<Guid>>;
