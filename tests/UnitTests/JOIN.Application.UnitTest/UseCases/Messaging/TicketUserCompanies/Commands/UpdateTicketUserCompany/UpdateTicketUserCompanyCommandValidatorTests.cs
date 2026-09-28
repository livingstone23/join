using FluentAssertions;
using JOIN.Application.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketUserCompanies.Commands.UpdateTicketUserCompany;

/// <summary>
/// Unit tests for <see cref="UpdateTicketUserCompanyCommandValidator"/>.
/// </summary>
public sealed class UpdateTicketUserCompanyCommandValidatorTests
{
    [Fact]
    public void Validate_WhenIdIsEmpty_ShouldFail()
    {
        var validator = new UpdateTicketUserCompanyCommandValidator();
        var result = validator.Validate(new UpdateTicketUserCompanyCommand { Id = Guid.Empty });
        result.IsValid.Should().BeFalse();
    }
}
