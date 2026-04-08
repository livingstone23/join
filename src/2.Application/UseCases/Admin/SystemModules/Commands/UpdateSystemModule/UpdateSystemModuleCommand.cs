using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;

/// <summary>
/// Command used to update an existing system module.
/// </summary>
public sealed record UpdateSystemModuleCommand : IRequest<Response<SystemModuleDto>>
{
    /// <summary>
    /// Gets or sets the unique identifier of the target system module.
    /// This value is populated by the API layer from the route and is not expected in the JSON payload.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the display name of the system module.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the detailed description of the system module.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the icon identifier associated with the system module.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the module is globally active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}