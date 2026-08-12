# SPEC 22 — RoleSystemOption DTO completeness (OrderMenu + CanDownload/Export/Execute + IsVisibleMenu)

> **Status:** Draft
> **Depends on:** SPEC 12 (CanExport/CanExecute en RoleSystemOption), SPEC 16 (CanExport/CanExecute en SystemOption), SPEC 17 (PermissionFlags), SPEC 21 (template de DTO completeness para SystemOption)
> **Date:** 2026-08-11
> **Objective:** Completar los DTOs (`RoleSystemOptionDto` + `RoleSystemOptionListItemDto`), los commands (`Create/Update`), validators, mapper y los handlers de query de `RoleSystemOptionsController` con los campos `OrderMenu`, `CanDownload`, `CanExport`, `CanExecute` e `IsVisibleMenu` ya presentes en el entity, exponer los 5 como filtros opcionales del paged endpoint, mantener cobertura ≥ 90% y no romper clientes existentes que envían el body viejo.

---

## Scope

**In:**

- `src/2.Application.DTO/Security/RoleSystemOption/RoleSystemOptionDto.cs` (extender): añadir `CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu` entre `CanDelete` y `Created` (mismo orden que SPEC 21 SystemOption para consistencia entre los dos DTOs hermanos).
- `src/2.Application.DTO/Security/RoleSystemOption/RoleSystemOptionListItemDto.cs` (extender): añadir los mismos 5 campos en el mismo orden.
- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/CreateRoleSystemOption/CreateRoleSystemOptionCommand.cs` (extender): añadir `bool CanDownload = true`, `bool CanExport = true`, `bool CanExecute = true`, `bool IsVisibleMenu = true`, `int? OrderMenu = 0` después de `CanDelete` (mismo patrón de defaults `true/true/true/true/0` que SPEC 21).
- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/UpdateRoleSystemOption/UpdateRoleSystemOptionCommand.cs` (extender): mismo set con los mismos defaults; `[property: JsonIgnore] Guid Id` intacto.
- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/CreateRoleSystemOption/CreateRoleSystemOptionCommandValidator.cs` (extender): regla `RuleFor(x => x.OrderMenu).GreaterThanOrEqualTo(0).LessThanOrEqualTo(10000).When(x => x.OrderMenu.HasValue);`. Sin reglas para los `bool`.
- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/UpdateRoleSystemOption/UpdateRoleSystemOptionCommandValidator.cs` (extender): misma regla.
- `src/2.Application/Mappings/Security/RoleSystemOption/RoleSystemOptionMapper.cs` (verificar/ajustar): Mapperly auto-mapea por nombre — `ToEntity`, `ToDto(entity)`, `ToDto(readModel)` y `ApplyUpdate` deben proyectar los 5 campos nuevos. Si Mapperly no infiere algún campo por diferencia de tipo (`int?` en command vs `int?` en entity = OK), no requiere cambios manuales; si falla la generación, agregar asignaciones explícitas en cada método.
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/RoleSystemOptionQuerySql.cs` (extender):
  - `SelectListProjection`: añadir 5 columnas después de `rso.CanDelete` → `rso.CanDownload`, `rso.CanExport`, `rso.CanExecute`, `rso.IsVisibleMenu`, `rso.OrderMenu`.
  - `BuildWhereClause`: añadir 5 ramas `if (filters.CanXxx.HasValue) { whereBuilder.Append(...); parameters.Add(...); }` después del bloque `CanDelete`.
  - `RoleSystemOptionQueryFilters` record: añadir 5 parámetros nullable al final (`bool? CanDownload`, `bool? CanExport`, `bool? CanExecute`, `bool? IsVisibleMenu`, `int? OrderMenu`).
  - `BuildPagedSql` / `BuildByIdSql`: sin cambios (componen sobre `SelectListProjection` + `whereClause`).
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetRoleSystemOptionsPaged/GetRoleSystemOptionsPagedQuery.cs` (extender): añadir los 5 parámetros nullable al final del record constructor.
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetRoleSystemOptionsPaged/GetRoleSystemOptionsPagedQueryHandler.cs` (extender): pasar los 5 nuevos campos al `RoleSystemOptionQueryFilters` en `BuildWhereClause`.
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetSuperAdminAllRoleSystemOptionsPaged/GetSuperAdminAllRoleSystemOptionsPagedQuery.cs` (extender): mismo set, antes del `CompanyId` final.
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetSuperAdminAllRoleSystemOptionsPaged/GetSuperAdminAllRoleSystemOptionsPagedQueryHandler.cs` (extender): mismo cambio que el handler Paged.
- `src/4.Services.WebApi/Controllers/Security/RoleSystemOptionsController.cs` (extender):
  - `GetPaged` (líneas 65-95): añadir 5 `[FromQuery] bool? / int?` entre `canDelete` y `CancellationToken`. Actualizar el `new GetRoleSystemOptionsPagedQuery(...)` con los 5 valores.
  - `GetSuperAdminPaged` (líneas 195-228): añadir los mismos 5 `[FromQuery]` antes del `companyId`. Actualizar el `new GetSuperAdminAllRoleSystemOptionsPagedQuery(...)`.
  - `GetById`, `Create`, `Update`, `Delete`: sin cambios (los nuevos flags entran por `[FromBody]` con defaults).
- Tests unitarios nuevos / actualizados en `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleSystemOptions/` (carpeta actualmente vacía — todo nuevo):
  - `CreateRoleSystemOptionCommandValidatorTests.cs` (nuevo): cubre `OrderMenu < 0`, `OrderMenu > 10000`, `OrderMenu = null` pasa, happy path.
  - `UpdateRoleSystemOptionCommandValidatorTests.cs` (nuevo): mismos casos.
  - `CreateRoleSystemOptionCommandHandlerTests.cs` (nuevo): mock `IUnitOfWork` + `IRoleSystemOptionMapper`. Verificar que el mapper recibe los 5 nuevos campos y el DTO retornado los expone.
  - `UpdateRoleSystemOptionCommandHandlerTests.cs` (nuevo): mock del mapper, verificar que `ApplyUpdate` recibe los 5 nuevos campos y los aplica al entity cargado.
  - `GetRoleSystemOptionByIdQueryHandlerTests.cs` (nuevo): mock `ISqlConnectionFactory`; assert que el SQL generado (vía `RoleSystemOptionQuerySql.BuildByIdSql`) incluye las 5 columnas nuevas en el SELECT.
  - `GetRoleSystemOptionsPagedQueryHandlerTests.cs` (nuevo): mock `ISqlConnectionFactory`; assert que el WHERE incluye los 5 nuevos filtros cuando se pasan, y no los incluye cuando son null. Paginación/filtros existentes intactos.
  - `GetSuperAdminAllRoleSystemOptionsPagedQueryHandlerTests.cs` (nuevo): assert que `RequireCompanyFilter` se sigue computando correctamente con los nuevos filtros.
  - Cobertura ≥ 90% en clases nuevas y modificadas (gate CI).
- `CURL_REQUESTS.md`: extender el bloque de `RoleSystemOptions` con un POST/PUT que incluya los nuevos campos + un GET-paged con `?canExport=true&orderMenu=0` + ejemplo de GET-by-id con respuesta completa.

**Out of scope:**

- Cambios al entity `RoleSystemOption` (los 5 campos ya existen desde SPEC 12/16).
- Migración EF Core (sin cambios de schema).
- Cambios al seeder `DatabaseSeeder.cs` (defaults del entity cubren filas existentes).
- Cambios a `RoleSystemOptionConfiguration.cs` (defaults ya están mapeados desde SPEC 12/16).
- Cambios al endpoint `DELETE` (no recibe body — sin cambios).
- Cambios al filtro `DynamicAuthorizationFilter` (enforcement de `CanDownload`/`CanExport`/`CanExecute`/`IsVisibleMenu` queda diferido al spec dedicado que cubre SPEC 21 también).
- Branch de paginación Postgres (`LIMIT/OFFSET`) — el SQL actual usa exclusivamente sintaxis SQL Server (`OFFSET ... ROWS FETCH NEXT ...`). Sin branch Postgres en `RoleSystemOptionQuerySql`.
- Reordenar campos pre-existentes en DTOs (orden de declaración = orden de serialización JSON; los nuevos van después de `CanDelete`).
- `RoleSystemOptionReadModel` (definido en `RoleSystemOptionsRepository.cs`): si el read model no expone los 5 campos, queda como gap — Dapper mapeará los nuevos SELECT columns a default (`false`/`0`) y un spec aparte lo cubre si el repo lo usa.

---

## Data model

Sin entidades nuevas. Entity `RoleSystemOption` ya expone los 5 campos (SPEC 12 + SPEC 16). Cambios viven en DTOs, commands, query record de filtros compartidos y mapper.

### `src/2.Application.DTO/Security/RoleSystemOption/RoleSystemOptionDto.cs` (extendido)

```csharp
namespace JOIN.Application.DTO.Security;

public sealed record RoleSystemOptionDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public Guid SystemOptionId { get; init; }
    public string SystemOptionName { get; init; } = string.Empty;
    public bool CanRead { get; init; }
    public bool CanCreate { get; init; }
    public bool CanUpdate { get; init; }
    public bool CanDelete { get; init; }
    public bool CanDownload { get; init; }
    public bool CanExport { get; init; }
    public bool CanExecute { get; init; }
    public bool IsVisibleMenu { get; init; }
    public int? OrderMenu { get; init; }
    public DateTime Created { get; init; }
}
```

### `src/2.Application.DTO/Security/RoleSystemOption/RoleSystemOptionListItemDto.cs` (extendido)

```csharp
namespace JOIN.Application.DTO.Security;

public sealed record RoleSystemOptionListItemDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public Guid RoleId { get; init; }
    public string RoleName { get; init; } = string.Empty;
    public Guid SystemOptionId { get; init; }
    public string SystemOptionName { get; init; } = string.Empty;
    public bool CanRead { get; init; }
    public bool CanCreate { get; init; }
    public bool CanUpdate { get; init; }
    public bool CanDelete { get; init; }
    public bool CanDownload { get; init; }
    public bool CanExport { get; init; }
    public bool CanExecute { get; init; }
    public bool IsVisibleMenu { get; init; }
    public int? OrderMenu { get; init; }
    public DateTime Created { get; init; }
}
```

### `CreateRoleSystemOptionCommand` (extendido)

```csharp
public sealed record CreateRoleSystemOptionCommand(
    Guid CompanyId,
    Guid RoleId,
    Guid SystemOptionId,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload = true,
    bool CanExport = true,
    bool CanExecute = true,
    bool IsVisibleMenu = true,
    int? OrderMenu = 0)
    : ITransactionalCommand<Response<RoleSystemOptionDto>>;
```

### `UpdateRoleSystemOptionCommand` (extendido)

```csharp
public sealed record UpdateRoleSystemOptionCommand(
    [property: JsonIgnore] Guid Id,
    Guid CompanyId,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload = true,
    bool CanExport = true,
    bool CanExecute = true,
    bool IsVisibleMenu = true,
    int? OrderMenu = 0)
    : ITransactionalCommand<Response<RoleSystemOptionDto>>;
```

### `RoleSystemOptionQueryFilters` (extendido, en `RoleSystemOptionQuerySql.cs`)

```csharp
internal sealed record RoleSystemOptionQueryFilters(
    Guid? CompanyId,
    bool RequireCompanyFilter,
    Guid? RoleId = null,
    Guid? SystemOptionId = null,
    string? RoleName = null,
    string? SystemOptionName = null,
    string? CompanyName = null,
    bool? CanRead = null,
    bool? CanCreate = null,
    bool? CanUpdate = null,
    bool? CanDelete = null,
    bool? CanDownload = null,
    bool? CanExport = null,
    bool? CanExecute = null,
    bool? IsVisibleMenu = null,
    int? OrderMenu = null);
```

### SQL — `SelectListProjection` (extendido)

```sql
SELECT
    rso.Id,
    rso.CompanyId,
    c.Name AS CompanyName,
    rso.RoleId,
    ar.Name AS RoleName,
    rso.SystemOptionId,
    so.Name AS SystemOptionName,
    rso.CanRead,
    rso.CanCreate,
    rso.CanUpdate,
    rso.CanDelete,
    rso.CanDownload,
    rso.CanExport,
    rso.CanExecute,
    rso.IsVisibleMenu,
    rso.OrderMenu,
    rso.Created
```

El cambio se aplica una sola vez en `RoleSystemOptionQuerySql.SelectListProjection` y se propaga automáticamente a `GetById` (vía `BuildByIdSql`), `GetPaged` (vía `BuildPagedSql`) y `GetSuperAdminPaged` (vía `BuildPagedSql` también).

---

## Implementation plan

### F1 — DTOs

1. Editar `src/2.Application.DTO/Security/RoleSystemOption/RoleSystemOptionDto.cs`: añadir 5 propiedades (`CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu`) entre `CanDelete` y `Created`.
2. Editar `src/2.Application.DTO/Security/RoleSystemOption/RoleSystemOptionListItemDto.cs`: añadir los mismos 5 campos en el mismo orden.
3. `dotnet build` → 0 errores. DTOs extendidos no rompen callers existentes (campos viejos siguen presentes).

### F2 — Commands

1. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/CreateRoleSystemOption/CreateRoleSystemOptionCommand.cs`: añadir 5 parámetros con defaults (`CanDownload = true`, `CanExport = true`, `CanExecute = true`, `IsVisibleMenu = true`, `OrderMenu = 0`) después de `CanDelete`.
2. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/UpdateRoleSystemOption/UpdateRoleSystemOptionCommand.cs`: mismo set con mismos defaults; `[property: JsonIgnore] Guid Id` intacto.
3. `dotnet build` → 0 errores. Clients que envían el body viejo (sin los 5 campos) siguen funcionando vía defaults.

### F3 — Validators

1. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/CreateRoleSystemOption/CreateRoleSystemOptionCommandValidator.cs`: añadir `RuleFor(x => x.OrderMenu).GreaterThanOrEqualTo(0).LessThanOrEqualTo(10000).When(x => x.OrderMenu.HasValue);`. Sin reglas para los `bool`.
2. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/UpdateRoleSystemOption/UpdateRoleSystemOptionCommandValidator.cs`: misma regla.
3. `dotnet build` → 0 errores.

### F4 — Mapper

1. Verificar `src/2.Application/Mappings/Security/RoleSystemOption/RoleSystemOptionMapper.cs`: Mapperly debe inferir los 5 campos nuevos automáticamente (mismo nombre/tipo en source y target). Si la generación falla, agregar asignaciones explícitas en `ToEntity`, `ToDto(entity)`, `ToDto(readModel)` y `ApplyUpdate`.
2. `dotnet build` → 0 errores. Confirmar que `RoleSystemOptionMapper.generated.cs` incluye los nuevos campos.

### F5 — Query SQL compartido

1. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/RoleSystemOptionQuerySql.cs`:
   - `SelectListProjection`: añadir 5 columnas (`rso.CanDownload`, `rso.CanExport`, `rso.CanExecute`, `rso.IsVisibleMenu`, `rso.OrderMenu`) entre `rso.CanDelete` y `rso.Created`.
   - `RoleSystemOptionQueryFilters` record: añadir 5 parámetros nullable al final (`bool? CanDownload`, `bool? CanExport`, `bool? CanExecute`, `bool? IsVisibleMenu`, `int? OrderMenu`).
   - `BuildWhereClause`: añadir 5 ramas `if (filters.CanXxx.HasValue) { ... }` después del bloque `CanDelete`. Para `OrderMenu` usar `rso.OrderMenu = @OrderMenu` cuando llega valor exacto.
2. `dotnet build` → 0 errores. El cambio se propaga automáticamente a GetById, GetPaged y GetSuperAdminPaged.

### F6 — Query records + handlers

1. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetRoleSystemOptionsPaged/GetRoleSystemOptionsPagedQuery.cs`: añadir los 5 parámetros al final del record constructor.
2. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetRoleSystemOptionsPaged/GetRoleSystemOptionsPagedQueryHandler.cs`: pasar los 5 valores al `new RoleSystemOptionQueryFilters(...)`.
3. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetSuperAdminAllRoleSystemOptionsPaged/GetSuperAdminAllRoleSystemOptionsPagedQuery.cs`: mismo set antes del `CompanyId` final.
4. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetSuperAdminAllRoleSystemOptionsPaged/GetSuperAdminAllRoleSystemOptionsPagedQueryHandler.cs`: mismo cambio.
5. `dotnet build` → 0 errores.

### F7 — Controller

1. Editar `src/4.Services.WebApi/Controllers/Security/RoleSystemOptionsController.cs`:
   - `GetPaged` (líneas 65-95): añadir `[FromQuery] bool? canDownload = null`, `[FromQuery] bool? canExport = null`, `[FromQuery] bool? canExecute = null`, `[FromQuery] bool? isVisibleMenu = null`, `[FromQuery] int? orderMenu = null` después de `canDelete`. Actualizar el `new GetRoleSystemOptionsPagedQuery(...)` con los 5 valores.
   - `GetSuperAdminPaged` (líneas 195-228): añadir los mismos 5 `[FromQuery]` antes del `companyId`. Actualizar el `new GetSuperAdminAllRoleSystemOptionsPagedQuery(...)`.
   - XML doc del método: actualizar la lista de parámetros en el summary.
   - `GetById`, `Create`, `Update`, `Delete`: sin cambios.
2. `dotnet build` → 0 errores.

### F8 — Tests unitarios

1. Crear `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleSystemOptions/CreateRoleSystemOptionCommandValidatorTests.cs` (4-5 `[Fact]`): `OrderMenu = -1` falla, `OrderMenu = 10001` falla, `OrderMenu = null` pasa, `OrderMenu = 0` pasa, happy path con todos los defaults.
2. Crear `UpdateRoleSystemOptionCommandValidatorTests.cs`: mismos casos + `Id = Guid.Empty` falla.
3. Crear `CreateRoleSystemOptionCommandHandlerTests.cs`: mock `IUnitOfWork` + `IRoleSystemOptionMapper`. Verificar que el mapper recibe `CanDownload/Export/Execute/IsVisibleMenu/OrderMenu` desde el command y que el DTO retornado los expone.
4. Crear `UpdateRoleSystemOptionCommandHandlerTests.cs`: mock del mapper, verificar que `ApplyUpdate` recibe los 5 nuevos campos.
5. Crear `GetRoleSystemOptionByIdQueryHandlerTests.cs`: mock `ISqlConnectionFactory` + `ICurrentUserService`. Assert que `RoleSystemOptionQuerySql.BuildByIdSql` incluye los 5 columnas en el SELECT (vía inspection del `CommandDefinition` pasado a `QuerySingleOrDefaultAsync`). Happy path, `CompanyId == Guid.Empty` retorna `INVALID_COMPANY_ID`, not-found retorna `ROLE_SYSTEM_OPTION_NOT_FOUND`.
6. Crear `GetRoleSystemOptionsPagedQueryHandlerTests.cs`: mock `ISqlConnectionFactory`. Assert que el WHERE incluye `AND rso.CanExport = @CanExport` cuando se pasa el filtro, y no lo incluye cuando es null. Paginación y filtros existentes intactos. `CompanyId == Guid.Empty` retorna `INVALID_COMPANY_ID`.
7. Crear `GetSuperAdminAllRoleSystemOptionsPagedQueryHandlerTests.cs`: assert que `RequireCompanyFilter` se computa correctamente (true solo si `CompanyId.HasValue`). Filtros nuevos aplican como en el handler Paged.
8. `dotnet test --filter "FullyQualifiedName~RoleSystemOptions"` → 0 fallidos. Cobertura ≥ 90% en clases nuevas y modificadas.

### F9 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test` con `--collect:"XPlat Code Coverage"` → cobertura total `JOIN.Application` ≥ 90% (gate CI).
3. Smoke test manual con `dotnet run`:
   - `GET /api/v1/RoleSystemOptions/{seedId}` → 200 con `canDownload`/`canExport`/`canExecute`/`isVisibleMenu`/`orderMenu` poblados.
   - `GET /api/v1/RoleSystemOptions?canExport=true&orderMenu=0` → 200, solo reglas con `CanExport = true` y `OrderMenu = 0`.
   - `GET /api/v1/RoleSystemOptions/superadmin/all?canDownload=true` → 200, solo reglas cross-tenant con `CanDownload = true`.
   - `POST /api/v1/RoleSystemOptions` con body conteniendo los 5 nuevos campos → 200/201 con DTO que refleja los valores.
   - `PUT /api/v1/RoleSystemOptions/{id}` con body conteniendo los 5 nuevos campos → 200 con DTO actualizado.
4. `CURL_REQUESTS.md`: extender el bloque `RoleSystemOptions` con POST/PUT que envíe los nuevos campos, GET-paged con filtros nuevos, y GET-by-id con respuesta completa.

---

## Acceptance criteria

- [ ] `RoleSystemOptionDto` expone `CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu` además de los campos previos. Mantiene `Id`, `CompanyId`, `CompanyName`, `RoleId`, `RoleName`, `SystemOptionId`, `SystemOptionName`, `CanRead/Create/Update/Delete`, `Created`.
- [ ] `RoleSystemOptionListItemDto` expone los mismos 5 campos en el mismo orden, además de los campos previos.
- [ ] `CreateRoleSystemOptionCommand` acepta los 5 nuevos parámetros con defaults (`CanDownload=true`, `CanExport=true`, `CanExecute=true`, `IsVisibleMenu=true`, `OrderMenu=0`) sin romper compatibilidad con clientes que envíen el body viejo.
- [ ] `UpdateRoleSystemOptionCommand` acepta los 5 nuevos parámetros con los mismos defaults; `[property: JsonIgnore] Guid Id` intacto.
- [ ] `CreateRoleSystemOptionCommandValidator` rechaza `OrderMenu < 0` y `OrderMenu > 10000`. Acepta `OrderMenu = null` y `OrderMenu = 0`.
- [ ] `UpdateRoleSystemOptionCommandValidator` rechaza los mismos casos que Create.
- [ ] `RoleSystemOptionMapper` (Mapperly source-gen) incluye los 5 campos en `ToEntity`, `ToDto(entity)`, `ToDto(readModel)` y `ApplyUpdate`. Si la generación falla, las asignaciones manuales reflejan los mismos 5 campos.
- [ ] `RoleSystemOptionQuerySql.SelectListProjection` proyecta los 5 nuevos campos después de `CanDelete`.
- [ ] `RoleSystemOptionQueryFilters` acepta los 5 nuevos parámetros nullable.
- [ ] `RoleSystemOptionQuerySql.BuildWhereClause` agrega los 5 nuevos `AND rso.CanXxx = @CanXxx` / `AND rso.OrderMenu = @OrderMenu` cuando los filtros llegan no-nulos, y los omite cuando son null.
- [ ] `GetRoleSystemOptionsPagedQuery` acepta los 5 nuevos filtros; `GetRoleSystemOptionsPagedQueryHandler` los pasa a `RoleSystemOptionQueryFilters`.
- [ ] `GetSuperAdminAllRoleSystemOptionsPagedQuery` acepta los 5 nuevos filtros; `GetSuperAdminAllRoleSystemOptionsPagedQueryHandler` los pasa a `RoleSystemOptionQueryFilters`. `RequireCompanyFilter` se sigue computando como `request.CompanyId.HasValue`.
- [ ] `GetRoleSystemOptionByIdQueryHandler` ejecuta SQL que incluye los 5 nuevos campos en el SELECT (vía `BuildByIdSql` sobre `SelectListProjection` actualizado).
- [ ] `RoleSystemOptionsController.GetPaged` expone los 5 nuevos `[FromQuery]` (`canDownload`, `canExport`, `canExecute`, `isVisibleMenu`, `orderMenu`).
- [ ] `RoleSystemOptionsController.GetSuperAdminPaged` expone los mismos 5 `[FromQuery]`.
- [ ] `RoleSystemOptionsController.GetById/Create/Update/Delete` sin cambios estructurales.
- [ ] Smoke: `GET /api/v1/RoleSystemOptions/{seedId}` retorna 200 con `canDownload`/`canExport`/`canExecute`/`isVisibleMenu`/`orderMenu` poblados desde la DB.
- [ ] Smoke: `GET /api/v1/RoleSystemOptions?canExport=true&orderMenu=0` retorna 200 con solo reglas que cumplen ambos filtros.
- [ ] Smoke: `GET /api/v1/RoleSystemOptions?canExport=true&orderMenu=0&canRead=true` retorna 200 respetando los 3 filtros combinados.
- [ ] Smoke: `GET /api/v1/RoleSystemOptions/superadmin/all?canDownload=true` (con JWT SuperAdmin) retorna 200 cross-tenant filtrado por `CanDownload = true`.
- [ ] Smoke: `POST /api/v1/RoleSystemOptions` con body que incluye `orderMenu: 5, canExport: false` retorna 200/201 con DTO que refleja esos valores.
- [ ] Smoke: `PUT /api/v1/RoleSystemOptions/{id}` con body que incluye `isVisibleMenu: false, canExecute: true` retorna 200 con DTO actualizado.
- [ ] Smoke: `POST /api/v1/RoleSystemOptions` con body viejo (sin los 5 campos nuevos) sigue funcionando vía defaults.
- [ ] Validator smoke: `POST` con `orderMenu: -1` retorna 400 con `ValidationFailure`.
- [ ] Validator smoke: `POST` con `orderMenu: 10001` retorna 400 con `ValidationFailure`.
- [ ] Existen tests en `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleSystemOptions/` cubriendo: `CreateRoleSystemOptionCommandValidator`, `UpdateRoleSystemOptionCommandValidator`, mapeo en Create/Update handlers, SQL projection en GetById, filtros WHERE en Paged, `RequireCompanyFilter` en SuperAdmin Paged.
- [ ] `dotnet test --filter "FullyQualifiedName~RoleSystemOptions"` → 0 fallidos.
- [ ] Cobertura total `JOIN.Application` ≥ 90% (gate CI en `.github/workflows/ci.yml`).
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` documenta el nuevo body de POST/PUT y la respuesta completa de GET-by-id + GET-paged con filtros nuevos.
- [ ] `RoleSystemOption` entity no fue modificado (los 5 campos ya existían desde SPEC 12/16).
- [ ] Migración EF Core no requerida (sin cambios de schema).
- [ ] `DatabaseSeeder` no fue modificado (defaults del entity cubren filas existentes).
- [ ] `DynamicAuthorizationFilter` / `IPermissionService` / `PermissionService` no fueron tocados — enforcement queda diferido al spec que cubre SPEC 21 también.

---

## Decisions taken and discarded

- **Defaults `true/true/true/true/0` en `CreateRoleSystemOptionCommand`** (elegido) vs. `false/false/false/false/null` (alineado con la siembra del entity que arranca todo en `false`). Mismo rationale que SPEC 21: cliente que envía el body viejo sin los nuevos campos sigue funcionando vía defaults `true`; el entity tiene `HasDefaultValue(false)` por seed pero el command es contrato de API, no espejo del seed. Cliente que quiere permisos restringidos los envía explícitamente.
- **SQL centralizado en `RoleSystemOptionQuerySql`** (elegido, ya existente) vs. duplicar SQL en cada handler. `SelectListProjection` + `BuildWhereClause` se comparten entre `GetById` / `GetPaged` / `GetSuperAdminPaged`. Un solo edit propaga los 5 campos nuevos a los 3 endpoints. Decisión de arquitectura ya consolidada en SPEC 19; este spec la respeta.
- **Filtros paged expuestos como `[FromQuery]`** (elegido) vs. solo al SELECT. Decisión confirmada por el usuario en fase de clarificación. Coherente con los 4 filtros `CanRead/Create/Update/Delete` ya existentes; añadir los 5 nuevos completa la simetría del contrato.
- **`OrderMenu` validado con rango `[0, 10000]`** (elegido) vs. sin validación. Mismo tope que SPEC 21 — menú CRM raramente supera 10K items; evita `int.MaxValue` malicioso y negativos accidentales.
- **`OrderMenu` se mantiene `int?` nullable** (elegido) vs. `int` no-nullable. El entity lo define como `int?` con default `0`; cambiar a `int` rompería filas existentes si la columna tiene `NULL`s históricos. Mantener nullable evita migración y respeta el contrato del entity.
- **Mapperly auto-inferencia para los 5 campos nuevos** (elegido por defecto) vs. asignaciones manuales explícitas. Mapperly mapea por nombre + tipo; `bool CanDownload` ↔ `bool CanDownload`, `int? OrderMenu` ↔ `int? OrderMenu` — no requiere intervención. Si la generación falla (cambio de versión, edge case tipo), fallback a asignación manual en cada método del mapper. Decisión tomada para minimizar diff; fallback documentado.
- **Orden de campos en DTOs: entre `CanDelete` y `Created`** (elegido) vs. al final del record. Mantiene los 7 flags `Can*` contiguos y a `IsVisibleMenu`/`OrderMenu` antes del `Created` de auditoría. Consistencia visual con SPEC 21 SystemOption DTO (mismo orden de declaración).
- **Sin reglas de validator para `bool`** (elegido) vs. validar rangos. `bool` no tiene valor inválido; defaults del command cubren omisión.
- **No tocar `DynamicAuthorizationFilter`** (elegido) vs. extender enforcement. Enforcement de `CanDownload`/`CanExport`/`CanExecute`/`IsVisibleMenu` queda para spec dedicado (mismo gap que SPEC 16 y SPEC 21 documentaron). Este spec solo completa el contrato DTO + endpoint; no cambia semántica de autorización.
- **`ListItemDto` extendido con el set completo** (elegido) vs. dejarlo ligero. Mismo criterio que SPEC 21: UI consume un único shape consistente entre lista y detalle. Inflamiento de payload aceptable para grilla administrativa de configuración.
- **`DELETE` sin cambios** (elegido) vs. agregar parámetros de body. El endpoint actual recibe `companyId` por query y `id` por ruta; sin body. Mantener intacto.
- **Tests del SQL via mock `ISqlConnectionFactory` + inspection del `CommandDefinition`** (elegido) vs. tests de integración con DB real. Convención del proyecto (SPEC 20, 21). Cobertura del SQL string sin necesidad de levantar SQL Server.
- **Tests nuevos en carpeta vacía** (`tests/UnitTests/JOIN.Application.UnitTest/Security/RoleSystemOptions/`) (elegido) vs. reusar carpeta existente de otro módulo. La carpeta está vacía — no hay tests previos que extender; todos los tests de validators/handlers/SQL son nuevos. Decisión confirmada en fase de clarificación.
- **No tocar seeder** (elegido) vs. actualizar `DatabaseSeeder.GetRoleSystemOptionSeeds()`. Ya setea `CanExport: true`/`CanExecute: true` para roles privilegiados desde SPEC 12. Los nuevos defaults `CanDownload=true`/`IsVisibleMenu=true`/`OrderMenu=0` se aplican por el `HasDefaultValue` del EF configuration al insertar nuevas filas. Filas existentes no se migran — quedan con sus valores pre-existentes (que ya eran `true`/`null`/`null` desde el seed original).
- **Orden de `OrderMenu` en filters record al final** (elegido) vs. agrupar todos los `Can*` contiguos. Mantiene el orden visual de los 4 `Can*` originales juntos, luego los 4 nuevos `Can*`/`IsVisibleMenu`, luego `OrderMenu` (el único no-bool). Consistencia con el orden en DTO y command.
- **`OrderMenu` filter usa comparación exacta `=` en SQL** (elegido) vs. `LIKE` o rango. Coherente con el resto de filtros bool/int (exacto). Si el cliente quiere rango (`>= 0 AND <= 100`), spec aparte.

---

## Identified risks

- **Cross-tenant leak** si `rso.CompanyId = @CompanyId` se rompe al extender filtros. Mitigado por: handler short-circuit con `Guid.Empty` + `RequireCompanyFilter: true` en `GetRoleSystemOptionsPagedQueryHandler` (línea 40) + SQL explícito que ya filtra por tenant. Los 5 nuevos filtros usan `rso.CanXxx` / `rso.OrderMenu`, no tocan la columna de tenant.
- **Dapper bypass a query filters globales de EF** (`GcRecord = 0`). Mitigado por SQL explícito `WHERE rso.GcRecord = 0` en `BuildWhereClause` (línea 58). Los nuevos campos no afectan el filtro de soft-delete.
- **Mapperly no infiere los 5 campos nuevos** (cambio de versión, edge case con `int?`). Mitigación: fallback documentado a asignación manual en cada método del mapper. Si la generación falla, `dotnet build` lo señala y el fix es local al mapper.
- **Coverage gate (≥90%) puede romperse** si las nuevas ramas del mapper o de `BuildWhereClause` no se cubren. Mitigación: tests de `CreateHandler` / `UpdateHandler` con mocks que verifican el mapper recibe los 5 campos; tests de `BuildWhereClause` que ejercitan cada `if (filters.CanXxx.HasValue)` con valor true, false y null.
- **Grilla paginada infla de ~250 bytes a ~350 bytes por item**. Con `pageSize=20` la respuesta pasa de ~5KB a ~7KB. Aceptable para endpoint administrativo; si se vuelve problema, el cliente puede usar `pageSize=10`.
- **Inconsistencia momentánea durante el deploy**: clientes que envían el body viejo siguen funcionando vía defaults `true/true/true/true/0`; clientes que envían body nuevo con un campo faltante reciben `true`/`0` por default. Documentado.
- **`RoleSystemOptionReadModel` gap**: si el read model (definido en `RoleSystemOptionsRepository.cs`) no expone los 5 campos nuevos, Dapper mapea las columnas del SELECT a default (`false`/`false`/`false`/`false`/`null`) al materializar el read model — datos inconsistentes si el repo se usa fuera de los 3 handlers de query. Out of scope del presente spec (decisión documentada en "Out of scope"). Spec de seguimiento si el read model se usa activamente.
- **`SuperAdmin` endpoint expone los 5 filtros cross-tenant** sin filtro de tenant en el WHERE (correcto por diseño — es para SuperAdmin). Si el caller no es SuperAdmin, `[Authorize(Roles = "SuperAdmin")]` (línea 196 del controller) bloquea con 403 antes de llegar al handler. Sin riesgo nuevo introducido por este spec.
- **Defaults `true` en `Create` chocan con seed `false` del entity**: el seeder pre-existente crea reglas con `CanDownload=false`/`IsVisibleMenu=false` para roles no privilegiados. Cliente que crea regla nueva vía API recibe `true` por default → más permisivo que seed. Mitigación: cliente que quiere replicar semántica del seed envía los flags explícitamente en `false`. Documentado en decision section.
