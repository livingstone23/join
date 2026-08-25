using System.Data;
using System.Data.Common;
using JOIN.Application.Interface;

namespace JOIN.Application.UnitTest.Common.TestDoubles;

/// <summary>
/// Minimal Dapper-friendly fake connection used by query-handler tests across SPEC 28.
/// Inherits from <see cref="DbConnection"/> / <see cref="DbCommand"/> so Dapper's
/// async path recognizes it. Returns pre-configured result sets in order.
/// </summary>
internal sealed class FakeSqlConnectionFactory : ISqlConnectionFactory
{
    private readonly FakeDbConnection _connection = new();

    public IDbConnection CreateConnection() => _connection;

    public void SetResults(params FakeResultSet[] resultSets)
        => _connection.SetResults(resultSets);

    public string LastCommandText => _connection.LastCommandText;

    public IReadOnlyDictionary<string, object?> CapturedParameters
        => _connection.CapturedParameters;
}

internal sealed class FakeDbConnection : DbConnection
{
    private readonly List<FakeResultSet> _resultSets = new();

    // Shared across every command created from this connection. The original
    // design put the cursor on the command, which meant every new DbCommand
    // reset to result set 0 — and Dapper's per-call command model then read
    // the first result set for *every* query, breaking multi-query handlers
    // (e.g. ReplaceUserRoles makes 3 sequential Dapper calls; the second and
    // third were getting the first result set). Sharing on the connection
    // mirrors a real ADO.NET connection: a single open cursor advances
    // across commands.
    internal int ResultSetCursor;

    public string LastCommandText { get; private set; } = string.Empty;

    public Dictionary<string, object?> CapturedParameters { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public void SetResults(params FakeResultSet[] resultSets)
    {
        _resultSets.Clear();
        _resultSets.AddRange(resultSets);
        ResultSetCursor = 0;
    }

    public override string ConnectionString { get; set; } = string.Empty;
    public override string Database => "FakeDb";
    public override string DataSource => "FakeSource";
    public override string ServerVersion => "1.0";
    public override ConnectionState State => ConnectionState.Open;

    public override void ChangeDatabase(string databaseName) { }
    public override void Close() { }
    public override void Open() { }

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();

    protected override DbCommand CreateDbCommand()
    {
        // Single-command mode: every command sees ALL configured result sets.
        // Dapper uses this for QueryMultipleAsync where one command produces
        // several grids that the reader iterates via NextResult().
        return new FakeDbCommand(this, _resultSets);
    }

    internal void CaptureParameter(string name, object? value)
    {
        CapturedParameters[name] = value;
    }

    internal void CaptureExecution(string commandText, DbParameterCollection parameters)
    {
        LastCommandText = commandText;
    }
}

internal sealed class FakeDbCommand : DbCommand
{
    private readonly FakeDbConnection _connection;
    private readonly List<FakeResultSet> _resultSets;
    private readonly FakeDbParameterCollection _parameters;
    private int _rowIndex = -1;

    public FakeDbCommand(FakeDbConnection connection, List<FakeResultSet> resultSets)
    {
        _connection = connection;
        _resultSets = resultSets;
        _parameters = new FakeDbParameterCollection(connection);
    }

    internal FakeResultSet? CurrentResultSet
        => _connection.ResultSetCursor >= 0 && _connection.ResultSetCursor < _resultSets.Count
            ? _resultSets[_connection.ResultSetCursor]
            : null;

    internal bool AdvanceToNextResultSet()
    {
        _rowIndex = -1;
        _connection.ResultSetCursor++;
        return _connection.ResultSetCursor < _resultSets.Count;
    }

    public override string? CommandText { get; set; }
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; } = CommandType.Text;
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection DbConnection { get; set; } = null!;
    protected override DbParameterCollection DbParameterCollection => _parameters;
    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel() { }
    public override void Prepare() { }

    public override int ExecuteNonQuery()
    {
        _connection.CaptureExecution(CommandText ?? string.Empty, _parameters);
        return 1;
    }

    public override object? ExecuteScalar()
    {
        _connection.CaptureExecution(CommandText ?? string.Empty, _parameters);
        return CurrentResultSet is { Rows.Count: > 0 } set
            ? set.Rows[0][0].Value
            : null;
    }

    protected override DbParameter CreateDbParameter() => new FakeDbParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        _connection.CaptureExecution(CommandText ?? string.Empty, _parameters);
        _rowIndex = -1;
        return new FakeDbDataReader(this);
    }
}

internal sealed class FakeDbParameter : DbParameter
{
    public override string ParameterName { get; set; } = string.Empty;
    public override object? Value { get; set; }
    public override DbType DbType { get; set; }
    public override ParameterDirection Direction { get; set; }
    public override bool IsNullable { get; set; }
    public override string SourceColumn { get; set; } = string.Empty;
    public override bool SourceColumnNullMapping { get; set; }
    public override int Size { get; set; }
    public override byte Precision { get; set; }
    public override byte Scale { get; set; }
    public override void ResetDbType() { }
    public override DataRowVersion SourceVersion { get; set; }
}

internal sealed class FakeDbParameterCollection : DbParameterCollection
{
    private readonly FakeDbConnection _connection;
    private readonly List<DbParameter> _items = new();

    public FakeDbParameterCollection(FakeDbConnection connection)
    {
        _connection = connection;
    }

    public override int Count => _items.Count;
    public override object SyncRoot => this;

    public override int Add(object value)
    {
        _items.Add((DbParameter)value);
        var parameter = (DbParameter)value;
        if (parameter.ParameterName is { Length: > 0 } name)
        {
            _connection.CaptureParameter(name.TrimStart('@'), parameter.Value);
        }
        return _items.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var item in values) _items.Add((DbParameter)item);
    }

    public override void Clear() => _items.Clear();
    public override bool Contains(object value) => _items.Contains((DbParameter)value);
    public override bool Contains(string value) => _items.Any(p => p.ParameterName == value);
    public override void CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);
    public override System.Collections.IEnumerator GetEnumerator() => _items.GetEnumerator();
    protected override DbParameter GetParameter(int index) => _items[index];
    protected override DbParameter GetParameter(string parameterName)
        => _items.First(p => p.ParameterName == parameterName);
    public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);
    public override int IndexOf(string parameterName)
        => _items.FindIndex(p => p.ParameterName == parameterName);
    public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
    public override void Remove(object value) => _items.Remove((DbParameter)value);
    public override void RemoveAt(int index) => _items.RemoveAt(index);
    public override void RemoveAt(string parameterName)
        => _items.RemoveAll(p => p.ParameterName == parameterName);
    protected override void SetParameter(int index, DbParameter value) => _items[index] = value;
    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index < 0) Add(value);
        else _items[index] = value;
    }
}

internal sealed class FakeResultSet
{
    public FakeResultSet(IEnumerable<IDictionary<string, object?>> rows, IEnumerable<string>? columns = null)
    {
        // Use a List<KeyValuePair> so column ordinals (i.e. ordinal-based reads)
        // stay stable across .NET versions. Dictionary.Values enumeration order is
        // not guaranteed, which broke Dapper's column mapping when this fake used
        // a plain Dictionary.Values.ElementAt lookup.
        Rows = rows
            .Select(r => (IReadOnlyList<KeyValuePair<string, object?>>)r
                .Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value))
                .ToList())
            .ToList();
        // Honor an explicit column list when no rows are present — Dapper throws
        // "No columns were selected" without it. When rows are present the column
        // list is derived from the first row's keys so positional reads stay in
        // sync with the row data.
        Columns = columns?.ToList() ?? Rows.FirstOrDefault()?.Select(kv => kv.Key).ToList() ?? new List<string>();
    }

    public IReadOnlyList<IReadOnlyList<KeyValuePair<string, object?>>> Rows { get; }
    public IReadOnlyList<string> Columns { get; }

    public static FakeResultSet FromRows(params IDictionary<string, object?>[] rows)
        => new(rows);

    public static FakeResultSet Empty(params string[] columns)
        => new(Array.Empty<IDictionary<string, object?>>(), columns);

    public static FakeResultSet FromScalar(object? value)
        => new(new[] { new Dictionary<string, object?> { ["Value"] = value } });
}

internal sealed class FakeDbDataReader : DbDataReader
{
    private readonly FakeDbCommand _command;
    private int _rowIndex = -1;

    public FakeDbDataReader(FakeDbCommand command) => _command = command;

    private IReadOnlyList<KeyValuePair<string, object?>>? CurrentRow
        => _command.CurrentResultSet is { Rows.Count: > 0 } set
            && _rowIndex >= 0 && _rowIndex < set.Rows.Count
            ? set.Rows[_rowIndex]
            : null;

    public override bool HasRows => _command.CurrentResultSet is { Rows.Count: > 0 };
    public override bool IsClosed => false;
    public override int RecordsAffected => 0;
    public override int FieldCount => _command.CurrentResultSet?.Columns.Count ?? 0;
    public override int Depth => 0;

    public override object this[int ordinal]
    {
        get
        {
            if (CurrentRow is not { } row) return DBNull.Value;
            return ordinal >= 0 && ordinal < row.Count
                ? row[ordinal].Value ?? DBNull.Value
                : throw new IndexOutOfRangeException();
        }
    }

    public override object this[string name]
    {
        get
        {
            if (CurrentRow is not { } row) return DBNull.Value;
            var match = row.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase));
            return match.Key is not null ? (match.Value ?? DBNull.Value) : throw new KeyNotFoundException(name);
        }
    }

    public override void Close() { }

    public override bool Read()
    {
        _rowIndex++;
        return _command.CurrentResultSet is { Rows: var rows } && _rowIndex < rows.Count;
    }

    public override System.Threading.Tasks.Task<bool> ReadAsync(System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(Read());

    public override bool NextResult() => _command.AdvanceToNextResultSet();

    public override System.Threading.Tasks.Task<bool> NextResultAsync(System.Threading.CancellationToken cancellationToken)
        => System.Threading.Tasks.Task.FromResult(_command.AdvanceToNextResultSet());

    public override DataTable GetSchemaTable() => throw new NotSupportedException();
    public override bool GetBoolean(int ordinal) => Convert.ToBoolean(this[ordinal]);
    public override byte GetByte(int ordinal) => Convert.ToByte(this[ordinal]);
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();
    public override char GetChar(int ordinal) => Convert.ToChar(this[ordinal]);
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length)
        => throw new NotSupportedException();
    public override string GetDataTypeName(int ordinal) => this[ordinal]?.GetType().Name ?? string.Empty;
    public override DateTime GetDateTime(int ordinal) => Convert.ToDateTime(this[ordinal]);
    public override decimal GetDecimal(int ordinal) => Convert.ToDecimal(this[ordinal]);
    public override double GetDouble(int ordinal) => Convert.ToDouble(this[ordinal]);
    public override Type GetFieldType(int ordinal)
    {
        // No row has been read yet — fall back to object so Dapper's column
        // hashing succeeds on empty result sets. With rows, infer from the
        // stored value's CLR type.
        var value = this[ordinal];
        return value is DBNull ? typeof(object) : value?.GetType() ?? typeof(object);
    }
    public override float GetFloat(int ordinal) => Convert.ToSingle(this[ordinal]);
    public override Guid GetGuid(int ordinal)
    {
        var value = this[ordinal];
        return value switch
        {
            Guid g => g,
            null => Guid.Empty,
            _ => Guid.Parse(value.ToString() ?? string.Empty)
        };
    }
    public override short GetInt16(int ordinal) => Convert.ToInt16(this[ordinal]);
    public override int GetInt32(int ordinal) => Convert.ToInt32(this[ordinal]);
    public override long GetInt64(int ordinal) => Convert.ToInt64(this[ordinal]);
    public override string GetName(int ordinal)
    {
        // Column metadata lives on the result set, not the row — even when the
        // row count is zero. The handler relies on this to map UserManagementReportSqlRow.
        var columns = _command.CurrentResultSet?.Columns;
        if (columns is null || ordinal < 0 || ordinal >= columns.Count)
        {
            throw new IndexOutOfRangeException();
        }
        return columns[ordinal];
    }
    public override int GetOrdinal(string name)
    {
        var columns = _command.CurrentResultSet?.Columns;
        if (columns is null) throw new IndexOutOfRangeException(name);
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        throw new IndexOutOfRangeException(name);
    }
    public override string GetString(int ordinal) => this[ordinal]?.ToString() ?? string.Empty;
    public override object GetValue(int ordinal) => this[ordinal] ?? DBNull.Value;
    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, FieldCount);
        for (var i = 0; i < count; i++) values[i] = GetValue(i);
        return count;
    }
    public override bool IsDBNull(int ordinal) => this[ordinal] is null or DBNull;

    public override System.Collections.IEnumerator GetEnumerator()
        => throw new NotSupportedException();
}