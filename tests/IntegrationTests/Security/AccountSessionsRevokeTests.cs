using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Audit;
using JOIN.Domain.Security;
using JOIN.Persistence.Contexts;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Security;

/// <summary>
/// SPEC 30 (F5) — End-to-end coverage for <c>DELETE /api/v1/account/sessions/{sessionId}</c>.
///
/// Regression test for the SQL 207 bug: pre-SPEC-30 the endpoint returned HTTP 500
/// "Invalid column name 'GcRecord'" because
/// <see cref="Persistence.Repositories.Security.RoleUserSessionRepository"/> filtered by
/// <c>GcRecord = 0</c> against <c>Security.UserConnectionLogs</c>, which had no such
/// column at the time. SPEC 30's migration + restored filter let the request round-trip
/// the entire pipeline (HTTP → middleware → MediatR → handler → Dapper SQL → SQL Server)
/// without throwing.
/// </summary>
[Collection("IntegrationTests")]
public sealed class AccountSessionsRevokeTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Fixture _fixture = new();

    public AccountSessionsRevokeTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RevokeOtherUsersSession_Returns200_WhenSessionExists()
    {
        var unauthClient = _factory.CreateClient();
        var email = $"{_fixture.Create<string>()}@integration.test";
        const string password = "Str0ng!Pass2026";

        // 1. Register. The endpoint returns 200 + UserId on success.
        var registerResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/register",
            new RegisterCommand
            {
                Email = email,
                Password = password,
                FirstName = _fixture.Create<string>(),
                LastName = _fixture.Create<string>(),
            });
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. First login → bearer token1. The token1 refresh row is the one we deliberately
        //    AVOID revoking. A second refresh-token row is materialised directly in the DB
        //    below — same observable state (extra live row in Security.UserRefreshTokens)
        //    without depending on a duplicated login round-trip.
        var loginResponse = await unauthClient.PostAsJsonAsync(
            "/api/v1/users/login",
            new LoginCommand { Email = email, Password = password });
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginPayload = await loginResponse.Content.ReadFromJsonAsync<Response<LoginResponse>>();
        loginPayload.Should().NotBeNull();
        loginPayload!.IsSuccess.Should().BeTrue();
        loginPayload.Data!.Token.Should().NotBeNullOrWhiteSpace();

        var bearerToken = loginPayload.Data.Token;
        var userId = ExtractSubjectFromJwt(bearerToken);

        var authClient = _factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", bearerToken);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sibling = new UserRefreshToken
        {
            UserId = userId,
            Token = $"rt-sibling-{Guid.NewGuid():N}",
            ExpiryDate = DateTime.UtcNow.AddDays(7),
            IsRevoked = false,
            GcRecord = BaseAuditableEntity.ActiveGcRecord,
            CreatedBy = nameof(AccountSessionsRevokeTests),
        };
        db.UserRefreshTokens.Add(sibling);
        await db.SaveChangesAsync();
        var secondSessionId = sibling.Id;

        try
        {
            // 3. Revoke the sibling row using the FIRST session's bearer token.
            //    Pre-SPEC-30 this returned 500 with SQL error 207.
            var revokeResponse = await authClient.DeleteAsync(
                $"/api/v1/account/sessions/{secondSessionId}");

            revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await revokeResponse.Content.ReadFromJsonAsync<Response<RevokeMySessionResponseDto>>();
            body.Should().NotBeNull();
            body!.IsSuccess.Should().BeTrue();
            body.Data.Should().NotBeNull();
            body.Data!.RevokedTokens.Should().Be(1, "exactly one UserRefreshTokens row was affected");
            body.Data.RevokedConnections.Should().Be(0);
            body.Data.SessionType.Should().Be(nameof(SessionType.UserRefreshToken));
        }
        finally
        {
            await db.Entry(sibling).ReloadAsync();
            sibling.GcRecord = BaseAuditableEntity.GetDeletionGcRecordStamp();
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Decodes the JWT "sub" claim without validating the signature — we issued the
    /// token via the host in the same test process so trust is implicit. Hand-parses
    /// to avoid adding a transitive package reference on
    /// <c>Microsoft.IdentityModel.JsonWebTokens</c>.
    /// </summary>
    private static Guid ExtractSubjectFromJwt(string token)
    {
        var parts = token.Split('.');
        parts.Length.Should().BeGreaterThanOrEqualTo(2);
        var payload = parts[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = JsonSerializer.Deserialize<JsonElement>(
            Encoding.UTF8.GetString(Convert.FromBase64String(padded.Replace('-', '+').Replace('_', '/'))));
        var sub = json.GetProperty("sub").GetString();
        return Guid.Parse(sub!);
    }
}
