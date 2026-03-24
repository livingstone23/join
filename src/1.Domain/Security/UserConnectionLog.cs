using JOIN.Domain.Audit;



namespace JOIN.Domain.Security;



/// <summary>
/// Tracks user login events, capturing origin data such as IP address and Country.
/// Also used to determine currently active sessions.
/// </summary>
public class UserConnectionLog : BaseEntity // Hereda solo de BaseEntity para tener el Guid Id
{
    /// <summary>
    /// Foreign key to the ApplicationUser.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The IP address from which the connection originated.
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// The country resolved from the IP address.
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// The User-Agent string of the browser or device used to connect.
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// The UTC timestamp when the user logged in.
    /// </summary>
    public DateTime ConnectionDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates if the user is currently connected (Active Session).
    /// </summary>
    public bool IsActiveSession { get; set; } = true;

    /// <summary>
    /// The UTC timestamp when the user logged out or the session expired.
    /// </summary>
    public DateTime? DisconnectionDate { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationUser User { get; set; } = null!;
    
}
