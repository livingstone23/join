# SPEC 21 — SystemOption DTO completeness + ModuleName/ParentName projection

> **Status:** Aprobado
> **Depends on:** SPEC 16 (CanExport/CanExecute), SPEC 18 (Roles CRUD), `SystemOption` entity actual
> **Date:** 2026-08-11
> **Objective:** Completar los DTOs (`SystemOptionDto` + `SystemOptionListItemDto`) y los commands (`Create/Update`) de `SystemOptionsController` con todos los campos del entity (`ModuleName`, `ControllerName`, `OrderMenu`, `Icon`, `CanRead/Create/Update/Delete/Download/Export/Execute`, `IsVisibleMenu`, `Created`), reemplazar las proyecciones hardcodeadas `ModuleName=''` / `ParentName=''` por JOINs reales a `SystemModules` y a `SystemOptions` (parent), y mantener cobertura ≥ 90%.

---

## Scope

**In:**
si ini
- `src/2.Application.DTO/Security/SystemOptionDto.cs` (extender): añadir campos faltantes para reflejar el entity completo — `CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu`. Reemplazar `ModuleName`/`ParentName` hardcoded por proyección real vía JOIN en SQL. Forma final:
  ```
  Guid Id, Guid ModuleId, string ModuleName,
  string Name, string Route, string? Icon,
  Guid? ParentId, string? ParentName,
  string? ControllerName,
  bool CanRead, CanCreate, CanUpdate, CanDelete, CanDownload, CanExport, CanExecute,
  bool IsVisibleMenu,
  int? OrderMenu,
  DateTime Created
  ```
- `src/2.Application.DTO/Security/SystemOptionListItemDto.cs` (extender): añadir `Icon`, `ControllerName`, `OrderMenu`, `IsVisibleMenu`, `CanRead/Create/Update/Delete/Download/Export/Execute` para que la grilla paginada muestre el set completo. Reemplazar `ModuleName = ''` por JOIN real a `SystemModules`. Forma final:
  ```
  Guid Id, Guid ModuleId, string ModuleName,
  string Name, string Route, string? Icon, string? ControllerName,
  Guid? ParentId,
  bool CanRead, CanCreate, CanUpdate, CanDelete, CanDownload, CanExport, CanExecute,
  bool IsVisibleMenu,
  int? OrderMenu,
  DateTime Created
  ```
- `src/2.Application/UseCases/Security/SystemOptions/Commands/CreateSystemOption/CreateSystemOptionCommand.cs` (extender): añadir `CanDownload = true`, `CanExport = true`, `CanExecute = true`, `IsVisibleMenu = true`, `OrderMenu = 0` (todos con default, mismo patrón que los `Can*` actuales).
- `src/2.Application/UseCases/Security/SystemOptions/Commands/UpdateSystemOption/UpdateSystemOptionCommand.cs` (extender): mismo set con `[property: JsonIgnore] Guid Id` intacto.
- `src/2.Application/UseCases/Security/SystemOptions/Commands/CreateSystemOption/CreateSystemOptionCommandValidator.cs` (extender): reglas nuevas — `OrderMenu` `GreaterThanOrEqualTo(0).LessThanOrEqualTo(10000)`; `Icon` `MaximumLength(100)`; `ControllerName` `MaximumLength(250)`; `Can*`/`IsVisibleMenu` sin reglas (bool, default `true`).
- `src/2.Application/UseCases/Security/SystemOptions/Commands/UpdateSystemOption/UpdateSystemOptionCommandValidator.cs` (extender): mismas reglas que Create.
- `src/2.Application/Mappings/Security/SystemOption/SystemOptionMapper.cs` (extender): `ToEntity` debe poblar `CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu` desde el command. `ToDto` debe proyectar esos campos desde el entity. `ApplyUpdate` debe copiar esos campos al entity cargado.
- `src/2.Application/UseCases/Security/SystemOptions/Queries/GetSystemOptionById/GetSystemOptionByIdQueryHandler.cs` (reemplazar SQL): JOIN real a `Security.SystemModules m` y `LEFT JOIN Security.SystemOptions p`. SELECT incluye los nuevos flags (`CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu`). Ya retorna DTO directo por Dapper (sin mapper).
- `src/2.Application/UseCases/Security/SystemOptions/Queries/GetSystemOptionsPaged/GetSystemOptionsPagedQueryHandler.cs` (reemplazar SQL): JOIN real a `Security.SystemModules m` para proyectar `ModuleName`. SELECT incluye los nuevos campos. La lista actualmente no proyecta `ParentName` — queda igual (no estaba en el pedido).
- Tests unitarios nuevos / actualizados en `tests/UnitTests/JOIN.Application.UnitTest/Security/SystemOptions/`:
  - `CreateSystemOptionCommandValidatorTests` (nuevo): cubre `OrderMenu` < 0 / > 10000, `Icon` > 100, `ControllerName` > 250, happy path.
  - `UpdateSystemOptionCommandValidatorTests` (nuevo o extendido): mismas reglas.
  - `CreateSystemOptionCommandHandlerTests` (extender): el mapper recibe los nuevos campos y los persiste; happy path sigue retornando DTO con campos poblados.
  - `UpdateSystemOptionCommandHandlerTests` (extender): `ApplyUpdate` copia los nuevos campos.
  - `GetSystemOptionByIdQueryHandlerTests` (extender o nuevo): el SQL proyecta `ModuleName` real (no vacío) cuando hay JOIN; `ParentName` poblado cuando hay parent.
  - `GetSystemOptionsPagedQueryHandlerTests` (extender o nuevo): el SQL proyecta `ModuleName` real; filtros/paginación intactos.
  - Cobertura ≥ 90% en clases nuevas y modificadas (gate CI).
- `CURL_REQUESTS.md`: extender el bloque de `SystemOptions` con un POST/PUT que incluya los nuevos campos + un GET que muestre la respuesta completa.

**Out of scope:**

- Cambios al entity `SystemOption` (los campos ya existen desde SPEC 16 y previos).
- Migración EF Core (sin cambios de schema).
- Cambios al seeder `DatabaseSeeder.cs` (los defaults del entity cubren las filas existentes; no se reescribe seed).
- Cambios al `SystemOptionConfiguration` (defaults ya están mapeados correctamente desde SPEC 16).
- Cambios al endpoint `DELETE` (no recibe body — no aplica).
- Cambios al filtro de autorización `DynamicAuthorizationFilter` (los flags ya están en `PermissionFlags` desde SPEC 17; enforcement de `CanDownload`/`CanExport`/`CanExecute` sigue pendiente de spec aparte, fuera de este alcance).
- Reordenar campos en la respuesta (orden de declaración del record es lo que se serializa).
- `ParentName` en `ListItemDto` (no estaba en el pedido del usuario; queda solo en `SystemOptionDto`).

---

## Data model

**Sin entidades nuevas.** Entity `SystemOption` ya expone todos los campos. El cambio vive en DTOs, commands, mapper y SQL.

### `src/2.Application.DTO/Security/SystemOption/SystemOptionDto.cs` (extendido)

```csharp
namespace JOIN.Application.DTO.Security;

public sealed record SystemOptionDto
{
    public Guid Id { get; init; }
    public Guid ModuleId { get; init; }
    public string ModuleName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public Guid? ParentId { get; init; }
    public string? ParentName { get; init; }
    public string? ControllerName { get; init; }
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

### `src/2.Application.DTO/Security/SystemOption/SystemOptionListItemDto.cs` (extendido)

```csharp
namespace JOIN.Application.DTO.Security;

public sealed record SystemOptionListItemDto
{
    public Guid Id { get; init; }
    public Guid ModuleId { get; init; }
    public string ModuleName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Route { get; init; } = string.Empty;
    public string? Icon { get; init; }
    public string? ControllerName { get; init; }
    public Guid? ParentId { get; init; }
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

### `src/2.Application/UseCases/Security/SystemOptions/Commands/CreateSystemOption/CreateSystemOptionCommand.cs` (extendido)

```csharp
public sealed record CreateSystemOptionCommand(
    Guid ModuleId,
    string Name,
    string Route,
    string? Icon,
    Guid? ParentId,
    string? ControllerName,
    bool CanRead = true,
    bool CanCreate = true,
    bool CanUpdate = true,
    bool CanDelete = true,
    bool CanDownload = true,
    bool CanExport = true,
    bool CanExecute = true,
    bool IsVisibleMenu = true,
    int? OrderMenu = 0)
    : ITransactionalCommand<Response<SystemOptionDto>>;
```

### `src/2.Application/UseCases/Security/SystemOptions/Commands/UpdateSystemOption/UpdateSystemOptionCommand.cs` (extendido)

```csharp
public sealed record UpdateSystemOptionCommand(
    [property: JsonIgnore] Guid Id,
    string Name,
    string Route,
    string? Icon,
    Guid? ParentId,
    string? ControllerName,
    bool CanRead = true,
    bool CanCreate = true,
    bool CanUpdate = true,
    bool CanDelete = true,
    bool CanDownload = true,
    bool CanExport = true,
    bool CanExecute = true,
    bool IsVisibleMenu = true,
    int? OrderMenu = 0)
    : ITransactionalCommand<Response<SystemOptionDto>>;
```

### `src/2.Application/Mappings/Security/SystemOption/SystemOptionMapper.cs` (extendido)

Agregar mapeo de los nuevos campos en `ToEntity`, `ToDto` y `ApplyUpdate`:

```csharp
[Mapper]
public partial interface ISystemOptionMapper
{
    SystemOption ToEntity(CreateSystemOptionCommand command);
    SystemOptionDto ToDto(SystemOption entity);
    void ApplyUpdate(UpdateSystemOptionCommand command, SystemOption entity);
}
```

`ToEntity` setea `CanDownload = command.CanDownload`, `CanExport = command.CanExport`, `CanExecute = command.CanExecute`, `IsVisibleMenu = command.IsVisibleMenu`, `OrderMenu = command.OrderMenu`.

`ToDto` proyecta los mismos cinco campos.

`ApplyUpdate` copia los cinco campos al entity cargado antes de `UpdateAsync`.

### SQL — `GetSystemOptionByIdQueryHandler` (reemplazo)

```sql
SELECT
    o.Id,
    o.ModuleId,
    m.Name AS ModuleName,
    o.Name,
    o.Route,
    o.Icon,
    o.ParentId,
    p.Name AS ParentName,
    o.ControllerName,
    o.CanRead,
    o.CanCreate,
    o.CanUpdate,
    o.CanDelete,
    o.CanDownload,
    o.CanExport,
    o.CanExecute,
    o.IsVisibleMenu,
    o.OrderMenu,
    o.Created
FROM Security.SystemOptions o
INNER JOIN Security.SystemModules m ON m.Id = o.ModuleId
LEFT JOIN Security.SystemOptions p ON p.Id = o.ParentId AND p.GcRecord = 0
WHERE o.Id = @Id AND o.GcRecord = 0;
```

### SQL — `GetSystemOptionsPagedQueryHandler` (reemplazo del SELECT)

```sql
SELECT
    o.Id,
    o.ModuleId,
    m.Name AS ModuleName,
    o.Name,
    o.Route,
    o.Icon,
    o.ControllerName,
    o.ParentId,
    o.CanRead,
    o.CanCreate,
    o.CanUpdate,
    o.CanDelete,
    o.CanDownload,
    o.CanExport,
    o.CanExecute,
    o.IsVisibleMenu,
    o.OrderMenu,
    o.Created
FROM Security.SystemOptions o
INNER JOIN Security.SystemModules m ON m.Id = o.ModuleId
{whereClause}
ORDER BY o.Name ASC
{GetPaginationClause};
```

`whereClause` se mantiene idéntica (`WHERE o.GcRecord = 0 [AND o.Name LIKE @Name]`). El filtro `Name` se aplica sobre `o.Name`, no sobre el `ModuleName`.

---

## Implementation plan

### F1 — DTOs

1. Editar `src/2.Application.DTO/Security/SystemOption/SystemOptionDto.cs`: añadir `CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu` entre `CanDelete` y `Created` (orden de declaración = orden de serialización JSON).
2. Editar `src/2.Application.DTO/Security/SystemOption/SystemOptionListItemDto.cs`: añadir `Icon`, `ControllerName`, `OrderMenu`, `IsVisibleMenu`, todos los `Can*` (`CanRead/Create/Update/Delete/Download/Export/Execute`).
3. `dotnet build` → 0 errores. DTOs extendidos no rompen callers (los campos viejos siguen presentes).

### F2 — Commands

1. Editar `src/2.Application/UseCases/Security/SystemOptions/Commands/CreateSystemOption/CreateSystemOptionCommand.cs`: añadir `CanDownload = true`, `CanExport = true`, `CanExecute = true`, `IsVisibleMenu = true`, `OrderMenu = 0` con default, después de `CanDelete = true`.
2. Editar `src/2.Application/UseCases/Security/SystemOptions/Commands/UpdateSystemOption/UpdateSystemOptionCommand.cs`: mismo set, mismo orden. `[property: JsonIgnore] Guid Id` se mantiene.
3. `dotnet build` → 0 errores. Commands viejos que no envían los nuevos campos siguen compilando (defaults cubren).

### F3 — Mapper

1. Editar `src/2.Application/Mappings/Security/SystemOption/SystemOptionMapper.cs`:
   - `ToEntity(CreateSystemOptionCommand)` → asignar los cinco nuevos campos desde el command al entity.
   - `ToDto(SystemOption)` → proyectar los cinco nuevos campos.
   - `ApplyUpdate(UpdateSystemOptionCommand, SystemOption)` → copiar los cinco nuevos campos al entity cargado antes de `UpdateAsync`.
2. `dotnet build` → 0 errores. Mapperly source-genera los nuevos mapeos.

### F4 — Validators

1. Editar `src/2.Application/UseCases/Security/SystemOptions/Commands/CreateSystemOption/CreateSystemOptionCommandValidator.cs`:
   - `RuleFor(c => c.OrderMenu).GreaterThanOrEqualTo(0).LessThanOrEqualTo(10000).When(c => c.OrderMenu.HasValue);` (nullable — solo aplica si viene en el body).
   - `RuleFor(c => c.Icon).MaximumLength(100).When(c => c.Icon != null);`.
   - `RuleFor(c => c.ControllerName).MaximumLength(250).When(c => c.ControllerName != null);`.
   - Sin reglas para los `bool` (defaults cubrían, sin valor inválido posible).
2. Editar `src/2.Application/UseCases/Security/SystemOptions/Commands/UpdateSystemOption/UpdateSystemOptionCommandValidator.cs`: reglas idénticas a Create.
3. `dotnet build` → 0 errores.

### F5 — Query SQL (GetById)

1. Editar `src/2.Application/UseCases/Security/SystemOptions/Queries/GetSystemOptionById/GetSystemOptionByIdQueryHandler.cs`:
   - Reemplazar el `const string sql` por la versión con JOIN a `SystemModules` y `LEFT JOIN SystemOptions` (parent).
   - SELECT incluye los cinco nuevos campos (`CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu`).
   - `NotFoundException` y mensaje quedan como están.
2. `dotnet build` → 0 errores.

### F6 — Query SQL (Paged)

1. Editar `src/2.Application/UseCases/Security/SystemOptions/Queries/GetSystemOptionsPaged/GetSystemOptionsPagedQueryHandler.cs`:
   - Reemplazar el SELECT (dentro del `multi`) por la versión con `INNER JOIN Security.SystemModules m ON m.Id = o.ModuleId` y los cinco nuevos campos. WHERE + COUNT + paginación intactas.
   - Branch `LIMIT/OFFSET` (Postgres) vs `OFFSET...FETCH NEXT` (SQL Server) intacto.
   - Alias `ModuleName` se proyecta desde `m.Name` (no string.Empty).
2. `dotnet build` → 0 errores.

### F7 — Tests unitarios

1. Crear `tests/UnitTests/JOIN.Application.UnitTest/Security/SystemOptions/CreateSystemOptionCommandValidatorTests.cs`:
   - `OrderMenu = -1` → `ValidationFailure`.
   - `OrderMenu = 10001` → `ValidationFailure`.
   - `Icon = "x".PadRight(101)` → `ValidationFailure`.
   - `ControllerName = "x".PadRight(251)` → `ValidationFailure`.
   - Happy path (todos null/0/true) → pasa.
2. Crear `tests/UnitTests/JOIN.Application.UnitTest/Security/SystemOptions/UpdateSystemOptionCommandValidatorTests.cs`: mismos casos.
3. Extender `CreateSystemOptionCommandHandlerTests` (si existe) o crear nuevo: mock `IUnitOfWork` + `ISystemOptionMapper`. Verificar que el mapper recibe los nuevos campos y los persiste; happy path retorna DTO con todos los flags/orden visibles.
4. Extender `UpdateSystemOptionCommandHandlerTests`: mock del mapper verifica que `ApplyUpdate` recibe los nuevos campos del command y los copia al entity.
5. Crear/Extender `GetSystemOptionByIdQueryHandlerTests`: mock `ISqlConnectionFactory`; assert que la query generada incluye `INNER JOIN Security.SystemModules` y `LEFT JOIN Security.SystemOptions p ON p.Id = o.ParentId` y los nuevos flags en el SELECT. Cobertura del SQL string vía inspection del `QuerySingleOrDefaultAsync` invocado.
6. Crear/Extender `GetSystemOptionsPagedQueryHandlerTests`: assert que el SELECT incluye el JOIN a `SystemModules` y los nuevos campos; paginación/filtros intactos.
7. `dotnet test --filter "FullyQualifiedName~SystemOptions"` → 0 fallidos. Cobertura ≥ 90% en clases nuevas y modificadas.

### F8 — Controller (sin cambios estructurales)

1. `SystemOptionsController` no requiere cambios: los actions ya hacen `mediator.Send(command)` con `[FromBody]` que deserializa los nuevos campos automáticamente (defaults del record cubren cuando el cliente no los envía).
2. Verificar que `[ProducesResponseType]` sigue apuntando a `Response<SystemOptionDto>` (forma completa del DTO actualizada).

### F9 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test` con `--collect:"XPlat Code Coverage"` → cobertura total `JOIN.Application` ≥ 90% (gate CI).
3. Smoke test manual con `dotnet run`:
   - `GET /api/v1/SystemOptions/{seedId}` → 200, `moduleName` poblado (no `""`), `parentName` poblado si tiene parent.
   - `GET /api/v1/SystemOptions?page=1&pageSize=20` → 200, `moduleName` poblado en cada item.
   - `POST /api/v1/SystemOptions` con body conteniendo todos los nuevos campos → 201, DTO retornado con los mismos campos.
   - `PUT /api/v1/SystemOptions/{id}` con body conteniendo los nuevos campos → 200, DTO retornado con los mismos campos.
4. `CURL_REQUESTS.md`: extender el bloque `SystemOptions` con un POST/PUT que envíe los nuevos campos + ejemplo de GET con respuesta completa.

---

## Acceptance criteria

- [ ] `SystemOptionDto` expone `CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu` además de los campos previos. Mantiene `Id`, `ModuleId`, `ModuleName`, `Name`, `Route`, `Icon`, `ParentId`, `ParentName`, `ControllerName`, `CanRead/Create/Update/Delete`, `Created`.
- [ ] `SystemOptionListItemDto` expone `Icon`, `ControllerName`, `OrderMenu`, `IsVisibleMenu`, `CanRead/Create/Update/Delete/Download/Export/Execute` además de los campos previos.
- [ ] `CreateSystemOptionCommand` acepta los nuevos campos con defaults (`CanDownload=true`, `CanExport=true`, `CanExecute=true`, `IsVisibleMenu=true`, `OrderMenu=0`) sin romper compatibilidad con clientes que envíen el body viejo.
- [ ] `UpdateSystemOptionCommand` acepta los nuevos campos con los mismos defaults; `[property: JsonIgnore] Guid Id` intacto.
- [ ] `CreateSystemOptionCommandValidator` rechaza: `OrderMenu < 0`, `OrderMenu > 10000`, `Icon` > 100 chars, `ControllerName` > 250 chars. Pasa con los defaults.
- [ ] `UpdateSystemOptionCommandValidator` rechaza los mismos casos que Create.
- [ ] `ISystemOptionMapper.ToEntity` setea `CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu` desde el `CreateSystemOptionCommand`.
- [ ] `ISystemOptionMapper.ToDto` proyecta los mismos cinco campos desde el entity.
- [ ] `ISystemOptionMapper.ApplyUpdate` copia los mismos cinco campos al entity cargado antes de `UpdateAsync`.
- [ ] `GetSystemOptionByIdQueryHandler` ejecuta SQL con `INNER JOIN Security.SystemModules m ON m.Id = o.ModuleId` y `LEFT JOIN Security.SystemOptions p ON p.Id = o.ParentId AND p.GcRecord = 0`, SELECT incluye `CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`, `OrderMenu`, alias `ModuleName` viene de `m.Name` (no `''`), alias `ParentName` viene de `p.Name` (no `''`).
- [ ] `GetSystemOptionsPagedQueryHandler` ejecuta SQL con `INNER JOIN Security.SystemModules m ON m.Id = o.ModuleId`, SELECT incluye los mismos cinco campos nuevos, alias `ModuleName` viene de `m.Name`.
- [ ] `GetSystemOptionsPagedQueryHandler` mantiene la rama `LIMIT @PageSize OFFSET @Offset` (Postgres) y `OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY` (SQL Server).
- [ ] `SystemOptionsController` no cambia de estructura: GET/POST/PUT/DELETE siguen intactos, los nuevos campos entran vía `[FromBody]` y deserialización JSON.
- [ ] Smoke: `GET /api/v1/SystemOptions/{seedId}` retorna 200 con `moduleName` poblado (no vacío), `parentName` poblado cuando hay parent, todos los `Can*` y `IsVisibleMenu`/`OrderMenu` presentes.
- [ ] Smoke: `GET /api/v1/SystemOptions?page=1&pageSize=20` retorna 200 con `moduleName` poblado en cada item y los nuevos campos presentes.
- [ ] Smoke: `POST /api/v1/SystemOptions` con body conteniendo los nuevos campos retorna 201 con DTO que refleja los valores enviados.
- [ ] Smoke: `PUT /api/v1/SystemOptions/{id}` con body conteniendo los nuevos campos retorna 200 con DTO actualizado.
- [ ] Smoke: cliente que envía el body viejo (sin los nuevos campos) sigue funcionando — defaults cubren.
- [ ] Existen tests en `tests/UnitTests/JOIN.Application.UnitTest/Security/SystemOptions/` cubriendo: `CreateSystemOptionCommandValidator`, `UpdateSystemOptionCommandValidator`, mapeo en Create/Update handler, SQL projection en GetById y Paged handlers.
- [ ] `dotnet test --filter "FullyQualifiedName~SystemOptions"` → 0 fallidos.
- [ ] Cobertura total `JOIN.Application` ≥ 90% (gate CI en `.github/workflows/ci.yml`).
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` documenta el nuevo body de POST/PUT y la respuesta completa de GET.
- [ ] `SystemOption` entity no fue modificado (los campos ya existían desde SPEC 16).
- [ ] Migración EF Core no requerida (sin cambios de schema).
- [ ] `DatabaseSeeder` no fue modificado (defaults del entity cubren filas existentes).

---

## Decisions taken and discarded

- **Ampliar `SystemOptionListItemDto` con el set completo** (elegido) vs. mantenerlo ligero y dejar el detalle para `GetById`. El cliente consume un único shape consistente entre lista y detalle; el agrandamiento de payload (~+12 columnas bool/int) es aceptable para una grilla administrativa de configuración.
- **JOIN real a `Security.SystemModules` para `ModuleName`** (elegido) vs. dejar el `''` hardcoded. El cliente espera un nombre legible; `''` es bug visible. El JOIN agrega una lectura por query pero sobre PK de `SystemModules` (filtrado por `Id`), impacto despreciable.
- **`LEFT JOIN Security.SystemOptions` para `ParentName`** (elegido) vs. `INNER JOIN`. El campo es nullable por diseño (`ParentId` opcional); un padre soft-deleted podría existir y traer `ParentName` poblado vía `LEFT JOIN` + `WHERE p.GcRecord = 0`. Decisión: `LEFT JOIN` con `AND p.GcRecord = 0` para no romper la fila si el padre está borrado.
- **Defaults del entity replicados en el command** (elegido) vs. forzar al cliente a enviar todos los flags. Mismo patrón que `CreateSystemOptionCommand` actual (`CanRead = true` por default); minimiza fricción con clientes existentes.
- **`OrderMenu` validado con rango `[0, 10000]`** (elegido) vs. sin validación. Tope arbitrario pero conservador — un menú CRM raramente supera 10K items; evita `int.MaxValue` malicioso y negativos accidentales.
- **`Icon` validado con `MaximumLength(100)`** (elegido, mirror del config) vs. sin validación. El `SystemOptionConfiguration` ya fija `HasMaxLength(100)`; el validator debe rechazar antes de llegar a EF.
- **`ControllerName` validado con `MaximumLength(250)`** (elegido, mirror del config) vs. sin validación. Mismo razonamiento que `Icon`; el config define `HasMaxLength(250)`.
- **Sin reglas para `Can*` / `IsVisibleMenu`** (elegido) vs. validar rangos. `bool` no tiene valor inválido; defaults `true` cubren omisión.
- **`OrderMenu` se mantiene `int?` nullable** (elegido) vs. `int` no-nullable. El entity lo define como `int?` con default `0`; cambiar a `int` rompería filas existentes si la columna tiene `NULL`s históricos. Mantener nullable evita migración.
- **Mapperly `ToDto` proyecta los cinco campos** (elegido) vs. SQL directo. El handler de GetById usa Dapper SQL → DTO directo sin mapper; el mapper solo aplica en Create/Update. Ambos paths exponen los mismos campos.
- **No tocar `SystemOptionsController`** (elegido) vs. agregar `[ProducesResponseType]` específicos por subconjunto de campos. Los nuevos campos entran automáticamente por `[FromBody]`; el controller queda intacto.
- **No tocar el seeder** (elegido) vs. actualizar `DatabaseSeeder` para setear explícitamente los nuevos flags. Los defaults del entity (`true`/`true`/`true`/`true`/`0`) cubren las filas existentes; el seeder corre idempotente y respeta los defaults.
- **No tocar `DynamicAuthorizationFilter`** (elegido) vs. extender enforcement de `CanDownload`/`CanExport`/`CanExecute`. El enforcement ya está pendiente de un spec aparte (riesgo documentado en SPEC 16); este spec solo completa el contrato DTO.
- **`ParentName` solo en `SystemOptionDto`, no en `ListItemDto`** (elegido) vs. ambos. No estaba en el pedido del usuario; mantener la lista ligera. El cliente que necesita el nombre del padre llama `GetById`.
- **Tests del SQL string via inspection del `QuerySingleOrDefaultAsync` / `QueryMultipleAsync` invocados** (elegido) vs. tests de integración con DB real. La convención del proyecto es mockear `ISqlConnectionFactory` y verificar argumentos; tests E2E con DB se manejan aparte.

## Identified risks

- **JOIN extra a `Security.SystemModules` por cada query** (bajo impacto, mitigado por PK lookup). Una llamada adicional a `m` por query, pero filtrada por PK — O(1).
- **`LEFT JOIN Security.SystemOptions p` para `ParentName`** puede arrastrar filas soft-deleted si no se filtra `p.GcRecord = 0`. Mitigación: agregar `AND p.GcRecord = 0` al LEFT JOIN (decisión tomada en F5).
- **Inconsistencia momentánea durante el deploy**: el `SystemOptionDto` ahora requiere que el cliente envíe (o acepte defaults) los nuevos flags. Un cliente que envíe explícitamente `CanExport = false` y luego升级 la API seguirá funcionando. Cliente que envíe el body viejo sigue funcionando por defaults.
- **`OrderMenu` nullable + default `0`**: si el cliente envía `null` explícito, queda `null` en el entity (no se aplica el default del command). Comportamiento aceptable — el entity tiene `int? OrderMenu = 0` y el config mapea `HasDefaultValue(0)`, así que la columna en DB termina en `0`. Riesgo de confusión para el cliente que ve `null` en el request body pero `0` en la respuesta (response muestra `OrderMenu = 0`). Documentado.
- **Grilla paginada infla de ~250 bytes por item a ~600 bytes**. Con `pageSize=20` la respuesta pasa de ~5KB a ~12KB. Aceptable para endpoint administrativo; si se vuelve problema, el cliente puede usar `pageSize=10`.
- **Filtro `Name` en paginación aplica sobre `o.Name`, no sobre `m.Name`**. La columna `ModuleName` ahora proyectada no es filtrable directamente. Si el cliente quiere filtrar por módulo, necesita extender el query (spec aparte). Decisión consciente: este spec no agrega filtro de módulo.
- **Coverage gate (≥90%) puede romperse si los tests no cubren las nuevas ramas del mapper**. Mitigación: tests de `CreateHandlerTests` / `UpdateHandlerTests` con mocks que verifican el mapper recibe los nuevos campos.