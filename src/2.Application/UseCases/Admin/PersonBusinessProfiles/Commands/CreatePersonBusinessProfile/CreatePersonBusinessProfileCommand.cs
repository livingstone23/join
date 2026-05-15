using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Command that creates a new business profile for an existing person in the current tenant context.
/// </summary>
public sealed record CreatePersonBusinessProfileCommand : IRequest<Response<Guid>>
{
    /// <summary>
    /// Gets the identifier of the person owner.
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
