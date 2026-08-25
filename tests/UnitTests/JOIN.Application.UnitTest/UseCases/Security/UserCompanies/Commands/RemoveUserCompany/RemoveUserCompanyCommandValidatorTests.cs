using FluentAssertions;
using FluentValidation.TestHelper;
using JOIN.Application.UseCases.Security.UserCompanies.Commands.RemoveUserCompany;

namespace JOIN.Application.UnitTest.UseCases.Security.UserCompanies.Commands.RemoveUserCompany;

/// <summary>
/// Validates <see cref="RemoveUserCompanyCommandValidator"/>. Business guards
/// (last-company / default-company) live in the handler and surface their own
/// error codes — covered separately.
/// </summary>
public sealed class RemoveUserCompanyCommandValidatorTests
{
    private readonly RemoveUserCompanyCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenUserIdIsEmpty_ShouldFail()
    {
        var command = new RemoveUserCompanyCommand(Guid.Empty, Guid.NewGuid());

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.UserId);
    }

    [Fact]
    public void Validate_WhenCompanyIdIsEmpty_ShouldFail()
    {
        var command = new RemoveUserCompanyCommand(Guid.NewGuid(), Guid.Empty);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.CompanyId);
    }

    [Fact]
    public void Validate_WhenBothIdsAreValid_ShouldPass()
    {
        var command = new RemoveUserCompanyCommand(Guid.NewGuid(), Guid.NewGuid());

        var result = _validator.TestValidate(command);

        result.IsValid.Should().BeTrue();
    }
}