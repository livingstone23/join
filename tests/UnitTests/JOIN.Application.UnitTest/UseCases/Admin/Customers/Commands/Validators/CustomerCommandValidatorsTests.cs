using FluentAssertions;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Domain.Enums;

namespace JOIN.Application.UnitTest.UseCases.Admin.Customers.Commands.Validators;

/// <summary>
/// Contains the unit tests for the customer command validators.
/// </summary>
public sealed class CustomerCommandValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldValidateIdsAndLifecycleStage()
    {
        var validator = new CreateCustomerCommandValidator();

        validator.Validate(new CreateCustomerCommand(Guid.NewGuid(), Guid.NewGuid(), PersonLifecycleStage.Lead)).IsValid.Should().BeTrue();
        validator.Validate(new CreateCustomerCommand(Guid.Empty, Guid.Empty, (PersonLifecycleStage)99))
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["PersonId", "UserId", "PersonLifecycleStage"]);
    }

    [Fact]
    public void UpdateValidator_ShouldValidateIdAndLifecycleStage()
    {
        var validator = new UpdateCustomerCommandValidator();

        validator.Validate(new UpdateCustomerCommand(Guid.NewGuid(), PersonLifecycleStage.FormerCustomer, true)).IsValid.Should().BeTrue();
        validator.Validate(new UpdateCustomerCommand(Guid.Empty, (PersonLifecycleStage)0, false))
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Id", "PersonLifecycleStage"]);
    }

    [Fact]
    public void DeleteValidator_ShouldRejectEmptyId()
    {
        new DeleteCustomerCommandValidator().Validate(new DeleteCustomerCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new DeleteCustomerCommandValidator().Validate(new DeleteCustomerCommand(Guid.NewGuid())).IsValid.Should().BeTrue();
    }
}
