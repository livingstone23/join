using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

/// <summary>
/// Command for creating a new system option (screen, menu item, or action) within a module.
/// </summary>
public sealed record CreateSystemOptionCommand(
    Guid ModuleId,
    string Name,
    string Route,
    string? Icon,
    Guid? ParentId,
    string? ControllerName,
    bool CanRead = true,
    bool CanCreate = true,
    bool CanUpdate = true,
    bool CanDelete = true,
    bool CanDownload = true,
    bool CanExport = true,
    bool CanExecute = true,
    bool IsVisibleMenu = true,
    int? OrderMenu = 0)
    : ITransactionalCommand<Response<SystemOptionDto>>;
