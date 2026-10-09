using System.Data;
using Dapper;



namespace JOIN.Infrastructure.Persistence;



/// <summary>
/// Dapper handler for <see cref="DateTime"/> that also accepts <see cref="DateOnly"/> values.
/// Npgsql returns <c>date</c> columns (e.g. <c>PersonEmployments.StartDate</c>,
/// <c>PersonBusinessProfiles.FoundationDate</c>) as <see cref="DateOnly"/>, which Dapper cannot
/// convert into the <see cref="DateTime"/> properties of the DTOs ("Error parsing column").
/// Parameters keep Dapper's default mapping (<see cref="DbType.DateTime"/>).
/// </summary>
public sealed class DateOnlyCompatibleDateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    /// <summary>
    /// Writes a <see cref="DateTime"/> parameter exactly as Dapper does without a handler.
    /// </summary>
    /// <param name="parameter">The command parameter.</param>
    /// <param name="value">The value to send.</param>
    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        parameter.DbType = DbType.DateTime;
        parameter.Value = value;
    }

    /// <summary>
    /// Reads a column value as <see cref="DateTime"/>; a <see cref="DateOnly"/> becomes midnight (Kind unspecified).
    /// </summary>
    /// <param name="value">The raw value returned by the provider.</param>
    /// <returns>The value as <see cref="DateTime"/>.</returns>
    public override DateTime Parse(object value) => value switch
    {
        DateTime dateTime => dateTime,
        DateOnly dateOnly => dateOnly.ToDateTime(TimeOnly.MinValue),
        DateTimeOffset dateTimeOffset => dateTimeOffset.UtcDateTime,
        _ => Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture)
    };
}
