// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;
using JOIN.Domain.Security;
using JOIN.IntegrationTests.Security;
using JOIN.Persistence.Contexts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace JOIN.IntegrationTests.Customers;

/// <summary>
/// Shared helpers of the commercial module tests (SPEC 59–61): a fresh company created with <c>POST /Companies</c>
/// (so it gets every per-company seed) and users bound to it.
/// </summary>
internal static class CommercialTestHelpers
{
    internal const string Password = "Integration!Pass123";

    /// <summary>Creates a company with <c>POST /Companies</c> and a fresh user with <paramref name="role"/> bound to it.</summary>
    internal static async Task<(Guid CompanyId, HttpClient Client, Guid UserId)> CreateCompanyWithUserAsync(
        CustomWebApplicationFactory factory, string role = "SuperAdmin")
    {
        using var superAdmin = await ModuleTestHelpers.LoginAsync(factory, "lcano@join.com");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await superAdmin.PostAsJsonAsync("/api/v1/Companies", new { name = $"COM-{suffix}", taxId = $"COM{suffix}", isActive = true });
        var body = await created.Content.ReadAsStringAsync();
        created.IsSuccessStatusCode.Should().BeTrue(body);
        using var doc = JsonDocument.Parse(body);
        var data = doc.RootElement.GetProperty("data");
        var companyId = data.ValueKind == JsonValueKind.String ? data.GetGuid() : data.GetProperty("id").GetGuid();

        var (client, userId) = await AddUserAsync(factory, companyId, role);
        return (companyId, client, userId);
    }

    /// <summary>Registers a user with <paramref name="role"/>, binds it to <paramref name="companyId"/> and logs it in.</summary>
    internal static async Task<(HttpClient Client, Guid UserId)> AddUserAsync(CustomWebApplicationFactory factory, Guid companyId, string role)
    {
        var email = $"com-{Guid.NewGuid():N}@t.local";
        using var anonymous = factory.CreateClient();
        (await anonymous.PostAsJsonAsync("/api/v1/users/register", new RegisterCommand { Email = email, Password = Password, FirstName = "Com", LastName = role }))
            .EnsureSuccessStatusCode();

        Guid userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            userId = user.Id;
            db.UserCompanies.Add(new UserCompany { UserId = user.Id, CompanyId = companyId, IsDefault = true, CreatedBy = "commercial-tests" });
            (await users.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();
            await db.SaveChangesAsync();
        }

        var login = await anonymous.PostAsJsonAsync("/api/v1/users/login", new LoginCommand { Email = email, Password = Password, TargetCompanyId = companyId });
        login.EnsureSuccessStatusCode();
        var payload = (await login.Content.ReadFromJsonAsync<Response<LoginResponse>>())!.Data!;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.Token);
        return (client, userId);
    }

    internal static JsonElement Data(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    internal static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return Data(body);
    }
}
