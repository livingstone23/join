# SPEC 24 — Roles hardening: `permissionsCount`, `ROLE_HAS_USERS`, `cloneFromRoleId`

> **Status:** Implementado
> **Depends on:** SPEC 18 (Roles CRUD), SPEC 19 (RoleCompanies), SPEC 22 (RoleSystemOption DTO)
> **Date:** 2026-08-15
> **Objective:** Cerrar los tres gaps funcionales del catálogo `ApplicationRole` que dejó pendientes SPEC 18 sin romper el contrato existente: (a) proteger `DELETE` contra borrado de roles con usuarios asignados, devolviendo `ROLE_HAS_USERS` con conteo; (b) exponer `PermissionsCount` en `RoleDto` para que la UI muestre cuántos `RoleSystemOption` activos tiene cada rol; (c) soportar `CloneFromRoleId` en `POST` para que un SuperAdmin cree un nuevo rol copiando los `RoleSystemOption` del rol origen en la misma transacción. Cambios viven solo en `RoleDto`, `CreateRoleCommand`, `DeleteRoleCommandHandler`, repos y Dapper queries existentes. No nuevos endpoints, no migraciones, no DTOs nuevos.

---

## Scope

**In:**

- `src/2.Application.DTO/Security/RoleDto.cs`: añadir `int PermissionsCount` con default `0`. Mantener el resto intacto.
- `src/2.Application/Interface/Persistence/Security/IRoleRepository.cs`:
  - Nuevo `Task<int> CountActiveUsersByRoleIdAsync(Guid roleId, Guid companyId, CancellationToken ct)`.
  - Nuevo `Task<RoleDto?> GetByIdAsync(Guid id, CancellationToken ct)` ya existe (SPEC 18) — ahora debe popular `PermissionsCount` vía subquery o join.
  - `GetPagedAsync(...)` ya existe — popular `PermissionsCount` por fila (subquery correlacionado en el `SELECT`).
- `src/3.Persistence/Repositories/Security/RoleRepository.cs`:
  - Implementar `CountActiveUsersByRoleIdAsync` con Dapper — cuenta `UserRoleCompany` activos para `(roleId, companyId)`.
  - Reescribir `GetByIdAsync` y `GetPagedAsync` para incluir `PermissionsCount` (subquery `SELECT (SELECT COUNT(*) FROM Security.RoleSystemOptions WHERE RoleId = r.Id AND CompanyId = @CompanyId AND GcRecord = 0)` proyectada como `PermissionsCount`).
- `src/2.Application/UseCases/Security/Roles/Commands/DeleteRole/DeleteRoleCommandHandler.cs`:
  - Antes del soft-delete, llamar `CountActiveUsersByRoleIdAsync(existing.Id, currentUserService.CompanyId, ct)`.
  - Si `count > 0` → `Response<bool>.Error("ROLE_HAS_USERS", [$"El rol tiene {count} usuario(s) asignado(s). Desasigná antes de eliminar."])`.
- `src/4.Services.WebApi/Controllers/Security/RolesController.cs`:
  - `Delete`: mapear `"ROLE_HAS_USERS"` → `409 Conflict` (mismo status que `ROLE_HAS_USERS` semánticamente: el cliente tiene que desasignar primero).
  - Resto sin cambios.
- `src/2.Application/UseCases/Security/Roles/Commands/CreateRole/CreateRoleCommand.cs`: añadir `Guid? CloneFromRoleId = null` como último param posicional.
- `src/2.Application/UseCases/Security/Roles/Commands/CreateRole/CreateRoleCommandHandler.cs`:
  - Inyectar `IRoleSystemOptionsRepository` (o `IUnitOfWork.RoleSystemOptions`).
  - Si `request.CloneFromRoleId.HasValue`:
    1. Validar que el rol origen existe (no soft-deleted) → si no, `ROLE_NOT_FOUND`.
    2. Validar que pertenece al `currentUserService.CompanyId` (mismo tenant) → si no, `ROLE_NOT_FOUND` (no leak cross-tenant).
    3. Leer los `RoleSystemOption` activos del origen vía `IRoleSystemOptionsRepository.GetActiveByRoleAndCompanyAsync(originId, companyId, ct)` (nuevo método — ver Out of scope para la alternativa).
    4. Después del `SaveChangesAsync` del rol nuevo, en la misma tx (TransactionBehavior), insertar un `RoleSystemOption` por cada uno del origen con `newRoleId`, mismo `SystemOptionId`, mismas flags, mismo `OrderMenu`, `CompanyId` del token.
    5. Si el origen no tiene `RoleSystemOption` activos → crear el rol sin permisos (no error).
- Tests en `tests/UnitTests/JOIN.Application.UnitTest/Security/Roles/`:
  - `DeleteRoleCommandHandlerTests`: añadir caso `ROLE_HAS_USERS` con `count > 0`; caso `count == 0` (happy path sigue); caso `count > 0` con `CompanyId` distinto (no debe filtrar usuarios de otro tenant).
  - `CreateRoleCommandHandlerTests`: añadir caso `CloneFromRoleId` con origen que tiene 2 `RoleSystemOption` activos → el rol nuevo tiene esos mismos 2 insertados con `CompanyId` del token; caso `CloneFromRoleId` con origen soft-deleted → `ROLE_NOT_FOUND`; caso `CloneFromRoleId` con origen cross-tenant → `ROLE_NOT_FOUND`; caso `CloneFromRoleId` con origen sin `RoleSystemOption` activos → rol nuevo creado, sin permisos.
  - `RoleRepositoryTests` (nuevo si no existe): cubrir el path de `PermissionsCount` en `GetByIdAsync` con un rol que tiene 3 permisos activos → DTO con `PermissionsCount = 3`. Si los tests del repo son de integración, usar InMemory DB.

**Out of scope:**

- Cambios en `Roles/{id}/users` (SPEC 20) — el conteo ya está implementado allá; SPEC 24 solo lo reutiliza en `Delete`.
- Cambios en `GetRolesDetailed` más allá de `PermissionsCount` — sin nuevos filtros ni ordenamientos.
- Cambios en `CreateRole` más allá del clonado — sin nuevos campos en el command ni en el DTO.
- `CloneFromRoleId` no clona relaciones fuera de `RoleSystemOption` (no clona `UserRoleCompany`, no clona `RoleCompany` — solo el catálogo de permisos). Decisión documentada más abajo.
- Eliminar el chequeo de `IsSystemDefault` en `Delete` — sigue siendo protección hard (SPEC 18). `ROLE_HAS_USERS` es defense adicional, no reemplazo.
- Nuevos endpoints (`POST /Roles/{id}/clone` standalone) — el clonado es un param opcional del `POST` existente.
- Migración EF / seeder — no aplica (sin cambios de schema, `RoleDto.PermissionsCount` es proyección).
- Cobertura específica de `PermissionsCount` en `UpdateRoleCommandHandler` — el flag de update no cambia permisos, no necesita relectura. Si los permisos cambian vía otros paths, el siguiente `GET` los refleja.

---

## Data model

### `RoleDto` (extendido)

```csharp
public sealed record RoleDto(
    Guid Id,
    string Name,
    string NormalizedName,
    string? Description,
    bool IsSystemDefault,
    string? CreatedBy,
    DateTime Created,
    int PermissionsCount = 0);
```

`PermissionsCount` se proyecta siempre (subquery correlacionado). Si el rol no tiene permisos activos, devuelve `0`. Default `0` para mantener compatibilidad con callers que aún no manden el campo (no aplica acá porque el DTO lo devuelve siempre, pero el default permite que `record` ctor sin ese param compile).

### `CreateRoleCommand` (extendido)

```csharp
public sealed record CreateRoleCommand(
    string Name,
    string? Description,
    bool IsSystemDefault,
    Guid? CloneFromRoleId = null)
    : IRequest<Response<RoleDto>>;
```

`CloneFromRoleId` opcional. Default `null` mantiene compatibilidad con clientes existentes (POST sin clonar sigue funcionando idéntico a hoy).

---

## Implementation plan

### F1 — `ROLE_HAS_USERS` en `DELETE`

1. `IRoleRepository.CountActiveUsersByRoleIdAsync(Guid roleId, Guid companyId, CancellationToken ct)` — Dapper:
   ```sql
   SELECT COUNT(*) AS UsersCount
   FROM Security.UserRoleCompanies urc
   INNER JOIN Security.Roles r ON r.Id = urc.RoleId
   WHERE urc.RoleId = @RoleId
     AND urc.CompanyId = @CompanyId
     AND urc.GcRecord = 0
     AND r.GcRecord = 0;
   ```
   Tenant-scoped: filtra por `CompanyId` del token para no leak cross-tenant. Devuelve `int` (no `int?` — `COUNT(*)` siempre devuelve fila).
2. Implementar en `RoleRepository` con `ISqlConnectionFactory.CreateConnection()` (mismo patrón que `ExistsByNameAsync`).
3. `DeleteRoleCommandHandler.Handle`: insertar después del chequeo `IsSystemDefault`:
   ```csharp
   var usersCount = await roleRepository.CountActiveUsersByRoleIdAsync(
       existing.Id, currentUserService.CompanyId, cancellationToken);
   if (usersCount > 0)
   {
       return Response<bool>.Error(
           "ROLE_HAS_USERS",
           [$"El rol tiene {usersCount} usuario(s) asignado(s). Desasigná antes de eliminar."]);
   }
   ```
4. `RolesController.Delete`: añadir mapping antes del `BadRequest`:
   ```csharp
   if (response.Message == "ROLE_HAS_USERS") return Conflict(response);
   ```
5. Tests:
   - `Handle_WhenRoleHasActiveUsers_ShouldReturnRoleHasUsersError` — mock `CountActiveUsersByRoleIdAsync` → 2 → mensaje `"ROLE_HAS_USERS"`, sin tocar `MarkAsDeleted`/`SaveChangesAsync`.
   - `Handle_WhenRoleHasZeroUsers_ShouldProceedToSoftDelete` — mock → 0 → happy path sigue.
   - `Handle_WhenRoleHasUsersInOtherTenant_ShouldProceedToSoftDelete` — mock con companyId distinto al del token → 0 → el rol se borra (defense-in-depth: tenant isolation).
6. `dotnet build -c Release` → 0 errores.

### F2 — `permissionsCount` en `RoleDto`

1. `RoleDto`: añadir `int PermissionsCount = 0` (ver Data model).
2. `RoleRepository.GetByIdAsync(Guid id, CancellationToken ct)`:
   - Reescribir el `SELECT` para incluir:
     ```sql
     SELECT r.Id, r.Name, r.NormalizedName, r.Description, r.IsSystemDefault,
            r.CreatedBy, r.Created,
            (SELECT COUNT(*) FROM Security.RoleSystemOptions rso
              WHERE rso.RoleId = r.Id
                AND rso.CompanyId = @CompanyId
                AND rso.GcRecord = 0) AS PermissionsCount
     FROM Security.Roles r
     WHERE r.Id = @Id AND r.GcRecord = 0;
     ```
   - `GetByIdAsync` actual probablemente ya recibe `companyId` por el SPEC 19 — confirmar firma. Si no, añadir como param (mismo patrón que `IRoleSystemOptionsRepository.GetWithNamesAsync`).
3. `RoleRepository.GetPagedAsync(name, isActive, page, pageSize, ct)`:
   - Añadir la misma subquery correlacionada en el `SELECT` del listado paginado.
   - El `COUNT(*)` para `Total` no cambia (sigue contando roles).
4. `GetRoleByIdQueryHandler`: validar que el query propague el `CompanyId` del token (vía `IRoleRepository` o handler-level). Hoy el handler no usa `ICurrentUserService` — añadir la inyección para pasar el tenant a la query. Si ya estaba (revisar SPEC 19 que añadió `RoleCompanies`), reusar.
5. `GetRolesDetailedQueryHandler`: añadir `ICurrentUserService` para pasar tenant al repo. Sin esto, `PermissionsCount` queda en 0 o se filtra cross-tenant.
6. Tests:
   - Si hay tests de integración del repo, añadir caso con 3 permisos → `PermissionsCount = 3`, caso con 0 → `0`. Si no hay, basta con que `RoleDto` compile y el handler unitario verifique que el DTO se devuelve con `PermissionsCount` (valor mockeado del repo).
   - `GetRoleByIdQueryHandlerTests`: mock `IRoleRepository.GetByIdAsync(...)` → devuelve `RoleDto` con `PermissionsCount = 5`. Assert sobre el campo.
7. `dotnet build -c Release` → 0 errores.

### F3 — `CloneFromRoleId` en `POST`

1. `CreateRoleCommand`: añadir `Guid? CloneFromRoleId = null` (ver Data model).
2. `IRoleSystemOptionsRepository.GetActiveByRoleAndCompanyAsync(Guid roleId, Guid companyId, CancellationToken ct)`:
   - Dapper sobre `connectionFactory.CreateConnection()`:
     ```sql
     SELECT Id, CompanyId, RoleId, SystemOptionId, CanRead, CanCreate, CanUpdate,
            CanDelete, CanDownload, CanExport, CanExecute, IsVisibleMenu, OrderMenu
     FROM Security.RoleSystemOptions
     WHERE RoleId = @RoleId AND CompanyId = @CompanyId AND GcRecord = 0;
     ```
   - Tipo de retorno: `IReadOnlyList<RoleSystemOption>` (entity completo, no readmodel — para copiar todos los flags incluyendo los 5 nuevos de SPEC 22).
3. Implementar en `RoleSystemOptionsRepository`.
4. `CreateRoleCommandHandler`:
   - Inyectar `IRoleSystemOptionsRepository` (vía `IUnitOfWork.RoleSystemOptions`).
   - Si `request.CloneFromRoleId.HasValue`:
     1. `var originRole = await roleRepository.GetByIdAsync(request.CloneFromRoleId.Value, cancellationToken);`
     2. Si `originRole is null || originRole.GcRecord != 0` → `ROLE_NOT_FOUND`.
     3. `var originCompanyId = currentUserService.CompanyId;` (necesitamos el origen esté en el mismo tenant — `RoleDto` no tiene `CompanyId`, pero como Roles es catálogo global, **todos los roles son visibles para SuperAdminCompany**; el tenant-isolation real está en `RoleSystemOption`, que siempre filtra por `CompanyId`). Validación práctica: `var originPermissions = await roleOptionRepository.GetActiveByRoleAndCompanyAsync(request.CloneFromRoleId.Value, originCompanyId, ct);` — si la lista viene vacía y el rol tiene `PermissionsCount > 0` en otro tenant, eso indicaría cross-tenant, devolver `ROLE_NOT_FOUND`. Implementación práctica: si `originPermissions.Count == 0` AND el origen tiene `PermissionsCount > 0` global → `ROLE_NOT_FOUND`. Si `originPermissions.Count == 0` y el origen genuinamente no tiene permisos → OK, crear sin permisos.
     4. Después del `SaveChangesAsync` del rol nuevo (la entity ya tiene su `Id`):
        ```csharp
        if (request.CloneFromRoleId.HasValue && originPermissions.Count > 0)
        {
            var newPermissions = originPermissions.Select(p => new RoleSystemOption
            {
                CompanyId = originCompanyId,
                RoleId = entity.Id,
                SystemOptionId = p.SystemOptionId,
                CanRead = p.CanRead,
                CanCreate = p.CanCreate,
                CanUpdate = p.CanUpdate,
                CanDelete = p.CanDelete,
                CanDownload = p.CanDownload,
                CanExport = p.CanExport,
                CanExecute = p.CanExecute,
                IsVisibleMenu = p.IsVisibleMenu,
                OrderMenu = p.OrderMenu,
                GcRecord = 0,
                CreatedBy = currentUserService.UserId,
                Created = DateTime.UtcNow
            }).ToList();
            foreach (var p in newPermissions) await roleOptionRepository.InsertAsync(p, ct);
            await unitOfWork.SaveChangesAsync(ct);
        }
        ```
     5. Todo esto corre dentro de la misma tx outer (TransactionBehavior) — si falla la inserción de permisos, el rol se hace rollback. Garantía atómica.
5. Tests:
   - `Handle_WhenCloneFromRoleIdProvidedAndOriginHasPermissions_ShouldInsertCopies`: mock origen con 2 permisos → mock `InsertAsync` capturando → assert que se llamó 2 veces con `RoleId == newRoleId`, mismas flags.
   - `Handle_WhenCloneFromRoleIdProvidedButOriginMissing_ShouldReturnRoleNotFound`: mock `GetByIdAsync(originId)` → null → `ROLE_NOT_FOUND`, no toca `AddAsync` del rol.
   - `Handle_WhenCloneFromRoleIdProvidedButOriginHasNoPermissions_ShouldCreateRoleWithoutPermissions`: mock origen con `PermissionsCount = 0` y `GetActiveByRoleAndCompanyAsync` → lista vacía → `AddAsync` del rol se llama 1 vez, `InsertAsync` de permisos 0 veces.
   - `Handle_WhenCloneFromRoleIdIsNull_ShouldSkipCloning`: comportamiento actual intacto.
6. `dotnet build -c Release` → 0 errores.

### F4 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test` con `--collect:"XPlat Code Coverage"` → gate 90% OK.
3. `dotnet test --filter "FullyQualifiedName~Roles"` → todos los handlers + validators pasan.
4. Smoke test manual:
   - `DELETE /api/v1/Roles/{id}` con rol sin usuarios → 204.
   - `DELETE` con rol que tiene 2 usuarios en `UserRoleCompany` → 409 con `ROLE_HAS_USERS` y mensaje `"El rol tiene 2 usuario(s) asignado(s)..."`.
   - `GET /api/v1/Roles/detailed` → cada item con `PermissionsCount` correcto.
   - `POST /api/v1/Roles { name, description, cloneFromRoleId: <existing> }` → 201 con el rol nuevo, y `GET /api/v1/RoleSystemOptions?roleId=<newId>` devuelve los mismos permisos que el origen.
5. `CURL_REQUESTS.md`: actualizar el bloque `Roles` con los nuevos ejemplos (DELETE con usuarios → 409, POST con `cloneFromRoleId`, GET detailed con `permissionsCount` en la respuesta).

---

## Acceptance criteria

### F1 — `ROLE_HAS_USERS`
- [ ] `IRoleRepository.CountActiveUsersByRoleIdAsync` filtra por `(RoleId, CompanyId, GcRecord = 0)`.
- [ ] `DeleteRoleCommandHandler` corta con `ROLE_HAS_USERS` y mensaje con `count` si `usersCount > 0`. El soft-delete no se ejecuta.
- [ ] `RolesController.Delete` mapea `"ROLE_HAS_USERS"` → `409 Conflict`.
- [ ] El chequeo `IsSystemDefault` (SPEC 18) sigue corriendo antes — un rol del sistema con usuarios da `403`, no `409`.
- [ ] Tenant isolation: si el rol tiene usuarios en otro `CompanyId`, el handler cuenta 0 y procede al soft-delete (no leak cross-tenant en el conteo).

### F2 — `permissionsCount`
- [ ] `RoleDto.PermissionsCount` es `int` con default `0`.
- [ ] `RoleRepository.GetByIdAsync` proyecta `PermissionsCount` desde `RoleSystemOptions WHERE RoleId = r.Id AND CompanyId = @CompanyId AND GcRecord = 0`.
- [ ] `RoleRepository.GetPagedAsync` proyecta `PermissionsCount` por fila con la misma subquery.
- [ ] `GetRoleByIdQueryHandler` y `GetRolesDetailedQueryHandler` propagan `currentUserService.CompanyId` al repo.
- [ ] Tenant isolation: un rol sin permisos en el tenant del caller devuelve `0`, aunque tenga permisos en otro tenant.

### F3 — `cloneFromRoleId`
- [ ] `CreateRoleCommand.CloneFromRoleId` es `Guid?` con default `null`.
- [ ] `CreateRoleCommandHandler`:
  - Si `CloneFromRoleId == null` → comportamiento actual intacto.
  - Si `CloneFromRoleId` apunta a rol inexistente o soft-deleted → `ROLE_NOT_FOUND`, no se crea el rol nuevo.
  - Si `CloneFromRoleId` apunta a rol que tiene permisos en otro tenant → `ROLE_NOT_FOUND` (no leak).
  - Si `CloneFromRoleId` apunta a rol válido del tenant con N permisos → se crea el rol + se insertan N `RoleSystemOption` en la misma tx. Atómico.
  - Si `CloneFromRoleId` apunta a rol válido sin permisos → se crea el rol sin permisos. OK.
- [ ] Los `RoleSystemOption` copiados tienen mismo `SystemOptionId`, mismas `Can*` flags (incluidos los 5 nuevos de SPEC 22), mismo `OrderMenu`, mismo `IsVisibleMenu`, `CompanyId = currentUserService.CompanyId`, `CreatedBy = currentUserService.UserId`.

### General
- [ ] Tests cubren los 3 caminos de `Delete` (sin usuarios / con usuarios / con usuarios cross-tenant), los 4 caminos de `Create` (sin clone / clone válido / clone inexistente / clone cross-tenant / clone sin permisos).
- [ ] `dotnet test --filter "FullyQualifiedName~Roles"` → 0 fallidos.
- [ ] Cobertura `JOIN.Application` ≥ 90% (gate CI).
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` actualizado con los nuevos ejemplos y el status 409 en el mapeo de errores de `Delete`.
- [ ] No nuevos endpoints, no nuevas migraciones, no cambios en otros controllers.

---

## Decisions taken and discarded

- **`ROLE_HAS_USERS` retorna 409** (elegido) vs. 422 o 400. 409 = "conflict con el estado actual del recurso" — coincide con cómo la UI pide desasignar primero. Consistente con `ROLE_SYSTEM_OPTION_ALREADY_EXISTS` (también 409).
- **Mensaje de `ROLE_HAS_USERS` incluye el `count`** (elegido) vs. solo el código. El frontend puede mostrar "3 usuarios afectados" sin un GET extra (SPEC 20 ya cubre el listado paginado, pero el confirm del DELETE usa este mensaje para el toast).
- **`permissionsCount` se calcula en SQL, no en C#** (elegido) vs. un `Include` de EF o un loop en el handler. Una subquery correlacionada es lo más performante para un listado paginado (no genera N+1). `COUNT(*)` con índice en `(RoleId, GcRecord)` es barato.
- **`CloneFromRoleId` solo clona `RoleSystemOption`** (elegido) vs. clonar también `UserRoleCompany` o `RoleCompany`. Un clon de un rol es para "arrancar con esta base de permisos en una nueva configuración" — no tiene sentido arrastrar usuarios o tenants. El operador decide manualmente las asignaciones posteriores.
- **El origen del clone se valida solo por existencia + tenant-isolation en `RoleSystemOption`** (elegido) vs. agregar `CompanyId` al entity `ApplicationRole`. Roles es catálogo global (Identity), no tenant-scoped. El aislamiento real está en `RoleSystemOption` que filtra por `CompanyId`. El test de "cross-tenant clone" verifica que un origen sin permisos en el tenant del caller da `ROLE_NOT_FOUND` (señal clara), no un éxito silencioso con permisos vacíos.
- **El clonado ocurre dentro de la misma tx outer (TransactionBehavior)** (elegido) vs. tx separada. Si falla la inserción de permisos, el rol se rollbackea — sin roles huérfanos sin permisos. Tradeoff: la tx es más larga, pero la garantía atómica es lo correcto.
- **No exponer un endpoint standalone `POST /Roles/{id}/clone`** (elegido) vs. duplicar la API. El param opcional del POST existente cubre el caso y mantiene el contrato simple. Si en el futuro hace falta clonar vía un endpoint dedicado (ej. clonar desde un rol de otro tenant), se agrega.
- **`GetActiveByRoleAndCompanyAsync` devuelve `RoleSystemOption` entity, no un readmodel** (elegido) vs. un readmodel específico. Necesitamos todos los campos (incluidos los 5 nuevos de SPEC 22) para copiar. El entity es el shape correcto.
- **Validación de cross-tenant: si el origen tiene `PermissionsCount > 0` global pero 0 en el tenant del caller → `ROLE_NOT_FOUND`** (elegido) vs. crear silencioso sin permisos. La señal de error es más clara que un éxito que requiere un GET extra para entender qué pasó.

---

## Identified risks

- **Count puede ser inexacto por race condition**: entre `CountActiveUsersByRoleIdAsync` y `MarkAsDeleted`, un usuario nuevo podría ser asignado al rol. Mitigación: aceptable — el `UserRoleCompany` insert contra el rol soft-deleted no falla en la FK check (no hay FK hard entre las tablas en este modelo), pero el siguiente `GET /Roles/{id}/users` filtrará por `GcRecord = 0` del rol. Riesgo bajo.
- **Subquery `PermissionsCount` en listado paginado**: si hay miles de `RoleSystemOption`, la subquery correlacionada puede ser lenta. Mitigación: índice en `RoleSystemOptions(RoleId, GcRecord, CompanyId)` ya existe o se crea. Si el perf es problema, futuro: denormalizar `PermissionsCount` en `Roles` con trigger.
- **Clonado dentro de tx**: si el origen tiene 200 permisos, la tx outer dura más. Mitigación: aceptable para un POST administrativo de baja frecuencia. Si se vuelve un problema, mover a background job.
- **DTO breaking change para clientes que deserializan `RoleDto` con ctor posicional estricto**: el nuevo param `PermissionsCount` tiene default `0`, pero algunos JSON deserializers no respetan defaults de records. Mitigación: clientes que usan System.Text.Json con ctor binding van a romper si el server omite el campo. **Decisión**: el server siempre devuelve el campo, los clientes lo ignoran si no lo esperan (System.Text.Json default). Si un cliente usa Json.NET con `MissingMemberHandling.Error`, hay que actualizarlo. Documentar en `CURL_REQUESTS.md`.
- **`RoleDto.PermissionsCount` requiere tenant propagation**: si en el futuro `GetRoleByIdQueryHandler` se llama desde un path sin `ICurrentUserService` (ej. SuperAdmin global cross-tenant), el conteo se filtra al tenant del token o devuelve 0. Mitigación: documentar que `PermissionsCount` siempre es tenant-scoped; un endpoint cross-tenant necesitaría un query dedicado.