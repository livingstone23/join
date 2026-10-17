using FluentAssertions;
using JOIN.Application.Services.Customers;
using JOIN.Application.UnitTest.UseCases.Customers;
using JOIN.Application.UseCases.Admin.Persons;
using JOIN.Application.UseCases.Customers.Customers.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace JOIN.Application.UnitTest.UseCases.Admin.Persons.Identification;

/// <summary>
/// SPEC 58: optional identification on persons, required to become a customer.
/// </summary>
public sealed class PersonIdentificationTests
{
    [Fact]
    public void Normalize_ShouldTrimTheNumber_AndClearBothFieldsWhenEitherIsMissing()
    {
        var typeId = Guid.NewGuid();
        var complete = new Person { IdentificationTypeId = typeId, IdentificationNumber = " 001-0000001-1 " };
        var onlyNumber = new Person { IdentificationTypeId = null, IdentificationNumber = "123" };
        var onlyType = new Person { IdentificationTypeId = typeId, IdentificationNumber = "  " };
        var emptyType = new Person { IdentificationTypeId = Guid.Empty, IdentificationNumber = "123" };

        PersonIdentification.Normalize(complete);
        PersonIdentification.Normalize(onlyNumber);
        PersonIdentification.Normalize(onlyType);
        PersonIdentification.Normalize(emptyType);

        complete.IdentificationNumber.Should().Be("001-0000001-1");
        complete.HasIdentification.Should().BeTrue();
        foreach (var person in new[] { onlyNumber, onlyType, emptyType })
        {
            person.IdentificationTypeId.Should().BeNull();
            person.IdentificationNumber.Should().BeNull();
            person.HasIdentification.Should().BeFalse();
        }
    }

    [Fact]
    public async Task ACustomer_RequiresAPersonWithIdentification()
    {
        using var ctx = await CustomerTestContext.CreateAsync();
        var person = new Person { CompanyId = ctx.CompanyId, PersonType = PersonType.Physical, FirstName = "Lead", LastName = "Sin Documento" };
        ctx.Db.Persons.Add(person);
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        var blocked = await ctx.SendAsync(new CreateCustomerCommand(person.Id, null));

        blocked.IsSuccess.Should().BeFalse();
        blocked.Message.Should().Be(CustomerErrorCodes.PersonIdentificationRequired);
        (await ctx.CustomersAsync()).Should().NotContain(c => c.PersonId == person.Id);

        var idType = await ctx.Db.IdentificationTypes.FirstAsync();
        var stored = await ctx.Db.Persons.IgnoreQueryFilters().SingleAsync(p => p.Id == person.Id);
        stored.IdentificationTypeId = idType.Id;
        stored.IdentificationNumber = "009";
        await ctx.Db.SaveChangesAsync();
        ctx.Db.ChangeTracker.Clear();

        (await ctx.SendAsync(new CreateCustomerCommand(person.Id, null))).IsSuccess.Should().BeTrue();
    }
}
