using JOIN.Application.UseCases.Admin.Persons.Commands;
using JOIN.Application.DTO.Admin;
using JOIN.Domain.Admin;



namespace JOIN.Application.Mappings;



/// <summary>
/// Defines mapping operations for person aggregate transformations.
/// </summary>
public interface IPersonMapper
{


    /// <summary>
    /// Maps a person aggregate root to a DTO.
    /// </summary>
    PersonDto ToDto(Person person);

    /// <summary>
    /// Maps a person address entity to a DTO.
    /// </summary>
    PersonAddressDto ToAddressDto(PersonAddress address);

    /// <summary>
    /// Maps a person contact entity to a DTO.
    /// </summary>
    PersonContactDto ToContactDto(PersonContact contact);

    /// <summary>
    /// Maps a person DTO to a domain entity.
    /// </summary>
    Person ToEntity(PersonDto personDto);

    /// <summary>
    /// Maps a create command payload to a full person aggregate.
    /// </summary>
    Person ToEntity(CreatePersonCommand command);

    /// <summary>
    /// Maps an update-address payload to a new person address entity.
    /// </summary>
    PersonAddress ToAddressEntity(UpdatePersonCommand.UpdatePersonAddressDto addressDto);

    /// <summary>
    /// Maps an update-contact payload to a new person contact entity.
    /// </summary>
    PersonContact ToContactEntity(UpdatePersonCommand.UpdatePersonContactDto contactDto);

    /// <summary>
    /// Applies scalar person updates from command to an existing person entity.
    /// </summary>
    void ApplyUpdate(UpdatePersonCommand command, Person person);

    /// <summary>
    /// Applies address updates from DTO to an existing address entity.
    /// </summary>
    void ApplyUpdate(UpdatePersonCommand.UpdatePersonAddressDto source, PersonAddress target);

    /// <summary>
    /// Applies contact updates from DTO to an existing contact entity.
    /// </summary>
    void ApplyUpdate(UpdatePersonCommand.UpdatePersonContactDto source, PersonContact target);

    /// <summary>
    /// Projects a queryable source to a queryable destination.
    /// </summary>
    IQueryable<PersonDto> ProjectToDto(IQueryable<Person> query);

    
}
