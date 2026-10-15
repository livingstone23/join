using FluentAssertions;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.CustomerStages.Commands;
using JOIN.Application.UseCases.Customers.Customers.Commands;
using JOIN.Domain.Customers;

namespace JOIN.Application.UnitTest.UseCases.Customers.CustomerStages;

/// <summary>
/// SPEC 55 part F: per-company stage catalog with exactly one active default stage.
/// </summary>
public sealed class CustomerStageCommandsTests
{
    private static CreateCustomerStageCommand NewStage(string code, bool isDefault = false, bool isActive = true) => new()
    {
        Code = code,
        Name = $"Etapa {code}",
        Description = " Descripción ",
        SortOrder = 5,
        IsDefault = isDefault,
        IsActive = isActive
    };

    private static UpdateCustomerStageCommand Edit(CustomerStage stage, bool? isDefault = null, bool? isActive = null, string? code = null) => new()
    {
        Id = stage.Id,
        Code = code ?? stage.Code,
        Name = stage.Name,
        Description = stage.Description,
        SortOrder = stage.SortOrder,
        IsDefault = isDefault ?? stage.IsDefault,
        IsActive = isActive ?? stage.IsActive
    };

    [Fact]
    public async Task Create_ShouldNormalizeTheCode()
    {
        using var ctx = await CustomerTestContext.CreateAsync();

        var response = await ctx.SendAsync(NewStage(" vip client "));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.Code.Should().Be("VIPCLIENT");
        response.Data.Description.Should().Be("Descripción");
        response.Data.IsDefault.Should().BeFalse();
    }

    [Fact]
    public async Task Create_AsDefault_ShouldUnmarkThePreviousDefault()
    {
        using var ctx = await CustomerTestContext.CreateAsync();

        var response = await ctx.SendAsync(NewStage("VIP", isDefault: true));

        response.Data!.IsDefault.Should().BeTrue();
        (await ctx.StagesAsync()).Where(s => s.IsDefault).Select(s => s.Code).Should().Equal("VIP");
    }

    [Fact]
    public async Task Create_WithDuplicateCodeOrInactiveDefault_ShouldFail()
    {
        using var ctx = await CustomerTestContext.CreateAsync();

        (await ctx.SendAsync(NewStage("lead"))).Message.Should().Be(CustomerErrorCodes.DuplicateKey);
        (await ctx.SendAsync(NewStage("VIP", isDefault: true, isActive: false))).Message.Should().Be(CustomerErrorCodes.CustomerStageDefaultInactive);
    }

    [Fact]
    public async Task Update_ShouldKeepOneDefault()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var stages = (await ctx.StagesAsync()).ToDictionary(s => s.Code);

        (await ctx.SendAsync(Edit(stages[CustomerStage.Codes.Customer], isDefault: false))).Message
            .Should().Be(CustomerErrorCodes.CustomerStageDefaultRequired, "the default cannot be unmarked without marking another");
        (await ctx.SendAsync(Edit(stages[CustomerStage.Codes.Customer], isActive: false))).Message
            .Should().Be(CustomerErrorCodes.CustomerStageDefaultInactive);

        var moved = await ctx.SendAsync(Edit(stages[CustomerStage.Codes.Lead], isDefault: true));

        moved.IsSuccess.Should().BeTrue(moved.Message);
        (await ctx.StagesAsync()).Where(s => s.IsDefault).Select(s => s.Code).Should().Equal(CustomerStage.Codes.Lead);
    }

    [Fact]
    public async Task Update_DeactivatingAStage_ShouldKeepItOnItsCustomers()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var customer = (await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId, ctx.Stage[CustomerStage.Codes.Prospect]))).Data!;
        var prospect = (await ctx.StagesAsync()).Single(s => s.Code == CustomerStage.Codes.Prospect);

        var response = await ctx.SendAsync(Edit(prospect, isActive: false));

        response.IsSuccess.Should().BeTrue(response.Message);
        response.Data!.CustomersCount.Should().Be(1);
        (await ctx.CustomersAsync()).Single(c => c.Id == customer.Id).CustomerStageId.Should().Be(prospect.Id);
    }

    [Fact]
    public async Task Update_WithErrors_ShouldAnswerTheirCodes()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var stages = (await ctx.StagesAsync()).ToDictionary(s => s.Code);

        (await ctx.SendAsync(Edit(stages[CustomerStage.Codes.Lead], code: CustomerStage.Codes.Former))).Message
            .Should().Be(CustomerErrorCodes.DuplicateKey);
        (await ctx.SendAsync(Edit(stages[CustomerStage.Codes.Lead]) with { Id = Guid.NewGuid() })).Message
            .Should().Be(CustomerErrorCodes.CustomerStageNotFound);
    }

    [Fact]
    public async Task Delete_ShouldBlockStagesInUseAndTheDefault_AndRestoreShouldBringItBack()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        await ctx.SendAsync(new CreateCustomerCommand(ctx.PersonId, ctx.Stage[CustomerStage.Codes.Lead]));

        (await ctx.SendAsync(new DeleteCustomerStageCommand(ctx.Stage[CustomerStage.Codes.Lead]))).Message
            .Should().Be(CustomerErrorCodes.CustomerStageInUse);
        (await ctx.SendAsync(new DeleteCustomerStageCommand(ctx.Stage[CustomerStage.Codes.Customer]))).Message
            .Should().Be(CustomerErrorCodes.CustomerStageDefaultRequired);
        (await ctx.SendAsync(new DeleteCustomerStageCommand(Guid.NewGuid()))).Message
            .Should().Be(CustomerErrorCodes.CustomerStageNotFound);

        var deleted = await ctx.SendAsync(new DeleteCustomerStageCommand(ctx.Stage[CustomerStage.Codes.Former]));
        deleted.IsSuccess.Should().BeTrue(deleted.Message);

        (await ctx.SendAsync(new RestoreCustomerStageCommand(deleted.Data))).Message.Should().Be("SUPERADMIN_REQUIRED");
        ctx.IsSuperAdmin = true;
        var restored = await ctx.SendAsync(new RestoreCustomerStageCommand(deleted.Data));
        restored.IsSuccess.Should().BeTrue(restored.Message);
        (await ctx.StagesAsync()).Single(s => s.Id == deleted.Data).GcRecord.Should().Be(0);
    }

    [Fact]
    public async Task Commands_WithoutCompany_ShouldAnswerCompanyRequired()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        ctx.ActingCompanyId = Guid.Empty;

        (await ctx.SendAsync(NewStage("VIP"))).Message.Should().Be(CustomerErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new UpdateCustomerStageCommand { Id = Guid.NewGuid(), Code = "X", Name = "X" })).Message.Should().Be(CustomerErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new DeleteCustomerStageCommand(Guid.NewGuid()))).Message.Should().Be(CustomerErrorCodes.CompanyRequired);
        (await ctx.SendAsync(new RestoreCustomerStageCommand(Guid.NewGuid()))).Message.Should().Be(CustomerErrorCodes.CompanyRequired);
    }

    [Fact]
    public void Validators_ShouldCheckLengthsAndIds()
    {
        new CreateCustomerStageCommandValidator().Validate(new CreateCustomerStageCommand { Code = "", Name = "", SortOrder = -1, Description = new string('x', 251) })
            .Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["Code", "Name", "SortOrder", "Description"]);
        new UpdateCustomerStageCommandValidator().Validate(new UpdateCustomerStageCommand { Code = "A", Name = "A" })
            .Errors.Should().ContainSingle(e => e.PropertyName == "Id");
        new DeleteCustomerStageCommandValidator().Validate(new DeleteCustomerStageCommand(Guid.Empty)).IsValid.Should().BeFalse();
        new RestoreCustomerStageCommandValidator().Validate(new RestoreCustomerStageCommand(Guid.Empty)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void CustomerStage_Domain_ShouldGuardItsArguments()
    {
        var noCompany = () => CustomerStage.Create(Guid.Empty, "A", "A", null, 1, false);
        noCompany.Should().Throw<ArgumentException>();
        var stage = CustomerStage.Create(Guid.NewGuid(), "a b", "Name", "  ", 1, false);
        stage.Code.Should().Be("AB");
        stage.Description.Should().BeNull();
        var noName = () => stage.Update("A", " ", null, 1);
        noName.Should().Throw<ArgumentException>();
        var noCode = () => stage.Update(" ", "A", null, 1);
        noCode.Should().Throw<ArgumentException>();
    }
}
