using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Command that updates an existing person financial profile in the current tenant context.
/// </summary>
public sealed record UpdatePersonFinancialProfileCommand : ITransactionalCommand<Response<Guid>>
{
    /// <summary>
    /// Gets the unique identifier of the financial profile record to update.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the person that owns the financial profile.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the income range catalog identifier.
    /// </summary>
    public Guid IncomeRangeId { get; init; }

    /// <summary>
    /// Gets the source of funds description.
    /// </summary>
    public string SourceOfFunds { get; init; } = string.Empty;

    /// <summary>
    /// Gets the date when the financial information was declared.
    /// </summary>
    public DateTime DeclaredDate { get; init; }

    /// <summary>
    /// Gets whether this is the most recent and valid financial profile.
    /// </summary>
    public bool? IsCurrent { get; init; }

    /// <summary>
    /// Gets whether this financial profile record should be active in the system.
    /// </summary>
    public bool? IsActive { get; init; }
}
