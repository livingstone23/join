# SPEC 18 — CRUD de Roles con /detailed y soft delete

> **Status:** Aprobado
> **Depends on:** SPEC 17 (PermissionFlags), convención CQRS de `CLAUDE.md`
> **Date:** 2026-08-08
> **Objective:** Exponer el CRUD de `ApplicationRole` mediante endpoints `GET /Roles/detailed` (paginado + filtros), `GET /Roles/{id}`, `POST /Roles`, `PUT /Roles/{id}`, `DELETE /Roles/{id}` (soft delete), respetando arquitectura JOIN (CQRS, validators, mappers, Dapper para reads, UnitOfWork + EF para writes, 90% cobertura).

---

## Scope

**In:**

- `src/2.Application.DTO/Security/RoleDto.cs` (nuevo): record inmutable `{ Guid Id, string Name, string NormalizedName, string? Description, bool IsSystemDefault, string? CreatedBy, DateTime Created }`.
- `src/2.Application/Interface/Persistence/Security/IRoleRepository.cs` (nuevo): contrato `Task<RoleDto?> GetByIdAsync(Guid id, CancellationToken ct)`, `Task<(IReadOnlyList<RoleDto> Items, int Total)> GetPagedAsync(string? nameFilter, bool? isActive, int page, int pageSize, CancellationToken ct)`, `Task AddAsync(ApplicationRole role, CancellationToken ct)`, `Task UpdateAsync(ApplicationRole role, CancellationToken ct)`, `Task SoftDeleteAsync(Guid id, string modifiedBy, CancellationToken ct)`, `Task<bool> ExistsByNameAsync(string normalizedName, CancellationToken ct)`.
- `src/3.Persistence/Repositories/Security/RoleRepository.cs` (nuevo): implementación con `DbContext` (writes) y `ISqlConnectionFactory` + Dapper (reads). Filtra `WHERE GcRecord = 0` o `= @isActive_value` según param. Soft delete: `UPDATE [Security].[Roles] SET GcRecord = GcRecord + 1, LastModified = UTC, LastModifiedBy = @userId WHERE Id = @id AND GcRecord = 0`.
- `src/3.Persistence/Configuration/Security/RoleRepositoryServiceCollectionExtensions.cs` (nuevo): `services.AddScoped<IRoleRepository, RoleRepository>()` invocado desde `ConfigureServices.cs` de Persistence.
- `src/2.Application/Mappings/Security/RoleMapper.cs` (nuevo): `IRoleMapper` Mapperly con `RoleDto FromEntity(ApplicationRole role)` y `ApplicationRole ToEntity(CreateRoleCommand cmd)`.
- `src/2.Application/UseCases/Security/Roles/Queries/GetRolesDetailed/GetRolesDetailedQuery.cs` + `GetRolesDetailedQueryHandler.cs`: query con Dapper, paginado (LIMIT/OFFSET y OFFSET...FETCH NEXT), ordena por `Name`.
- `src/2.Application/UseCases/Security/Roles/Queries/GetRoleById/GetRoleByIdQuery.cs` + `GetRoleByIdQueryHandler.cs`: query Dapper por Id.
- `src/2.Application/UseCases/Security/Roles/Commands/CreateRole/CreateRoleCommand.cs` + `Handler` + `Validator` (FluentValidation: `Name` requerido, `MaxLength(256)`, normalizado a mayúsculas; `Description` opcional, `MaxLength(500)`).
- `src/2.Application/UseCases/Security/Roles/Commands/UpdateRole/UpdateRoleCommand.cs` + `Handler` + `Validator`: valida `Name`/`Description` igual que create.
- `src/2.Application/UseCases/Security/Roles/Commands/DeleteRole/DeleteRoleCommand.cs` + `Handler`: sin validator (solo reglas de negocio en handler).
- `src/4.Services.WebApi/Controllers/Security/RolesController.cs` (extendido):
  - `GET /api/v1/Roles` se mantiene como hoy (devuelve `IEnumerable<string>`).
  - `GET /api/v1/Roles/detailed` (nuevo): `[HttpGet("detailed")]` con `[FromQuery] string? name`, `[FromQuery] bool? isActive`, `[FromQuery] int page = 1`, `[FromQuery] int pageSize = 20`. Devuelve `Response<PagedResult<RoleDto>>` con `Items`, `Total`, `Page`, `PageSize`.
  - `GET /api/v1/Roles/{id:guid}` (nuevo): 200 con `RoleDto`, 404 si no existe o `GcRecord != 0`.
  - `POST /api/v1/Roles` (nuevo): body `CreateRoleCommand`, 201, header `Location` apuntando al nuevo endpoint.
  - `PUT /api/v1/Roles/{id:guid}` (nuevo): body `UpdateRoleCommand`, 200 con `RoleDto` actualizado.
  - `DELETE /api/v1/Roles/{id:guid}` (nuevo): 204 si ok, 403 si `IsSystemDefault = true`, 404 si no existe.
- Reglas de negocio en `UpdateRoleCommandHandler`:
  - Si `role.IsSystemDefault == true` y el `Name` enviado difiere del actual → `Response<T>.Error("No se puede modificar el nombre de un rol del sistema. Solo es editable su descripción.")` (mensaje ≥ 50 chars, descriptivo).
  - Si `role.IsSystemDefault == true` y el `IsSystemDefault` enviado es `false` → `Response<T>.Error("No se puede dejar de marcar un rol del sistema. Cree un rol personalizado equivalente en su lugar.")` (mensaje ≥ 50 chars).
  - `Description` siempre editable.
- Reglas en `DeleteRoleCommandHandler`:
  - Si `role.IsSystemDefault == true` → `Response<T>.Error("No se puede eliminar un rol del sistema. Es requerido para el funcionamiento de la aplicación.")` (mensaje ≥ 50 chars).
  - Loggear warning con `ILogger` si hay `UserRoleCompanies` activos referenciando el rol (no bloquea).
- Tests unitarios en `tests/UnitTests/JOIN.Application.UnitTest/Security/Roles/`:
  - `GetRolesDetailedQueryHandlerTests`, `GetRoleByIdQueryHandlerTests`, `CreateRoleCommandHandlerTests`, `CreateRoleCommandValidatorTests`, `UpdateRoleCommandHandlerTests`, `UpdateRoleCommandValidatorTests`, `DeleteRoleCommandHandlerTests`.
  - Cobertura ≥ 90% en clases nuevas (gate de CI).

**Out of scope (para specs futuros):**

- Multi-tenancy de roles (`companyId` en `RoleDto`, scoping por tenant): roles siguen siendo globales (Identity estándar). Si negocio exige scope por tenant, va como spec aparte con su propia migración de modelo.
- Columna `IsActive` real en `AspNetRoles`: `isActive` se deriva de `GcRecord == 0` (estándar del resto del modelo).
- Asignación de roles a usuarios (`POST /Roles/{id}/users` o similar): vive en spec aparte focusing en `UserRoleCompany`.
- Hard delete físico: spec es estrictamente soft delete vía `GcRecord`.
- Invalidación explícita del cache de `RoleManager<ApplicationRole>` (Identity cachea roles en memoria): documentar el riesgo, mitigación = TTL natural. Hard reset del cache queda como ticket.
- Cambio de status code 403 → 401 con flag false: ya cubierto por SPEC 17 (semántica RFC). No se reabre.
- `GET /Roles/detailed` con `HATEOAS` u ordenamiento dinámico: queda out, ordenamiento fijo por `Name`.

---

## Data model

No nuevas entidades. `ApplicationRole` (IdentityRole\<Guid\>) ya existe con `Description`, `IsSystemDefault`, `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `GcRecord`. No se agregan columnas ni tablas.

### `src/2.Application.DTO/Security/RoleDto.cs` (nuevo)

```csharp
namespace JOIN.Application.DTO.Security;

public sealed record RoleDto(
    Guid Id,
    string Name,
    string NormalizedName,
    string? Description,
    bool IsSystemDefault,
    string? CreatedBy,
    DateTime Created);
```

### `src/2.Application/Interface/Persistence/Security/IRoleRepository.cs` (nuevo)

```csharp
using JOIN.Application.DTO.Security;
using JOIN.Domain.Security;

namespace JOIN.Application.Interface.Persistence.Security;

public interface IRoleRepository
{
    Task<RoleDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<RoleDto> Items, int Total)> GetPagedAsync(
        string? nameFilter,
        bool? isActive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task AddAsync(ApplicationRole role, CancellationToken cancellationToken = default);

    Task UpdateAsync(ApplicationRole role, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(Guid id, string modifiedBy, CancellationToken cancellationToken = default);

    Task<bool> ExistsByNameAsync(string normalizedName, CancellationToken cancellationToken = default);
}
```

Convenciones:

- `Id` siempre `Guid` (CLAUDE.md).
- `NormalizedName` lo calcula Identity (`UPPER(name)`); el repo no lo transforma.
- `GetPagedAsync` devuelve tupla `(Items, Total)` para que el handler arme `PagedResult<T>` sin segunda query.
- `SoftDeleteAsync` recibe `modifiedBy` para escribir `LastModifiedBy` en el mismo `UPDATE`.

### `src/2.Application/Mappings/Security/RoleMapper.cs` (nuevo)

```csharp
using JOIN.Application.DTO.Security;
using JOIN.Application.UseCases.Security.Roles.Commands.CreateRole;
using Riok.Mapperly.Abstractions;

namespace JOIN.Application.Mappings.Security;

[Mapper]
public partial interface IRoleMapper
{
    RoleDto FromEntity(Domain.Security.ApplicationRole role);
    Domain.Security.ApplicationRole ToEntity(CreateRoleCommand command);
}
```

Mapperly source-generated. Handler lo inyecta vía DI.

### `src/2.Application/Common/PagedResult.cs` (nuevo, reusable)

```csharp
namespace JOIN.Application.Common;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize);
```

Reusable para futuros endpoints paginados. Ya existen `Response<T>` y este completa el set estándar.

### Cambios en `src/3.Persistence/Configuration/Security/ApplicationRoleConfiguration.cs`

Sin cambios estructurales. Se confirma que el mapeo actual ya incluye `Description`, `IsSystemDefault`, `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `GcRecord`. Verificación previa a F1 del plan.

---

## Implementation plan

### F1 — DTOs, contratos, esqueleto sin lógica

1. Crear `src/2.Application.DTO/Security/RoleDto.cs` con el record.
2. Crear `src/2.Application/Common/PagedResult.cs` con el record genérico.
3. Crear `src/2.Application/Interface/Persistence/Security/IRoleRepository.cs` con la firma completa.
4. Registrar `IRoleRepository` en `src/3.Persistence/Configuration/ConfigureServices.cs` (extensión) y verificar que `Program.cs` ya invoca esa extensión.
5. Crear `src/2.Application/Mappings/Security/RoleMapper.cs` con `IRoleMapper` Mapperly (métodos básicos `FromEntity`/`ToEntity`).
6. `dotnet build -c Release` → 0 errores. El sistema corre igual (no se invoca nada nuevo).

### F2 — `RoleRepository` (Dapper + EF)

1. Crear `src/3.Persistence/Repositories/Security/RoleRepository.cs`. Inyecta `IApplicationDbContext` (writes) + `ISqlConnectionFactory` (reads).
2. `GetByIdAsync`: query Dapper con `WHERE Id = @Id AND GcRecord = 0`. Si `null` → retorna `null`.
3. `GetPagedAsync`:
   - Query count: `SELECT COUNT(*) FROM [Security].[Roles] WHERE (@nameFilter IS NULL OR Name LIKE '%' + @nameFilter + '%') AND (@isActive IS NULL OR ((@isActive = 1 AND GcRecord = 0) OR (@isActive = 0 AND GcRecord <> 0)))`.
   - Query page: `SELECT Id, Name, NormalizedName, Description, IsSystemDefault, CreatedBy, Created FROM [Security].[Roles] WHERE ... ORDER BY Name OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY` (SQL Server) o `LIMIT @pageSize OFFSET @offset` (rama Postgres, ver patrón `GetPersonsPagedQueryHandler`).
   - Si `DbContext` expone provider, branchear; si no, default SQL Server hasta que se conecte Postgres.
   - Devolver `(Items, Total)`.
4. `AddAsync`: `await _dbContext.Roles.AddAsync(role); await _dbContext.SaveChangesAsync(ct);`.
5. `UpdateAsync`: localizar con `FindAsync(id)`, mapear propiedades mutables, `SaveChangesAsync`.
6. `SoftDeleteAsync`: `UPDATE [Security].[Roles] SET GcRecord = GcRecord + 1, LastModified = SYSUTCDATETIME(), LastModifiedBy = @modifiedBy WHERE Id = @id AND GcRecord = 0` vía `ExecuteSqlInterpolatedAsync` o `ExecuteAsync` con parámetros. Retorna `int affected`.
7. `ExistsByNameAsync`: `SELECT 1 FROM [Security].[Roles] WHERE NormalizedName = @normalizedName AND GcRecord = 0`.
8. `dotnet build` → 0 errores. Sin endpoints expuestos, no rompe API.

### F3 — Queries (handlers Dapper)

1. `src/2.Application/UseCases/Security/Roles/Queries/GetRolesDetailed/GetRolesDetailedQuery.cs`:
   ```csharp
   public sealed record GetRolesDetailedQuery(
       string? Name,
       bool? IsActive,
       int Page = 1,
       int PageSize = 20) : IRequest<Response<PagedResult<RoleDto>>>;
   ```
2. `GetRolesDetailedQueryHandler.cs`: inyecta `IRoleRepository`, normaliza `page >= 1`, `pageSize clamp [1, 100]`, llama repo, retorna `Response<PagedResult<RoleDto>>.Ok(...)`.
3. `src/2.Application/UseCases/Security/Roles/Queries/GetRoleById/GetRoleByIdQuery.cs`:
   ```csharp
   public sealed record GetRoleByIdQuery(Guid Id) : IRequest<Response<RoleDto>>;
   ```
4. `GetRoleByIdQueryHandler.cs`: repo `GetByIdAsync` → si null, `Response<RoleDto>.Error("Rol no encontrado o inactivo.", status: 404)`. Si OK, `Response<RoleDto>.Ok(...)`.
5. `dotnet build` → 0 errores.

### F4 — Commands (handlers con UnitOfWork)

1. `src/2.Application/UseCases/Security/Roles/Commands/CreateRole/CreateRoleCommand.cs`:
   ```csharp
   public sealed record CreateRoleCommand(string Name, string? Description, bool IsSystemDefault)
       : IRequest<Response<RoleDto>>;
   ```
2. `CreateRoleCommandValidator.cs`: `RuleFor(c => c.Name).NotEmpty().MaximumLength(256); RuleFor(c => c.Description).MaximumLength(500);`.
3. `CreateRoleCommandHandler.cs`: inyecta `IUnitOfWork`, `IRoleMapper`, `ICurrentUserService`. Calcula `normalizedName = cmd.Name.Trim().ToUpperInvariant()`. Si `ExistsByNameAsync(normalizedName)` → `Response.Error("Ya existe un rol con el nombre '{name}'.", status: 409)`. Construye `ApplicationRole { Name = Name, NormalizedName = normalizedName, Description = Description, IsSystemDefault = IsSystemDefault, CreatedBy = userId, Created = DateTime.UtcNow, GcRecord = 0 }`. Persiste, mapea a DTO, retorna 201.
4. `src/2.Application/UseCases/Security/Roles/Commands/UpdateRole/UpdateRoleCommand.cs`:
   ```csharp
   public sealed record UpdateRoleCommand(Guid Id, string Name, string? Description, bool IsSystemDefault)
       : IRequest<Response<RoleDto>>;
   ```
5. `UpdateRoleCommandValidator.cs`: mismas reglas que Create.
6. `UpdateRoleCommandHandler.cs`: cargar rol por Id (EF, con filter `GcRecord = 0`). Si null → 404. Reglas:
   - `IsSystemDefault = true` actual + `Name` distinto → error descriptivo ≥ 50 chars.
   - `IsSystemDefault = true` actual + `IsSystemDefault=false` enviado → error descriptivo ≥ 50 chars.
   - `IsSystemDefault = false` actual + `IsSystemDefault = true` enviado → error descriptivo ≥ 50 chars (no se permite ascender a system default).
   - Si `Name` cambia → recalcular `NormalizedName`, validar `ExistsByNameAsync` (excluyendo el `Id` actual) → 409 si colisiona.
   - Aplicar cambios: `Name`, `NormalizedName`, `Description`, `IsSystemDefault`, `LastModified = UtcNow`, `LastModifiedBy = userId`. Save. Mapear DTO.
7. `src/2.Application/UseCases/Security/Roles/Commands/DeleteRole/DeleteRoleCommand.cs`:
   ```csharp
   public sealed record DeleteRoleCommand(Guid Id) : IRequest<Response<bool>>;
   ```
8. `DeleteRoleCommandHandler.cs`: cargar rol. Si null → 404. Si `IsSystemDefault = true` → error 403 descriptivo ≥ 50 chars. Contar `UserRoleCompanies` activos (query EF sobre `UserRoleCompany` con `GcRecord = 0`); si > 0, loggear warning. Llamar `IRoleRepository.SoftDeleteAsync(id, userId)`. Confirmar `affected == 1` → 204, else 404.
9. `dotnet build` → 0 errores.

### F5 — Endpoints en `RolesController`

1. `src/4.Services.WebApi/Controllers/Security/RolesController.cs`: extender sin romper `GET /` actual.
   - Inyectar `IMediator` (no seguir con `RoleManager` directo salvo legacy).
   - `GET /api/v1/Roles` se conserva intacto.
   - `GET /api/v1/Roles/detailed` (nuevo): `[HttpGet("detailed")]`, `[ProducesResponseType(typeof(Response<PagedResult<RoleDto>>), 200)]` + `[ProducesResponseType(401)]`. `await _mediator.Send(new GetRolesDetailedQuery(name, isActive, page, pageSize))`.
   - `GET /api/v1/Roles/{id:guid}` (nuevo): `[HttpGet("{id:guid}")]`, `[ProducesResponseType(typeof(Response<RoleDto>), 200)]` + 404. Devuelve handler.
   - `POST /api/v1/Roles` (nuevo): `[HttpPost]`, `[ProducesResponseType(typeof(Response<RoleDto>), 201)]` + 400 + 409. `CreatedAtAction(nameof(GetById), new { id = result.Data.Id }, result)`.
   - `PUT /api/v1/Roles/{id:guid}` (nuevo): `[HttpPut("{id:guid}")]`, 200 + 400 + 404 + 409.
   - `DELETE /api/v1/Roles/{id:guid}` (nuevo): `[HttpDelete("{id:guid}")]`, 204 + 403 + 404.
2. Atributos de permiso por endpoint:
   - `GET /` → sin cambio (HTTP verb → `CanRead`).
   - `GET /detailed` → `[RequirePermission(PermissionFlags.CanRead)]` (default). No requiere override.
   - `GET /{id}` → `CanRead`.
   - `POST` → `CanCreate`.
   - `PUT` → `CanUpdate`.
   - `DELETE` → `CanDelete`.
3. `dotnet build` → 0 errores. Smoke test manual: levantar API, login, navegar a `GET /Roles` (sigue devolviendo strings) y `GET /Roles/detailed?page=1&pageSize=5` (devuelve nueva estructura).

### F6 — Tests unitarios

1. Carpeta `tests/UnitTests/JOIN.Application.UnitTest/Security/Roles/`.
2. `GetRolesDetailedQueryHandlerTests`:
   - Sin filtros → llama repo con `null, null, 1, 20`, retorna `Total` correcto.
   - Filtro `name` lowercase → repo recibe `nameFilter` con case preserved.
   - Filtro `isActive = true` → repo recibe `isActive = true`.
   - `pageSize > 100` → clamp a 100 antes de llamar repo.
   - `page < 1` → clamp a 1.
3. `GetRoleByIdQueryHandlerTests`:
   - Repo devuelve DTO → 200 OK.
   - Repo devuelve null → 404 con mensaje descriptivo.
4. `CreateRoleCommandHandlerTests`:
   - Rol nuevo → `AddAsync` llamado, `ExistsByNameAsync` retorna false, retorna 201 con DTO.
   - `ExistsByNameAsync` true → 409 sin llamar `AddAsync`.
   - `ICurrentUserService.CompanyId == Guid.Empty` → 401 antes de tocar repo.
5. `CreateRoleCommandValidatorTests`:
   - `Name` vacío → `ValidationFailure`.
   - `Name` > 256 chars → `ValidationFailure`.
   - `Description` > 500 chars → `ValidationFailure`.
   - Happy path → pasa.
6. `UpdateRoleCommandHandlerTests`:
   - `IsSystemDefault = true` + `Name` distinto → error descriptivo, no se llama `UpdateAsync`.
   - `IsSystemDefault = true` + `IsSystemDefault = false` → error descriptivo.
   - `IsSystemDefault = false` + `IsSystemDefault = true` → error descriptivo.
   - `Name` cambia + colisión con otro rol → 409.
   - `Name` cambia + sin colisión → update OK.
   - Rol no encontrado → 404.
7. `UpdateRoleCommandValidatorTests`: mismo set que Create.
8. `DeleteRoleCommandHandlerTests`:
   - `IsSystemDefault = true` → 403 descriptivo, no llama `SoftDeleteAsync`.
   - Rol con `UserRoleCompanies` activos → log warning, igual soft delete.
   - Rol no encontrado → 404.
   - Happy path → 204, `SoftDeleteAsync` llamado con `(id, userId)`.
9. `dotnet test` con `--collect:"XPlat Code Coverage"` → cobertura ≥ 90% en clases nuevas.

### F7 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test` con coverage → ≥ 90% Application (cumpliendo CI gate de `.github/workflows/ci.yml`).
3. Smoke: arrancar API, login, `GET /Roles` (sigue devolviendo `IEnumerable<string>`), `GET /Roles/detailed?page=1` (devuelve `PagedResult<RoleDto>`), `POST /Roles` (201), `PUT /Roles/{id}` (200), `DELETE /Roles/{newId}` (204), `DELETE /Roles/{id SystemDefault}` (403 con mensaje).
4. `CURL_REQUESTS.md`: agregar bloque con los 5 nuevos endpoints (igual estilo que otros bloques existentes).
5. Revisar `StandardResilienceHandler` y `Validators` configurados en `JOIN.Application/Common/ConfigureServices.cs` para confirmar que `CreateRoleValidator`/`UpdateRoleValidator` se registran vía `AddValidatorsFromAssemblyContaining` (sin agregar DI manual).

---

## Acceptance criteria

- [ ] Existe `src/2.Application.DTO/Security/RoleDto.cs` con record `{ Guid Id, string Name, string NormalizedName, string? Description, bool IsSystemDefault, string? CreatedBy, DateTime Created }`.
- [ ] Existe `src/2.Application/Common/PagedResult.cs` con record genérico `PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)`.
- [ ] Existe `src/2.Application/Interface/Persistence/Security/IRoleRepository.cs` con los 6 métodos del scope.
- [ ] Existe `src/3.Persistence/Repositories/Security/RoleRepository.cs` con implementación Dapper (`GetById`, `GetPaged`) y EF (writes/`SoftDelete`).
- [ ] `IRoleRepository` está registrado en `ConfigureServices.cs` de Persistence y resuelto correctamente desde `Program.cs`.
- [ ] Existe `src/2.Application/Mappings/Security/RoleMapper.cs` con `IRoleMapper` Mapperly (source-generated, no reflection).
- [ ] `GetRolesDetailedQueryHandler` acepta `name`, `isActive`, `page`, `pageSize`; clamp `pageSize` a `[1, 100]` y `page` a `>= 1`; llama repo; retorna `Response<PagedResult<RoleDto>>`.
- [ ] `GetRolesDetailedQueryHandler` consulta Dapper con `LIMIT/OFFSET` (Postgres) y `OFFSET...FETCH NEXT` (SQL Server) según provider.
- [ ] `GetRoleByIdQueryHandler` retorna 404 con mensaje `Rol no encontrado o inactivo.` cuando repo devuelve null.
- [ ] `CreateRoleCommandHandler` rechaza 409 con mensaje `Ya existe un rol con el nombre '{name}'.` si `ExistsByNameAsync` true.
- [ ] `CreateRoleCommandHandler` setea `NormalizedName = name.Trim().ToUpperInvariant()` antes de persistir.
- [ ] `CreateRoleCommandHandler` retorna 401 si `ICurrentUserService.CompanyId == Guid.Empty`.
- [ ] `UpdateRoleCommandHandler` rechaza con error descriptivo (≥ 50 chars) si `IsSystemDefault = true` y se intenta cambiar `Name`.
- [ ] `UpdateRoleCommandHandler` rechaza con error descriptivo (≥ 50 chars) si `IsSystemDefault = true` y se intenta poner `IsSystemDefault = false`.
- [ ] `UpdateRoleCommandHandler` rechaza con error descriptivo (≥ 50 chars) si `IsSystemDefault = false` y se intenta ascender a `IsSystemDefault = true`.
- [ ] `UpdateRoleCommandHandler` rechaza 409 si `Name` cambia y colisiona con otro rol existente.
- [ ] `DeleteRoleCommandHandler` rechaza 403 con mensaje descriptivo (≥ 50 chars) si `IsSystemDefault = true`.
- [ ] `DeleteRoleCommandHandler` loggea warning y continúa soft delete cuando el rol tiene `UserRoleCompanies` activos.
- [ ] `DeleteRoleCommandHandler` ejecuta `UPDATE [Security].[Roles] SET GcRecord = GcRecord + 1, LastModified = SYSUTCDATETIME(), LastModifiedBy = @userId WHERE Id = @id AND GcRecord = 0`.
- [ ] `RolesController` mantiene `GET /api/v1/Roles` con respuesta `IEnumerable<string>` intacta (no rompe sidebar ni selectors existentes).
- [ ] `GET /api/v1/Roles/detailed?page=1&pageSize=20&name=ad&isActive=true` retorna 200 con `Response<PagedResult<RoleDto>>`.
- [ ] `GET /api/v1/Roles/{id}` retorna 200 con `RoleDto` o 404 con `Role not found`.
- [ ] `POST /api/v1/Roles` con body válido retorna 201 + header `Location: /api/v1/Roles/{newId}`.
- [ ] `PUT /api/v1/Roles/{id}` con body válido retorna 200 con `RoleDto` actualizado.
- [ ] `DELETE /api/v1/Roles/{id}` sobre rol no system default retorna 204.
- [ ] `DELETE /api/v1/Roles/{id}` sobre rol con `IsSystemDefault = true` retorna 403 con mensaje `No se puede eliminar un rol del sistema. Es requerido para el funcionamiento de la aplicación.`.
- [ ] Los 5 endpoints nuevos están decorados con el atributo de permiso correcto (`CanRead`/`CanCreate`/`CanUpdate`/`CanDelete`) según HTTP verb default.
- [ ] Existe `tests/UnitTests/JOIN.Application.UnitTest/Security/Roles/` con 8 archivos de tests cubriendo handlers y validators.
- [ ] Todos los tests pasan: `dotnet test` con `--filter "FullyQualifiedName~Roles"` → 0 fallidos.
- [ ] Cobertura total del proyecto `JOIN.Application` ≥ 90% (gate de CI).
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` incluye los 5 nuevos endpoints con ejemplos de request/response.
- [ ] `Program.cs` corre `MigrateAsync` + seed sin errores al arrancar la API (sin migración nueva, no se requiere `dotnet ef`).

---

## Decisions taken and discarded

- **Mantener `GET /api/v1/Roles` intacto + agregar `GET /Roles/detailed`** (elegido) vs. romper el contrato de `GET /Roles` o deprecado. Elegido porque el sidebar consume `IEnumerable<string>` y romperlo rompe UI. La ruta `/detailed` es paralela, no interrumpe a nadie, y la depreciación se evalúa en spec posterior.
- **`isActive` derivado de `GcRecord == 0`** (elegido) vs. columna nueva `IsActive bool`. Sin nueva columna, sin migración, consistente con patrón del resto del modelo (`Person`, `Company`, etc.). El campo nunca aparece en body de request; solo filtra.
- **`IRoleRepository` por `IUnitOfWork`/EF para writes + Dapper para reads** (elegido) vs. seguir con `RoleManager<ApplicationRole>`. `RoleManager` no soporta soft delete, no soporta cambios de propiedades custom (`IsSystemDefault`, `Description`) sin extension points, y no se integra con `IUnitOfWork.SaveAsync()` para transacciones multi-agregado. Repositorio custom es la única ruta limpia.
- **`SoftDeleteAsync` vía `UPDATE ... SET GcRecord = GcRecord + 1`** (elegido) vs. setear `GcRecord = 1` directo. Incremental permite auditoría de "cuántas veces se soft-deleteó" (no expuesto aún, pero barato y reversible).
- **Bloquear `DELETE` sobre `IsSystemDefault = true`** (elegido) vs. permitir soft delete. Roles seeded (`SuperAdmin`, `Admin`, etc.) no son recuperables sin rerun de seed; bloquear evita footguns.
- **Bloquear cambio de `Name` cuando `IsSystemDefault = true`** (elegido) vs. permitir rename. Renombrar un rol del sistema rompe `RoleManager` checks (claims, validaciones) y sus `RoleSystemOptions` seedeados. Solo `Description` es editable.
- **Bloquear ascenso a `IsSystemDefault = true`** (elegido) vs. permitir flag manual. Escalar un rol custom a system default es una escalada de privilegios grave; debe pasar por otro flujo (spec aparte).
- **Mensajes de error descriptivos ≥ 50 chars** (elegido) vs. mensajes cortos. Usuario explícito pidió que el mensaje fuera suficientemente descriptivo. Largo mínimo evita mensajes crípticos tipo `"Forbidden"` que no orientan.
- **Soft delete con warning cuando hay `UserRoleCompanies` activos** (elegido) vs. bloquear. Las relaciones se mantienen en `AspNetUserRoles`; soft-deleted role rompe autorización para usuarios asignados, pero la limpieza es operación aparte (spec futuro). Warnear visibiliza el problema sin bloquear el spec.
- **Mapperly para `IRoleMapper`** (elegido) vs. AutoMapper. Mapperly es source-generated, no reflection, ya es convención del proyecto (ver `2.Application/Mappings/Security/`, `Admin/`, `Messaging/`).
- **`PagedResult<T>` reusable en `2.Application/Common/`** (elegido) vs. nuevo `RolePagedResult` especifico. La forma `(Items, Total, Page, PageSize)` la van a querer otros endpoints paginados (Persons, Companies, etc.); un solo lugar para reutilizar.
- **No invalidar cache de `RoleManager` en `SoftDeleteAsync`** (elegido) vs. agregar `IRoleStore` reset. Identity cachea roles en memoria con TTL implícito; forzar reset añade complejidad sin ganancia observable para este spec. Documentado en risks.
- **Cobertura ≥ 90% en JOIN.Application** (elegido, mandato de CI) vs. agregar nuevo proyecto o bajar el piso. La regla ya está en `.github/workflows/ci.yml`; el spec no la baja.
- **Sin migración EF** (elegido) vs. agregar columna `IsActive`. Confirmado por usuario: estructura actual se mantiene.
- **Sin multi-tenancy de roles** (elegido, out of scope) vs. `CompanyId` en `RoleDto`. Roles de Identity son globales por convención; cambiar el modelo es un spec aparte de mucho mayor alcance.
- **Default SQL Server en `GetPagedAsync`** (elegido) vs. rama Postgres desde F1. Hasta que se conecte Postgres al proyecto, la rama está pero no se ejercita. Patrón `GetPersonsPagedQueryHandler` ya bifurcaba; replicar.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **`RoleManager<ApplicationRole>` cachea roles en memoria** y `SoftDeleteAsync` no invalida el cache. Tras DELETE, el rol sigue apareciendo en `RoleManager.Roles` hasta TTL/reset. | Documentar el riesgo. Mitigación inmediata: el endpoint `DELETE /Roles/{id}` no se usa en flujo que dependa de `RoleManager` (sidebar/selectors siguen yendo por `GET /Roles` que devuelve strings de la tabla directo). Mitigación futura: ticket TS-18.1 que invalide el cache o reescriba los consumidores de `RoleManager` a `IRoleRepository`. |
| **`UserRoleCompanies` quedan apuntando a roles soft-deleted** → al pedir autorización, `UserRoleCompany` carga rol con `GcRecord > 0` y `PermissionService` puede no encontrar el `SystemOption` (que ya no aplica). | Warning loggeado en `DeleteRoleCommandHandler`. Limpieza de relaciones huérfanas queda en spec aparte (TS-18.2). |
| **Renombrar `Name` de un custom role rompe el FK lógico** de cualquier `RoleSystemOption` que tenga `ControllerName = "Roles"` y permisos por nombre. | `NormalizedName` se recalcula al cambiar `Name`. `RoleSystemOption` referencia por `RoleId` (FK), no por nombre, así que no se rompe. Confirmado en D1 del seed. |
| **Concurrencia en `UpdateRole`**: dos requests PUT simultáneos al mismo Id → segundo sobrescribe al primero sin warning. | `ApplicationRole` no tiene `RowVersion` definido. Mitigación: agregar `RowVersion` queda en TS-18.3. Para MVP, basta con `LastModified` que se sobreescribe (operación idempotente). |
| **DTO `RoleDto` no expone `LastModified`/`LastModifiedBy`** → auditoría incompleta en el contrato. | Decisión consciente: GET por Id y listado devuelven `Created`/`CreatedBy` (auditoría de origen). Mutaciones posteriores viven en `LastModified` interno, no expuesto en esta versión. Si negocio pide, expandir. |
| **`GET /Roles/detailed` paginado sin ordenamiento dinámico** → el cliente no puede elegir orden. | Documentado como out of scope. Orden fijo por `Name ASC` consistente con `GET /Roles/simple` para predictibilidad. |
| **`RequirePermission` resuelve a `descriptor.ControllerName` → `Roles`** (per SPEC 17 fallback). Si `SystemOption.ControllerName` en seed no matchea exactamente `Roles`, los endpoints quedan bloqueados. | Verificar que `SystemOption` con `ControllerName = "Roles"` existe en seed (debería por convención). Si falta, se agrega en spec aparte; este spec no toca configuración de `SystemOption`. |

---

## What is **not** in this spec

- Multi-tenancy de roles (`companyId` en `RoleDto`, scoping por tenant).
- Columna `IsActive` real en `AspNetRoles`.
- Asignación de roles a usuarios (`POST /Roles/{id}/users` o similar).
- Hard delete físico de roles.
- Invalidación explícita del cache de `RoleManager<ApplicationRole>`.
- `GET /Roles/detailed` con `HATEOAS` u ordenamiento dinámico.
- Cambio de status code 403 → 401 (ya cerrado en SPEC 17).
- Migración EF Core (no se modifican tablas).

Cada uno, si llega, va en su propio spec.
