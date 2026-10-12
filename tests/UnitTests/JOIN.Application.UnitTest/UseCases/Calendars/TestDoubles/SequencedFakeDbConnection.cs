using System.Data.Common;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;

namespace JOIN.Application.UnitTest.UseCases.Calendars.TestDoubles;

/// <summary>
/// Fake Dapper connection that answers each command with its own batch of result sets, in order, for handlers that
/// run several queries (calendar configuration, then rows, then exceptions…). Records every SQL text executed.
/// </summary>
internal sealed class SequencedFakeDbConnection : FakeDbConnection
{
    private readonly Queue<FakeResultSet[]> _batches = new();

    public List<string> Commands { get; } = [];

    /// <summary> Queues the result sets of the next command. </summary>
    public SequencedFakeDbConnection Then(params FakeResultSet[] resultSets)
    {
        _batches.Enqueue(resultSets);
        return this;
    }

    protected override DbCommand CreateDbCommand() =>
        new RecordingCommand(this, _batches.Count > 0 ? _batches.Dequeue() : []);

    private sealed class RecordingCommand(SequencedFakeDbConnection owner, FakeResultSet[] resultSets) : DbCommand
    {
        private readonly FakeDbCommand _inner = new(owner, resultSets);

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get => _inner.CommandText ?? string.Empty; set => _inner.CommandText = value; }
        public override int CommandTimeout { get => _inner.CommandTimeout; set => _inner.CommandTimeout = value; }
        public override System.Data.CommandType CommandType { get => _inner.CommandType; set => _inner.CommandType = value; }
        public override bool DesignTimeVisible { get => _inner.DesignTimeVisible; set => _inner.DesignTimeVisible = value; }
        public override System.Data.UpdateRowSource UpdatedRowSource { get => _inner.UpdatedRowSource; set => _inner.UpdatedRowSource = value; }
        protected override DbConnection? DbConnection { get => owner; set { } }
        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() => _inner.Cancel();
        public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();
        public override object? ExecuteScalar() => _inner.ExecuteScalar();
        public override void Prepare() => _inner.Prepare();
        protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

        protected override DbDataReader ExecuteDbDataReader(System.Data.CommandBehavior behavior)
        {
            owner.Commands.Add(CommandText);
            return _inner.ExecuteReader(behavior);
        }
    }
}
