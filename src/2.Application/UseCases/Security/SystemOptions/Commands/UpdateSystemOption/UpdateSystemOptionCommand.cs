using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

/// <summary>
/// Command for updating an existing system option.
/// </summary>
public sealed record UpdateSystemOptionCommand(
    [property: JsonIgnore] Guid Id,
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
