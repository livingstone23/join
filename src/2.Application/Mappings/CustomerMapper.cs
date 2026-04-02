using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.Customers.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using Riok.Mapperly.Abstractions;



namespace JOIN.Application.Mappings;



/// <summary>
/// Auto-generated mapper for Customer entities and DTOs using Riok.Mapperly.
/// Operates at compile-time, ensuring zero reflection overhead and type safety.
/// </summary>
[Mapper]
public partial class CustomerMapper : ICustomerMapper
{


    /// <summary>
    /// Maps a Customer domain entity to a CustomerDto.
    /// </summary>
    /// <param name="customer">The source Customer entity.</param>
    /// <returns>The mapped CustomerDto.</returns>
    public partial CustomerDto ToDto(Customer customer);


    /// <summary>
    /// Maps a CustomerAddress domain entity to a CustomerAddressDto.
    /// </summary>
    /// <param name="address">The source CustomerAddress entity.</param>
    /// <returns>The mapped CustomerAddressDto.</returns>
    [MapProperty($"{nameof(CustomerAddress.StreetType)}.{nameof(StreetType.Name)}", nameof(CustomerAddressDto.StreetTypeName))]
    [MapProperty($"{nameof(CustomerAddress.Country)}.{nameof(Country.Name)}", nameof(CustomerAddressDto.CountryName))]
    [MapProperty($"{nameof(CustomerAddress.Region)}.{nameof(Region.Name)}", nameof(CustomerAddressDto.RegionName))]
    [MapProperty($"{nameof(CustomerAddress.Province)}.{nameof(Province.Name)}", nameof(CustomerAddressDto.ProvinceName))]
    [MapProperty($"{nameof(CustomerAddress.Municipality)}.{nameof(Municipality.Name)}", nameof(CustomerAddressDto.MunicipalityName))]
    [MapperIgnoreSource(nameof(CustomerAddress.CustomerId))]
    public partial CustomerAddressDto ToAddressDto(CustomerAddress address);


    /// <summary>
    /// Maps a CustomerContact domain entity to a CustomerContactDto.
    /// </summary>
    /// <param name="contact">The source CustomerContact entity.</param>
    /// <returns>The mapped CustomerContactDto.</returns>
    [MapperIgnoreSource(nameof(CustomerContact.CustomerId))]
    public partial CustomerContactDto ToContactDto(CustomerContact contact);


    /// <summary>
    /// Maps a CustomerDto back to a Customer domain entity.
    /// Ignores the Id property on the target to prevent accidental overwrites during creation.
    /// </summary>
    /// <param name="customerDto">The source CustomerDto.</param>
    /// <returns>The mapped Customer entity.</returns>
    [MapperIgnoreTarget(nameof(Customer.Id))]
    [MapperIgnoreSource(nameof(CustomerDto.IdentificationTypeName))]
    [MapperIgnoreSource(nameof(CustomerDto.Addresses))]
    [MapperIgnoreSource(nameof(CustomerDto.Contacts))]
    public partial Customer ToEntity(CustomerDto customerDto);


    /// <summary>
    /// Maps a create command to a Customer aggregate including nested addresses and contacts.
    /// Tenant ownership (CompanyId) is resolved from the authenticated context in the handler.
    /// </summary>
    /// <param name="command">The source create command payload.</param>
    /// <returns>The mapped Customer entity.</returns>
    [MapperIgnoreTarget(nameof(Customer.Id))]
    [MapperIgnoreTarget(nameof(Customer.CompanyId))]
    [MapperIgnoreTarget(nameof(Customer.Created))]
    [MapperIgnoreTarget(nameof(Customer.CreatedBy))]
    [MapperIgnoreTarget(nameof(Customer.LastModified))]
    [MapperIgnoreTarget(nameof(Customer.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(Customer.GcRecord))]
    [MapperIgnoreTarget(nameof(Customer.Company))]
    [MapperIgnoreTarget(nameof(Customer.IdentificationType))]
    [MapperIgnoreTarget(nameof(Customer.Tickets))]
    public partial Customer ToEntity(CreateCustomerCommand command);


    /// <summary>
    /// Maps a nested create-address payload to a CustomerAddress entity.
    /// </summary>
    /// <param name="addressDto">The source create-address payload.</param>
    /// <returns>The mapped CustomerAddress entity.</returns>
    [MapperIgnoreTarget(nameof(CustomerAddress.Id))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CustomerId))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CompanyId))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Created))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CreatedBy))]
    [MapperIgnoreTarget(nameof(CustomerAddress.LastModified))]
    [MapperIgnoreTarget(nameof(CustomerAddress.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(CustomerAddress.GcRecord))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Company))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Customer))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Country))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Region))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Province))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Municipality))]
    [MapperIgnoreTarget(nameof(CustomerAddress.StreetType))]
    public partial CustomerAddress ToAddressEntity(CreateCustomerCommand.CreateCustomerAddressDto addressDto);


    /// <summary>
    /// Maps a nested create-contact payload to a CustomerContact entity.
    /// </summary>
    /// <param name="contactDto">The source create-contact payload.</param>
    /// <returns>The mapped CustomerContact entity.</returns>
    [MapperIgnoreTarget(nameof(CustomerContact.Id))]
    [MapperIgnoreTarget(nameof(CustomerContact.CustomerId))]
    [MapperIgnoreTarget(nameof(CustomerContact.CompanyId))]
    [MapperIgnoreTarget(nameof(CustomerContact.Created))]
    [MapperIgnoreTarget(nameof(CustomerContact.CreatedBy))]
    [MapperIgnoreTarget(nameof(CustomerContact.LastModified))]
    [MapperIgnoreTarget(nameof(CustomerContact.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(CustomerContact.GcRecord))]
    [MapperIgnoreTarget(nameof(CustomerContact.Company))]
    [MapperIgnoreTarget(nameof(CustomerContact.Customer))]
    public partial CustomerContact ToContactEntity(CreateCustomerCommand.CreateCustomerContactDto contactDto);


    /// <summary>
    /// Maps an update-address payload to a CustomerAddress entity.
    /// </summary>
    /// <param name="addressDto">The source update-address payload.</param>
    /// <returns>The mapped CustomerAddress entity.</returns>
    [MapperIgnoreTarget(nameof(CustomerAddress.Id))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CustomerId))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CompanyId))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Created))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CreatedBy))]
    [MapperIgnoreTarget(nameof(CustomerAddress.LastModified))]
    [MapperIgnoreTarget(nameof(CustomerAddress.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(CustomerAddress.GcRecord))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Company))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Customer))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Country))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Region))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Province))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Municipality))]
    [MapperIgnoreTarget(nameof(CustomerAddress.StreetType))]
    [MapperIgnoreSource(nameof(UpdateCustomerCommand.UpdateCustomerAddressDto.Id))]
    public partial CustomerAddress ToAddressEntity(UpdateCustomerCommand.UpdateCustomerAddressDto addressDto);


    /// <summary>
    /// Maps an update-contact payload to a CustomerContact entity.
    /// </summary>
    /// <param name="contactDto">The source update-contact payload.</param>
    /// <returns>The mapped CustomerContact entity.</returns>
    [MapperIgnoreTarget(nameof(CustomerContact.Id))]
    [MapperIgnoreTarget(nameof(CustomerContact.CustomerId))]
    [MapperIgnoreTarget(nameof(CustomerContact.CompanyId))]
    [MapperIgnoreTarget(nameof(CustomerContact.Created))]
    [MapperIgnoreTarget(nameof(CustomerContact.CreatedBy))]
    [MapperIgnoreTarget(nameof(CustomerContact.LastModified))]
    [MapperIgnoreTarget(nameof(CustomerContact.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(CustomerContact.GcRecord))]
    [MapperIgnoreTarget(nameof(CustomerContact.Company))]
    [MapperIgnoreTarget(nameof(CustomerContact.Customer))]
    [MapperIgnoreSource(nameof(UpdateCustomerCommand.UpdateCustomerContactDto.Id))]
    public partial CustomerContact ToContactEntity(UpdateCustomerCommand.UpdateCustomerContactDto contactDto);


    /// <summary>
    /// Applies scalar updates from command to an existing customer entity.
    /// </summary>
    /// <param name="command">Source command data.</param>
    /// <param name="customer">Target tracked customer entity.</param>
    [MapperIgnoreTarget(nameof(Customer.Id))]
    [MapperIgnoreTarget(nameof(Customer.CompanyId))]
    [MapperIgnoreTarget(nameof(Customer.Created))]
    [MapperIgnoreTarget(nameof(Customer.CreatedBy))]
    [MapperIgnoreTarget(nameof(Customer.LastModified))]
    [MapperIgnoreTarget(nameof(Customer.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(Customer.GcRecord))]
    [MapperIgnoreTarget(nameof(Customer.Company))]
    [MapperIgnoreTarget(nameof(Customer.IdentificationType))]
    [MapperIgnoreTarget(nameof(Customer.Addresses))]
    [MapperIgnoreTarget(nameof(Customer.Contacts))]
    [MapperIgnoreTarget(nameof(Customer.Tickets))]
    [MapperIgnoreSource(nameof(UpdateCustomerCommand.Id))]
    [MapperIgnoreSource(nameof(UpdateCustomerCommand.Addresses))]
    [MapperIgnoreSource(nameof(UpdateCustomerCommand.Contacts))]
    public partial void ApplyUpdate(UpdateCustomerCommand command, Customer customer);


    /// <summary>
    /// Applies updates from payload to an existing customer address entity.
    /// </summary>
    /// <param name="source">Source update payload.</param>
    /// <param name="target">Target tracked address entity.</param>
    [MapperIgnoreTarget(nameof(CustomerAddress.Id))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CustomerId))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CompanyId))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Created))]
    [MapperIgnoreTarget(nameof(CustomerAddress.CreatedBy))]
    [MapperIgnoreTarget(nameof(CustomerAddress.LastModified))]
    [MapperIgnoreTarget(nameof(CustomerAddress.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(CustomerAddress.GcRecord))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Company))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Customer))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Country))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Region))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Province))]
    [MapperIgnoreTarget(nameof(CustomerAddress.Municipality))]
    [MapperIgnoreTarget(nameof(CustomerAddress.StreetType))]
    [MapperIgnoreSource(nameof(UpdateCustomerCommand.UpdateCustomerAddressDto.Id))]
    public partial void ApplyUpdate(UpdateCustomerCommand.UpdateCustomerAddressDto source, CustomerAddress target);


    /// <summary>
    /// Applies updates from payload to an existing customer contact entity.
    /// </summary>
    /// <param name="source">Source update payload.</param>
    /// <param name="target">Target tracked contact entity.</param>
    [MapperIgnoreTarget(nameof(CustomerContact.Id))]
    [MapperIgnoreTarget(nameof(CustomerContact.CustomerId))]
    [MapperIgnoreTarget(nameof(CustomerContact.CompanyId))]
    [MapperIgnoreTarget(nameof(CustomerContact.Created))]
    [MapperIgnoreTarget(nameof(CustomerContact.CreatedBy))]
    [MapperIgnoreTarget(nameof(CustomerContact.LastModified))]
    [MapperIgnoreTarget(nameof(CustomerContact.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(CustomerContact.GcRecord))]
    [MapperIgnoreTarget(nameof(CustomerContact.Company))]
    [MapperIgnoreTarget(nameof(CustomerContact.Customer))]
    [MapperIgnoreSource(nameof(UpdateCustomerCommand.UpdateCustomerContactDto.Id))]
    public partial void ApplyUpdate(UpdateCustomerCommand.UpdateCustomerContactDto source, CustomerContact target);

    
    /// <summary>
    /// Projects an IQueryable of Customer entities to an IQueryable of CustomerDtos.
    /// Highly optimized for Entity Framework Core to execute the mapping directly in the SQL SELECT statement.
    /// </summary>
    /// <param name="query">The source IQueryable query from the database context.</param>
    /// <returns>The projected IQueryable of DTOs.</returns>
    public partial IQueryable<CustomerDto> ProjectToDto(IQueryable<Customer> query);


    /// <summary>
    /// Converts the incoming person type string into the domain enum.
    /// </summary>
    /// <param name="personType">The person type text.</param>
    /// <returns>The mapped <see cref="PersonType"/> value.</returns>
    private static PersonType MapPersonType(string personType) => Enum.Parse<PersonType>(personType, true);


    /// <summary>
    /// Converts the incoming contact type string into the domain enum.
    /// </summary>
    /// <param name="contactType">The contact type text.</param>
    /// <returns>The mapped <see cref="ContactType"/> value.</returns>
    private static ContactType MapContactType(string contactType) => Enum.Parse<ContactType>(contactType, true);

}