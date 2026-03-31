# Blueprint: MediatR Query (Dapper & Multi-Tenancy)
Este blueprint define el estándar obligatorio para todas las operaciones de Lectura (Queries) en JOIN CRM. Se utiliza Dapper para garantizar una respuesta en milisegundos y evitar la sobrecarga del Change Tracker de Entity Framework, cumpliendo con el pilar de CQRS Optimizado.

## 1. Definición del DTO (Capa: JOIN.Application.DTO)
Los DTOs deben ser records inmutables. Ubicación: src/2.Application.DTO/{Modulo}/.

```csharp
namespace JOIN.Application.DTO.Admin;

/// <summary>
/// Data transfer object for Customer details
/// </summary>
public record CustomerDto(Guid Id, string Name, string Email, string TaxId);
```

## 2. Definición de la Query (Capa: JOIN.Application)
La Query es un record que transporta los parámetros y hereda de IRequest<Response<T>>. Ubicación: src/2.Application/UseCases/{Modulo}/Queries/.

 ```csharp
using JOIN.Application.Common.Models;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Queries;

public record GetCustomerByIdQuery(Guid Id) : IRequest<Response<CustomerDto>>;
```

## 3. Implementación del Handler (Capa: JOIN.Application)
El Handler ejecuta el SQL crudo. Regla de Oro: Está prohibido usar DbContext o IUnitOfWork en esta clase para operaciones de lectura.

Ubicación: src/2.Application/UseCases/{Modulo}/Queries/

```csharp
using Dapper;
using JOIN.Application.Common.Interfaces;
using JOIN.Application.Common.Models;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Queries;

// Uso obligatorio de Primary Constructors para inyección
public class GetCustomerByIdHandler(
    ISqlConnectionFactory connectionFactory, 
    ICurrentUserService currentUserService) 
    : IRequestHandler<GetCustomerByIdQuery, Response<CustomerDto>>
{
    public async Task<Response<CustomerDto>> Handle(GetCustomerByIdQuery request, CancellationToken ct)
    {
        // 1. Obtener conexión desde la fábrica (Agnóstica a SQL Server/PostgreSQL)
        using var connection = connectionFactory.CreateConnection();

        // 2. SQL Raw con "REGLA DE ORO": Filtrar siempre por CompanyId (Multi-Tenancy)
        // Se utiliza la sintaxis de triple comilla """ para legibilidad.
        const string sql = """
            SELECT Id, Name, Email, TaxId 
            FROM Admin.Customers 
            WHERE Id = @Id AND CompanyId = @TenantId
            """;

        // 3. Ejecución directa con Dapper (Mapeo automático al DTO)
        var result = await connection.QueryFirstOrDefaultAsync<CustomerDto>(sql, new 
        { 
            Id = request.Id, 
            TenantId = currentUserService.CompanyId 
        });

        // 4. Retorno estandarizado usando el modelo Response
        return result is not null 
            ? Response<CustomerDto>.Success(result)
            : Response<CustomerDto>.Error("Customer not found");
    }
}
```

## 4. Reglas Críticas de Cumplimiento (Checklist)
Multi-Tenancy Manual: Toda consulta a entidades que hereden de BaseTenantEntity DEBE incluir el filtro CompanyId = @TenantId de forma manual en el SQL.
Agnosticismo de DB: Prohibido usar funciones específicas de un motor (ej: NOLOCK de SQL Server o ILIKE de Postgres). El SQL debe ser compatible con ambos.
Gestión de Memoria: Siempre usar using var connection para asegurar que la conexión se libere inmediatamente.
Mapeo Directo: Dapper debe mapear directamente al DTO. No instanciar entidades de dominio dentro de una Query.
Namespaces: Respetar la estructura de carpetas UseCases/{Modulo}/Queries/.
Raw Strings: Definir el SQL como una const string dentro del Handler para mantener el caso de uso autocontenido.









