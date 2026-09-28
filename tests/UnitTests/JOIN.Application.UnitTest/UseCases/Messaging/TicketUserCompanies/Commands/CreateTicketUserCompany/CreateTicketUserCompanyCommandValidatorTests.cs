using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Commands.CreateTicketUserCompany;

/// <summary>
/// Unit tests for <see cref="CreateTicketUserCompanyCommandValidator"/>.
/// </summary>
public sealed class CreateTicketUserCompanyCommandValidatorTests
{
    [Fact]
    public void Validate_WhenUserIdIsEmpty_ShouldFail()
    {
        var validator = new CreateTicketUserCompanyCommandValidator();
        var result = validator.Validate(new CreateTicketUserCompanyCommand { UserId = Guid.Empty });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WhenUserIdProvided_ShouldPass()
    {
        var validator = new CreateTicketUserCompanyCommandValidator();
        var result = validator.Validate(new CreateTicketUserCompanyCommand { UserId = Guid.NewGuid() });
        result.IsValid.Should().BeTrue();
    }
}
