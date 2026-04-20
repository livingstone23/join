using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Common.CommunicationChannels.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Common.CommunicationChannels.Queries.GetCommunicationChannelById;

/// <summary>
/// Contains the unit tests for the communication channel detail query handler.
/// These tests verify the successful detail path and the not-found behavior.
/// </summary>
public sealed class GetCommunicationChannelByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path when the communication channel exists.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCommunicationChannelExists_ShouldReturnCommunicationChannelDetails()
    {
        // Arrange
        var channelId = _fixture.Create<Guid>();
        var context = new GetCommunicationChannelByIdQueryHandlerTestContext();

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["Id"] = channelId,
                    ["Name"] = "WhatsApp",
                    ["Provider"] = "Twilio",
                    ["Code"] = "WA-001",
                    ["IsActive"] = true
                }));

        var query = new GetCommunicationChannelByIdQuery(channelId);
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Communication channel retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(channelId);
        response.Data.Name.Should().Be("WhatsApp");
        response.Data.Provider.Should().Be("Twilio");
        response.Data.Code.Should().Be("WA-001");
        response.Data.IsActive.Should().BeTrue();

        context.Connection.LastCommandText.Should().Contain("WHERE cc.Id = @Id");
        context.Connection.LastCommandText.Should().Contain("AND cc.GcRecord = 0");
        context.Connection.CapturedParameters["Id"].Should().Be(channelId);
    }

    /// <summary>
    /// Verifies the not-found branch when no active communication channel matches the request.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCommunicationChannelDoesNotExist_ShouldReturnNotFoundError()
    {
        // Arrange
        var context = new GetCommunicationChannelByIdQueryHandlerTestContext();
        context.Connection.SetResults(
            FakeResultSet.Empty(
                "Id",
                "Name",
                "Provider",
                "Code",
                "IsActive"));

        var query = new GetCommunicationChannelByIdQuery(_fixture.Create<Guid>());
        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(query, CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMMUNICATIONCHANNEL_NOT_FOUND");
        response.Errors.Should().Contain("Communication channel not found.");
    }

    /// <summary>
    /// Holds the reusable mocks and fake connection used by the communication channel detail tests.
    /// </summary>
    private sealed class GetCommunicationChannelByIdQueryHandlerTestContext
    {
        public GetCommunicationChannelByIdQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetCommunicationChannelByIdQueryHandler CreateHandler()
        {
            return new GetCommunicationChannelByIdQueryHandler(ConnectionFactoryMock.Object);
        }
    }
}
