using JOIN.Application.DTO.Admin;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using Riok.Mapperly.Abstractions;



namespace JOIN.Application.Mappings;



/// <summary>
/// Auto-generated mapper for Customer entities and DTOs using Riok.Mapperly.
/// Operates at compile-time, ensuring zero reflection overhead and type safety.
/// </summary>
[Mapper]
public partial class CustomerMapper
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
    /// Projects an IQueryable of Customer entities to an IQueryable of CustomerDtos.
    /// Highly optimized for Entity Framework Core to execute the mapping directly in the SQL SELECT statement.
    /// </summary>
    /// <param name="query">The source IQueryable query from the database context.</param>
    /// <returns>The projected IQueryable of DTOs.</returns>
    public partial IQueryable<CustomerDto> ProjectToDto(IQueryable<Customer> query);

}