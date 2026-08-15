using AutoFixture;
using FluentAssertions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.RevokeOtherMySessions;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.RevokeOtherMySessions;

/// <summary>
/// Unit tests for the <c>POST /api/v1/account/sessions/revoke-others</c> handler (SPEC 26 / F3).
/// </summary>
public sealed class RevokeOtherMySessionsCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Happy path: a mix of refresh tokens + active connection logs is closed except the
    /// current refresh token.
    /// </summary>
    [Fact]
    public async Task Handle_WhenMixedSessionsExist_ShouldRevokeExceptCurrent()
    {
        // Arrange
        var context = new RevokeOtherMySessionsCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var currentRefreshTokenId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeRefreshTokensExceptAsync(
                callerId, currentRefreshTokenId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeActiveConnectionsAsync(
                callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeOtherMySessionsCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.RevokedTokens.Should().Be(2);
        response.Data.RevokedConnections.Should().Be(3);

        VerifyBulkRevokeCalled(context, callerId, currentRefreshTokenId);
        VerifySecurityEventLogged(context, callerId);
    }

    /// <summary>
    /// Edge case: no other sessions exist. Returns success with both counters at zero.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNoOtherSessionsExist_ShouldReturnZeroCountersAndStillLog()
    {
        // Arrange
        var context = new RevokeOtherMySessionsCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var currentRefreshTokenId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeRefreshTokensExceptAsync(
                callerId, currentRefreshTokenId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeActiveConnectionsAsync(
                callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeOtherMySessionsCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.RevokedTokens.Should().Be(0);
        response.Data.RevokedConnections.Should().Be(0);
        response.Message.Should().Contain("No other active sessions");

        VerifySecurityEventLogged(context, callerId);
    }

    /// <summary>
    /// Edge case: the only active session is the current refresh token. Bulk update should
    /// exclude the live row, returning zero.
    /// </summary>
    [Fact]
    public async Task Handle_WhenOnlyCurrentSessionExists_ShouldReturnZeroAndExcludeCurrent()
    {
        // Arrange
        var context = new RevokeOtherMySessionsCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var currentRefreshTokenId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeRefreshTokensExceptAsync(
                callerId, currentRefreshTokenId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeActiveConnectionsAsync(
                callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeOtherMySessionsCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.RevokedTokens.Should().Be(0);
        response.Data.RevokedConnections.Should().Be(0);

        // Critically: the current refresh token id must be supplied to the repo as the exclusion.
        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeRefreshTokensExceptAsync(
                callerId,
                currentRefreshTokenId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Pre-spec token fallback: when the JWT was issued before SPEC 26 the refresh_token_id
    /// claim is absent. The handler must still attempt to revoke everything (every active token,
    /// since no current row can be excluded).
    /// </summary>
    [Fact]
    public async Task Handle_WhenCurrentRefreshTokenIdIsNull_ShouldRevokeAllTokensAndConnections()
    {
        // Arrange
        var context = new RevokeOtherMySessionsCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId: null);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeRefreshTokensExceptAsync(
                callerId, null, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeActiveConnectionsAsync(
                callerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeOtherMySessionsCommand(), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.RevokedTokens.Should().Be(4);
        response.Data.RevokedConnections.Should().Be(1);

        // Null current-refresh-token must reach the repo so the SQL filter stays a no-op
        // and ALL active tokens are revoked (acceptable for the post-deploy stale-token window).
        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeRefreshTokensExceptAsync(
                callerId,
                null,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        VerifySecurityEventLogged(context, callerId);
    }

    /// <summary>
    /// Asserts the bulk-revoke pair reached the repository.
    /// </summary>
    private static void VerifyBulkRevokeCalled(
        RevokeOtherMySessionsCommandHandlerTestContext context,
        Guid expectedUserId,
        Guid expectedCurrentRefreshTokenId)
    {
        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeRefreshTokensExceptAsync(
                expectedUserId,
                expectedCurrentRefreshTokenId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeActiveConnectionsAsync(
                expectedUserId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Asserts the security-event logger received exactly one <c>SessionsRevokedOthers</c> call.
    /// </summary>
    private static void VerifySecurityEventLogged(
        RevokeOtherMySessionsCommandHandlerTestContext context,
        Guid expectedUserId)
    {
        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                SecurityEventType.SessionsRevokedOthers,
                SecurityEventResult.Success,
                expectedUserId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Self-contained mocks for the handler dependencies.
    /// </summary>
    private sealed class RevokeOtherMySessionsCommandHandlerTestContext
    {
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IRoleUserSessionRepository> SessionRepositoryMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public void SetCaller(Guid userId, Guid? currentRefreshTokenId)
        {
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(userId.ToString());
            CurrentUserServiceMock.SetupGet(x => x.RefreshTokenId).Returns(currentRefreshTokenId);
            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns("127.0.0.1");
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns("unit-test");
        }

        public RevokeOtherMySessionsCommandHandler CreateHandler() =>
            new(
                CurrentUserServiceMock.Object,
                SessionRepositoryMock.Object,
                SecurityEventLoggerMock.Object);
    }
}
