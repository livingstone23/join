using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Application.DTO.Admin;
using JOIN.Domain.Admin;

namespace JOIN.Application.Mappings;

/// <summary>
/// Defines mapping operations for customer aggregate transformations.
/// </summary>
public interface ICustomerMapper
{
    /// <summary>
    /// Maps a customer aggregate root to a DTO.
    /// </summary>
    CustomerDto ToDto(Customer customer);

    /// <summary>
    /// Maps a customer address entity to a DTO.
    /// </summary>
    CustomerAddressDto ToAddressDto(CustomerAddress address);

    /// <summary>
    /// Maps a customer contact entity to a DTO.
    /// </summary>
    CustomerContactDto ToContactDto(CustomerContact contact);

    /// <summary>
    /// Maps a customer DTO to a domain entity.
    /// </summary>
    Customer ToEntity(CustomerDto customerDto);

    /// <summary>
    /// Maps a create command payload to a full customer aggregate.
    /// </summary>
    Customer ToEntity(CreateCustomerCommand command);

    /// <summary>
    /// Maps an update-address payload to a new customer address entity.
    /// </summary>
    CustomerAddress ToAddressEntity(UpdateCustomerCommand.UpdateCustomerAddressDto addressDto);

    /// <summary>
    /// Maps an update-contact payload to a new customer contact entity.
    /// </summary>
    CustomerContact ToContactEntity(UpdateCustomerCommand.UpdateCustomerContactDto contactDto);

    /// <summary>
    /// Applies scalar customer updates from command to an existing customer entity.
    /// </summary>
    void ApplyUpdate(UpdateCustomerCommand command, Customer customer);

    /// <summary>
    /// Applies address updates from DTO to an existing address entity.
    /// </summary>
    void ApplyUpdate(UpdateCustomerCommand.UpdateCustomerAddressDto source, CustomerAddress target);

    /// <summary>
    /// Applies contact updates from DTO to an existing contact entity.
    /// </summary>
    void ApplyUpdate(UpdateCustomerCommand.UpdateCustomerContactDto source, CustomerContact target);

    /// <summary>
    /// Projects a queryable source to a queryable destination.
    /// </summary>
    IQueryable<CustomerDto> ProjectToDto(IQueryable<Customer> query);
}
