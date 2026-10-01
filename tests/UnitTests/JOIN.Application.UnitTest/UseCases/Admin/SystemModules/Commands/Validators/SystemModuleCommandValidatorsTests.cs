using FluentAssertions;
using JOIN.Application.UseCases.Admin.SystemModules.Commands;

namespace JOIN.Application.UnitTest.UseCases.Admin.SystemModules.Commands.Validators;

/// <summary>
/// Contains the unit tests for the system module command validators.
/// </summary>
public sealed class SystemModuleCommandValidatorsTests
{
    [Fact]
    public void CreateValidator_ShouldAcceptValidPayload()
        => new CreateSystemModuleCommandValidator()
            .Validate(new CreateSystemModuleCommand { Name = "Admin", Description = "d", Icon = "i", Order = 0 })
            .IsValid.Should().BeTrue();

    [Fact]
    public void CreateValidator_ShouldRejectInvalidFields()
        => new CreateSystemModuleCommandValidator()
            .Validate(new CreateSystemModuleCommand { Name = "", Description = new string('x', 251), Icon = new string('x', 101), Order = -1 })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Name", "Description", "Icon", "Order"]);

    [Fact]
    public void UpdateValidator_ShouldAcceptValidPayload()
        => new UpdateSystemModuleCommandValidator()
            .Validate(new UpdateSystemModuleCommand { Id = Guid.NewGuid(), Name = "Admin" })
            .IsValid.Should().BeTrue();

    [Fact]
    public void UpdateValidator_ShouldRejectInvalidFields()
        => new UpdateSystemModuleCommandValidator()
            .Validate(new UpdateSystemModuleCommand { Name = new string('x', 101), Description = new string('x', 251), Icon = new string('x', 101), Order = -5 })
            .Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["Id", "Name", "Description", "Icon", "Order"]);
}
