using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Command used to create a new identification type.
/// </summary>
public sealed record CreateIdentificationTypeCommand : IRequest<Response<IdentificationTypeDto>>
{
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