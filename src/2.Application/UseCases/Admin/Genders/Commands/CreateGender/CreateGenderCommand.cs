using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Command used to create a new tenant-scoped gender catalog entry.
/// </summary>
public sealed record CreateGenderCommand : IRequest<Response<GenderDto>>
{
    /// <summary>
    /// Gets or sets the standard or internal code for the gender.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the gender.
    /// </summary>
    public string Name { get; init; } = string.Empty;
}
