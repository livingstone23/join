
using System.Globalization;

namespace JOIN.Domain.Audit;

/// <summary>
/// Extends BaseEntity to include traceability properties.
/// Essential for monitoring who performed actions and when,
/// supporting security audits and data integrity checks.
/// </summary>
public abstract class BaseAuditableEntity : BaseEntity, IAuditableEntity
{
    /// <summary>
    /// Represents an active record that has not been logically deleted.
    /// </summary>
    public const int ActiveGcRecord = 0;

    /// <summary>
    /// Gets or sets the UTC date and time when the entity was created.
    /// </summary>
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the user identifier who created this record.
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time of the last modification.
    /// </summary>
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who last modified this record.
    /// </summary>
    public string? LastModifiedBy { get; set; }

    /// <summary>
    /// Property used for soft deletion. A value of 0 indicates an active record,
    /// while any value greater than 0 stores the UTC deletion stamp in yyyyMMdd format.
    /// Example: 20260408.
    /// </summary>
    public int GcRecord { get; set; } = ActiveGcRecord;

    /// <summary>
    /// Generates the logical delete stamp for <see cref="GcRecord"/> using UTC time.
    /// </summary>
    /// <param name="utcNow">Optional UTC date to use for deterministic scenarios.</param>
    /// <returns>An integer in yyyyMMdd format, for example 20260408.</returns>
    public static int GetDeletionGcRecordStamp(DateTime? utcNow = null)
    {
        var value = utcNow ?? DateTime.UtcNow;
        return int.Parse(value.ToString("yyyyMMdd", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Marks the current entity as logically deleted using the standardized GcRecord stamp.
    /// </summary>
    /// <param name="utcNow">Optional UTC date to use for deterministic scenarios.</param>
    public void MarkAsDeleted(DateTime? utcNow = null)
    {
        GcRecord = GetDeletionGcRecordStamp(utcNow);
    }
}