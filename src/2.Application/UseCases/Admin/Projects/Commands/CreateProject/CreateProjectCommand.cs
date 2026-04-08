using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Command used to create a new tenant-scoped project.
/// </summary>
public sealed record CreateProjectCommand : IRequest<Response<ProjectDto>>
{
    /// <summary>
    /// Gets or sets the tenant identifier extracted from the <c>X-Company-Id</c> header.
    /// This value is populated by the API layer and is not expected in the JSON payload.
    /// </summary>
    [JsonIgnore]
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets or sets the display name of the project.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier of the linked entity status.
    /// </summary>
    public Guid EntityStatusId { get; init; }
}