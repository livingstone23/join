using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.DTO.Security.Auth;
using JOIN.Application.UseCases.Security.Account.Commands.EnableEmailOtp;
using JOIN.Application.UseCases.Security.Account.Commands.EnableMfa;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;

namespace JOIN.IntegrationTests.Security;

/// <summary>
/// SPEC 32 (F11) — End-to-end coverage for the MFA login challenge: TOTP and email flows,
/// the 5-attempt lockout that invalidates the whole challenge, and challenge expiration.
/// Exercises the full pipeline (HTTP → middleware → MediatR → handlers → EF/Dapper → ephemeral
/// SQL Server in Testcontainers). Email dispatch is captured via
/// <see cref="CapturingEmailService"/> — no test depends on an external network.
/// </summary>
[Collection("IntegrationTests")]
public sealed class MfaLoginChallengeTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Password = "Str0ng!Pass2026";
    private static readonly Regex SixDigitCodeRegex = new(@"\b(\d{6})\b", RegexOptions.Compiled);

    private readonly CustomWebApplicationFactory _factory;
    private readonly Fixture _fixture = new();

    public MfaLoginChallengeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TotpChallenge_EndToEnd_LoginRequiresChallengeThenVerifyIssuesRealJwt()
    {
        var (email, authClient) = await RegisterSeedAndLoginAsync();

        var secret = await EnableTotpAsync(authClient);

        // Second login: the user now has TOTP active, so login must stop at a challenge.
        var unauthClient = _factory.CreateClient();
        var challengeResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/login",
            new LoginCommand { Email = email, Password = Password });
        challengeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var challengePayload = await challengeResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        challengePayload!.IsSuccess.Should().BeTrue();
        challengePayload.Data!.Token.Should().BeNull();
        challengePayload.Data.RefreshToken.Should().BeNull();
        challengePayload.Data.Expiration.Should().BeNull();
        challengePayload.Data.Roles.Should().BeEmpty();
        challengePayload.Data.ChallengeToken.Should().NotBeNullOrWhiteSpace();
        challengePayload.Data.AvailableMethods.Should().BeEquivalentTo(["totp"]);

        // Verify with a live TOTP code.
        var verifyResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/challenge/verify",
            new VerifyMfaChallengeRequestDto
            {
                ChallengeToken = challengePayload.Data.ChallengeToken!,
                Method = "totp",
                Code = ComputeTotpCode(secret)
            });

        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyPayload = await verifyResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        verifyPayload!.IsSuccess.Should().BeTrue();
        verifyPayload.Data!.Token.Should().NotBeNullOrWhiteSpace();
        verifyPayload.Data.RefreshToken.Should().NotBeNullOrWhiteSpace();
        verifyPayload.Data.ChallengeToken.Should().BeNull();
    }

    [Fact]
    public async Task EmailChallenge_EndToEnd_SendEnforcesCooldownThenVerifyIssuesRealJwt()
    {
        var (email, authClient) = await RegisterSeedAndLoginAsync();

        await ConfirmEmailAsync(email);
        await EnableEmailOtpAsync(authClient, email);

        var unauthClient = _factory.CreateClient();
        var challengeResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/login",
            new LoginCommand { Email = email, Password = Password });
        challengeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var challengePayload = await challengeResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        challengePayload!.Data!.Token.Should().BeNull();
        challengePayload.Data.AvailableMethods.Should().BeEquivalentTo(["email"]);
        var challengeToken = challengePayload.Data.ChallengeToken!;

        // Request the login-challenge email code.
        var sendResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/challenge/send",
            new SendMfaChallengeRequestDto { ChallengeToken = challengeToken, Method = "email" });
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var emailService = _factory.Services.GetRequiredService<CapturingEmailService>();
        var sentEmail = emailService.LastSentTo(email);
        sentEmail.Should().NotBeNull("send-code must email a fresh 6-digit code");
        var code = ExtractSixDigitCode(sentEmail!.HtmlContent);

        // Immediate resend must be rejected by the 60s cooldown.
        var cooldownResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/challenge/send",
            new SendMfaChallengeRequestDto { ChallengeToken = challengeToken, Method = "email" });
        cooldownResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var cooldownPayload = await cooldownResponse.Content.ReadFromJsonAsync<Response<bool>>();
        cooldownPayload!.Message.Should().Be("CHALLENGE_SEND_COOLDOWN");

        // "totp" is never resendable, regardless of challenge state.
        var totpResendResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/challenge/send",
            new SendMfaChallengeRequestDto { ChallengeToken = challengeToken, Method = "totp" });
        totpResendResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var totpResendPayload = await totpResendResponse.Content.ReadFromJsonAsync<Response<bool>>();
        totpResendPayload!.Message.Should().Be("CHALLENGE_METHOD_NOT_RESENDABLE");

        // Verify with the emailed code.
        var verifyResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/challenge/verify",
            new VerifyMfaChallengeRequestDto { ChallengeToken = challengeToken, Method = "email", Code = code });

        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var verifyPayload = await verifyResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        verifyPayload!.IsSuccess.Should().BeTrue();
        verifyPayload.Data!.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task VerifyMfaChallenge_AfterFiveFailedAttempts_LocksEntireChallengeEvenForACorrectCode()
    {
        var (email, authClient) = await RegisterSeedAndLoginAsync();
        var secret = await EnableTotpAsync(authClient);

        var unauthClient = _factory.CreateClient();
        var challengeResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/login",
            new LoginCommand { Email = email, Password = Password });
        var challengePayload = await challengeResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        var challengeToken = challengePayload!.Data!.ChallengeToken!;

        // 5 wrong attempts: each is evaluated normally and rejected as an invalid code.
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var wrongResponse = await unauthClient.PostAsJsonAsync(
                "/api/v1/auth/mfa/challenge/verify",
                new VerifyMfaChallengeRequestDto { ChallengeToken = challengeToken, Method = "totp", Code = "000000" });

            wrongResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"attempt {attempt} should be rejected as an invalid code, not yet locked");

            var wrongPayload = await wrongResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
            wrongPayload!.Message.Should().Be("CHALLENGE_INVALID_CODE");
        }

        // 6th attempt: the challenge is now locked — even a CORRECT code must fail.
        var lockedResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/challenge/verify",
            new VerifyMfaChallengeRequestDto { ChallengeToken = challengeToken, Method = "totp", Code = ComputeTotpCode(secret) });

        lockedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var lockedPayload = await lockedResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        lockedPayload!.Message.Should().Be("CHALLENGE_LOCKED");
    }

    [Fact]
    public async Task VerifyMfaChallenge_WhenChallengeExpired_ReturnsChallengeExpired()
    {
        var (email, authClient) = await RegisterSeedAndLoginAsync();
        var secret = await EnableTotpAsync(authClient);

        var unauthClient = _factory.CreateClient();
        var challengeResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/login",
            new LoginCommand { Email = email, Password = Password });
        var challengePayload = await challengeResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        var challengeToken = challengePayload!.Data!.ChallengeToken!;

        // Backdate the just-issued challenge's expiration directly in the DB — the only way to
        // exercise expiration deterministically without sleeping past Mfa:ChallengeExpirationMinutes.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);

            var challenge = await db.MfaLoginChallenges
                .Where(c => c.UserId == user!.Id && c.GcRecord == 0)
                .OrderByDescending(c => c.Created)
                .FirstAsync();

            challenge.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var verifyResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/auth/mfa/challenge/verify",
            new VerifyMfaChallengeRequestDto { ChallengeToken = challengeToken, Method = "totp", Code = ComputeTotpCode(secret) });

        verifyResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var verifyPayload = await verifyResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        verifyPayload!.IsSuccess.Should().BeFalse();
        verifyPayload.Message.Should().Be("CHALLENGE_EXPIRED");
    }

    /// <summary>
    /// Registers a fresh user, seeds a Company + SuperAdmin role (required for the
    /// <c>DynamicAuthorizationFilter</c> CompanyId check on authenticated endpoints — see
    /// <c>AccountSessionsRevokeTests</c>), logs in once (no 2FA active yet — a normal 200 with a
    /// real JWT), and returns the email plus an authenticated <see cref="HttpClient"/>.
    /// </summary>
    private async Task<(string Email, HttpClient AuthClient)> RegisterSeedAndLoginAsync()
    {
        var unauthClient = _factory.CreateClient();
        var email = $"{_fixture.Create<string>()}@integration.test";

        var registerResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/register",
            new RegisterCommand
            {
                Email = email,
                Password = Password,
                FirstName = _fixture.Create<string>(),
                LastName = _fixture.Create<string>(),
            });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var defaultCompany = await seedDb.Companies
                .Where(c => c.IsActive && c.GcRecord == 0)
                .OrderBy(c => c.Created)
                .FirstOrDefaultAsync();

            var registeredUser = await userManager.FindByEmailAsync(email);
            registeredUser.Should().NotBeNull();
            defaultCompany.Should().NotBeNull("DatabaseSeeder must populate Security.Companies");

            seedDb.UserCompanies.Add(new UserCompany
            {
                UserId = registeredUser!.Id,
                CompanyId = defaultCompany!.Id,
                IsDefault = true,
                GcRecord = BaseAuditableEntity.ActiveGcRecord,
                CreatedBy = nameof(MfaLoginChallengeTests),
            });

            (await userManager.AddToRoleAsync(registeredUser, "SuperAdmin"))
                .Succeeded.Should().BeTrue("AddToRoleAsync must succeed for the seeded SuperAdmin role");

            await seedDb.SaveChangesAsync();
        }

        var loginResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/login",
            new LoginCommand { Email = email, Password = Password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        loginPayload!.Data!.Token.Should().NotBeNullOrWhiteSpace("no 2FA method is active yet");

        var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginPayload.Data.Token);

        return (email, authClient);
    }

    /// <summary>
    /// Runs the real enrolment dance (<c>mfa/setup</c> → <c>mfa/enable</c>) through the HTTP
    /// pipeline so <c>IsMfaEnabled</c>/<c>MfaSecretKey</c> end up exactly as production would set
    /// them, and returns the Base32 secret so the test can compute live TOTP codes.
    /// </summary>
    private static async Task<string> EnableTotpAsync(HttpClient authClient)
    {
        var setupResponse = await authClient.PostAsync("/api/v1/account/mfa/setup", content: null);
        setupResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var setupPayload = await setupResponse.Content.ReadFromJsonAsync<Response<SetupMfaResponseDto>>();
        var secret = setupPayload!.Data!.Secret;

        var enableResponse = await authClient.PostAsJsonAsync(
            "/api/v1/account/mfa/enable",
            new EnableMfaCommand(ComputeTotpCode(secret)));
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        return secret;
    }

    /// <summary>
    /// Runs the real enrolment dance (<c>email-otp/send-code</c> → <c>email-otp/enable</c>)
    /// through the HTTP pipeline, reading the plaintext code back from
    /// <see cref="CapturingEmailService"/> since only its hash is ever persisted.
    /// </summary>
    private async Task EnableEmailOtpAsync(HttpClient authClient, string email)
    {
        var sendResponse = await authClient.PostAsync("/api/v1/account/email-otp/send-code", content: null);
        sendResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var emailService = _factory.Services.GetRequiredService<CapturingEmailService>();
        var sentEmail = emailService.LastSentTo(email);
        sentEmail.Should().NotBeNull();
        var code = ExtractSixDigitCode(sentEmail!.HtmlContent);

        var enableResponse = await authClient.PostAsJsonAsync(
            "/api/v1/account/email-otp/enable",
            new EnableEmailOtpCommand(code));
        enableResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Flips <c>EmailConfirmed</c> directly in the DB — <c>RegisterCommandHandler</c> does not
    /// confirm the email, and <c>email-otp/send-code</c> requires it (EMAIL_NOT_CONFIRMED gate).
    /// </summary>
    private async Task ConfirmEmailAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user!.EmailConfirmed = true;
        (await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();
    }

    private static string ComputeTotpCode(string base32Secret)
    {
        var totp = new Totp(Base32Encoding.ToBytes(base32Secret));
        return totp.ComputeTotp();
    }

    private static string ExtractSixDigitCode(string htmlContent)
    {
        var match = SixDigitCodeRegex.Match(htmlContent);
        match.Success.Should().BeTrue("the emailed body must contain the 6-digit code");
        return match.Groups[1].Value;
    }
}
