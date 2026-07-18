using System.Net;
using System.Net.Http.Json;
using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Register;

namespace JOIN.IntegrationTests.Auth;

/// <summary>
/// Pilot integration test for the registration endpoint.
/// Exercises the full pipeline (HTTP → middleware → MediatR → behaviors →
/// UserManager → EF Core → ephemeral SQL Server in Testcontainers) end-to-end.
/// </summary>
public sealed class RegisterEndpointTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly Fixture _fixture = new();

    public RegisterEndpointTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WithValidPayload_ReturnsSuccessAndPersistsUser()
    {
        var client = _factory.CreateClient();

        // Hand-built password — must satisfy PasswordPolicySettings (MinimumLength=8,
        // RequireDigit, no repetitive chars, no common sequences, no personal data).
        // AutoFixture's random strings would fail these rules.
        var command = new RegisterCommand
        {
            Email = $"{_fixture.Create<string>()}@integration.test",
            Password = "Str0ng!Pass2026",
            FirstName = _fixture.Create<string>(),
            LastName = _fixture.Create<string>()
        };

        var response = await client.PostAsJsonAsync("/api/v1/users/register", command);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<Response<RegisterResponseDto>>();

        body.Should().NotBeNull();
        body!.IsSuccess.Should().BeTrue();
        body.Data.Should().NotBeNull();
        body.Data!.Email.Should().Be(command.Email);
        body.Data.UserId.Should().NotBe(Guid.Empty);
    }
}