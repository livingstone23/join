using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Command used to create a role-system-option permission rule for the specified company.
/// </summary>
public sealed record CreateRoleSystemOptionCommand(
    Guid CompanyId,
    Guid RoleId,
    Guid SystemOptionId,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload = true,
    bool CanExport = true,
    bool CanExecute = true,
    bool IsVisibleMenu = true,
    int? OrderMenu = 0)
    : ITransactionalCommand<Response<RoleSystemOptionDto>>;
