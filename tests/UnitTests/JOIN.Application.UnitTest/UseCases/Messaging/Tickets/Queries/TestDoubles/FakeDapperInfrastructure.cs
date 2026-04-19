using System.Collections;
using System.Data;
using System.Data.Common;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;

/// <summary>
/// Provides lightweight database test doubles used to exercise Dapper-based query handlers.
/// These classes capture SQL text and parameters while returning deterministic result sets.
/// </summary>
internal class FakeDbConnection : DbConnection
{
    private readonly List<FakeResultSet> _configuredResultSets = [];
    private ConnectionState _state = ConnectionState.Open;

    /// <summary>
    /// Gets the last SQL command executed by the connection.
    /// </summary>
    public string LastCommandText { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the captured command parameters from the last executed command.
    /// </summary>
    public Dictionary<string, object?> CapturedParameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Configures the result sets that should be returned by the next command execution.
    /// </summary>
    public void SetResults(params FakeResultSet[] resultSets)
    {
        _configuredResultSets.Clear();
        _configuredResultSets.AddRange(resultSets);
    }

    /// <inheritdoc />
    public override string? ConnectionString { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string Database => "FakeDb";

    /// <inheritdoc />
    public override string DataSource => "FakeSource";

    /// <inheritdoc />
    public override string ServerVersion => "1.0";

    /// <inheritdoc />
    public override ConnectionState State => _state;

    /// <inheritdoc />
    public override void ChangeDatabase(string databaseName)
    {
    }

    /// <inheritdoc />
    public override void Close()
    {
        _state = ConnectionState.Closed;
    }

    /// <inheritdoc />
    public override void Open()
    {
        _state = ConnectionState.Open;
    }

    /// <inheritdoc />
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
    {
        throw new NotSupportedException("Transactions are not required in these query unit tests.");
    }

    /// <inheritdoc />
    protected override DbCommand CreateDbCommand()
    {
        return new FakeDbCommand(this, _configuredResultSets);
    }

    internal void CaptureExecution(string commandText, DbParameterCollection parameters)
    {
        LastCommandText = commandText;
        CapturedParameters.Clear();

        foreach (DbParameter parameter in parameters)
        {
            CapturedParameters[parameter.ParameterName.TrimStart('@')] = parameter.Value;
        }
    }
}

/// <summary>
/// Represents a PostgreSQL-flavoured fake connection so pagination SQL branches can be verified.
/// </summary>
internal sealed class FakeNpgsqlDbConnection : FakeDbConnection
{
}

/// <summary>
/// Represents a single tabular result set returned by the fake data reader.
/// </summary>
internal sealed class FakeResultSet(IEnumerable<IDictionary<string, object?>> rows, IEnumerable<string>? columns = null)
{
    /// <summary>
    /// Gets the rows contained in the current result set.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows { get; } = rows
        .Select(row => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase))
        .ToList();

    /// <summary>
    /// Gets the column names exposed by the current result set.
    /// </summary>
    public IReadOnlyList<string> Columns { get; } = columns?.ToList()
        ?? (rows.FirstOrDefault()?.Keys.ToList() ?? []);

    /// <summary>
    /// Creates a result set from the provided rows.
    /// </summary>
    public static FakeResultSet FromRows(params IDictionary<string, object?>[] rows)
    {
        return new FakeResultSet(rows);
    }

    /// <summary>
    /// Creates an empty result set while preserving the expected schema.
    /// </summary>
    public static FakeResultSet Empty(params string[] columns)
    {
        return new FakeResultSet(Array.Empty<Dictionary<string, object?>>(), columns);
    }

    /// <summary>
    /// Creates a scalar result set using the first column only.
    /// </summary>
    public static FakeResultSet FromScalar(object? value)
    {
        return new FakeResultSet(
        [
            new Dictionary<string, object?>
            {
                ["Value"] = value
            }
        ]);
    }
}

internal sealed class FakeDbCommand(FakeDbConnection connection, IReadOnlyList<FakeResultSet> resultSets) : DbCommand
{
    private readonly FakeDbParameterCollection _parameters = new();

    /// <inheritdoc />
    public override string? CommandText { get; set; } = string.Empty;

    /// <inheritdoc />
    public override int CommandTimeout { get; set; }

    /// <inheritdoc />
    public override CommandType CommandType { get; set; } = CommandType.Text;

    /// <inheritdoc />
    public override bool DesignTimeVisible { get; set; }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource { get; set; }

    /// <inheritdoc />
    protected override DbConnection DbConnection { get; set; } = connection;

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection => _parameters;

    /// <inheritdoc />
    protected override DbTransaction? DbTransaction { get; set; }

    /// <inheritdoc />
    public override void Cancel()
    {
    }

    /// <inheritdoc />
    public override int ExecuteNonQuery()
    {
        connection.CaptureExecution(CommandText, _parameters);
        return 1;
    }

    /// <inheritdoc />
    public override object? ExecuteScalar()
    {
        connection.CaptureExecution(CommandText, _parameters);
        return null;
    }

    /// <inheritdoc />
    public override void Prepare()
    {
    }

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter()
    {
        return new FakeDbParameter();
    }

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        connection.CaptureExecution(CommandText, _parameters);
        return new FakeDbDataReader(resultSets);
    }
}

internal sealed class FakeDbParameter : DbParameter
{
    /// <inheritdoc />
    public override DbType DbType { get; set; }

    /// <inheritdoc />
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <inheritdoc />
    public override bool IsNullable { get; set; }

    /// <inheritdoc />
    public override string? ParameterName { get; set; } = string.Empty;

    /// <inheritdoc />
    public override string? SourceColumn { get; set; } = string.Empty;

    /// <inheritdoc />
    public override object? Value { get; set; }

    /// <inheritdoc />
    public override bool SourceColumnNullMapping { get; set; }

    /// <inheritdoc />
    public override int Size { get; set; }

    /// <inheritdoc />
    public override void ResetDbType()
    {
    }
}

internal sealed class FakeDbParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _items = [];

    /// <inheritdoc />
    public override int Count => _items.Count;

    /// <inheritdoc />
    public override object SyncRoot => ((ICollection)_items).SyncRoot;

    /// <inheritdoc />
    public override int Add(object value)
    {
        _items.Add((DbParameter)value);
        return _items.Count - 1;
    }

    /// <inheritdoc />
    public override void AddRange(Array values)
    {
        foreach (var value in values)
        {
            Add(value!);
        }
    }

    /// <inheritdoc />
    public override void Clear()
    {
        _items.Clear();
    }

    /// <inheritdoc />
    public override bool Contains(string value)
    {
        return _items.Any(parameter => string.Equals(parameter.ParameterName, value, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public override bool Contains(object value)
    {
        return _items.Contains((DbParameter)value);
    }

    /// <inheritdoc />
    public override void CopyTo(Array array, int index)
    {
        ((ICollection)_items).CopyTo(array, index);
    }

    /// <inheritdoc />
    public override IEnumerator GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    /// <inheritdoc />
    public override int IndexOf(string parameterName)
    {
        return _items.FindIndex(parameter => string.Equals(parameter.ParameterName, parameterName, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public override int IndexOf(object value)
    {
        return _items.IndexOf((DbParameter)value);
    }

    /// <inheritdoc />
    public override void Insert(int index, object value)
    {
        _items.Insert(index, (DbParameter)value);
    }

    /// <inheritdoc />
    public override void Remove(object value)
    {
        _items.Remove((DbParameter)value);
    }

    /// <inheritdoc />
    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            _items.RemoveAt(index);
        }
    }

    /// <inheritdoc />
    public override void RemoveAt(int index)
    {
        _items.RemoveAt(index);
    }

    /// <inheritdoc />
    protected override DbParameter GetParameter(string parameterName)
    {
        return _items[IndexOf(parameterName)];
    }

    /// <inheritdoc />
    protected override DbParameter GetParameter(int index)
    {
        return _items[index];
    }

    /// <inheritdoc />
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
        {
            _items[index] = value;
            return;
        }

        _items.Add(value);
    }

    /// <inheritdoc />
    protected override void SetParameter(int index, DbParameter value)
    {
        _items[index] = value;
    }
}

internal sealed class FakeDbDataReader(IReadOnlyList<FakeResultSet> resultSets) : DbDataReader
{
    private int _resultSetIndex;
    private int _rowIndex = -1;

    private IReadOnlyList<IReadOnlyDictionary<string, object?>> CurrentRows =>
        resultSets.Count == 0
            ? Array.Empty<IReadOnlyDictionary<string, object?>>()
            : resultSets[Math.Min(_resultSetIndex, resultSets.Count - 1)].Rows;

    private IReadOnlyList<string> CurrentColumns =>
        resultSets.Count == 0
            ? Array.Empty<string>()
            : resultSets[Math.Min(_resultSetIndex, resultSets.Count - 1)].Columns;

    /// <inheritdoc />
    public override int FieldCount => CurrentColumns.Count;

    /// <inheritdoc />
    public override bool HasRows => CurrentRows.Count > 0;

    /// <inheritdoc />
    public override bool IsClosed => false;

    /// <inheritdoc />
    public override int RecordsAffected => 0;

    /// <inheritdoc />
    public override int Depth => 0;

    /// <inheritdoc />
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc />
    public override object this[string name] => CurrentRows[_rowIndex][name] ?? DBNull.Value;

    /// <inheritdoc />
    public override bool Read()
    {
        if (_rowIndex + 1 >= CurrentRows.Count)
        {
            return false;
        }

        _rowIndex++;
        return true;
    }

    /// <inheritdoc />
    public override bool NextResult()
    {
        if (_resultSetIndex + 1 >= resultSets.Count)
        {
            return false;
        }

        _resultSetIndex++;
        _rowIndex = -1;
        return true;
    }

    /// <inheritdoc />
    public override string GetName(int ordinal)
    {
        return CurrentColumns[ordinal];
    }

    /// <inheritdoc />
    public override int GetOrdinal(string name)
    {
        return CurrentColumns.ToList().FindIndex(column => string.Equals(column, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public override object GetValue(int ordinal)
    {
        return GetCurrentValue(ordinal) ?? DBNull.Value;
    }

    /// <inheritdoc />
    public override int GetValues(object[] values)
    {
        var length = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < length; i++)
        {
            values[i] = GetValue(i);
        }

        return length;
    }

    /// <inheritdoc />
    public override bool IsDBNull(int ordinal)
    {
        var value = GetCurrentValue(ordinal);
        return value is null || value == DBNull.Value;
    }

    /// <inheritdoc />
    public override string GetDataTypeName(int ordinal)
    {
        return GetFieldType(ordinal).Name;
    }

    /// <inheritdoc />
    public override Type GetFieldType(int ordinal)
    {
        return GetCurrentValue(ordinal)?.GetType() ?? typeof(object);
    }

    /// <inheritdoc />
    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(GetValue(ordinal));

    /// <inheritdoc />
    public override byte GetByte(int ordinal) => Convert.ToByte(GetValue(ordinal));

    /// <inheritdoc />
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;

    /// <inheritdoc />
    public override char GetChar(int ordinal) => Convert.ToChar(GetValue(ordinal));

    /// <inheritdoc />
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;

    /// <inheritdoc />
    public override Guid GetGuid(int ordinal)
    {
        var value = GetValue(ordinal);
        return value is Guid guid ? guid : Guid.Parse(value.ToString()!);
    }

    /// <inheritdoc />
    public override short GetInt16(int ordinal) => Convert.ToInt16(GetValue(ordinal));

    /// <inheritdoc />
    public override int GetInt32(int ordinal) => Convert.ToInt32(GetValue(ordinal));

    /// <inheritdoc />
    public override long GetInt64(int ordinal) => Convert.ToInt64(GetValue(ordinal));

    /// <inheritdoc />
    public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(GetValue(ordinal));

    /// <inheritdoc />
    public override string GetString(int ordinal) => Convert.ToString(GetValue(ordinal)) ?? string.Empty;

    /// <inheritdoc />
    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(GetValue(ordinal));

    /// <inheritdoc />
    public override double GetDouble(int ordinal) => Convert.ToDouble(GetValue(ordinal));

    /// <inheritdoc />
    public override float GetFloat(int ordinal) => Convert.ToSingle(GetValue(ordinal));

    /// <inheritdoc />
    public override IEnumerator GetEnumerator()
    {
        return CurrentRows.GetEnumerator();
    }

    private object? GetCurrentValue(int ordinal)
    {
        if (_rowIndex < 0 || _rowIndex >= CurrentRows.Count || ordinal < 0 || ordinal >= CurrentColumns.Count)
        {
            return null;
        }

        var row = CurrentRows[_rowIndex];
        var column = CurrentColumns[ordinal];
        return row.TryGetValue(column, out var value) ? value : null;
    }
}
