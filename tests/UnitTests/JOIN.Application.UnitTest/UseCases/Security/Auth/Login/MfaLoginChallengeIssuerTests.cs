using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Common.Options;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Domain.Security;
using Microsoft.Extensions.Options;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.Login;

/// <summary>
/// Contains the unit tests for <see cref="MfaLoginChallengeIssuer"/>: challenge persistence,
/// available-methods derivation, and the challenge-shaped <see cref="LoginResponse"/> — SPEC 32 F5.
/// </summary>
public sealed class MfaLoginChallengeIssuerTests
{
    private readonly Fixture _fixture = new();

    /// <summary>
    /// Verifies that only TOTP is offered when it is the sole active method, and that the
    /// response is shaped as a challenge (no Token/RefreshToken/Expiration, Roles empty).
    /// </summary>
    [Fact]
    public async Task IssueChallengeAsync_WhenOnlyTotpEnabled_ShouldOfferTotpOnlyAndShapeChallengeResponse()
    {
        // Arrange
        var context = new MfaLoginChallengeIssuerTestContext();
        var user = CreateUser(isMfaEnabled: true, isEmailOtpEnabled: false);
        var targetCompanyId = _fixture.Create<Guid>();

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueChallengeAsync(user, targetCompanyId, CancellationToken.None);

        // Assert
        response.UserId.Should().Be(user.Id);
        response.Email.Should().Be(user.Email);
        response.CompanyId.Should().BeNull();
        response.Roles.Should().BeEmpty();
        response.Token.Should().BeNull();
        response.RefreshToken.Should().BeNull();
        response.Expiration.Should().BeNull();
        response.ChallengeToken.Should().NotBeNullOrWhiteSpace();
        response.AvailableMethods.Should().ContainSingle().Which.Should().Be("totp");
        response.PreferredMethod.Should().Be(user.PreferredMfaMethod);
    }

    /// <summary>
    /// Verifies that only email is offered when it is the sole active method.
    /// </summary>
    [Fact]
    public async Task IssueChallengeAsync_WhenOnlyEmailOtpEnabled_ShouldOfferEmailOnly()
    {
        // Arrange
        var context = new MfaLoginChallengeIssuerTestContext();
        var user = CreateUser(isMfaEnabled: false, isEmailOtpEnabled: true);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueChallengeAsync(user, null, CancellationToken.None);

        // Assert
        response.AvailableMethods.Should().ContainSingle().Which.Should().Be("email");
    }

    /// <summary>
    /// Verifies that both methods are offered when both are active, and that the preferred
    /// method reflects the persisted user preference.
    /// </summary>
    [Fact]
    public async Task IssueChallengeAsync_WhenBothMethodsEnabled_ShouldOfferBothAndReflectPreferredMethod()
    {
        // Arrange
        var context = new MfaLoginChallengeIssuerTestContext();
        var user = CreateUser(isMfaEnabled: true, isEmailOtpEnabled: true);
        user.PreferredMfaMethod = "email";

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueChallengeAsync(user, null, CancellationToken.None);

        // Assert
        response.AvailableMethods.Should().BeEquivalentTo(["totp", "email"]);
        response.PreferredMethod.Should().Be("email");
    }

    /// <summary>
    /// Verifies that the persisted challenge row carries a token hash that verifies against the
    /// opaque token returned to the client, and expires per the configured window.
    /// </summary>
    [Fact]
    public async Task IssueChallengeAsync_ShouldPersistChallengeWithVerifiableTokenHashAndConfiguredExpiration()
    {
        // Arrange
        var context = new MfaLoginChallengeIssuerTestContext(challengeExpirationMinutes: 7);
        var user = CreateUser(isMfaEnabled: true, isEmailOtpEnabled: false);
        var targetCompanyId = _fixture.Create<Guid>();
        var beforeUtc = DateTime.UtcNow;

        MfaLoginChallenge? insertedChallenge = null;
        context.ChallengeRepositoryMock
            .Setup(x => x.InsertAsync(It.IsAny<MfaLoginChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<MfaLoginChallenge, CancellationToken>((challenge, _) => insertedChallenge = challenge)
            .Returns(Task.CompletedTask);

        var issuer = context.CreateIssuer();

        // Act
        var response = await issuer.IssueChallengeAsync(user, targetCompanyId, CancellationToken.None);
        var afterUtc = DateTime.UtcNow;

        // Assert
        insertedChallenge.Should().NotBeNull();
        insertedChallenge!.UserId.Should().Be(user.Id);
        insertedChallenge.TargetCompanyId.Should().Be(targetCompanyId);
        insertedChallenge.AttemptCount.Should().Be(0);
        insertedChallenge.ConsumedAtUtc.Should().BeNull();

        OpaqueTokenHasher.Hash(response.ChallengeToken!).Should().Be(insertedChallenge.TokenHash);

        insertedChallenge.ExpiresAtUtc.Should().BeOnOrAfter(beforeUtc.AddMinutes(7));
        insertedChallenge.ExpiresAtUtc.Should().BeOnOrBefore(afterUtc.AddMinutes(7));
    }

    /// <summary>
    /// Verifies that the <c>LoginMfaRequired</c> security event is logged for the challenged user.
    /// </summary>
    [Fact]
    public async Task IssueChallengeAsync_ShouldLogLoginMfaRequiredEvent()
    {
        // Arrange
        var context = new MfaLoginChallengeIssuerTestContext();
        var user = CreateUser(isMfaEnabled: true, isEmailOtpEnabled: true);

        var issuer = context.CreateIssuer();

        // Act
        await issuer.IssueChallengeAsync(user, null, CancellationToken.None);

        // Assert
        context.SecurityEventLoggerMock.Verify(
            x => x.LogAsync(
                SecurityEventType.LoginMfaRequired,
                SecurityEventResult.Success,
                user.Id,
                context.IpAddress,
                context.UserAgent,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Creates a user with the requested 2FA methods active.
    /// </summary>
    private static ApplicationUser CreateUser(bool isMfaEnabled, bool isEmailOtpEnabled)
    {
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "join.user",
            Email = "user@join.com",
            IsActive = true,
            IsMfaEnabled = isMfaEnabled,
            IsEmailOtpEnabled = isEmailOtpEnabled,
            GcRecord = 0
        };
    }

    /// <summary>
    /// Holds the reusable mocks and helper factory for the challenge issuer.
    /// </summary>
    private sealed class MfaLoginChallengeIssuerTestContext
    {
        public MfaLoginChallengeIssuerTestContext(int challengeExpirationMinutes = 5)
        {
            ChallengeRepositoryMock
                .Setup(x => x.InsertAsync(It.IsAny<MfaLoginChallenge>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            MfaOptions = Options.Create(new MfaOptions { ChallengeExpirationMinutes = challengeExpirationMinutes });

            CurrentUserServiceMock.SetupGet(x => x.IpAddress).Returns(IpAddress);
            CurrentUserServiceMock.SetupGet(x => x.UserAgent).Returns(UserAgent);
        }

        public string IpAddress { get; } = "127.0.0.1";
        public string UserAgent { get; } = "unit-test";

        public Mock<IMfaLoginChallengeRepository> ChallengeRepositoryMock { get; } = new();
        public IOptions<MfaOptions> MfaOptions { get; }
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<ISecurityEventLogger> SecurityEventLoggerMock { get; } = new();

        public MfaLoginChallengeIssuer CreateIssuer()
        {
            return new MfaLoginChallengeIssuer(
                ChallengeRepositoryMock.Object,
                MfaOptions,
                CurrentUserServiceMock.Object,
                SecurityEventLoggerMock.Object);
        }
    }
}
