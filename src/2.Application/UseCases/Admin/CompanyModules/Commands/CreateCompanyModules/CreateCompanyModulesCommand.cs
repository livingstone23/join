using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Commands;

/// <summary>
/// Command used to create a tenant-scoped company module assignment.
/// </summary>
public sealed record CreateCompanyModulesCommand : ITransactionalCommand<Response<CompanyModuleDto>>
{
    /// <summary>
    /// Gets or sets the unique identifier of the company that owns the assignment.
    /// This value is expected in the JSON payload.
    /// </summary>
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets or sets the unique identifier of the system module that will be assigned to the company.
    /// </summary>
    public Guid ModuleId { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the module is active for the company.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
