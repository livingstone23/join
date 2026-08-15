using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Account.Commands.RevokeMySession;
using JOIN.Domain.Security;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Commands.RevokeMySession;

/// <summary>
/// Unit tests for the <c>DELETE /api/v1/account/sessions/{sessionId}</c> handler (SPEC 26 / F2).
/// </summary>
public sealed class RevokeMySessionCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// UserConnectionLog hit: the revoke targets the connection log row and marks the session closed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSessionIsUserConnectionLogAndOwnedByCaller_ShouldCloseSession()
    {
        // Arrange
        var context = new RevokeMySessionCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var sessionId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId: null);

        context.SessionRepositoryMock
            .Setup(x => x.FindActiveByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionLookupResult(sessionId, SessionType.UserConnectionLog));

        context.SessionRepositoryMock
            .Setup(x => x.GetUserIdBySessionIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerId);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeSingleConnectionAsync(sessionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeMySessionCommand(sessionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.RevokedConnections.Should().Be(1);
        response.Data.RevokedTokens.Should().Be(0);
        response.Data.SessionType.Should().Be(nameof(SessionType.UserConnectionLog));

        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeSingleConnectionAsync(sessionId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeSingleRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifySecurityEventLogged(context, callerId, SecurityEventType.SessionRevoked);
    }

    /// <summary>
    /// UserRefreshToken hit: the revoke targets a refresh token that is NOT the live one.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSessionIsUserRefreshTokenAndNotCurrent_ShouldRevokeToken()
    {
        // Arrange
        var context = new RevokeMySessionCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var sessionId = _fixture.Create<Guid>();
        var currentRefreshTokenId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId);

        context.SessionRepositoryMock
            .Setup(x => x.FindActiveByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionLookupResult(sessionId, SessionType.UserRefreshToken));

        context.SessionRepositoryMock
            .Setup(x => x.GetUserIdBySessionIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerId);

        context.SessionRepositoryMock
            .Setup(x => x.SoftRevokeSingleRefreshTokenAsync(sessionId, currentRefreshTokenId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeMySessionCommand(sessionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeTrue();
        response.Data!.RevokedConnections.Should().Be(0);
        response.Data.RevokedTokens.Should().Be(1);
        response.Data.SessionType.Should().Be(nameof(SessionType.UserRefreshToken));

        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeSingleRefreshTokenAsync(sessionId, currentRefreshTokenId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        VerifySecurityEventLogged(context, callerId, SecurityEventType.SessionRevoked);
    }

    /// <summary>
    /// Cross-user attempt: id resolves to another user. We deliberately return SESSION_NOT_FOUND,
    /// not FORBIDDEN, so the API does not leak row ownership.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSessionBelongsToAnotherUser_ShouldReturnSessionNotFound()
    {
        // Arrange
        var context = new RevokeMySessionCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var otherUserId = _fixture.Create<Guid>();
        var sessionId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId: null);

        context.SessionRepositoryMock
            .Setup(x => x.FindActiveByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionLookupResult(sessionId, SessionType.UserRefreshToken));

        context.SessionRepositoryMock
            .Setup(x => x.GetUserIdBySessionIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherUserId);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeMySessionCommand(sessionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SESSION_NOT_FOUND");

        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeSingleConnectionAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeSingleRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoSecurityEvent(context);
    }

    /// <summary>
    /// Trying to revoke the current refresh token that authenticated the request fails fast
    /// with CANNOT_REVOKE_CURRENT.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSessionIsCurrentRefreshToken_ShouldReturnCannotRevokeCurrent()
    {
        // Arrange
        var context = new RevokeMySessionCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var sessionId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId: sessionId);

        context.SessionRepositoryMock
            .Setup(x => x.FindActiveByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SessionLookupResult(sessionId, SessionType.UserRefreshToken));

        context.SessionRepositoryMock
            .Setup(x => x.GetUserIdBySessionIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(callerId);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeMySessionCommand(sessionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("CANNOT_REVOKE_CURRENT");

        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeSingleRefreshTokenAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.SessionRepositoryMock.Verify(
            x => x.SoftRevokeSingleConnectionAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoSecurityEvent(context);
    }

    /// <summary>
    /// Not-found branch: the supplied id does not resolve to any live row in either table.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSessionDoesNotExist_ShouldReturnSessionNotFound()
    {
        // Arrange
        var context = new RevokeMySessionCommandHandlerTestContext();
        var callerId = _fixture.Create<Guid>();
        var sessionId = _fixture.Create<Guid>();

        context.SetCaller(callerId, currentRefreshTokenId: null);

        context.SessionRepositoryMock
            .Setup(x => x.FindActiveByIdAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionLookupResult?)null);

        var handler = context.CreateHandler();

        // Act
        var response = await handler.Handle(new RevokeMySessionCommand(sessionId), CancellationToken.None);

        // Assert
        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("SESSION_NOT_FOUND");

        context.SessionRepositoryMock.Verify(
            x => x.GetUserIdBySessionIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoSecurityEvent(context);
    }

    /// <summary>
    /// Asserts that the supplied logger recorded exactly one event of the expected type for the user.
    /// </summary>
    private static void VerifySecurityEventLogged(
        RevokeMySessionCommandHandlerTestContext context,
        Guid expectedUserId,
        SecurityEventType expectedType)
    {
        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                expectedType,
                SecurityEventResult.Success,
                expectedUserId,
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Asserts that the logger was not invoked at all for the current test.
    /// </summary>
    private static void VerifyNoSecurityEvent(RevokeMySessionCommandHandlerTestContext context)
    {
        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                It.IsAny<SecurityEventType>(),
                It.IsAny<SecurityEventResult>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Self-contained mocks for the handler dependencies. Keeps the test file independent.
    /// </summary>
    private sealed class RevokeMySessionCommandHandlerTestContext
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

        public RevokeMySessionCommandHandler CreateHandler() =>
            new(
                CurrentUserServiceMock.Object,
                SessionRepositoryMock.Object,
                SecurityEventLoggerMock.Object);
    }
}
