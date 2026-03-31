# Blueprint: MediatR Command Handler (Riok.Mapperly version)
Usa esta estructura para todos los Commands en JOIN.Application.

## Estructura
1. **Namespace**: `JOIN.Application.UseCases.{Modulo}.Commands`
2. **Request**: Usar `record` y heredar de `IRequest<Response<{T}>>`.
3. **Handler**: Usar Primary Constructors para inyectar `IUnitOfWork` y el Mapper específico (ej: `ICustomerMapper`).
4. **Respuesta**: Siempre retornar `Response<{T}>.Success(data)` o `.Error(message)`.

```csharp
namespace JOIN.Application.UseCases.Admin.Commands;

public record CreateCustomerCommand(string Name, string Email) : IRequest<Response<Guid>>;

// Inyectamos ICustomerMapper (generado por Mapperly) no un IMapper genérico
public class CreateCustomerHandler(IUnitOfWork unitOfWork, ICustomerMapper mapper) 
    : IRequestHandler<CreateCustomerCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(CreateCustomerCommand request, CancellationToken ct)
    {
        // Mapperly genera la implementación de ToEntity en tiempo de compilación
        var entity = mapper.ToEntity(request);
        
        await unitOfWork.Repository<Customer>().AddAsync(entity);
        await unitOfWork.SaveChangesAsync(ct);
        
        return Response<Guid>.Success(entity.Id);
    }
}