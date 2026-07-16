using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Command that updates an existing person employment record in the current tenant context.
/// </summary>
public sealed record UpdatePersonEmploymentCommand : ITransactionalCommand<Response<Guid>>
{
    /// <summary>
    /// Gets the unique identifier of the employment record to update.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the person that owns the employment record.
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
    /// Gets the employment end date when the job is no longer current.
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
