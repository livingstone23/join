using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Command that creates a new employment record for an existing person in the current tenant context.
/// </summary>
public sealed record CreatePersonEmploymentCommand : ITransactionalCommand<Response<Guid>>
{
    /// <summary>
    /// Gets the identifier of the person owner.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the employer or organization name.
    /// </summary>
    public string EmployerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the job title or role.
    /// </summary>
    public string JobTitle { get; init; } = string.Empty;

    /// <summary>
    /// Gets the employment start date.
    /// </summary>
    public DateTime StartDate { get; init; }

    /// <summary>
    /// Gets the employment end date when the job is not current.
    /// </summary>
    public DateTime? EndDate { get; init; }

    /// <summary>
    /// Gets whether this is the person's current primary employment.
    /// </summary>
    public bool? IsCurrent { get; init; }

    /// <summary>
    /// Gets whether this employment record should be active in the system.
    /// </summary>
    public bool? IsActive { get; init; }
}
