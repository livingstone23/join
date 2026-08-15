# SPEC 25 — `RoleSystemOptions` bulk upsert + matrix

> **Status:** Borrador
> **Depends on:** SPEC 18 (Roles CRUD), SPEC 22 (RoleSystemOption DTO), SPEC 23 (tenant from token), SPEC 24 (Roles hardening)
> **Date:** 2026-08-15
> **Objective:** Cerrar los dos gaps de UX que dejaron pendientes SPEC 18/22 sin tocar los endpoints existentes: (a) `PUT /bulk` para que el front guarde toda la matriz de permisos de un rol en una sola llamada atómica con diff `{ created, updated, removed }` e invalidación de caché de sidebar/permisos de los usuarios del rol; (b) `GET /matrix?roleId=` para que el front renderice la grilla de permisos agrupada por módulo sin tener que cruzar manualmente `SystemOptions` paginado con `RoleSystemOptions` paginado. Ambos endpoints siguen la convención CQRS, son tenant-scoped desde el JWT (SPEC 23), reusan el `PermissionFlags` shape de 7 flags de SPEC 22, y no rompen el contrato de los seis endpoints ya expuestos por `RoleSystemOptionsController`.

---

## Scope

**In:**

- `src/2.Application.DTO/Security/BulkUpsertRoleSystemOptionsDtos.cs` (nuevo archivo):
  - `UpsertRoleSystemOptionItemDto` — payload de un item (`systemOptionId` + 7 flags `Can*`).
  - `BulkUpsertRoleSystemOptionsRequest` — `{ roleId, items[] }`.
  - `BulkUpsertRoleSystemOptionsResult` — `{ created, updated, removed }` con los `Guid` IDs resultantes.
- `src/2.Application.DTO/Security/RoleSystemOptionMatrixDtos.cs` (nuevo archivo):
  - `RoleSystemOptionMatrixDto` — `{ roleId, roleName, modules[] }`.
  - `RoleSystemOptionMatrixModuleDto` — `{ moduleId, moduleName, options[] }`.
  - `RoleSystemOptionMatrixOptionDto` — `{ systemOptionId, name, route, supports, granted }`.
  - `RoleSystemOptionSupportFlags` y `RoleSystemOptionGrantedFlags` — los 7 `Can*` en cada uno.
- `src/2.Application/Interface/Persistence/Security/IRoleSystemOptionsRepository.cs`:
  - Nuevo `Task<IReadOnlyList<RoleSystemOption>> GetActiveByRoleAndCompanyAsync(Guid roleId, Guid companyId, CancellationToken ct)` (dedicado al reemplazo de set del bulk — devuelve entity, no DTO, para tener todos los flags).
  - Nuevo `Task<RoleSystemOptionMatrixDto?> GetMatrixByRoleAsync(Guid roleId, Guid companyId, CancellationToken ct)` (Dapper puro, una sola query con JOINs).
  - Nuevo `Task<(IReadOnlyList<Guid> Created, IReadOnlyList<Guid> Updated, IReadOnlyList<Guid> Removed)> BulkUpsertAsync(Guid roleId, Guid companyId, IReadOnlyList<RoleSystemOption> newItems, CancellationToken ct)` — implementación Dapper con MERGE/INSERT/UPDATE/soft-delete en una transacción SQL.
- `src/3.Persistence/Repositories/Security/RoleSystemOptionsRepository.cs`:
  - Implementar los tres métodos nuevos.
- `src/2.Application/Interface/IPermissionService.cs`:
  - Nuevo `Task InvalidateUserCacheAsync(Guid companyId, Guid userId, CancellationToken ct)` — sabe que la clave real es `permissions:v2:{companyId}:{userId}` (la rama `permission`/`permissions` del `InvalidateSidebarCacheCommand` hoy apunta a la clave vieja, bug pre-existente — spec 25 fija el contorno).
- `src/3.Infrastructure/Security/PermissionService.cs`: implementar `InvalidateUserCacheAsync` (invoca `Remove("permissions:v2:{companyId}:{userId}")` + `Remove("sidebar:{companyId}:{userId}")`).
- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/BulkUpsertRoleSystemOptions/`:
  - `BulkUpsertRoleSystemOptionsCommand.cs` — `IRequest<Response<BulkUpsertRoleSystemOptionsResult>>`.
  - `BulkUpsertRoleSystemOptionsCommandHandler.cs` — orquesta: valida tenant, valida role, llama `IRoleSystemOptionsRepository.BulkUpsertAsync(...)`, **dentro de la misma tx** carga `UserRoleCompany` activos del rol y dispara `IPermissionService.InvalidateUserCacheAsync` para cada uno. Devuelve `Response<BulkUpsertRoleSystemOptionsResult>`.
  - `BulkUpsertRoleSystemOptionsCommandValidator.cs` — FluentValidation (ver reglas abajo).
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetRoleSystemOptionMatrix/`:
  - `GetRoleSystemOptionMatrixQuery.cs` — `IRequest<Response<RoleSystemOptionMatrixDto>>`.
  - `GetRoleSystemOptionMatrixQueryHandler.cs` — valida tenant, llama `IRoleSystemOptionsRepository.GetMatrixByRoleAsync(...)`.
- `src/4.Services.WebApi/Controllers/Security/RoleSystemOptionsController.cs`:
  - `PUT /api/v1/RoleSystemOptions/bulk` — usa `BulkUpsertRoleSystemOptionsCommand`. Devuelve `200 OK` con el result. Mapea `ROLE_NOT_FOUND` → `404`, `BULK_EMPTY` → `400`.
  - `GET /api/v1/RoleSystemOptions/matrix?roleId=` — usa `GetRoleSystemOptionMatrixQuery`. Devuelve `200 OK`. Mapea `ROLE_NOT_FOUND` → `404`.
- Tests en `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleSystemOptions/`:
  - `BulkUpsertRoleSystemOptionsCommandHandlerTests`: 5 caminos (happy / item nuevo / item actualizado / item removido / cambio atómico cuando `SaveAsync` falla / tenant vacío).
  - `BulkUpsertRoleSystemOptionsCommandValidatorTests`: 5 reglas (roleId vacío, items null, items vacío, > N, duplicados).
  - `GetRoleSystemOptionMatrixQueryHandlerTests`: 3 caminos (rol con 2 módulos y permisos / rol sin permisos / rol no encontrado).

**Out of scope:**

- Cambios sobre `PUT /{id}`, `DELETE /{id}`, `POST`, `GET paged`, `GET superadmin/all` — el contrato existente no se toca. El bulk NO reemplaza al single-update; ambos conviven.
- Cambios sobre `PermissionService` distintos a la nueva `InvalidateUserCacheAsync`. La fix del bug pre-existente de `InvalidateSidebarCacheCommand` queda **fuera** (anotado en `Identified risks`); el spec 25 introduce un método nuevo que sí sabe la clave `v2`, y desde el bulk handler se llama a este, no al comando viejo.
- Caché de sidebar de llamadas no relacionadas al rol tocado — solo se invalidan los usuarios que tienen `UserRoleCompany` activo (rol = este, companyId = este, GcRecord = 0). No se barre la cache global.
- Denormalizar el contador de `PermissionsCount` en `RoleSystemOption` o en `Role` — el cálculo se hace en SQL (matrix: un JOIN con LEFT JOIN sobre el role).
- Internacionalización del `RoleName` / `ModuleName` — se devuelve en el idioma del seed (es-AR) tal cual está en la DB. Sin tabla de traducciones.
- Soporte de `bulk` cross-tenant (superadmin only) — el endpoint es tenant-scoped. SuperAdmin sigue usando `GET /superadmin/all` + `PUT /{id}` uno por uno. Si se necesita un bulk admin cross-tenant, va en spec aparte.
- Indicador de progreso o streaming — el bulk retorna el result completo al final. Sin SignalR ni SSE.

---

## Data model

### Nuevos DTOs (en `src/2.Application.DTO/Security/`)

```csharp
public sealed record UpsertRoleSystemOptionItemDto(
    Guid SystemOptionId,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload,
    bool CanExport,
    bool CanExecute);

public sealed record BulkUpsertRoleSystemOptionsRequest(
    Guid RoleId,
    IReadOnlyList<UpsertRoleSystemOptionItemDto> Items);

public sealed record BulkUpsertRoleSystemOptionsResult(
    IReadOnlyList<Guid> Created,
    IReadOnlyList<Guid> Updated,
    IReadOnlyList<Guid> Removed);
```

`Items` representa el **conjunto final** deseado de `RoleSystemOption` para ese `(roleId, companyId)`. Lo que esté en la DB y no en `Items` se soft-deletea (`Removed`). Lo que esté en `Items` y no en la DB se inserta (`Created`). Lo que esté en ambos se actualiza con los flags nuevos (`Updated`).

```csharp
public sealed record RoleSystemOptionSupportFlags(
    bool CanRead, bool CanCreate, bool CanUpdate, bool CanDelete,
    bool CanDownload, bool CanExport, bool CanExecute);

public sealed record RoleSystemOptionGrantedFlags(
    bool CanRead, bool CanCreate, bool CanUpdate, bool CanDelete,
    bool CanDownload, bool CanExport, bool CanExecute);

public sealed record RoleSystemOptionMatrixOptionDto(
    Guid SystemOptionId,
    string Name,
    string Route,
    RoleSystemOptionSupportFlags Supports,
    RoleSystemOptionGrantedFlags Granted);

public sealed record RoleSystemOptionMatrixModuleDto(
    Guid ModuleId,
    string ModuleName,
    IReadOnlyList<RoleSystemOptionMatrixOptionDto> Options);

public sealed record RoleSystemOptionMatrixDto(
    Guid RoleId,
    string RoleName,
    IReadOnlyList<RoleSystemOptionMatrixModuleDto> Modules);
```

`Supports` viene de `SystemOptions` (defaults de la opción: `CanRead`, `CanCreate`, `CanDownload`, etc.). `Granted` viene de `RoleSystemOptions` para ese rol; **todas las 7 flags devuelven `false` si no existe fila** (LEFT JOIN con `NULL` colapsado a `false` en la capa de mapping).

### Métodos nuevos en `IRoleSystemOptionsRepository`

```csharp
Task<IReadOnlyList<RoleSystemOption>> GetActiveByRoleAndCompanyAsync(
    Guid roleId, Guid companyId, CancellationToken ct = default);

Task<RoleSystemOptionMatrixDto?> GetMatrixByRoleAsync(
    Guid roleId, Guid companyId, CancellationToken ct = default);

Task<(IReadOnlyList<Guid> Created, IReadOnlyList<Guid> Updated, IReadOnlyList<Guid> Removed)>
    BulkUpsertAsync(
        Guid roleId,
        Guid companyId,
        IReadOnlyList<RoleSystemOption> newItems,
        CancellationToken ct = default);
```

`GetActiveByRoleAndCompanyAsync` filtra `GcRecord = 0` y devuelve `RoleSystemOption` entity (no DTO — para tener todos los flags). Reutiliza la firma que ya introdujo SPEC 24 en plan (`CreateRoleCommandHandler.CloneFromRoleId`) — esta spec la materializa.

`BulkUpsertAsync` recibe el **set final** (`newItems`) y aplica el diff contra la DB en una **transacción SQL única** (no EF + Dapper mezclados). Devuelve los IDs resultado.

### Método nuevo en `IPermissionService`

```csharp
Task InvalidateUserCacheAsync(Guid companyId, Guid userId, CancellationToken ct = default);
```

Implementación: `Remove("permissions:v2:{companyId}:{userId}")` + `Remove("sidebar:{companyId}:{userId}")`. Sabe la clave real `v2` (SPEC 17). NO depende del `InvalidateSidebarCacheCommand` actual (cuya rama `permission`/`permissions` apunta a la clave vieja, bug pre-existente).

---

## Implementation plan

### F1 — DTOs y repositories

1. Crear `src/2.Application.DTO/Security/BulkUpsertRoleSystemOptionsDtos.cs` con los tres records del data model.
2. Crear `src/2.Application.DTO/Security/RoleSystemOptionMatrixDtos.cs` con los cinco records del data model.
3. `IRoleSystemOptionsRepository`:
   - Añadir `GetActiveByRoleAndCompanyAsync` (firma arriba).
   - Añadir `GetMatrixByRoleAsync` (firma arriba).
   - Añadir `BulkUpsertAsync` (firma arriba).
4. `RoleSystemOptionsRepository`:
   - `GetActiveByRoleAndCompanyAsync`: Dapper — `SELECT * FROM Security.RoleSystemOptions WHERE RoleId = @RoleId AND CompanyId = @CompanyId AND GcRecord = 0;`.
   - `GetMatrixByRoleAsync`: Dapper — **una sola query** que une `SystemModules` LEFT JOIN `SystemOptions` LEFT JOIN `RoleSystemOptions` filtrado por `(roleId, companyId)`. Map en C# al DTO anidado. **Importante**: la query debe traer TODOS los `SystemOptions` activos (no solo los que tengan `RoleSystemOption`), porque la UI pinta toda la grilla y marca los que el rol tiene concedidos. Si el rol no tiene ninguna fila, devuelve `Modules` con `Options` completas y `Granted = all false`.
   - `BulkUpsertAsync`: Dapper con `connection.BeginTransaction()`. Estrategia:
     1. `SELECT Id, SystemOptionId FROM Security.RoleSystemOptions WHERE RoleId = @RoleId AND CompanyId = @CompanyId AND GcRecord = 0 FOR UPDATE` (bloquea las filas existentes del rol — evita race con `PUT /{id}` concurrente).
     2. `INSERT` por cada item nuevo (no existe en DB).
     3. `UPDATE` por cada item existente.
     4. `UPDATE ... SET GcRecord = 1, Updated = SYSUTCDATETIME(), UpdatedBy = @UserId WHERE Id IN (...)` por cada item removido.
     5. `COMMIT`. Si cualquier paso falla, `ROLLBACK` y propagar la excepción.
   - **No** usar EF Core change tracker para esto — Dapper con transacción explícita es 10x más performante para un bulk de N items y no choca con el cache de Identity.
5. `IPermissionService`: añadir `InvalidateUserCacheAsync(Guid companyId, Guid userId, CancellationToken ct)`.
6. `PermissionService`: implementar `InvalidateUserCacheAsync` con `_memoryCache.Remove($"permissions:v2:{companyId}:{userId}")` + `_memoryCache.Remove($"sidebar:{companyId}:{userId}")`.
7. `dotnet build -c Release` → 0 errores.

### F2 — Command + handler + validator (bulk upsert)

1. `BulkUpsertRoleSystemOptionsCommand.cs`:
   ```csharp
   public sealed record BulkUpsertRoleSystemOptionsCommand(
       Guid RoleId,
       IReadOnlyList<UpsertRoleSystemOptionItemDto> Items)
       : IRequest<Response<BulkUpsertRoleSystemOptionsResult>>;
   ```
2. `BulkUpsertRoleSystemOptionsCommandValidator.cs`:
   - `RuleFor(x => x.RoleId).NotEmpty()`.
   - `RuleFor(x => x.Items).NotNull().NotEmpty().Must(i => i.Count <= 500).WithMessage("BULK_TOO_LARGE")`.
   - `RuleFor(x => x.Items).Must(items => items.Select(i => i.SystemOptionId).Distinct().Count() == items.Count).WithMessage("BULK_DUPLICATE_OPTION")`.
   - Cada `Items[i].SystemOptionId` → `NotEmpty()`.
3. `BulkUpsertRoleSystemOptionsCommandHandler.cs`:
   ```csharp
   public sealed class BulkUpsertRoleSystemOptionsCommandHandler(
       IRoleSystemOptionsRepository repo,
       IRoleRepository roleRepo,
       IUnitOfWork unitOfWork,
       ICurrentUserService currentUser,
       IPermissionService permissionService)
       : IRequestHandler<BulkUpsertRoleSystemOptionsCommand, Response<BulkUpsertRoleSystemOptionsResult>>
   {
       public async Task<Response<BulkUpsertRoleSystemOptionsResult>> Handle(
           BulkUpsertRoleSystemOptionsCommand request, CancellationToken ct)
       {
           var companyId = currentUser.CompanyId;
           if (companyId == Guid.Empty)
               return Response<BulkUpsertRoleSystemOptionsResult>.Error(
                   "TENANT_REQUIRED", ["CompanyId required from token."]);

           if (!await roleRepo.ExistsAndActiveAsync(request.RoleId, ct))
               return Response<BulkUpsertRoleSystemOptionsResult>.Error(
                   "ROLE_NOT_FOUND", [$"Role {request.RoleId} not found or inactive."]);

           var newItems = request.Items.Select(i => new RoleSystemOption
           {
               Id = Guid.NewGuid(),
               CompanyId = companyId,
               RoleId = request.RoleId,
               SystemOptionId = i.SystemOptionId,
               CanRead = i.CanRead,
               CanCreate = i.CanCreate,
               CanUpdate = i.CanUpdate,
               CanDelete = i.CanDelete,
               CanDownload = i.CanDownload,
               CanExport = i.CanExport,
               CanExecute = i.CanExecute,
               GcRecord = 0,
               CreatedBy = currentUser.UserId,
               Created = DateTime.UtcNow
           }).ToList();

           var (created, updated, removed) = await repo.BulkUpsertAsync(
               request.RoleId, companyId, newItems, ct);

           // Cache invalidation — solo usuarios del tenant con este rol activo.
           // EF (no Dapper) porque es lectura con navegación pequeña y aprovecha la sesión ya abierta.
           var affectedUserIds = await unitOfWork.GetRepository<Domain.Security.UserRoleCompany>()
               .GetAllAsync(u => u.RoleId == request.RoleId
                              && u.CompanyId == companyId
                              && u.GcRecord == 0, ct);
           foreach (var u in affectedUserIds)
               await permissionService.InvalidateUserCacheAsync(companyId, u.UserId, ct);

           return Response<BulkUpsertRoleSystemOptionsResult>.Success(
               new BulkUpsertRoleSystemOptionsResult(created, updated, removed));
       }
   }
   ```
   **Nota crítica**: `_unitOfWork.GetRepository<UserRoleCompany>()` es un `IGenericRepository<T>` — si no expone `GetAllAsync` con predicado, usar `IUnitOfWork.UserRoleCompanies` (named repo) o un método ad-hoc. Confirmar con el repo concreto durante implementación.
4. `dotnet build -c Release` → 0 errores.

### F3 — Query + handler (matrix)

1. `GetRoleSystemOptionMatrixQuery.cs`:
   ```csharp
   public sealed record GetRoleSystemOptionMatrixQuery(Guid RoleId)
       : IRequest<Response<RoleSystemOptionMatrixDto>>;
   ```
2. `GetRoleSystemOptionMatrixQueryHandler.cs`:
   ```csharp
   public sealed class GetRoleSystemOptionMatrixQueryHandler(
       IRoleSystemOptionsRepository repo,
       IRoleRepository roleRepo,
       ICurrentUserService currentUser)
       : IRequestHandler<GetRoleSystemOptionMatrixQuery, Response<RoleSystemOptionMatrixDto>>
   {
       public async Task<Response<RoleSystemOptionMatrixDto>> Handle(
           GetRoleSystemOptionMatrixQuery request, CancellationToken ct)
       {
           var companyId = currentUser.CompanyId;
           if (companyId == Guid.Empty)
               return Response<RoleSystemOptionMatrixDto>.Error(
                   "TENANT_REQUIRED", ["CompanyId required from token."]);

           if (!await roleRepo.ExistsAndActiveAsync(request.RoleId, ct))
               return Response<RoleSystemOptionMatrixDto>.Error(
                   "ROLE_NOT_FOUND", [$"Role {request.RoleId} not found or inactive."]);

           var matrix = await repo.GetMatrixByRoleAsync(request.RoleId, companyId, ct);
           // matrix es null si el rol no existe (ExistsAndActiveAsync ya cubre, pero defensa redundante).
           return matrix is null
               ? Response<RoleSystemOptionMatrixDto>.Error("ROLE_NOT_FOUND", [$"Role {request.RoleId}."])
               : Response<RoleSystemOptionMatrixDto>.Success(matrix);
       }
   }
   ```
3. `dotnet build -c Release` → 0 errores.

### F4 — Controller

1. `RoleSystemOptionsController.cs` — añadir dos endpoints (no romper los existentes):
   ```csharp
   [HttpPut("bulk")]
   public async Task<ActionResult<Response<BulkUpsertRoleSystemOptionsResult>>> BulkUpsert(
       [FromBody] BulkUpsertRoleSystemOptionsRequest request,
       CancellationToken ct)
   {
       var response = await mediator.Send(new BulkUpsertRoleSystemOptionsCommand(request.RoleId, request.Items), ct);
       if (!response.IsSuccess && response.Message == "ROLE_NOT_FOUND") return NotFound(response);
       if (!response.IsSuccess && response.Message == "BULK_EMPTY") return BadRequest(response);
       return response.IsSuccess ? Ok(response) : BadRequest(response);
   }

   [HttpGet("matrix")]
   public async Task<ActionResult<Response<RoleSystemOptionMatrixDto>>> GetMatrix(
       [FromQuery] Guid roleId,
       CancellationToken ct)
   {
       var response = await mediator.Send(new GetRoleSystemOptionMatrixQuery(roleId), ct);
       if (!response.IsSuccess && response.Message == "ROLE_NOT_FOUND") return NotFound(response);
       return response.IsSuccess ? Ok(response) : BadRequest(response);
   }
   ```
2. Permisos: `[PermissionResource("RoleSystemOption")]` ya está a nivel de clase. `PUT → CanUpdate`, `GET → CanRead` por default. **No** añadir `[RequirePermission]` overrides.
3. `dotnet build -c Release` → 0 errores.

### F5 — Tests

1. `BulkUpsertRoleSystemOptionsCommandHandlerTests`:
   - `Handle_WhenTenantEmpty_ShouldReturnTenantRequiredError` — mock `currentUser.CompanyId` → `Guid.Empty` → corta antes de tocar repo.
   - `Handle_WhenRoleNotFound_ShouldReturnRoleNotFoundError` — mock `ExistsAndActiveAsync` → `false` → corta antes de `BulkUpsertAsync`.
   - `Handle_WhenValid_ShouldCallBulkUpsertAndInvalidateCache` — mock `ExistsAndActiveAsync` → `true`, mock `BulkUpsertAsync` → `(created:[a], updated:[b], removed:[c])`, mock `GetAllAsync<UserRoleCompany>` → 2 users → `InvalidateUserCacheAsync` se llama 2 veces con los `userId` correctos.
   - `Handle_WhenNoUsersInRole_ShouldNotCallInvalidate` — mock `GetAllAsync` → lista vacía → `InvalidateUserCacheAsync` se llama 0 veces.
   - `Handle_WhenBulkUpsertThrows_ShouldPropagateException` — mock `BulkUpsertAsync` → throw → handler propaga, no llama `InvalidateUserCacheAsync`.
2. `BulkUpsertRoleSystemOptionsCommandValidatorTests`:
   - `RoleId_Empty` → fail.
   - `Items_Null` → fail.
   - `Items_Empty` → fail with `BULK_EMPTY`.
   - `Items_501` → fail with `BULK_TOO_LARGE`.
   - `Items_WithDuplicateSystemOptionId` → fail with `BULK_DUPLICATE_OPTION`.
   - `Items_500_Unique` → pass.
3. `GetRoleSystemOptionMatrixQueryHandlerTests`:
   - `Handle_WhenRoleHasPermissionsInTwoModules_ShouldReturnGroupedMatrix` — mock `GetMatrixByRoleAsync` → DTO con 2 módulos, 3 options en total, `Granted` mixto.
   - `Handle_WhenRoleHasNoPermissions_ShouldReturnMatrixWithAllGrantedFalse` — mock → DTO con 1 módulo, 2 options, `Granted = all false`.
   - `Handle_WhenRoleNotFound_ShouldReturnRoleNotFoundError` — mock `ExistsAndActiveAsync` → `false` → no llama `GetMatrixByRoleAsync`.
4. `dotnet test --filter "FullyQualifiedName~RoleSystemOptions"` → 0 fallidos.
5. `dotnet test --collect:"XPlat Code Coverage"` → `JOIN.Application` ≥ 90% (gate CI).

### F6 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test` → 0 fallidos, cobertura ≥ 90%.
3. `CURL_REQUESTS.md` — añadir bloque `RoleSystemOptions bulk` y `RoleSystemOptions matrix` con los requests de los acceptance criteria.
4. Smoke test manual:
   - `PUT /api/v1/RoleSystemOptions/bulk` con `{roleId, items: [...]}` → 200 con `{created: [..], updated: [..], removed: [..]}`.
   - Invalidar memoria cache (`IMemoryCache` reset) → `GET /api/v1/RoleSystemOptions/matrix?roleId=X` → 200 con todos los módulos/options.
   - Verificar en DB que `RoleSystemOptions` refleja el diff.

---

## Acceptance criteria

### F1 — DTOs y repositories
- [ ] `UpsertRoleSystemOptionItemDto`, `BulkUpsertRoleSystemOptionsRequest`, `BulkUpsertRoleSystemOptionsResult` compilan con los 7 flags `Can*` por item.
- [ ] `RoleSystemOptionMatrixDto` con `RoleName`, `Modules[]`, `Options[]` con `Supports` + `Granted` (7 flags cada uno).
- [ ] `IRoleSystemOptionsRepository.GetActiveByRoleAndCompanyAsync` filtra `(RoleId, CompanyId, GcRecord = 0)`.
- [ ] `IRoleSystemOptionsRepository.GetMatrixByRoleAsync` devuelve `null` si el rol no existe, o DTO con todos los `SystemOptions` activos agrupados por `SystemModule` aunque el rol no tenga `RoleSystemOption` alguna.
- [ ] `IRoleSystemOptionsRepository.BulkUpsertAsync` ejecuta INSERT/UPDATE/soft-delete en una sola transacción SQL (no EF change tracker).
- [ ] `IPermissionService.InvalidateUserCacheAsync` apunta a `permissions:v2:{companyId}:{userId}` + `sidebar:{companyId}:{userId}` (claves v2 reales, no las legacy).

### F2 — Bulk upsert
- [ ] `BulkUpsertRoleSystemOptionsCommand`: `RoleId` `Guid`, `Items` `IReadOnlyList<UpsertRoleSystemOptionItemDto>`.
- [ ] Validator falla con `BULK_EMPTY` / `BULK_TOO_LARGE` (>500) / `BULK_DUPLICATE_OPTION` (mismo `SystemOptionId` dos veces).
- [ ] Handler corta con `ROLE_NOT_FOUND` (404) si el rol no existe o está soft-deleted.
- [ ] Handler corta con `TENANT_REQUIRED` si `currentUserService.CompanyId == Guid.Empty`.
- [ ] Handler pasa `IReadOnlyList<RoleSystemOption>` con `CompanyId = currentUser.CompanyId`, `CreatedBy = currentUser.UserId`, `Created = DateTime.UtcNow` a `BulkUpsertAsync`.
- [ ] Tras `BulkUpsertAsync`, el handler invalida la caché (`IPermissionService.InvalidateUserCacheAsync`) **únicamente** para los `UserId` con `UserRoleCompany` activo en `(roleId, companyId)`.
- [ ] Si `UserRoleCompany` afectados = 0, no se invoca `InvalidateUserCacheAsync` (no-op, no error).
- [ ] Si `BulkUpsertAsync` lanza excepción, no se invoca `InvalidateUserCacheAsync` (consistencia: cache stale es mejor que cache limpia con DB inconsistente).

### F3 — Matrix
- [ ] `GetRoleSystemOptionMatrixQuery` con `RoleId` `Guid`.
- [ ] Handler corta con `ROLE_NOT_FOUND` (404) si el rol no existe o está soft-deleted.
- [ ] Handler corta con `TENANT_REQUIRED` si `currentUserService.CompanyId == Guid.Empty`.
- [ ] Respuesta: `roleId`, `roleName`, `modules[]` con `moduleId`, `moduleName`, `options[]`.
- [ ] Cada `option` tiene `systemOptionId`, `name`, `route`, `supports` (defaults de `SystemOptions`), `granted` (estado real del rol, `false` si no hay `RoleSystemOption`).
- [ ] Orden: módulos por `Order` (o `Name`), opciones dentro de módulo por `OrderMenu`, `Name`.

### F4 — Controller
- [ ] `PUT /api/v1/RoleSystemOptions/bulk` mapea `ROLE_NOT_FOUND` → 404, `BULK_EMPTY` → 400, resto non-success → 400.
- [ ] `GET /api/v1/RoleSystemOptions/matrix?roleId=x` mapea `ROLE_NOT_FOUND` → 404, resto non-success → 400.
- [ ] Ningún endpoint existente (`GET /{id}`, `GET`, `POST`, `PUT /{id}`, `DELETE /{id}`, `GET /superadmin/all`) cambia de ruta, firma o comportamiento.
- [ ] Las dos rutas nuevas pasan por `[PermissionResource("RoleSystemOption")]` heredado: `PUT /bulk` requiere `CanUpdate`, `GET /matrix` requiere `CanRead` (HTTP-verb default mapping). Sin `[RequirePermission]` explícito.

### F5 — Tests
- [ ] Cobertura de los 5 caminos del handler bulk + 5 validaciones del validator + 3 caminos del query handler.
- [ ] `dotnet test --filter "FullyQualifiedName~RoleSystemOptions"` → 0 fallidos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate CI).

### F6 — General
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` actualizado con los bloques `bulk` y `matrix`.
- [ ] No migraciones EF, no cambios en `RolesController`, no cambios en `PermissionService` más allá del método nuevo.

---

## Decisions taken and discarded

- **`PUT /bulk` con semántica de "replace set"** (elegido) vs. "diff apply". El front guarda toda la grilla cada vez; calcular qué cambió en el cliente y mandar solo el diff es propenso a errores. Replace set + `{created, updated, removed}` permite a la UI mostrar "se modificaron 3, se quitaron 2" sin lógica adicional. Más simple y más seguro.
- **Bulk por `roleId`, no por `roleId + companyId` en el body** (elegido) vs. permitir `companyId` en el body. El `CompanyId` siempre viene del token (SPEC 23). Aceptar `companyId` en el body abre la puerta a tenants cruzados. Si se necesita bulk admin cross-tenant, va en spec aparte.
- **Tope de `Items.Count <= 500`** (elegido) vs. sin tope. 500 cubre la UI real (matrix típica tiene ~150 opciones + 50 acciones custom). Más de 500 es caso de bulk admin cross-tenant, fuera de scope. El handler corta con `BULK_TOO_LARGE` antes de tocar DB.
- **Dapper con transacción SQL explícita para `BulkUpsertAsync`** (elegido) vs. EF Core change tracker. EF guardaría una a una las N entidades con N round-trips; Dapper con `BeginTransaction` + `FOR UPDATE` + batch INSERT/UPDATE/soft-delete es 10x más rápido. La entity `RoleSystemOption` tiene referencia a `ApplicationRole` y `SystemOption` (navigation props) — no las usa el bulk, solo columnas planas.
- **`"removed"` cuenta soft-deletes, no hard deletes** (elegido) vs. `DELETE FROM ... WHERE ...`. Consistencia con el resto del modelo (`GcRecord = 0` everywhere). Si se hace `DELETE FROM` se rompe la auditoría y el contrato del modelo `BaseAuditableEntity`.
- **`InvalidateUserCacheAsync` apunta a `permissions:v2:` + `sidebar:`** (elegido) vs. reusar `InvalidateSidebarCacheCommand`. La rama `permission`/`permissions` del comando actual es **bug pre-existente** (apunta a `permissions:{...}` legacy, no `v2`). Spec 25 introduce un método nuevo en la interfaz que sí sabe la clave real. Spec 26 (futuro) puede arreglar el comando viejo.
- **`GET /matrix` devuelve TODOS los `SystemOptions` aunque el rol no los tenga** (elegido) vs. solo los que el rol tiene concedidos. La UI necesita pintar la grilla completa y mostrar checkboxes grises para los no concedidos. Devolver subset obligaría al front a hacer un `GET /SystemOptions` separado y cruzar manualmente (lo que precisamente evita este endpoint).
- **`GET /matrix` agrupa por `SystemModule`** (elegido) vs. flat list. La UI renderiza acordeones por módulo; agrupar en server-side reduce el procesamiento del cliente. El DTO anidado (`Modules[]` → `Options[]`) es directo de mapear con LINQ post-Dapper.
- **No `order`, `pageSize`, `filter` en `GET /matrix`** (elegido) vs. parámetros opcionales. La matrix siempre es completa (un rol tiene sentido solo si ves su grilla entera). Paginación aplica a `GET /` paged existente, no a matrix.
- **No devolver `RoleName` desde un `JOIN` extra cuando el handler ya lo conoce** (elegido) vs. traerlo desde `ApplicationRoles` con Dapper. El handler ya llama `ExistsAndActiveAsync` (que toca `Security.Roles`). Para no hacer round-trip extra, el repo `GetMatrixByRoleAsync` puede traer `RoleName` en la misma query si añade `LEFT JOIN Security.Roles r ON r.Id = @RoleId`. **Implementación**: incluir `r.Name AS RoleName` en la SELECT y mapearlo. (Alternativa descartada: el repo espera a que el handler le pase `RoleName` por param — feo y rompe la simetría con el resto de repos).
- **`POST /RoleSystemOptions` se mantiene para single-create** (elegido) vs. deprecación. El caso de uso "agregar un permiso suelto desde un modal" sigue existiendo. El bulk es para "guardar toda la grilla a la vez". Ambos conviven.
- **`PUT /{id}` se mantiene para single-update** (elegido) vs. deprecación. Mismo razonamiento. Un SuperAdmin editando una fila puntual no debería verse obligado a mandar 500 items.

---

## Identified risks

- **Bug pre-existente en `InvalidateSidebarCacheCommand`**: la rama `permission`/`permissions` apunta a `permissions:{companyId}:{userId}` (sin `v2`). La clave real es `permissions:v2:...`. Resultado: cuando un usuario invalida solo permisos via ese endpoint, **no** se invalida la cache del `PermissionService` (la fresh query la reconstruye igual porque el `GetOrCreateAsync` la crea con la clave nueva). Spec 25 introduce `InvalidateUserCacheAsync` que SÍ sabe la clave v2, evitando el bug. **Mitigación adicional**: una spec 26 debería fixear el `InvalidateSidebarCacheCommand` para usar la clave v2. Fuera de scope de 25.
- **Race entre `PUT /{id}` y `PUT /bulk` simultáneos sobre el mismo rol**: el `FOR UPDATE` en `BulkUpsertAsync` bloquea las filas existentes durante la tx, pero un `PUT /{id}` que llegue **antes** del `FOR UPDATE` puede crear un row nuevo que el bulk luego duplica. Mitigación: baja probabilidad (UI normalmente no hace bulk + single al mismo tiempo), pero agregar índice unique en `(RoleId, SystemOptionId, CompanyId, GcRecord = 0)` filter index para que la DB rechace el duplicado. Si se decide, va como migration en spec aparte.
- **Tenant-isolation en `GetMatrixByRoleAsync`**: la query filtra `RoleSystemOptions WHERE CompanyId = @CompanyId`, pero `SystemOptions` y `SystemModules` son catálogos globales (no tenant-scoped). Aceptable — esos catálogos son seed del sistema, no datos del tenant. Si en el futuro se vuelven tenant-scoped, el JOIN agrega `AND CompanyId = @CompanyId` en cada uno.
- **Performance del matrix con muchos `SystemOptions`**: si el seed crece a >1000 options, una sola query con GROUP BY + JSON_AGG puede ser más performante que el LEFT JOIN + map en C#. Mitigación: si el rowcount supera 5000, migrar a `string_agg` con `FOR JSON PATH`. Fuera de scope de 25.
- **Cache invalidation no transaccional**: si `BulkUpsertAsync` committea y luego la app crashea antes de invocar `InvalidateUserCacheAsync`, los usuarios afectados ven permisos stale hasta el TTL del cache (30 min absolute, 10 min sliding). Mitigación: aceptable — el siguiente request después de los TTL verá los permisos correctos. Si es crítico, mover la invalidación a un event handler post-commit (MediatR notification `RoleSystemOptionsBulkUpserted`).
- **Tope de 500 items es arbitrario**: si la UI real tiene 600 options para un rol, el PUT bulk falla con `BULK_TOO_LARGE`. Mitigación: 500 es techo blando; si la realidad lo supera, subir a 1000 o 2000. Documentar el límite en `CURL_REQUESTS.md`.
- **Hard-fail de la cache invalidation**: si `_memoryCache.Remove` lanza (no debería, pero…), el handler propaga la excepción y el cliente ve 500 aunque la DB ya committeó. Mitigación: envolver la invalidación en `try/catch` con log warning, no propagar. La DB está consistente, la cache stale se autovence.
- **DTO breaking change en `RoleSystemOptionDto`**: ninguno. Los nuevos DTOs (`BulkUpsertRoleSystemOptionsResult`, `RoleSystemOptionMatrixDto`) son archivos nuevos, no extienden el existente. Cero impacto en consumidores de `RoleSystemOptionDto` / `RoleSystemOptionListItemDto`.
- **Sin `[RequirePermission]` override en `PUT /bulk`**: el default HTTP-verb mapping evalúa `CanUpdate`. Si un operador solo tiene `CanCreate` (raro pero posible), no puede ejecutar el bulk. Esto es correcto — el bulk es write, requiere update. Si la UI necesita un endpoint "bulk create" sin update, va en spec aparte.
