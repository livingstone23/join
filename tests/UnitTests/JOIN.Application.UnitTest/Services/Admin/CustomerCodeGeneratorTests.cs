using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Admin;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Moq;

namespace JOIN.Application.UnitTest.Services.Admin;

/// <summary>
/// Contains the unit tests for the sequential customer code generator.
/// </summary>
public sealed class CustomerCodeGeneratorTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    [Fact]
    public async Task GenerateNextAsync_WhenTenantHasNoCustomers_ShouldStartAtOne()
    {
        var generator = CreateGenerator([]);

        var code = await generator.GenerateNextAsync(CompanyId);

        code.Should().Be("C000000001");
    }

    [Fact]
    public async Task GenerateNextAsync_ShouldIgnoreOtherTenantsAndMalformedCodes()
    {
        var generator = CreateGenerator(
        [
            Customer.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), "C000000041", PersonLifecycleStage.Lead),
            Customer.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), "X000000900", PersonLifecycleStage.Lead),
            Customer.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), "CABC", PersonLifecycleStage.Lead),
            Customer.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), "C", PersonLifecycleStage.Lead),
            Customer.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "C000000500", PersonLifecycleStage.Lead)
        ]);

        var code = await generator.GenerateNextAsync(CompanyId);

        code.Should().Be("C000000042");
    }

    [Fact]
    public async Task GenerateNextAsync_WhenSequenceOverflowsMaxLength_ShouldThrow()
    {
        var generator = CreateGenerator(
            [Customer.Create(CompanyId, Guid.NewGuid(), Guid.NewGuid(), "C999999999", PersonLifecycleStage.Lead)]);

        var act = () => generator.GenerateNextAsync(CompanyId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static CustomerCodeGenerator CreateGenerator(IEnumerable<Customer> customers)
    {
        var repository = new Mock<IGenericRepository<Customer>>();
        repository.Setup(x => x.GetAllAsync()).ReturnsAsync(customers);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.GetRepository<Customer>()).Returns(repository.Object);
        return new CustomerCodeGenerator(unitOfWork.Object);
    }
}
