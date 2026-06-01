using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents the employment history and current job details of a natural person.
/// Crucial for risk assessment, onboarding scoring, and demographic segmentation.
/// </summary>
public class PersonEmployment : BaseTenantEntity
{
    /// <summary>
    /// Gets the foreign key to the core Person entity.
    /// </summary>
    public Guid PersonId { get; private set; }

    /// <summary>
    /// Gets the name of the company or organization where the person is employed.
    /// </summary>
    public string EmployerName { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the specific job title or role the person holds.
    /// </summary>
    public string JobTitle { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the date the employment started.
    /// </summary>
    public DateTime StartDate { get; private set; }

    /// <summary>
    /// Gets the date the employment ended. Null if it is the current job.
    /// </summary>
    public DateTime? EndDate { get; private set; }

    /// <summary>
    /// Indicates if this is the person's current primary employment.
    /// </summary>
    public bool IsCurrent { get; private set; }

    /// <summary>
    /// Indicates whether this employment record is active in the system.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Gets whether the employment is eligible to be marked as current (active, not soft-deleted, and open-ended).
    /// </summary>
    public bool CanBeCurrent => IsActive && GcRecord == ActiveGcRecord && !EndDate.HasValue;

    // --- Navigation Properties ---

    /// <summary>
    /// Reference to the Person entity that owns this employment record.
    /// </summary>
    public virtual Person Person { get; set; } = null!;

    // --- Factory & Mutation ---

    /// <summary>
    /// Creates a new active employment record for a person within a tenant.
    /// </summary>
    /// <param name="companyId">The tenant company identifier.</param>
    /// <param name="personId">The person identifier.</param>
    /// <param name="employerName">The employer name.</param>
    /// <param name="jobTitle">The job title.</param>
    /// <param name="startDate">The employment start date.</param>
    /// <returns>A new <see cref="PersonEmployment"/> with <see cref="IsCurrent"/> set to <c>false</c>.</returns>
    public static PersonEmployment Create(
        Guid companyId,
        Guid personId,
        string employerName,
        string jobTitle,
        DateTime startDate)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (personId == Guid.Empty)
        {
            throw new ArgumentException("PersonId is required.", nameof(personId));
        }

        if (string.IsNullOrWhiteSpace(employerName))
        {
            throw new ArgumentException("EmployerName is required.", nameof(employerName));
        }

        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            throw new ArgumentException("JobTitle is required.", nameof(jobTitle));
        }

        return new PersonEmployment
        {
            CompanyId = companyId,
            PersonId = personId,
            EmployerName = employerName.Trim(),
            JobTitle = jobTitle.Trim(),
            StartDate = startDate.Date,
            IsCurrent = false,
            IsActive = true,
            GcRecord = ActiveGcRecord
        };
    }

    /// <summary>
    /// Updates the editable employment data.
    /// </summary>
    /// <param name="employerName">The employer name.</param>
    /// <param name="jobTitle">The job title.</param>
    /// <param name="startDate">The employment start date.</param>
    /// <param name="endDate">The optional employment end date.</param>
    public void Update(string employerName, string jobTitle, DateTime startDate, DateTime? endDate)
    {
        if (string.IsNullOrWhiteSpace(employerName))
        {
            throw new ArgumentException("EmployerName is required.", nameof(employerName));
        }

        if (string.IsNullOrWhiteSpace(jobTitle))
        {
            throw new ArgumentException("JobTitle is required.", nameof(jobTitle));
        }

        var normalizedStart = startDate.Date;
        var normalizedEnd = endDate?.Date;

        if (normalizedEnd.HasValue && normalizedEnd.Value < normalizedStart)
        {
            throw new ArgumentException("EndDate must be greater than or equal to StartDate.", nameof(endDate));
        }

        EmployerName = employerName.Trim();
        JobTitle = jobTitle.Trim();
        StartDate = normalizedStart;
        EndDate = normalizedEnd;

        if (EndDate.HasValue && IsCurrent)
        {
            RemoveCurrent();
        }
    }

    // --- Domain Behavior ---

    /// <summary>
    /// Marks this employment as the current job for its owner.
    /// Only active, open-ended employments can be set as current.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the employment is not eligible to be current.</exception>
    public void SetAsCurrent()
    {
        if (!CanBeCurrent)
        {
            throw new InvalidOperationException("Only an active, open-ended employment can be marked as current.");
        }

        IsCurrent = true;
    }

    /// <summary>
    /// Clears the current flag for this employment.
    /// </summary>
    public void RemoveCurrent()
    {
        if (!IsCurrent)
        {
            return;
        }

        IsCurrent = false;
    }

    /// <summary>
    /// Marks the employment period as finished.
    /// </summary>
    /// <param name="endDate">The date the employment concluded.</param>
    public void MarkAsEnded(DateTime endDate)
    {
        var normalizedEnd = endDate.Date;

        if (normalizedEnd < StartDate)
        {
            throw new ArgumentException("EndDate must be greater than or equal to StartDate.", nameof(endDate));
        }

        EndDate = normalizedEnd;
        RemoveCurrent();
    }

    /// <summary>
    /// Deactivates the employment and clears its current flag.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        RemoveCurrent();
    }

    /// <summary>
    /// Reactivates the employment in the system.
    /// This action is heavily restricted at the Application layer.
    /// </summary>
    public void Reactivate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
    }
}
