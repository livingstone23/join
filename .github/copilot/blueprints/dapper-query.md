# Blueprint: MediatR Query (Dapper & Performance)

This blueprint defines the mandatory standard for all Read operations (Queries) within JOIN CRM, ensuring extreme performance and multi-engine compatibility (SQL Server/PostgreSQL).

## 1. DTO Definition (Layer: JOIN.Application.DTO)
DTOs must be immutable records.

```csharp
namespace JOIN.Application.DTO.Admin;

public record CustomerDto {
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string IdentificationTypeName { get; init; } = string.Empty;
    public List<CustomerAddressDto>? Addresses { get; init; }
}
´´

## 2. IHandler Implementation (Layer: JOIN.Application)
Golden Rules
- Injection: Use ISqlConnectionFactory to obtain the connection.
- Multi-Tenancy: Always filter by @TenantId.
- Single Roundtrip: For related collections, use QueryMultipleAsync.
- Agnosticism: Use CONCAT() for strings and format dates in C#.

Complex Handler Example

```csharp
public class GetCustomerByIdQueryHandler(ISqlConnectionFactory connectionFactory,ICurrentUserService currentUserService) : IRequestHandler<GetCustomerByIdQuery, Response<CustomerDto>>
{
    public async Task<Response<CustomerDto>> Handle(GetCustomerByIdQuery request, CancellationToken ct)
    {
        using var connection = connectionFactory.CreateConnection();

        // SQL using standard JOINS and multiple SELECTs
        const string sql = """
            -- Query 1: Main Entity + Catalogs
            SELECT c.Id, CONCAT(c.FirstName, ' ', c.LastName) AS Name, it.Name AS IdentificationTypeName
            FROM Admin.Customers c
            LEFT JOIN Admin.IdentificationTypes it ON c.IdentificationTypeId = it.Id
            WHERE c.Id = @Id AND c.CompanyId = @TenantId AND c.GcRecord = 0;

            -- Query 2: Related Collection
            SELECT a.Id, a.AddressLine1, co.Name AS CountryName, a.Created
            FROM Admin.CustomerAddresses a
            LEFT JOIN Common.Countries co ON a.CountryId = co.Id
            WHERE a.CustomerId = @Id AND a.CompanyId = @TenantId AND a.GcRecord = 0;
            """;

        using var multi = await connection.QueryMultipleAsync(sql, new 
        { 
            Id = request.CustomerId, 
            TenantId = currentUserService.CompanyId 
        });

        var customer = await multi.ReadFirstOrDefaultAsync<CustomerDto>();
        if (customer == null) return Response<CustomerDto>.Error("Not found");

        var addressesRaw = await multi.ReadAsync<dynamic>();
        
        // Final mapping with C# formatting
        return Response<CustomerDto>.Success(customer with {
            Addresses = addressesRaw.Select(a => new CustomerAddressDto {
                Id = a.Id,
                FullAddress = a.AddressLine1,
                CountryName = a.CountryName,
                CreatedAt = a.Created != null ? ((DateTime)a.Created).ToString("yyyy-MM-dd") : ""
            }).ToList()
        });
    }
}
´´

## 3. Review Checklist
[ ] Uses ISqlConnectionFactory?
[ ] Filters by CompanyId and GcRecord?
[ ] Uses CONCAT instead of + or ||?
[ ] SQL is a const string with triple quotes?
[ ] Date formatting is handled in C# Select, not in SQL?











