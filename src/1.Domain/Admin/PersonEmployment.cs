using JOIN.Domain.Audit;



namespace JOIN.Domain.Admin;



/// <summary>
/// Represents the employment history and current job details of a natural person.
/// Crucial for risk assessment, onboarding scoring, and demographic segmentation.
/// </summary>
public class PersonEmployment : BaseTenantEntity
{


    /// <summary>
    /// Foreign key to the core Person entity.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// The name of the company or organization where the person is employed.
    /// </summary>
    public string EmployerName { get; set; } = string.Empty;

    /// <summary>
    /// The specific job title or role the person holds (e.g., "Software Engineer", "Manager").
    /// </summary>
    public string JobTitle { get; set; } = string.Empty;

    /// <summary>
    /// The date the employment started.
    /// </summary>
    public DateTime StartDate { get; set; }

    /// <summary>
    /// The date the employment ended. Null if it is the current job.
    /// </summary>
    public DateTime? EndDate { get; private set; }

    /// <summary>
    /// Indicates if this is the person's current primary employment.
    /// </summary>
    public bool IsCurrent { get; private set; } = true;

    /// <summary>
    /// Indicates whether this employment record is active in the system.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    // --- Navigation Properties ---
    public virtual Person Person { get; set; } = null!;

    // --- Domain Behavior ---

    /// <summary>
    /// Marks the employment period as finished.
    /// </summary>
    /// <param name="endDate">The date the employment concluded.</param>
    public void MarkAsEnded(DateTime endDate)
    {
        if (!IsCurrent) return;
        IsCurrent = false;
        EndDate = endDate;
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        IsCurrent = false; // Business Rule: An inactive record cannot be the current job.
    }

    public void Reactivate()
    {
        if (IsActive) return;
        IsActive = true;
    }

    
}
