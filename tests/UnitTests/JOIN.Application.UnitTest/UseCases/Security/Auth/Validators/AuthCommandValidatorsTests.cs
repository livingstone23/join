using FluentAssertions;
using JOIN.Application.UseCases.Security.Auth.Login;
using JOIN.Application.UseCases.Security.Auth.Register;

namespace JOIN.Application.UnitTest.UseCases.Security.Auth.Validators;

/// <summary>
/// Contains the unit tests for the login and registration validators.
/// </summary>
public sealed class AuthCommandValidatorsTests
{
    [Fact]
    public void LoginValidator_ShouldAcceptValidPayloadWithOptionalTargetCompany()
    {
        var validator = new LoginCommandValidator();

        validator.Validate(new LoginCommand { Email = "a@join.test", Password = "secret" }).IsValid.Should().BeTrue();
        validator.Validate(new LoginCommand { Email = "a@join.test", Password = "secret", TargetCompanyId = Guid.NewGuid() }).IsValid.Should().BeTrue();
    }

    [Fact]
    public void LoginValidator_ShouldRejectInvalidFields()
        => new LoginCommandValidator()
            .Validate(new LoginCommand { Email = "not-an-email", Password = new string('x', 201), TargetCompanyId = Guid.Empty })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Email", "Password", "TargetCompanyId"]);

    [Fact]
    public void RegisterValidator_ShouldAcceptValidPayload()
        => new RegisterCommandValidator()
            .Validate(new RegisterCommand { Email = "a@join.test", Password = "Passw0rd!", FirstName = "Ana", LastName = "Lopez" })
            .IsValid.Should().BeTrue();

    [Fact]
    public void RegisterValidator_ShouldRejectInvalidFields()
        => new RegisterCommandValidator()
            .Validate(new RegisterCommand { Email = "", Password = "short", FirstName = new string('x', 101), LastName = "" })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Email", "Password", "FirstName", "LastName"]);
}
