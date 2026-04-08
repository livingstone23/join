using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Command used to update an existing identification type.
/// </summary>
public sealed record UpdateIdentificationTypeCommand : IRequest<Response<IdentificationTypeDto>>
{
    /// <summary>
    /// Gets or sets the unique identifier of the target identification type.
    /// This value is populated by the API layer from the route and is not expected in the JSON payload.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the display name of the identification type.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the detailed description of the identification type.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the optional validation pattern associated with the identification type.
    /// </summary>
    public string? ValidationPattern { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the identification type is active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}