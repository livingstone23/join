using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Commands;

/// <summary>
/// Command used to update the permission flags of an existing role-system-option rule.
/// </summary>
public sealed record UpdateRoleSystemOptionCommand(
    [property: JsonIgnore]
    Guid Id,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload = true,
    bool CanExport = true,
    bool CanExecute = true,
    bool IsVisibleMenu = true,
    int? OrderMenu = 0,
    Guid? CompanyId = null)
    : ITransactionalCommand<Response<RoleSystemOptionDto>>;
