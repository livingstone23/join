using System.Globalization;
using System.Text.Json.Serialization;

namespace JOIN.Application.DTO.Common;

/// <summary>
/// Base record for DTOs of soft-deletable entities (SPEC 41). Query handlers select the row's
/// <c>GcRecord</c> into <see cref="GcRecord"/> (hidden from the JSON payload) and the DTO exposes
/// <see cref="IsDeleted"/> and <see cref="DeletedOn"/>, derived from the <c>yyyyMMdd</c> stamp.
/// Deleted rows only reach a DTO when a <c>SuperAdmin</c> asks for them (<c>includeDeleted=true</c>).
/// </summary>
public abstract record SoftDeletableDto
{
    /// <summary>
    /// Gets the raw soft-delete stamp: <c>0</c> for an active row, <c>yyyyMMdd</c> of the deletion otherwise.
    /// </summary>
    [JsonIgnore]
    public int GcRecord { get; init; }

    /// <summary>
    /// Gets whether the record is logically deleted.
    /// </summary>
    public bool IsDeleted => GcRecord != 0;

    /// <summary>
    /// Gets the deletion date, or <c>null</c> for an active record.
    /// </summary>
    public DateOnly? DeletedOn =>
        GcRecord != 0
        && DateOnly.TryParseExact(
            GcRecord.ToString(CultureInfo.InvariantCulture),
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var deletedOn)
            ? deletedOn
            : null;
}
