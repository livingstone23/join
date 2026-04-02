# Blueprint: MediatR Query (Dapper & Performance)

Este blueprint define el estándar obligatorio para todas las operaciones de Lectura (Queries) en JOIN CRM, asegurando un rendimiento extremo y compatibilidad multi-motor (SQL Server/PostgreSQL).

## 1. Definición del DTO (Capa: JOIN.Application.DTO)
Los DTOs deben ser `record` inmutables.
```csharp
namespace JOIN.Application.DTO.Admin;

public record CustomerDto {
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string IdentificationTypeName { get; init; } = string.Empty;
    public List<CustomerAddressDto>? Addresses { get; init; }
}
2. Implementación del Handler (Capa: JOIN.Application)
Reglas de Oro
Inyección: Usar ISqlConnectionFactory para obtener la conexión.
Multi-Tenancy: Filtrar siempre por @TenantId.
Single Roundtrip: Si hay colecciones relacionadas, usar QueryMultipleAsync.
Agnosticismo: Usar CONCAT() para strings y formatear fechas en C#.
Ejemplo de Handler Complejo
C#
public class GetCustomerByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService) 
    : IRequestHandler<GetCustomerByIdQuery, Response<CustomerDto>>
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
3. Checklist de Revisión
[ ] ¿Usa ISqlConnectionFactory?
[ ] ¿Filtra por CompanyId y GcRecord?
[ ] ¿Usa CONCAT en lugar de + o ||?
[ ] ¿El SQL es una const string con triple comilla?
[ ] ¿Se formatea la fecha en el Select de C# y no en el SQL?











