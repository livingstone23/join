using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Command to register a new street type.
/// </summary>
public record CreateStreetTypeCommand : IRequest<Response<StreetTypeDto>>
{
    /// <summary>
    /// Gets or sets the street type name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the street type abbreviation.
    /// </summary>
    public string Abbreviation { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the street type is active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
