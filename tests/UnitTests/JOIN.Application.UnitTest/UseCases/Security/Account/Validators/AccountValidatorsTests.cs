using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.UseCases.Security.Account.Commands.ConfirmEmailChange;
using JOIN.Application.UseCases.Security.Account.Queries.GetSecurityActivity;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UnitTest.UseCases.Security.Account.Validators;

/// <summary>
/// Contains the unit tests for the self-service account validators.
/// </summary>
public sealed class AccountValidatorsTests
{
    [Fact]
    public void ConfirmEmailChangeValidator_ShouldAcceptValidPayload()
        => new ConfirmEmailChangeCommandValidator()
            .Validate(new ConfirmEmailChangeCommand("new@join.test", "token"))
            .IsValid.Should().BeTrue();

    [Fact]
    public void ConfirmEmailChangeValidator_ShouldRejectInvalidEmailAndMissingToken()
    {
        var result = new ConfirmEmailChangeCommandValidator().Validate(new ConfirmEmailChangeCommand("bad", ""));

        result.Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["NewEmail", "Token"]);
        result.Errors.Should().OnlyContain(e => e.ErrorCode == "EMAIL_CHANGE_FAILED");
    }

    [Fact]
    public void GetSecurityActivityValidator_ShouldEnforceConfiguredPageBounds()
    {
        var validator = new GetSecurityActivityQueryValidator(Options.Create(new PaginationSettings { MinPageSize = 1, MaxPageSize = 50 }));

        validator.Validate(new GetSecurityActivityQuery(1, 50)).IsValid.Should().BeTrue();
        validator.Validate(new GetSecurityActivityQuery(0, 51)).Errors.Select(e => e.ErrorCode)
            .Should().BeEquivalentTo(["PAGE_NUMBER_INVALID", "PAGE_SIZE_INVALID"]);
    }
}
