using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Industries.Commands;

/// <summary>
/// Command used to create a new tenant-scoped industry catalog entry.
/// </summary>
public sealed record CreateIndustryCommand : ITransactionalCommand<Response<IndustryDto>>
{
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
}
