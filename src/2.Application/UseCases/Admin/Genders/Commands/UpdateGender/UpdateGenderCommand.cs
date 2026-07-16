using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Command used to update an existing tenant-scoped gender catalog entry.
/// </summary>
public sealed record UpdateGenderCommand : ITransactionalCommand<Response<GenderDto>>
{
    /// <summary>
    /// Gets or sets the unique identifier of the target gender.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the standard or internal code for the gender.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the gender.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the gender is active for new selections.
    /// When omitted, the current active state is preserved.
    /// </summary>
    public bool? IsActive { get; init; }
}
