using JOIN.Application.DTO.Admin;
using JOIN.Application.UseCases.Admin.Persons.Commands;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using Riok.Mapperly.Abstractions;



namespace JOIN.Application.Mappings;



/// <summary>
/// Auto-generated mapper for Person entities and DTOs using Riok.Mapperly.
/// Operates at compile-time, ensuring zero reflection overhead and type safety.
/// </summary>
[Mapper]
public partial class PersonMapper : IPersonMapper
{


    /// <summary>
    /// Maps a Person domain entity to a PersonDto.
    /// </summary>
    /// <param name="person">The source Person entity.</param>
    /// <returns>The mapped PersonDto.</returns>
    public partial PersonDto ToDto(Person person);


    /// <summary>
    /// Maps a PersonAddress domain entity to a PersonAddressDto.
    /// </summary>
    /// <param name="address">The source PersonAddress entity.</param>
    /// <returns>The mapped PersonAddressDto.</returns>
    [MapProperty($"{nameof(PersonAddress.StreetType)}.{nameof(StreetType.Name)}", nameof(PersonAddressDto.StreetTypeName))]
    [MapProperty($"{nameof(PersonAddress.Country)}.{nameof(Country.Name)}", nameof(PersonAddressDto.CountryName))]
    [MapProperty($"{nameof(PersonAddress.Region)}.{nameof(Region.Name)}", nameof(PersonAddressDto.RegionName))]
    [MapProperty($"{nameof(PersonAddress.Province)}.{nameof(Province.Name)}", nameof(PersonAddressDto.ProvinceName))]
    [MapProperty($"{nameof(PersonAddress.Municipality)}.{nameof(Municipality.Name)}", nameof(PersonAddressDto.MunicipalityName))]
    [MapperIgnoreSource(nameof(PersonAddress.PersonId))]
    public partial PersonAddressDto ToAddressDto(PersonAddress address);


    /// <summary>
    /// Maps a PersonContact domain entity to a PersonContactDto.
    /// </summary>
    /// <param name="contact">The source PersonContact entity.</param>
    /// <returns>The mapped PersonContactDto.</returns>
    [MapperIgnoreSource(nameof(PersonContact.PersonId))]
    public partial PersonContactDto ToContactDto(PersonContact contact);


    /// <summary>
    /// Maps a PersonDto back to a Person domain entity.
    /// Ignores the Id property on the target to prevent accidental overwrites during creation.
    /// </summary>
    /// <param name="personDto">The source PersonDto.</param>
    /// <returns>The mapped Person entity.</returns>
    [MapperIgnoreTarget(nameof(Person.Id))]
    [MapperIgnoreSource(nameof(PersonDto.IdentificationTypeName))]
    [MapperIgnoreSource(nameof(PersonDto.Addresses))]
    [MapperIgnoreSource(nameof(PersonDto.Contacts))]
    public partial Person ToEntity(PersonDto personDto);


    /// <summary>
    /// Maps a create command to a Person aggregate including nested addresses and contacts.
    /// Tenant ownership (CompanyId) is resolved from the authenticated context in the handler.
    /// </summary>
    /// <param name="command">The source create command payload.</param>
    /// <returns>The mapped Person entity.</returns>
    [MapperIgnoreTarget(nameof(Person.Id))]
    [MapperIgnoreTarget(nameof(Person.CompanyId))]
    [MapperIgnoreTarget(nameof(Person.Created))]
    [MapperIgnoreTarget(nameof(Person.CreatedBy))]
    [MapperIgnoreTarget(nameof(Person.LastModified))]
    [MapperIgnoreTarget(nameof(Person.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(Person.GcRecord))]
    [MapperIgnoreTarget(nameof(Person.Company))]
    [MapperIgnoreTarget(nameof(Person.IdentificationType))]
    [MapperIgnoreTarget(nameof(Person.Gender))]
    [MapperIgnoreTarget(nameof(Person.Tickets))]
    [MapperIgnoreTarget(nameof(Person.Contacts))]
    public partial Person ToEntity(CreatePersonCommand command);


    /// <summary>
    /// Maps a nested create-address payload to a PersonAddress entity.
    /// </summary>
    /// <param name="addressDto">The source create-address payload.</param>
    /// <returns>The mapped PersonAddress entity.</returns>
    [MapperIgnoreTarget(nameof(PersonAddress.Id))]
    [MapperIgnoreTarget(nameof(PersonAddress.PersonId))]
    [MapperIgnoreTarget(nameof(PersonAddress.CompanyId))]
    [MapperIgnoreTarget(nameof(PersonAddress.Created))]
    [MapperIgnoreTarget(nameof(PersonAddress.CreatedBy))]
    [MapperIgnoreTarget(nameof(PersonAddress.LastModified))]
    [MapperIgnoreTarget(nameof(PersonAddress.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(PersonAddress.GcRecord))]
    [MapperIgnoreTarget(nameof(PersonAddress.Company))]
    [MapperIgnoreTarget(nameof(PersonAddress.Person))]
    [MapperIgnoreTarget(nameof(PersonAddress.Country))]
    [MapperIgnoreTarget(nameof(PersonAddress.Region))]
    [MapperIgnoreTarget(nameof(PersonAddress.Province))]
    [MapperIgnoreTarget(nameof(PersonAddress.Municipality))]
    [MapperIgnoreTarget(nameof(PersonAddress.StreetType))]
    public partial PersonAddress ToAddressEntity(CreatePersonCommand.CreatePersonAddressDto addressDto);


    /// <summary>
    /// Maps a nested create-contact payload to a PersonContact entity.
    /// </summary>
    /// <param name="contactDto">The source create-contact payload.</param>
    /// <returns>The mapped PersonContact entity.</returns>
    [MapperIgnoreTarget(nameof(PersonContact.Id))]
    [MapperIgnoreTarget(nameof(PersonContact.PersonId))]
    [MapperIgnoreTarget(nameof(PersonContact.CompanyId))]
    [MapperIgnoreTarget(nameof(PersonContact.Created))]
    [MapperIgnoreTarget(nameof(PersonContact.CreatedBy))]
    [MapperIgnoreTarget(nameof(PersonContact.LastModified))]
    [MapperIgnoreTarget(nameof(PersonContact.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(PersonContact.GcRecord))]
    [MapperIgnoreTarget(nameof(PersonContact.Company))]
    [MapperIgnoreTarget(nameof(PersonContact.Person))]
    [MapperIgnoreTarget(nameof(PersonContact.ContactType))]
    [MapperIgnoreTarget(nameof(PersonContact.ContactValue))]
    [MapperIgnoreTarget(nameof(PersonContact.IsPrimary))]
    [MapperIgnoreTarget(nameof(PersonContact.Comments))]
    [MapperIgnoreTarget(nameof(PersonContact.IsActive))]
    public partial PersonContact ToContactEntity(CreatePersonCommand.CreatePersonContactDto contactDto);


    /// <summary>
    /// Maps an update-address payload to a PersonAddress entity.
    /// </summary>
    /// <param name="addressDto">The source update-address payload.</param>
    /// <returns>The mapped PersonAddress entity.</returns>
    [MapperIgnoreTarget(nameof(PersonAddress.Id))]
    [MapperIgnoreTarget(nameof(PersonAddress.PersonId))]
    [MapperIgnoreTarget(nameof(PersonAddress.CompanyId))]
    [MapperIgnoreTarget(nameof(PersonAddress.Created))]
    [MapperIgnoreTarget(nameof(PersonAddress.CreatedBy))]
    [MapperIgnoreTarget(nameof(PersonAddress.LastModified))]
    [MapperIgnoreTarget(nameof(PersonAddress.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(PersonAddress.GcRecord))]
    [MapperIgnoreTarget(nameof(PersonAddress.Company))]
    [MapperIgnoreTarget(nameof(PersonAddress.Person))]
    [MapperIgnoreTarget(nameof(PersonAddress.Country))]
    [MapperIgnoreTarget(nameof(PersonAddress.Region))]
    [MapperIgnoreTarget(nameof(PersonAddress.Province))]
    [MapperIgnoreTarget(nameof(PersonAddress.Municipality))]
    [MapperIgnoreTarget(nameof(PersonAddress.StreetType))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.UpdatePersonAddressDto.Id))]
    public partial PersonAddress ToAddressEntity(UpdatePersonCommand.UpdatePersonAddressDto addressDto);


    /// <summary>
    /// Maps an update-contact payload to a PersonContact entity.
    /// </summary>
    /// <param name="contactDto">The source update-contact payload.</param>
    /// <returns>The mapped PersonContact entity.</returns>
    [MapperIgnoreTarget(nameof(PersonContact.Id))]
    [MapperIgnoreTarget(nameof(PersonContact.PersonId))]
    [MapperIgnoreTarget(nameof(PersonContact.CompanyId))]
    [MapperIgnoreTarget(nameof(PersonContact.Created))]
    [MapperIgnoreTarget(nameof(PersonContact.CreatedBy))]
    [MapperIgnoreTarget(nameof(PersonContact.LastModified))]
    [MapperIgnoreTarget(nameof(PersonContact.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(PersonContact.GcRecord))]
    [MapperIgnoreTarget(nameof(PersonContact.Company))]
    [MapperIgnoreTarget(nameof(PersonContact.Person))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.UpdatePersonContactDto.Id))]
    [MapperIgnoreTarget(nameof(PersonContact.ContactType))]
    [MapperIgnoreTarget(nameof(PersonContact.ContactValue))]
    [MapperIgnoreTarget(nameof(PersonContact.IsPrimary))]
    [MapperIgnoreTarget(nameof(PersonContact.Comments))]
    [MapperIgnoreTarget(nameof(PersonContact.IsActive))]
    public partial PersonContact ToContactEntity(UpdatePersonCommand.UpdatePersonContactDto contactDto);


    /// <summary>
    /// Applies scalar updates from command to an existing person entity.
    /// </summary>
    /// <param name="command">Source command data.</param>
    /// <param name="person">Target tracked person  entity.</param>
    [MapperIgnoreTarget(nameof(Person.Id))]
    [MapperIgnoreTarget(nameof(Person.CompanyId))]
    [MapperIgnoreTarget(nameof(Person.Created))]
    [MapperIgnoreTarget(nameof(Person.CreatedBy))]
    [MapperIgnoreTarget(nameof(Person.LastModified))]
    [MapperIgnoreTarget(nameof(Person.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(Person.GcRecord))]
    [MapperIgnoreTarget(nameof(Person.Company))]
    [MapperIgnoreTarget(nameof(Person.IdentificationType))]
    [MapperIgnoreTarget(nameof(Person.Gender))]
    [MapperIgnoreTarget(nameof(Person.IsActive))]
    [MapperIgnoreTarget(nameof(Person.Addresses))]
    [MapperIgnoreTarget(nameof(Person.Contacts))]
    [MapperIgnoreTarget(nameof(Person.Tickets))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.Id))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.GenderId))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.IsActive))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.Addresses))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.Contacts))]
    public partial void ApplyUpdate(UpdatePersonCommand command, Person person);


    /// <summary>
    /// Applies updates from payload to an existing person address entity.
    /// </summary>
    /// <param name="source">Source update payload.</param>
    /// <param name="target">Target tracked address entity.</param>
    [MapperIgnoreTarget(nameof(PersonAddress.Id))]
    [MapperIgnoreTarget(nameof(PersonAddress.PersonId))]
    [MapperIgnoreTarget(nameof(PersonAddress.CompanyId))]
    [MapperIgnoreTarget(nameof(PersonAddress.Created))]
    [MapperIgnoreTarget(nameof(PersonAddress.CreatedBy))]
    [MapperIgnoreTarget(nameof(PersonAddress.LastModified))]
    [MapperIgnoreTarget(nameof(PersonAddress.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(PersonAddress.GcRecord))]
    [MapperIgnoreTarget(nameof(PersonAddress.Company))]
    [MapperIgnoreTarget(nameof(PersonAddress.Person))]
    [MapperIgnoreTarget(nameof(PersonAddress.Country))]
    [MapperIgnoreTarget(nameof(PersonAddress.Region))]
    [MapperIgnoreTarget(nameof(PersonAddress.Province))]
    [MapperIgnoreTarget(nameof(PersonAddress.Municipality))]
    [MapperIgnoreTarget(nameof(PersonAddress.StreetType))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.UpdatePersonAddressDto.Id))]
    public partial void ApplyUpdate(UpdatePersonCommand.UpdatePersonAddressDto source, PersonAddress target);


    /// <summary>
    /// Applies updates from payload to an existing person contact entity.
    /// </summary>
    /// <param name="source">Source update payload.</param>
    /// <param name="target">Target tracked contact entity.</param>
    [MapperIgnoreTarget(nameof(PersonContact.Id))]
    [MapperIgnoreTarget(nameof(PersonContact.PersonId))]
    [MapperIgnoreTarget(nameof(PersonContact.CompanyId))]
    [MapperIgnoreTarget(nameof(PersonContact.Created))]
    [MapperIgnoreTarget(nameof(PersonContact.CreatedBy))]
    [MapperIgnoreTarget(nameof(PersonContact.LastModified))]
    [MapperIgnoreTarget(nameof(PersonContact.LastModifiedBy))]
    [MapperIgnoreTarget(nameof(PersonContact.GcRecord))]
    [MapperIgnoreTarget(nameof(PersonContact.Company))]
    [MapperIgnoreTarget(nameof(PersonContact.Person))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.UpdatePersonContactDto.Id))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.UpdatePersonContactDto.ContactType))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.UpdatePersonContactDto.ContactValue))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.UpdatePersonContactDto.IsPrimary))]
    [MapperIgnoreSource(nameof(UpdatePersonCommand.UpdatePersonContactDto.Comments))]
    public partial void ApplyUpdate(UpdatePersonCommand.UpdatePersonContactDto source, PersonContact target);

    
    /// <summary>
    /// Projects an IQueryable of Person entities to an IQueryable of PersonDtos.
    /// Highly optimized for Entity Framework Core to execute the mapping directly in the SQL SELECT statement.
    /// </summary>
    /// <param name="query">The source IQueryable query from the database context.</param>
    /// <returns>The projected IQueryable of DTOs.</returns>
    public partial IQueryable<PersonDto> ProjectToDto(IQueryable<Person> query);


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