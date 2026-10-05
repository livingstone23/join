using System.Data;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.Common.TestDoubles;
using FakeResultSet = JOIN.Application.UnitTest.Common.TestDoubles.FakeResultSet;
using JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Queries.GetTicketStatusTransitions;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketStatusTransitions.Queries.GetTicketStatusTransitions;

/// <summary>
/// Unit tests for <see cref="GetTicketStatusTransitionsQueryHandler"/>. Verifies
/// tenant-scoped filtering, optional <c>FromStatusId</c> filter, and the Dapper SQL
/// boundary built by the handler.
/// </summary>
public sealed class GetTicketStatusTransitionsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequired()
    {
        var context = new GetTicketStatusTransitionsContext(Guid.Empty);
        var handler = context.CreateHandler();

        var response = await handler.Handle(new GetTicketStatusTransitionsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNoFilter_ShouldQueryWithoutExtraClauses()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetTicketStatusTransitionsContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "FromStatusId", "FromStatusName", "ToStatusId", "ToStatusName", "CreatedAt"));

        var handler = context.CreateHandler();
        var response = await handler.Handle(new GetTicketStatusTransitionsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Connection.LastCommandText.Should().Contain("WHERE tst.CompanyId = @TenantId AND tst.GcRecord = 0");
        context.Connection.LastCommandText.Should().NotContain("AND tst.FromStatusId = @FromStatusId");
    }

    [Fact]
    public async Task Handle_WhenFromStatusIdFilterProvided_ShouldAppendExtraClause()
    {
        var companyId = _fixture.Create<Guid>();
        var fromStatusId = _fixture.Create<Guid>();
        var context = new GetTicketStatusTransitionsContext(companyId);
        context.Connection.SetResults(
            FakeResultSet.Empty("Id", "FromStatusId", "FromStatusName", "ToStatusId", "ToStatusName", "CreatedAt"));

        var handler = context.CreateHandler();
        var response = await handler.Handle(
            new GetTicketStatusTransitionsQuery { FromStatusId = fromStatusId },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Connection.LastCommandText.Should().Contain("AND tst.FromStatusId = @FromStatusId");
        context.Connection.CapturedParameters.Should().ContainKey("FromStatusId");
    }

    /// <summary>
    /// Wires the fake Dapper connection, the mocked connection factory, and the
    /// mocked <c>ICurrentUserService</c> for the tenant the test wants to simulate.
    /// </summary>
    private sealed class GetTicketStatusTransitionsContext
    {
        public GetTicketStatusTransitionsContext(Guid companyId)
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(() => Connection.CreateConnection());
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
        }

        public FakeSqlConnectionFactory Connection { get; } = new();
        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new(MockBehavior.Strict);
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();

        public GetTicketStatusTransitionsQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}