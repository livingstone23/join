using System.Linq.Expressions;
using FluentAssertions;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers;
using Moq;

namespace JOIN.Application.UnitTest.Services.Customers;

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
            NewCustomer(CompanyId, "C000000041"),
            NewCustomer(CompanyId, "X000000900"),
            NewCustomer(CompanyId, "CABC"),
            NewCustomer(CompanyId, "C"),
            NewCustomer(Guid.NewGuid(), "C000000500")
        ]);

        var code = await generator.GenerateNextAsync(CompanyId);

        code.Should().Be("C000000042");
    }

    [Fact]
    public async Task GenerateNextAsync_WhenSequenceOverflowsMaxLength_ShouldThrow()
    {
        var generator = CreateGenerator([NewCustomer(CompanyId, "C999999999")]);

        var act = () => generator.GenerateNextAsync(CompanyId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("C000000007", 7L)]
    [InlineData("c000000007", null)]
    [InlineData(null, null)]
    [InlineData("C12A", null)]
    public void TryParseSequence_ShouldParseOnlyWellFormedCodes(string? code, long? expected)
    {
        CustomerCodeGenerator.TryParseSequence(code).Should().Be(expected);
    }

    private static Customer NewCustomer(Guid companyId, string code) =>
        Customer.Create(companyId, Guid.NewGuid(), Guid.NewGuid(), code, Guid.NewGuid());

    private static CustomerCodeGenerator CreateGenerator(IEnumerable<Customer> customers)
    {
        var repository = new Mock<IGenericRepository<Customer>>();
        repository
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<Customer, bool>>>()))
            .ReturnsAsync((Expression<Func<Customer, bool>> predicate) => customers.Where(predicate.Compile()).ToList());
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(x => x.GetRepository<Customer>()).Returns(repository.Object);
        return new CustomerCodeGenerator(unitOfWork.Object);
    }
}
