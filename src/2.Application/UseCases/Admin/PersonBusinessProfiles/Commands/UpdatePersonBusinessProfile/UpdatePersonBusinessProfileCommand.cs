using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Command that updates an existing person business profile in the current tenant context.
/// </summary>
public sealed record UpdatePersonBusinessProfileCommand : ITransactionalCommand<Response<Guid>>
{
    /// <summary>
    /// Gets the unique identifier of the business profile record to update.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the person that owns the business profile.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the industry catalog identifier.
    /// </summary>
    public Guid IndustryId { get; init; }

    /// <summary>
    /// Gets the tax regime catalog identifier.
    /// </summary>
    public Guid TaxRegimeId { get; init; }

    /// <summary>
    /// Gets the official corporate website of the business.
    /// </summary>
    public string? Website { get; init; }

    /// <summary>
    /// Gets the date the company was legally founded or registered.
    /// </summary>
    public DateTime? FoundationDate { get; init; }

    /// <summary>
    /// Gets whether this business profile record should be active in the system.
    /// </summary>
    public bool? IsActive { get; init; }
}
