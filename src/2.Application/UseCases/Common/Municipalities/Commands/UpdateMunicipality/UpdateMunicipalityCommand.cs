using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Command to update an existing municipality catalog item.
/// </summary>
public record UpdateMunicipalityCommand : IRequest<Response<MunicipalityDto>>
{
    /// <summary>
    /// Gets the municipality identifier to update.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the municipality display name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional municipality code.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// Gets or sets the parent province identifier.
    /// </summary>
    public Guid ProvinceId { get; init; }
}
