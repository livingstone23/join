using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.Account.Queries.GetMySessions;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Queries.GetMySessions;

/// <summary>
/// Contains the unit tests for the authenticated user active sessions query handler.
/// </summary>
public sealed class GetMySessionsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies the happy path when no active sessions exist for the user.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoSessionsExist_ShouldReturnEmptyCollection()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var context = new GetMySessionsQueryHandlerTestContext();

        context.Connection.SetResults(
            FakeResultSet.Empty(
                "SessionId",
                "ConnectedAtUtc",
                "LastActivityAtUtc",
                "Device",
                "IpAddress"));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMySessionsQuery(userId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Message.Should().Be("Active sessions retrieved successfully.");
        response.Data.Should().NotBeNull();
        response.Data!.Should().BeEmpty();

        context.Connection.LastCommandText.Should().Contain("UNION ALL");
        context.Connection.LastCommandText.Should().Contain("IsActiveSession = 1");
        context.Connection.LastCommandText.Should().Contain("IsRevoked = 0");
        context.Connection.LastCommandText.Should().Contain("ExpiryDate > @UtcNow");
        context.Connection.CapturedParameters["UserId"].Should().Be(userId);
    }

    /// <summary>
    /// Verifies that mixed sessions are returned ordered by last activity and the one whose SessionId
    /// matches the caller's own refresh_token_id claim is marked as current — here that happens to be
    /// the most recently active row too.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSessionsExist_ShouldMarkCallersOwnSessionAsCurrent()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var newestSessionId = _fixture.Create<Guid>();
        var olderSessionId = _fixture.Create<Guid>();
        var newestActivity = new DateTime(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);
        var olderActivity = new DateTime(2026, 7, 11, 9, 0, 0, DateTimeKind.Utc);
        var context = new GetMySessionsQueryHandlerTestContext();
        context.CurrentUserServiceMock.Setup(x => x.RefreshTokenId).Returns(newestSessionId);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["SessionId"] = newestSessionId,
                    ["ConnectedAtUtc"] = newestActivity.AddHours(-1),
                    ["LastActivityAtUtc"] = newestActivity,
                    ["Device"] = "Mozilla/5.0",
                    ["IpAddress"] = "10.0.0.1"
                },
                new Dictionary<string, object?>
                {
                    ["SessionId"] = olderSessionId,
                    ["ConnectedAtUtc"] = olderActivity.AddHours(-2),
                    ["LastActivityAtUtc"] = olderActivity,
                    ["Device"] = "JWT Refresh Token",
                    ["IpAddress"] = null
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMySessionsQuery(userId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Should().HaveCount(2);

        var sessions = response.Data.ToList();
        sessions[0].SessionId.Should().Be(newestSessionId);
        sessions[0].IsCurrent.Should().BeTrue();
        sessions[0].Device.Should().Be("Mozilla/5.0");
        sessions[0].IpAddress.Should().Be("10.0.0.1");

        sessions[1].SessionId.Should().Be(olderSessionId);
        sessions[1].IsCurrent.Should().BeFalse();
        sessions[1].Device.Should().Be("JWT Refresh Token");
        sessions[1].IpAddress.Should().BeNull();
    }

    /// <summary>
    /// Regression test for the bug where <c>IsCurrent</c> was assigned to whichever row was most
    /// recently active instead of the row matching the caller's own refresh_token_id claim. This
    /// surfaced when a user had two browsers open: loading the list from the browser that had been
    /// idle longer showed the *other* browser's session as "current" (and its own as revocable),
    /// so revoking the mislabeled row was then rejected by RevokeMySessionCommandHandler with
    /// CANNOT_REVOKE_CURRENT because that row's id really did match the request's own refresh token.
    /// </summary>
    [Fact]
    public async Task Handle_WhenCallersOwnSessionIsNotTheMostRecentlyActive_ShouldStillMarkItAsCurrent()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var newestSessionId = _fixture.Create<Guid>(); // Another of the user's own sessions (e.g. a second browser), busier right now.
        var callersOwnSessionId = _fixture.Create<Guid>(); // The session actually making this request — idle for a minute, but still "current" from its own point of view.
        var newestActivity = new DateTime(2026, 7, 12, 9, 0, 0, DateTimeKind.Utc);
        var olderActivity = newestActivity.AddMinutes(-1);
        var context = new GetMySessionsQueryHandlerTestContext();
        context.CurrentUserServiceMock.Setup(x => x.RefreshTokenId).Returns(callersOwnSessionId);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["SessionId"] = newestSessionId,
                    ["ConnectedAtUtc"] = newestActivity.AddHours(-1),
                    ["LastActivityAtUtc"] = newestActivity,
                    ["Device"] = "JWT Refresh Token",
                    ["IpAddress"] = null
                },
                new Dictionary<string, object?>
                {
                    ["SessionId"] = callersOwnSessionId,
                    ["ConnectedAtUtc"] = olderActivity.AddHours(-2),
                    ["LastActivityAtUtc"] = olderActivity,
                    ["Device"] = "JWT Refresh Token",
                    ["IpAddress"] = null
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMySessionsQuery(userId), CancellationToken.None);

        // Assert
        var sessions = response.Data!.ToList();

        sessions.Single(s => s.SessionId == callersOwnSessionId).IsCurrent.Should().BeTrue();
        sessions.Single(s => s.SessionId == newestSessionId).IsCurrent.Should().BeFalse();
    }

    /// <summary>
    /// Legacy tokens predating the refresh_token_id claim resolve to a null RefreshTokenId — falls
    /// back to the previous heuristic (most recently active row) rather than marking nothing current.
    /// </summary>
    [Fact]
    public async Task Handle_WhenRefreshTokenIdClaimIsMissing_ShouldFallBackToMostRecentRow()
    {
        // Arrange
        var userId = _fixture.Create<Guid>();
        var newestSessionId = _fixture.Create<Guid>();
        var context = new GetMySessionsQueryHandlerTestContext();
        context.CurrentUserServiceMock.Setup(x => x.RefreshTokenId).Returns((Guid?)null);

        context.Connection.SetResults(
            FakeResultSet.FromRows(
                new Dictionary<string, object?>
                {
                    ["SessionId"] = newestSessionId,
                    ["ConnectedAtUtc"] = DateTime.UtcNow,
                    ["LastActivityAtUtc"] = DateTime.UtcNow,
                    ["Device"] = "JWT Refresh Token",
                    ["IpAddress"] = null
                }));

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new GetMySessionsQuery(userId), CancellationToken.None);

        // Assert
        response.Data!.Single().IsCurrent.Should().BeTrue();
    }

    private sealed class GetMySessionsQueryHandlerTestContext
    {
        public GetMySessionsQueryHandlerTestContext()
        {
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetMySessionsQueryHandler CreateHandler()
        {
            return new GetMySessionsQueryHandler(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
        }
    }
}
