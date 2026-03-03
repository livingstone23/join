using JOIN.Application.DTO.Admin;
using JOIN.Domain.Admin;
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
    /// Maps a CustomerDto back to a Customer domain entity.
    /// Ignores the Id property on the target to prevent accidental overwrites during creation.
    /// </summary>
    /// <param name="customerDto">The source CustomerDto.</param>
    /// <returns>The mapped Customer entity.</returns>
    [MapperIgnoreTarget(nameof(Customer.Id))]
    public partial Customer ToEntity(CustomerDto customerDto);

    
    /// <summary>
    /// Projects an IQueryable of Customer entities to an IQueryable of CustomerDtos.
    /// Highly optimized for Entity Framework Core to execute the mapping directly in the SQL SELECT statement.
    /// </summary>
    /// <param name="query">The source IQueryable query from the database context.</param>
    /// <returns>The projected IQueryable of DTOs.</returns>
    public partial IQueryable<CustomerDto> ProjectToDto(IQueryable<Customer> query);

}