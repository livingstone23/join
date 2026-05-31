namespace JOIN.Application.DTO.Admin;



/// <summary>
/// Data transfer object representing a person employment payload exposed by Application queries.
/// </summary>
public sealed record PersonEmploymentResponseDto
{
    /// <summary>
    /// Gets the unique identifier of the person employment record.
    /// </summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the unique identifier of the owner person.
    /// </summary>
    public Guid PersonId { get; init; }

    /// <summary>
    /// Gets the employer or organization name.
    /// </summary>
    public string EmployerName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the job title or role held by the person.
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
    public bool IsCurrent { get; init; }

    /// <summary>
    /// Gets whether this employment record is active in the system.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// Gets the UTC timestamp when the employment record was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
