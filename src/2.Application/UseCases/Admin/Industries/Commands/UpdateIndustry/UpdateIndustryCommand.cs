using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Industries.Commands;

/// <summary>
/// Command used to update an existing tenant-scoped industry catalog entry.
/// </summary>
public sealed record UpdateIndustryCommand : IRequest<Response<IndustryDto>>
{
    /// <summary>
    /// Gets or sets the unique identifier of the target industry.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the standard or internal code for the industry.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the industry.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional description of the industry.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets whether the industry is active for new selections.
    /// When omitted, the current active state is preserved.
    /// </summary>
    public bool? IsActive { get; init; }
}
