using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Command used to update an existing administrative entity status.
/// </summary>
public sealed record UpdateEntityStatusCommand : IRequest<Response<EntityStatusDto>>
{
    /// <summary>
    /// Gets or sets the unique identifier of the target entity status.
    /// This value is populated by the API layer from the route and is not expected in the JSON payload.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the tenant identifier extracted from the <c>X-Company-Id</c> header.
    /// This value is populated by the API layer and is not expected in the JSON payload.
    /// </summary>
    [JsonIgnore]
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets or sets the display name of the entity status.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the detailed description of the entity status.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the internal numeric code of the entity status.
    /// </summary>
    public int Code { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the status is operative.
    /// </summary>
    public bool IsOperative { get; init; }
}
