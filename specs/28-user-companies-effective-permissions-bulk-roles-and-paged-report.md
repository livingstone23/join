# SPEC 28 — Membresía multi-empresa, permisos efectivos, asignación masiva de roles y reporte de usuarios paginado

> **Status:** Implementado
> **Depends on:** SPEC 17 (PermissionResource + flag override), SPEC 23 (tenant desde el token), SPEC 25 (sub-DTOs de matriz + `IPermissionService.InvalidateUserCacheAsync`), **SPEC 27** (arreglo de `TransactionBehavior` + `IUserAdminRepository`)
> **Date:** 2026-08-15
> **Objective:** Completar la administración de usuarios de empresa desde la tabla del panel —alta y baja de pertenencia multi-empresa, matriz de permisos efectivos por usuario, asignación masiva de roles y reporte paginado con búsqueda— y corregir en el camino que los roles se escriban en la tabla de Identity en lugar de `UserRoleCompanies`, que es la única que gobierna la autorización.

---

## Por qué existe esta spec

Segunda de las tres specs en que se partió el bloque "Administración de usuarios de la empresa" del backlog (items 15 a 22). Cubre los items 18, 19, 20 y 21.

| Spec | Items | Núcleo |
|---|---|---|
| 27 | 15, 16, 17 | Ciclo de vida admin: invite + status + force-reset. |
| **28** (esta) | 18, 19, 20, 21 | Membresía multi-empresa, permisos efectivos, roles bulk, reporte paginado. |
| 29 | 22 | Bitácora de seguridad: tabla `Security.AuditLogs` + interceptor de diff + endpoint. |

`Depends on: SPEC 27` es una dependencia **dura**, no de conveniencia. Los items 18 y 20 son comandos transaccionales que cortan con `Response.Error` después de escribir filas, así que sin el arreglo de `TransactionBehavior` (F0 de la SPEC 27) commitearían escrituras parciales. Además reusan `IUserAdminRepository`, que la 27 introduce.

Dos hallazgos del relevamiento condicionan el alcance:

1. **Los roles se escriben en la tabla que no gobierna la autorización (H1).** `PermissionService.cs:57` lee `_dbContext.UserRoleCompanies` y `LoginCommandHandler.cs:64` arma los claims del JWT desde `UserRoleCompany`. Pero `ReplaceUserRolesCommandHandler` (`PUT /Users/{userId}/roles`) escribe con `userManager.AddToRolesAsync` / `RemoveFromRolesAsync`, o sea en `Security.UserRoles` (tabla de Identity, sin `CompanyId`), y `GetUsersWithRolesQueryHandler` lee de ahí. Resultado: **el endpoint no cambia ningún permiso efectivo y tampoco invalida caché**. Fuera del seed, nada escribe `UserRoleCompanies`.
2. **`GET /Users/{userId}/companies` no filtra por tenant (H2).** Devuelve todas las empresas del usuario, de cualquier tenant, a cualquier usuario autenticado con permiso de lectura sobre `Users`.

El item 20 sería inútil sin corregir H1: escribiría en `UserRoleCompanies` mientras el endpoint single sigue escribiendo en `Security.UserRoles`, dejando dos fuentes de verdad divergentes.

No se menciona SPEC 26: es independiente, y su F1 (`refresh_token_id` en el JWT) ya está en el código.

---

## Scope

**In:**

### A. Corrección de H1 — los roles se escriben donde no gobiernan

- `src/2.Application/UseCases/Security/Users/Commands/ReplaceUserRoles/ReplaceUserRolesCommandHandler.cs` — reescribir para que opere sobre `UserRoleCompany` con el `companyId` del token, en vez de `userManager.AddToRolesAsync` / `RemoveFromRolesAsync`. Agrega `IPermissionService.InvalidateUserCacheAsync` al final.
- `ReplaceUserRolesCommand` y `UpdateUserRolesDto` — el contrato sigue aceptando **nombres** de rol (`IReadOnlyCollection<string> Roles`) para no romper al front. Los nombres se resuelven a `RoleId` contra los roles del tenant (`Security.RoleCompanies`), y un nombre que no exista en el tenant corta con `ROLE_NOT_FOUND`.
- `src/2.Application/UseCases/Security/Users/Queries/GetUsersWithRoles/GetUsersWithRolesQueryHandler.cs` — reescribir el SQL para leer de `Security.UserRoleCompanies` filtrado por el `companyId` del token, y agregar el filtro de tenant que hoy no tiene.
- **La tabla `Security.UserRoles` de Identity queda sin escritores en producción.** No se borra ni se migra en esta spec (ver Out of scope).

### B. Item 18 — pertenencia multi-empresa

- `src/2.Application/UseCases/Security/UserCompanies/Commands/AddUserCompany/` — command (`ITransactionalCommand<Response<AddUserCompanyResultDto>>`), handler, validator.
- `src/2.Application/UseCases/Security/UserCompanies/Commands/RemoveUserCompany/` — command (`ITransactionalCommand<Response<bool>>`), handler, validator.
- `src/2.Application.DTO/Security/User/AddUserCompanyRequestDto.cs` — `{ CompanyId, RoleIds[] }`.
- `src/2.Application.DTO/Security/User/AddUserCompanyResultDto.cs` — `{ UserId, CompanyId, IsDefault, RoleIdsAssigned[] }`.
- `POST` escribe `UserCompany` (`IsDefault = true` solo si el usuario no tenía ninguna) + un `UserRoleCompany` por `roleId`. Idempotente: si la membresía ya existe, reemplaza el set de roles y devuelve 200.
- `DELETE` hace soft-delete de la fila `UserCompany` y de todos los `UserRoleCompany` de ese `(userId, companyId)`. Invalida `permissions:v2:{companyId}:{userId}` + `sidebar:{companyId}:{userId}`.
- `DELETE` sobre la empresa por defecto → `CANNOT_REMOVE_DEFAULT_COMPANY` (409). Hay que mover el default primero con el `PUT /default-company/{companyId}` existente.
- `DELETE` sobre la última empresa del usuario → `CANNOT_REMOVE_LAST_COMPANY` (409). Un usuario sin ninguna empresa no puede loguearse y queda inaccesible.
- **Los tres endpoints de `/companies` (el `GET` existente más estos dos) pasan a exigir SuperAdmin**, con `[Authorize(Roles = "SuperAdmin")]` + `[SkipDynamicAuthorization]`, siguiendo el patrón de `GET /Users/reports/system`. Cierra de paso el leak cross-tenant del `GET`.

### C. Item 19 — permisos efectivos por usuario

- `src/2.Application/UseCases/Security/Users/Queries/GetUserEffectivePermissions/` — query (`IRequest<Response<UserEffectivePermissionsDto>>`), handler.
- `src/2.Application.DTO/Security/User/UserEffectivePermissionsDto.cs` — `{ UserId, CompanyId, RoleIds[], RoleNames[], Modules[] }`. Reusa `RoleSystemOptionMatrixModuleDto` y `RoleSystemOptionMatrixOptionDto` de SPEC 25 sin tocarlos.
- `Granted` de cada opción es el **OR** de los flags de todos los roles activos del usuario en ese tenant. `Supports` sigue viniendo de `SystemOptions`.
- Devuelve **todos** los `SystemOptions` activos agrupados por módulo, aunque el usuario no tenga ningún rol: en ese caso `RoleIds` y `RoleNames` van vacíos y todos los `Granted` en `false`. Mismo criterio que `GET /RoleSystemOptions/matrix` de SPEC 25.
- **Sin parámetro `?companyId=`.** El tenant sale del JWT (SPEC 23). La ruta final es `GET /api/v1/Users/{userId}/effective-permissions`.
- Query fresca por Dapper. **No** lee la caché de `PermissionService`: el panel tiene que mostrar el estado real de la DB, no un snapshot de hasta 30 minutos.

### D. Item 20 — asignación masiva de roles

- `src/2.Application/UseCases/Security/Users/Commands/BulkUpdateUserRoles/` — command (`ITransactionalCommand<Response<BulkUpdateUserRolesResultDto>>`), handler, validator.
- `src/2.Application.DTO/Security/User/BulkUpdateUserRolesRequestDto.cs` — `{ UserIds[], AddRoleIds[], RemoveRoleIds[] }`. Renombrado respecto del backlog (`addRoles`/`removeRoles`) porque son `Guid`, no nombres.
- `src/2.Application.DTO/Security/User/BulkUpdateUserRolesResultDto.cs` — `{ Items[], UsersUpdated, UsersSkipped }` con `BulkUpdateUserRoleItemDto { UserId, Outcome, RolesAdded, RolesRemoved }`.
- `src/2.Application.DTO/Security/User/BulkRoleOutcome.cs` — enum `Updated = 1, NoChange = 2, UserNotFound = 3`.
- Semántica **delta**, no replace: agrega los `AddRoleIds` que falten y soft-deletea los `RemoveRoleIds` que existan, sin tocar el resto. Distinto del `PUT /Users/{userId}/roles`, que es replace de set.
- Un `userId` sin membresía en el tenant no corta el lote: devuelve `UserNotFound` en su ítem y el resto se procesa.
- Invalida la caché de cada usuario con `Outcome = Updated`.
- Escribe `UserRoleCompany`, nunca `Security.UserRoles`.

### E. Item 21 — reporte de `my-company` paginado y con búsqueda

- `GetMyCompanyUserReportQuery` — agregar `PageNumber`, `PageSize`, `Search`, `IsActive`. Pasa a devolver `Response<PagedResult<UserManagementReportDto>>`.
- `UserManagementReportQueryHelper` — agregar `ReadPagedAsync`. `ReadAsync` conserva su firma.
- `GetSystemWideUserReportQuery` sigue usando `ReadAsync`: `GET /Users/reports/system` no cambia de contrato ni de shape.
- Paginación sobre `(UserId, CompanyId)` distintos, no sobre las filas planas de `user × company × role`. Se resuelve con una CTE que numera los pares distintos y un `INNER JOIN` de la CTE paginada contra el SQL de detalle, de modo que el agrupado en C# siga recibiendo todas las filas de rol de cada par incluido.
- `TotalCount` cuenta pares `(UserId, CompanyId)` distintos que pasan los filtros, no filas.
- `Search` matchea `FirstName`, `LastName` y `Email` con coincidencia parcial case-insensitive. Usa `CONCAT()` para el nombre completo, sin funciones propietarias (portabilidad cross-DB, ver `CLAUDE.md`).
- `IsActive` filtra `u.IsActive = @IsActive` cuando viene con valor; null trae ambos. **Funciona porque el reporte va por Dapper**: el filtro global de EF que esconde a los inactivos no aplica acá.
- Clamps: `PageNumber >= 1`, `PageSize` en `[1, 50]`, con `DefaultPageSize = 10`. Mismo patrón que `GetPersonsPagedQueryHandler`.
- Branching de la cláusula de paginación entre `LIMIT/OFFSET` (Postgres) y `OFFSET … FETCH NEXT` (SQL Server), como el resto de los handlers paginados.

### F. Repositorios

- `src/2.Application/Interface/Persistence/Security/IUserAdminRepository.cs` (creado en SPEC 27) — agregar `FilterExistingRoleIdsByNameAsync`, `GetMembershipInfoAsync`, `GetEffectivePermissionsAsync`, `FilterUsersWithMembershipAsync` y `CompanyExistsAsync` (firmas en la sección Data model).
- Escrituras de los items 18 y 20 vía `IUnitOfWork.GetRepository<UserCompany>()` / `GetRepository<UserRoleCompany>()`. `IGenericRepository<T>` **no** tiene `GetAllAsync` con predicado, así que toda lectura selectiva va por Dapper.

### G. Controller

- `src/4.Services.WebApi/Controllers/Security/UsersController.cs`:
  - `POST /{userId:guid}/companies` — SuperAdmin.
  - `DELETE /{userId:guid}/companies/{companyId:guid}` — SuperAdmin.
  - `GET /{userId:guid}/companies` — **existente**, pasa a SuperAdmin.
  - `GET /{userId:guid}/effective-permissions` — `[PermissionResource("Users")]` heredado, default `CanRead`.
  - `PUT /roles/bulk` — heredado, default `CanUpdate`.
  - `GET /reports/my-company` — **existente**, gana los 4 query params.
- Ruta `PUT /roles/bulk` declarada **antes** de `PUT /{userId:guid}/roles` para que el literal `roles` no se coma el `userId` en el routing.

### H. Tests

- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/UserCompanies/Commands/…` para los dos comandos del item 18.
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/…` para el item 19, el 20, el 21 y el `ReplaceUserRoles` reescrito.

### I. Migración

- **Ninguna.** No hay columnas ni tablas nuevas.

**Out of scope (para specs futuras):**

- **Item 22 y la tabla `Security.AuditLogs`** — SPEC 29.
- **Borrar o migrar la tabla `Security.UserRoles` de Identity.** Queda sin escritores pero con las filas históricas del seed. Limpiarla necesita migración y decidir qué pasa con `userManager.GetRolesAsync` (que quedaría devolviendo vacío). Spec aparte.
- **`userManager.IsInRoleAsync` / `GetRolesAsync` en `DatabaseSeeder`** — el seed sigue escribiendo la tabla de Identity. No se toca.
- **`SetDefaultCompanyCommandHandler`** — carga todas las filas de `UserCompany` en memoria y filtra en C#. Bug de performance preexistente, no se arregla acá.
- **Sacar `&& u.IsActive` del filtro global de EF** — anotado en SPEC 27, sigue pendiente.
- **Paginación de `GET /Users`** (`GetUsersWithRoles`) — se le arregla la fuente de datos y el filtro de tenant, pero sigue devolviendo la colección completa.
- **Paginación de `GET /Users/reports/system`** — mantiene su contrato actual sin paginar.
- **Permisos efectivos cross-tenant** (ver el usuario en una empresa que no es la del token) — siempre el tenant del JWT.
- **Permisos efectivos del propio usuario logueado** (`GET /account/my-permissions`) — es SPEC 26.
- **Leer la caché de `PermissionService` en el item 19** — query fresca siempre.
- **Bulk de membresía multi-empresa** (agregar N usuarios a una empresa de una vez) — el item 20 es bulk de roles, no de empresas.
- **Semántica replace en el item 20** — es delta. El replace por usuario lo cubre `PUT /Users/{userId}/roles`.
- **Notificación por correo** de cambios de rol o de membresía.
- **Cambios sobre `PUT /Users/{userId}/default-company/{companyId}`**.

---

## Data model

Esta spec **no introduce ninguna tabla ni columna nueva**. Reusa `Security.UserCompanies`, `Security.UserRoleCompanies`, `Security.Roles`, `Security.RoleCompanies`, `Security.SystemOptions`, `Security.SystemModules` y `Security.RoleSystemOptions` tal como están. Sin migración EF.

Índices únicos existentes que condicionan las escrituras:

- `UserCompanies`: único en `(UserId, CompanyId)`, más un índice único filtrado `UX_UserCompanies_UserId_Default` sobre `UserId` con predicado `[IsDefault] = 1 AND [GcRecord] = 0`. **Un usuario no puede tener dos empresas por defecto.**
- `UserRoleCompanies`: único en `(UserId, RoleId, CompanyId)`.

Consecuencia para los items 18 y 20: el soft-delete (`GcRecord = stamp`) **no** libera el índice único de `(UserId, CompanyId)`, porque el índice no filtra por `GcRecord`. Reagregar una membresía dada de baja tiene que **reactivar la fila existente** (`GcRecord = 0`), no insertar una nueva. Igual para `UserRoleCompanies`.

### Item 19 — permisos efectivos

```csharp
// src/2.Application.DTO/Security/User/UserEffectivePermissionsDto.cs
public sealed record UserEffectivePermissionsDto(
    Guid UserId,
    Guid CompanyId,
    IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<string> RoleNames,
    IReadOnlyList<RoleSystemOptionMatrixModuleDto> Modules);
```

`RoleSystemOptionMatrixModuleDto` y `RoleSystemOptionMatrixOptionDto` vienen de `src/2.Application.DTO/Security/RoleSystemOptionMatrixDtos.cs` (SPEC 25, ya implementado) y **no se modifican**. `RoleIds` y `RoleNames` son listas paralelas ordenadas por nombre de rol.

Se descartó reusar `RoleSystemOptionMatrixDto` directamente: su campo `RoleName` es un `string` único y la unión de varios roles no es un rol. Meter `"Contador + 2 más"` ahí obliga al front a parsear texto.

`Granted` de cada opción es el OR de los flags de todos los `RoleSystemOption` de los roles activos del usuario en el tenant. En SQL, `MAX(CAST(rso.CanRead AS int))` por opción, colapsado a `bool` en el mapeo.

### Item 18 — membresía

```csharp
// src/2.Application.DTO/Security/User/AddUserCompanyRequestDto.cs
public sealed record AddUserCompanyRequestDto(
    Guid CompanyId,
    IReadOnlyList<Guid> RoleIds);

// src/2.Application.DTO/Security/User/AddUserCompanyResultDto.cs
public sealed record AddUserCompanyResultDto(
    Guid UserId,
    Guid CompanyId,
    bool IsDefault,
    IReadOnlyList<Guid> RoleIdsAssigned);

// src/2.Application/UseCases/Security/UserCompanies/Commands/AddUserCompany/AddUserCompanyCommand.cs
public sealed record AddUserCompanyCommand(
    Guid UserId,
    Guid CompanyId,
    IReadOnlyList<Guid> RoleIds)
    : ITransactionalCommand<Response<AddUserCompanyResultDto>>;

// src/2.Application/UseCases/Security/UserCompanies/Commands/RemoveUserCompany/RemoveUserCompanyCommand.cs
public sealed record RemoveUserCompanyCommand(Guid UserId, Guid CompanyId)
    : ITransactionalCommand<Response<bool>>;
```

**Guardas del `POST`**, en orden:

1. Usuario inexistente o `GcRecord != 0` → `USER_NOT_FOUND` (404). Vía `IUserAdminRepository.GetAdminSnapshotAsync` de SPEC 27, que no filtra por `IsActive`.
2. `CompanyId` inexistente o `GcRecord != 0` → `COMPANY_NOT_FOUND` (404).
3. Algún `RoleId` que no exista en `Security.RoleCompanies` para ese `CompanyId` → `ROLE_NOT_FOUND` (404). Ojo: el tenant de validación es el **`CompanyId` del body**, no el del token.
4. Si la membresía existe activa → reemplaza el set de `UserRoleCompany` y devuelve 200 con `IsDefault` actual.
5. Si la membresía existe con `GcRecord != 0` → la reactiva (`GcRecord = 0`) en vez de insertar.
6. Si no existe → inserta con `IsDefault = true` solo cuando el usuario no tiene ninguna otra empresa activa.

**Guardas del `DELETE`**, en orden:

1. `GetMembershipInfoAsync` → `Exists == false` → `MEMBERSHIP_NOT_FOUND` (404).
2. `TotalActiveCompanies == 1` → `CANNOT_REMOVE_LAST_COMPANY` (409).
3. `IsDefault == true` → `CANNOT_REMOVE_DEFAULT_COMPANY` (409).

El orden importa: si el usuario tiene una sola empresa, esa empresa es necesariamente la default, y `CANNOT_REMOVE_LAST_COMPANY` es el mensaje útil.

### Item 20 — bulk de roles

```csharp
// src/2.Application.DTO/Security/User/BulkUpdateUserRolesRequestDto.cs
public sealed record BulkUpdateUserRolesRequestDto(
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<Guid> AddRoleIds,
    IReadOnlyList<Guid> RemoveRoleIds);

// src/2.Application.DTO/Security/User/BulkRoleOutcome.cs
public enum BulkRoleOutcome
{
    /// <summary>Se agregó o quitó al menos un rol.</summary>
    Updated = 1,
    /// <summary>El usuario ya tenía exactamente ese set; no se escribió nada.</summary>
    NoChange = 2,
    /// <summary>El userId no tiene membresía activa en el tenant del token.</summary>
    UserNotFound = 3
}

// src/2.Application.DTO/Security/User/BulkUpdateUserRolesResultDto.cs
public sealed record BulkUpdateUserRoleItemDto(
    Guid UserId,
    BulkRoleOutcome Outcome,
    int RolesAdded,
    int RolesRemoved);

public sealed record BulkUpdateUserRolesResultDto(
    IReadOnlyList<BulkUpdateUserRoleItemDto> Items,
    int UsersUpdated,
    int UsersSkipped);

// src/2.Application/UseCases/Security/Users/Commands/BulkUpdateUserRoles/BulkUpdateUserRolesCommand.cs
public sealed record BulkUpdateUserRolesCommand(
    IReadOnlyList<Guid> UserIds,
    IReadOnlyList<Guid> AddRoleIds,
    IReadOnlyList<Guid> RemoveRoleIds)
    : ITransactionalCommand<Response<BulkUpdateUserRolesResultDto>>;
```

Semántica **delta**. Para cada usuario con membresía:

- Por cada `AddRoleIds` sin fila activa: inserta `UserRoleCompany`, o reactiva la fila soft-deleted si existe (índice único `(UserId, RoleId, CompanyId)`).
- Por cada `RemoveRoleIds` con fila activa: soft-delete.
- `RolesAdded` / `RolesRemoved` cuentan lo efectivamente escrito, no lo pedido.

Reglas del validator:

- `UserIds` `NotNull().NotEmpty()`, tope `<= 200` (`BULK_TOO_MANY_USERS`), sin duplicados (`BULK_DUPLICATE_USER`).
- `AddRoleIds` y `RemoveRoleIds` pueden venir vacíos, pero **no los dos** (`BULK_NO_OP`).
- Tope combinado `AddRoleIds.Count + RemoveRoleIds.Count <= 40` (`BULK_TOO_MANY_ROLES`).
- Ningún `Guid` en `Empty` en las tres listas.
- Un mismo `roleId` en `AddRoleIds` y `RemoveRoleIds` → `ROLE_IN_BOTH_LISTS`.

Un `roleId` que no pertenece al tenant corta **todo** el lote con `ROLE_NOT_FOUND` (404), antes de escribir nada: es un error del operador, no del dato. En cambio un `userId` sin membresía **no** corta: devuelve `UserNotFound` en su ítem.

### Item 21 — reporte paginado

```csharp
// src/2.Application/UseCases/Security/Queries/GetMyCompanyUserReport/GetMyCompanyUserReportQuery.cs
public record GetMyCompanyUserReportQuery(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string[]? RoleNames = null,
    int PageNumber = 1,
    int PageSize = 10,
    string? Search = null,
    bool? IsActive = null)
    : IRequest<Response<PagedResult<UserManagementReportDto>>>;
```

`UserManagementReportDto` **no cambia**. Lo que cambia es el envoltorio: de `IReadOnlyCollection<T>` a `PagedResult<T>`. Es un breaking change del shape de respuesta de `GET /Users/reports/my-company`, y es exactamente lo que pide el item 21.

Firma nueva del helper compartido:

```csharp
// src/2.Application/UseCases/Security/Queries/GetSystemWideUserReport/GetSystemWideUserReportQueryHandler.cs
internal static class UserManagementReportQueryHelper
{
    public static async Task<IReadOnlyCollection<UserManagementReportDto>> ReadAsync(
        ISqlConnectionFactory connectionFactory,
        Guid? scopedCompanyId,
        Guid? targetCompanyId,
        DateTime? fromDate,
        DateTime? toDate,
        IReadOnlyCollection<string>? roleNames,
        CancellationToken cancellationToken);          // sin cambios — la usa /reports/system

    public static async Task<(IReadOnlyCollection<UserManagementReportDto> Items, int TotalCount)>
        ReadPagedAsync(
            ISqlConnectionFactory connectionFactory,
            Guid? scopedCompanyId,
            Guid? targetCompanyId,
            DateTime? fromDate,
            DateTime? toDate,
            IReadOnlyCollection<string>? roleNames,
            int pageNumber,
            int pageSize,
            string? search,
            bool? isActive,
            CancellationToken cancellationToken);       // nueva
}
```

Dos métodos en vez de parámetros opcionales en uno: el paginado devuelve `TotalCount` y el otro no, así que la firma no unifica sin ensuciar el retorno. `GetSystemWideUserReportQueryHandler` sigue llamando a `ReadAsync` y **no se toca**. Ambos comparten el `NormalizeDateRange`, el `NormalizeGuid`, el `BuildFullName` y el agrupado en C# ya existentes, extraídos a helpers privados.

**SQL del paginado.** El problema: el SQL actual devuelve una fila por `user × company × role`, así que un `OFFSET` sobre esas filas parte usuarios al medio. Se resuelve numerando los pares distintos primero:

```sql
WITH filtered AS (
    SELECT DISTINCT u.Id AS UserId, uc.CompanyId, c.Name AS CompanyName,
           u.FirstName, u.LastName, u.Email
    FROM Security.Users u
    LEFT JOIN Security.UserCompanies uc ON uc.UserId = u.Id AND uc.GcRecord = 0
    LEFT JOIN Common.Companies c ON c.Id = uc.CompanyId AND c.GcRecord = 0
    -- + los mismos filtros de fecha, rol y tenant del SQL actual
    WHERE u.GcRecord = 0
      AND (@ScopedCompanyId IS NULL OR uc.CompanyId = @ScopedCompanyId)
      AND (@IsActive IS NULL OR u.IsActive = @IsActive)
      AND (@Search IS NULL
           OR u.Email LIKE @Search
           OR CONCAT(u.FirstName, ' ', u.LastName) LIKE @Search)
),
page AS (
    SELECT UserId, CompanyId
    FROM filtered
    ORDER BY CompanyName, FirstName, LastName, Email
    <paginationClause>
)
SELECT /* las mismas columnas del SELECT actual, incluida r.Name AS RoleName */
FROM page p
INNER JOIN Security.Users u ON u.Id = p.UserId
LEFT JOIN Security.UserCompanies uc ON uc.UserId = u.Id AND uc.CompanyId = p.CompanyId AND uc.GcRecord = 0
/* + el resto de los JOIN actuales, incluido el lastLogin */
ORDER BY c.Name, u.FirstName, u.LastName, u.Email;
```

`<paginationClause>` se resuelve con el mismo branching de `GetPersonsPagedQueryHandler`:

```csharp
private static string GetPaginationClause(IDbConnection connection)
    => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
        ? "LIMIT @PageSize OFFSET @Offset"
        : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
```

El `COUNT` va en el mismo `QueryMultipleAsync`: `SELECT COUNT(*) FROM filtered`. Cuenta pares `(UserId, CompanyId)`, no filas de detalle.

**Nota de portabilidad:** `'%' + @Search + '%'` es sintaxis SQL Server y `||` es Postgres. El comodín se arma en C# (`Search = $"%{search}%"`) y el SQL usa `LIKE @Search` a secas, que es portable. Es la forma que se implementa.

### Repositorios — records y métodos nuevos

```csharp
public sealed record UserCompanyMembershipInfo(
    bool Exists,
    bool IsDefault,
    int TotalActiveCompanies);
```

Métodos agregados a `IUserAdminRepository` (creado en SPEC 27):

```csharp
Task<IReadOnlyList<Guid>> FilterExistingRoleIdsByNameAsync(
    IReadOnlyList<string> roleNames, Guid companyId, CancellationToken ct = default);

Task<UserCompanyMembershipInfo?> GetMembershipInfoAsync(
    Guid userId, Guid companyId, CancellationToken ct = default);

Task<UserEffectivePermissionsDto?> GetEffectivePermissionsAsync(
    Guid userId, Guid companyId, CancellationToken ct = default);

Task<IReadOnlyList<Guid>> FilterUsersWithMembershipAsync(
    IReadOnlyList<Guid> userIds, Guid companyId, CancellationToken ct = default);

Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken ct = default);
```

`GetEffectivePermissionsAsync` devuelve `null` solo si el usuario no existe. Si existe sin roles, devuelve el DTO con `RoleIds`/`RoleNames` vacíos y la grilla completa en `false`.

### Reescritura de `ReplaceUserRolesCommandHandler`

El contrato externo no cambia (`{ roles: ["Admin", "Contador"] }` → `Response<UserWithRolesDto>`). Lo que cambia es el interior:

| Antes | Después |
|---|---|
| `userManager.GetRolesAsync(user)` | `SELECT` de `UserRoleCompanies` por `(userId, companyId)` |
| `roleManager.Roles` filtrado por `NormalizedName` | `FilterExistingRoleIdsByNameAsync(roleNames, companyId)` |
| `userManager.AddToRolesAsync` | `InsertAsync`/reactivación de `UserRoleCompany` |
| `userManager.RemoveFromRolesAsync` | soft-delete de `UserRoleCompany` |
| sin invalidación de caché | `IPermissionService.InvalidateUserCacheAsync(companyId, userId)` |

Guarda nueva: `companyId == Guid.Empty` → `TENANT_REQUIRED`. Hoy el handler no mira el tenant en absoluto.

---

## Implementation plan

Pre-requisito: **SPEC 27 implementada**. F0 de la 27 (rollback de `TransactionBehavior`) y `IUserAdminRepository` tienen que existir antes de arrancar acá.

### F1 — Ampliar `IUserAdminRepository`

1. Agregar a la interfaz los 5 métodos de la sección Data model más el record `UserCompanyMembershipInfo`.
2. Implementar en `src/3.Persistence/Repositories/Security/UserAdminRepository.cs`:
   - `CompanyExistsAsync`: `SELECT COUNT(1) FROM Common.Companies WHERE Id = @CompanyId AND GcRecord = 0`.
   - `FilterExistingRoleIdsByNameAsync`: `SELECT r.Id FROM Security.Roles r INNER JOIN Security.RoleCompanies rc ON rc.RoleId = r.Id AND rc.CompanyId = @CompanyId AND rc.GcRecord = 0 WHERE UPPER(r.Name) IN @RoleNames AND r.GcRecord = 0`. Los nombres llegan normalizados a upper-invariant desde el handler.
   - `GetMembershipInfoAsync`: una query con dos subselects — `EXISTS` de la fila activa, su `IsDefault`, y el `COUNT` de empresas activas del usuario.
   - `FilterUsersWithMembershipAsync`: `SELECT DISTINCT UserId FROM Security.UserCompanies WHERE UserId IN @UserIds AND CompanyId = @CompanyId AND GcRecord = 0`.
   - `GetEffectivePermissionsAsync`: ver F5.
3. `dotnet build -c Release` → 0 errores. Nada cambia en runtime.

### F2 — Corrección de H1: los roles pasan a `UserRoleCompanies`

1. Agregar un helper privado reusable con la semántica **upsert-or-reactivate** para `UserRoleCompany`: si existe fila con `GcRecord != 0` para `(userId, roleId, companyId)`, la reactiva; si no existe, inserta. Nunca `InsertAsync` a ciegas — el índice único `(UserId, RoleId, CompanyId)` no filtra por `GcRecord`.
2. Reescribir `ReplaceUserRolesCommandHandler` según la tabla de la sección Data model. Dependencias nuevas: `IUserAdminRepository`, `IUnitOfWork`, `ICurrentUserService`, `IPermissionService`. Se van `RoleManager<ApplicationRole>` y las llamadas de rol de `UserManager`.
3. Guarda nueva al principio: `companyId == Guid.Empty` → `TENANT_REQUIRED`.
4. `UserWithRolesDto.Roles` se arma desde los nombres de rol resueltos de `UserRoleCompanies`, no de `userManager.GetRolesAsync`.
5. Reescribir el SQL de `GetUsersWithRolesQueryHandler`:

   ```sql
   SELECT u.Id, u.UserName, u.Email, u.IsActive, r.Name AS RoleName
   FROM Security.Users u
   INNER JOIN Security.UserCompanies uc
       ON uc.UserId = u.Id AND uc.CompanyId = @CompanyId AND uc.GcRecord = 0
   LEFT JOIN Security.UserRoleCompanies urc
       ON urc.UserId = u.Id AND urc.CompanyId = @CompanyId AND urc.GcRecord = 0
   LEFT JOIN Security.Roles r ON r.Id = urc.RoleId AND r.GcRecord = 0
   WHERE u.GcRecord = 0
   ORDER BY u.UserName, r.Name;
   ```

   `INNER JOIN` sobre `UserCompanies` porque ahora el endpoint es tenant-scoped: solo usuarios de la empresa del token. `GetUsersWithRolesQueryHandler` gana la dependencia de `ICurrentUserService` y la guarda `TENANT_REQUIRED`.
6. El agrupado en C# no cambia.
7. `dotnet build -c Release` → 0 errores. Smoke: `PUT /Users/{id}/roles` con un rol nuevo → verificar en DB que aparece la fila en `UserRoleCompanies` (no en `UserRoles`) y que el siguiente login trae el claim del rol.

### F3 — Item 18: `AddUserCompany`

1. Crear `AddUserCompanyRequestDto`, `AddUserCompanyResultDto`, `AddUserCompanyCommand`.
2. `AddUserCompanyCommandValidator`: `UserId` `NotEmpty()`; `CompanyId` `NotEmpty()`; `RoleIds` `NotNull().NotEmpty()`, tope `<= 20` (`TOO_MANY_ROLES`), sin duplicados (`DUPLICATE_ROLE`), cada `Guid` `NotEmpty()`.
3. `AddUserCompanyCommandHandler` — dependencias: `IUserAdminRepository`, `IUnitOfWork`, `ICurrentUserService`, `IPermissionService`. Aplica las 6 guardas de la sección Data model.
   - **El tenant de validación de roles es el `CompanyId` del body**, no el del token: el endpoint es SuperAdmin y su propósito es operar sobre otra empresa.
   - Membresía y roles se escriben con la semántica upsert-or-reactivate de F2.
   - Invalida caché de `(companyId del body, userId)` al final.
4. `dotnet build -c Release` → 0 errores.

### F4 — Item 18: `RemoveUserCompany`

1. Crear `RemoveUserCompanyCommand` + validator (`UserId` y `CompanyId` `NotEmpty()`).
2. `RemoveUserCompanyCommandHandler`: las 3 guardas de la sección Data model, en ese orden. Luego soft-delete de la fila `UserCompany` y de todas las `UserRoleCompany` de `(userId, companyId)`. Invalida caché.
3. El soft-delete usa `MarkAsDeleted()` de `BaseAuditableEntity` (setea `GcRecord` al stamp `yyyyMMdd`), no `GcRecord = 1` a mano.
4. `dotnet build -c Release` → 0 errores. Smoke: dar de baja una membresía y volver a agregarla con el `POST` → debe reactivar, no reventar por índice único.

### F5 — Item 19: permisos efectivos

1. Crear `UserEffectivePermissionsDto`.
2. Implementar `IUserAdminRepository.GetEffectivePermissionsAsync` con una sola query:

   ```sql
   SELECT
       sm.Id AS ModuleId, sm.Name AS ModuleName,
       so.Id AS SystemOptionId, so.Name, so.Route,
       so.CanRead AS SupportsCanRead, /* ... los 7 Supports ... */
       MAX(CAST(rso.CanRead AS int))    AS GrantedCanRead,
       /* ... los 7 Granted con MAX(CAST(... AS int)) ... */
   FROM Security.SystemModules sm
   LEFT JOIN Security.SystemOptions so ON so.SystemModuleId = sm.Id AND so.GcRecord = 0
   LEFT JOIN Security.UserRoleCompanies urc
       ON urc.UserId = @UserId AND urc.CompanyId = @CompanyId AND urc.GcRecord = 0
   LEFT JOIN Security.RoleSystemOptions rso
       ON rso.RoleId = urc.RoleId AND rso.SystemOptionId = so.Id
      AND rso.CompanyId = @CompanyId AND rso.GcRecord = 0
   WHERE sm.GcRecord = 0
   GROUP BY sm.Id, sm.Name, so.Id, so.Name, so.Route, /* + los 7 Supports */
   ORDER BY sm.Name, so.Name;
   ```

   Más una segunda query en el mismo `QueryMultipleAsync` para `RoleIds` + `RoleNames`:

   ```sql
   SELECT r.Id, r.Name
   FROM Security.UserRoleCompanies urc
   INNER JOIN Security.Roles r ON r.Id = urc.RoleId AND r.GcRecord = 0
   WHERE urc.UserId = @UserId AND urc.CompanyId = @CompanyId AND urc.GcRecord = 0
   ORDER BY r.Name;
   ```

   `MAX(CAST(... AS int))` es el OR entre roles. `CAST` porque `MAX` no acepta `bit` en SQL Server. En el mapeo, `!= 0` → `bool`.
3. Crear `GetUserEffectivePermissionsQuery` (`IRequest<Response<UserEffectivePermissionsDto>>`) y su handler: guarda `TENANT_REQUIRED`, guarda `USER_NOT_FOUND` vía `GetAdminSnapshotAsync` (incluye el chequeo de `HasMembership`), y luego el repositorio.
4. `dotnet build -c Release` → 0 errores.

### F6 — Item 20: bulk de roles

1. Crear `BulkUpdateUserRolesRequestDto`, `BulkRoleOutcome`, `BulkUpdateUserRoleItemDto`, `BulkUpdateUserRolesResultDto`, `BulkUpdateUserRolesCommand`.
2. `BulkUpdateUserRolesCommandValidator` con las 6 reglas de la sección Data model.
3. `BulkUpdateUserRolesCommandHandler`:
   1. `companyId == Guid.Empty` → `TENANT_REQUIRED`.
   2. `FilterExistingRoleIdsAsync` sobre la unión de `AddRoleIds` y `RemoveRoleIds` → si el count no coincide, `ROLE_NOT_FOUND` y no escribe nada.
   3. `FilterUsersWithMembershipAsync(UserIds, companyId)` → una query, no N.
   4. Un `SELECT` de las filas activas de `UserRoleCompanies` para todos los `UserIds` válidos → una query, no N.
   5. Recorre en memoria: calcula el delta por usuario, arma los insert/reactivación/soft-delete, y el `BulkUpdateUserRoleItemDto` con los counts reales.
   6. Un solo `SaveAsync`.
   7. Invalida caché de los usuarios con `Outcome = Updated`, cada llamada en su propio `try/catch` con log warning.
4. `dotnet build -c Release` → 0 errores.

### F7 — Item 21: reporte paginado

1. Extraer de `UserManagementReportQueryHelper` los helpers privados compartidos (`NormalizeDateRange`, `NormalizeGuid`, `BuildFullName`, y el agrupado en C# como `MapRows`) para que los usen los dos métodos públicos.
2. Agregar `ReadPagedAsync` con la firma de la sección Data model y el SQL con CTE. El comodín del `LIKE` se arma en C# (`$"%{search}%"`) para no depender de `+` ni `||`.
3. `QueryMultipleAsync`: primero el `SELECT` de detalle, después `SELECT COUNT(*) FROM filtered`.
4. Modificar `GetMyCompanyUserReportQuery` con los 4 parámetros nuevos y el retorno `Response<PagedResult<UserManagementReportDto>>`.
5. `GetMyCompanyUserReportQueryHandler`: clamps (`PageNumber >= 1`, `PageSize` en `[1, 50]`, default 10), llama `ReadPagedAsync`, arma el `PagedResult<T>` con `TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize)`.
6. **Sustituir el `throw new UnauthorizedAccessException`** del handler actual por `Response<T>.Error("TENANT_REQUIRED", …)`, alineándolo con la convención de `CLAUDE.md`. Es un cambio de comportamiento del endpoint: pasa de 401 a 400.
7. `GetSystemWideUserReportQueryHandler` **no se toca**. Verificar que sigue compilando contra `ReadAsync`.
8. `dotnet build -c Release` → 0 errores. Smoke: `GET /Users/reports/my-company?pageNumber=1&pageSize=5&search=juan&isActive=false` → devuelve solo inactivos que matcheen, con `totalCount` de pares distintos.

### F8 — Controller

1. `UsersController` — endpoints nuevos y modificados:
   - `POST /{userId:guid}/companies` — `[Authorize(Roles = "SuperAdmin")] [SkipDynamicAuthorization]`.
   - `DELETE /{userId:guid}/companies/{companyId:guid}` — idem.
   - `GET /{userId:guid}/companies` — **agregar** los mismos dos atributos al endpoint existente.
   - `GET /{userId:guid}/effective-permissions` — sin atributos extra; hereda `[PermissionResource("Users")]`, default `CanRead`.
   - `PUT /roles/bulk` — sin atributos extra; default `CanUpdate`. **Declarado antes** de `PUT /{userId:guid}/roles`.
   - `GET /reports/my-company` — agregar los 4 query params y cambiar el tipo de retorno a `Response<PagedResult<UserManagementReportDto>>`.
2. Mapeo de errores a HTTP:

   | Código | HTTP |
   |---|---|
   | `TENANT_REQUIRED` | 400 |
   | `USER_NOT_FOUND` | 404 |
   | `COMPANY_NOT_FOUND` | 404 |
   | `ROLE_NOT_FOUND` | 404 |
   | `MEMBERSHIP_NOT_FOUND` | 404 |
   | `CANNOT_REMOVE_DEFAULT_COMPANY` | 409 |
   | `CANNOT_REMOVE_LAST_COMPANY` | 409 |
   | resto sin éxito | 400 |

3. Actualizar los `[ProducesResponseType]` y el XML doc de los endpoints modificados.
4. `dotnet build -c Release` → 0 errores.

### F9 — Tests (~46 casos)

- `ReplaceUserRolesCommandHandlerTests` — **reescribir los existentes**. 7 casos: agrega rol faltante; quita rol sobrante; reactiva fila soft-deleted en vez de insertar; nombre de rol de otro tenant → `ROLE_NOT_FOUND`; tenant vacío → `TENANT_REQUIRED`; usuario inexistente → `USER_NOT_FOUND`; invalida caché exactamente una vez.
- `GetUsersWithRolesQueryHandlerTests` — 3: solo devuelve usuarios de la empresa del token; usuario sin roles aparece con `Roles` vacío; tenant vacío → `TENANT_REQUIRED`.
- `AddUserCompanyCommandHandlerTests` — 7: membresía nueva con `IsDefault = true`; membresía nueva con empresa previa → `IsDefault = false`; membresía existente activa → reemplaza roles; membresía soft-deleted → reactiva; usuario inexistente → `USER_NOT_FOUND`; empresa inexistente → `COMPANY_NOT_FOUND`; rol de otra empresa → `ROLE_NOT_FOUND`.
- `AddUserCompanyCommandValidatorTests` — 3: `RoleIds` vacío; 21 roles; duplicados.
- `RemoveUserCompanyCommandHandlerTests` — 5: baja normal soft-deletea membresía y roles; membresía inexistente → `MEMBERSHIP_NOT_FOUND`; última empresa → `CANNOT_REMOVE_LAST_COMPANY`; empresa default con otras disponibles → `CANNOT_REMOVE_DEFAULT_COMPANY`; invalida caché.
- `GetUserEffectivePermissionsQueryHandlerTests` — 4: usuario con 2 roles → `Granted` es el OR de ambos; usuario sin roles → grilla completa en `false` con `RoleIds` vacío; tenant vacío → `TENANT_REQUIRED`; usuario sin membresía → `USER_NOT_FOUND`.
- `BulkUpdateUserRolesCommandHandlerTests` — 7: agrega a 3 usuarios; quita a 2; usuario sin membresía → `UserNotFound` sin cortar el lote; usuario que ya tenía el set → `NoChange` sin escribir; rol de otro tenant → `ROLE_NOT_FOUND` sin escribir nada; reactivación de fila soft-deleted; invalida caché solo de los `Updated`.
- `BulkUpdateUserRolesCommandValidatorTests` — 6: `UserIds` vacío; 201 usuarios; usuarios duplicados; ambas listas de roles vacías → `BULK_NO_OP`; 41 roles combinados; `roleId` en ambas listas → `ROLE_IN_BOTH_LISTS`.
- `GetMyCompanyUserReportQueryHandlerTests` — 6: paginación devuelve `PagedResult` con `TotalPages` correcto; `pageNumber = 0` clampea a 1; `pageSize = 200` clampea a 50; `search` filtra por email y por nombre completo; `isActive = false` devuelve inactivos; tenant vacío → `TENANT_REQUIRED` (400, ya no excepción).
- Verificar que `GetSystemWideUserReportQueryHandlerTests` existentes siguen pasando sin cambios.

`dotnet test --filter "FullyQualifiedName~UserCompany|FullyQualifiedName~UserRoles|FullyQualifiedName~EffectivePermissions|FullyQualifiedName~MyCompanyUserReport"` → 0 fallidos.

### F10 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → gate de 90% en `JOIN.Application` pasa.
3. **Sin migración EF.** Verificar que `dotnet ef migrations list` no muestra ninguna pendiente nueva.
4. Smoke manual del ciclo completo de membresía: `POST /Users/{id}/companies` → `GET /Users/{id}/companies` muestra la nueva → `PUT /Users/{id}/default-company/{companyId}` la marca default → `DELETE` de la otra → 200 → `DELETE` de la default → 409.
5. Smoke manual de roles: `PUT /roles/bulk` sobre 3 usuarios → `GET /Users/{id}/effective-permissions` de uno de ellos refleja los flags nuevos → login de ese usuario trae los claims de rol correctos.
6. Smoke manual del reporte: recorrer 2 páginas y verificar que ningún usuario aparece partido entre páginas ni duplicado.
7. `CURL_REQUESTS.md` — agregar los 4 endpoints nuevos y actualizar los 3 modificados (`GET /companies` ahora SuperAdmin, `GET /reports/my-company` con params, `PUT /{userId}/roles` sin cambio de contrato pero con comportamiento nuevo).

---

## Acceptance criteria

### F1 — `IUserAdminRepository` ampliado

- [ ] Los 5 métodos nuevos existen con la firma de la sección Data model, más el record `UserCompanyMembershipInfo`.
- [ ] `FilterExistingRoleIdsByNameAsync` no devuelve roles que no estén en `Security.RoleCompanies` para el `companyId` pasado.
- [ ] `GetMembershipInfoAsync` devuelve `Exists`, `IsDefault` y `TotalActiveCompanies` en una sola query.
- [ ] `FilterUsersWithMembershipAsync` resuelve N usuarios en una query, no N queries.
- [ ] Todas las queries filtran `GcRecord = 0` y ninguna filtra por `IsActive`.

### F2 — Corrección de H1

- [ ] `PUT /api/v1/Users/{userId}/roles` con un rol nuevo escribe una fila en `Security.UserRoleCompanies` con el `CompanyId` del token.
- [ ] El mismo request **no** escribe nada en `Security.UserRoles`.
- [ ] Un rol quitado queda soft-deleted (`GcRecord != 0`) en `UserRoleCompanies`, no borrado.
- [ ] Re-agregar un rol previamente quitado **reactiva** la fila (`GcRecord = 0`) y no lanza violación de índice único.
- [ ] El handler llama `IPermissionService.InvalidateUserCacheAsync(companyId, userId)` exactamente una vez por request exitoso.
- [ ] Un nombre de rol que existe en otro tenant pero no en el del token → 404 `ROLE_NOT_FOUND`.
- [ ] `currentUserService.CompanyId == Guid.Empty` → 400 `TENANT_REQUIRED`.
- [ ] El contrato externo no cambia: sigue aceptando `{ roles: ["Nombre"] }` y devolviendo `Response<UserWithRolesDto>`.
- [ ] `UserWithRolesDto.Roles` se arma desde `UserRoleCompanies`, no desde `userManager.GetRolesAsync`.
- [ ] Después de un `PUT /roles`, el siguiente login del usuario trae los claims de rol actualizados.
- [ ] `GET /api/v1/Users` devuelve **solo** usuarios con membresía activa en la empresa del token.
- [ ] `GET /api/v1/Users` muestra los roles de `UserRoleCompanies` del tenant, no los de Identity.
- [ ] `GET /api/v1/Users` con tenant vacío → 400 `TENANT_REQUIRED`.
- [ ] Un usuario sin roles aparece en la lista con `Roles` vacío, no ausente.

### F3 — Item 18: `POST /companies`

- [ ] `POST /api/v1/Users/{userId}/companies` con `{companyId, roleIds[]}` sobre un usuario sin esa membresía → 200 con `{userId, companyId, isDefault, roleIdsAssigned[]}`.
- [ ] `isDefault = true` cuando el usuario no tenía ninguna otra empresa activa; `false` cuando sí.
- [ ] Se crea una fila `UserRoleCompany` por cada `roleId`, con el `CompanyId` **del body**.
- [ ] Membresía ya existente y activa → 200, reemplaza el set de roles, `isDefault` mantiene su valor actual.
- [ ] Membresía existente con `GcRecord != 0` → la reactiva; no inserta una fila nueva ni lanza violación de índice único.
- [ ] `userId` inexistente o soft-deleted → 404 `USER_NOT_FOUND`.
- [ ] `companyId` inexistente o soft-deleted → 404 `COMPANY_NOT_FOUND`.
- [ ] Un `roleId` que no está en `RoleCompanies` para el `companyId` del body → 404 `ROLE_NOT_FOUND`, sin escribir nada.
- [ ] Invalida la caché de `(companyId del body, userId)`.
- [ ] Validator: `roleIds` vacío, 21 roles (`TOO_MANY_ROLES`) y duplicados (`DUPLICATE_ROLE`) fallan con 400.
- [ ] Un usuario sin rol SuperAdmin recibe 403.

### F4 — Item 18: `DELETE /companies/{companyId}`

- [ ] `DELETE /api/v1/Users/{userId}/companies/{companyId}` sobre una membresía no default con otras disponibles → 200. La fila `UserCompany` y todas las `UserRoleCompany` de ese par quedan soft-deleted.
- [ ] El soft-delete usa `MarkAsDeleted()` y deja `GcRecord` con el stamp `yyyyMMdd`, no `1`.
- [ ] Membresía inexistente → 404 `MEMBERSHIP_NOT_FOUND`.
- [ ] Usuario con una sola empresa → 409 `CANNOT_REMOVE_LAST_COMPANY`, sin mutar nada.
- [ ] Empresa default con otras empresas disponibles → 409 `CANNOT_REMOVE_DEFAULT_COMPANY`, sin mutar nada.
- [ ] El orden de guardas se respeta: con una sola empresa (que es default) el error es `CANNOT_REMOVE_LAST_COMPANY`, no `CANNOT_REMOVE_DEFAULT_COMPANY`.
- [ ] Invalida la caché de `(companyId, userId)`.
- [ ] Después de un `DELETE`, un `POST` de la misma membresía la reactiva y devuelve 200.
- [ ] Un usuario sin rol SuperAdmin recibe 403.

### F5 — Item 19: permisos efectivos

- [ ] `GET /api/v1/Users/{userId}/effective-permissions` → 200 con `{userId, companyId, roleIds[], roleNames[], modules[]}`.
- [ ] **La ruta no expone `?companyId=`**; el `companyId` de la respuesta es el del JWT.
- [ ] Un usuario con 2 roles donde uno concede `CanRead` y el otro `CanUpdate` sobre la misma opción devuelve ambos en `true` (OR, no AND ni "último gana").
- [ ] Un usuario sin roles → 200 con `roleIds` y `roleNames` vacíos y **todos** los `granted` en `false`.
- [ ] La respuesta incluye todos los `SystemOptions` activos agrupados por módulo, no solo los concedidos.
- [ ] Cada opción trae `supports` (defaults de `SystemOptions`) y `granted` (efectivo del usuario), con los 7 flags cada uno.
- [ ] `roleIds` y `roleNames` son listas paralelas ordenadas por nombre de rol.
- [ ] `currentUserService.CompanyId == Guid.Empty` → 400 `TENANT_REQUIRED`.
- [ ] `userId` sin membresía en el tenant del token → 404 `USER_NOT_FOUND`.
- [ ] El handler **no** lee `IPermissionService` ni `IMemoryCache`: dos requests seguidos con un cambio de permisos en el medio devuelven valores distintos.
- [ ] `RoleSystemOptionMatrixModuleDto` y `RoleSystemOptionMatrixOptionDto` de SPEC 25 no se modifican.

### F6 — Item 20: bulk de roles

- [ ] `PUT /api/v1/Users/roles/bulk` con `{userIds[], addRoleIds[], removeRoleIds[]}` → 200 con `{items[], usersUpdated, usersSkipped}`.
- [ ] Semántica delta: los roles que el usuario ya tenía y no están en `removeRoleIds` **no se tocan**.
- [ ] `rolesAdded` y `rolesRemoved` de cada ítem cuentan lo efectivamente escrito, no lo pedido.
- [ ] Un usuario que ya tenía exactamente ese set → `outcome = "NoChange"` y cero escrituras para ese usuario.
- [ ] Un `userId` sin membresía en el tenant → `outcome = "UserNotFound"` y **el resto del lote se procesa**.
- [ ] Un `roleId` que no pertenece al tenant → 404 `ROLE_NOT_FOUND` y **cero escrituras** en todo el lote.
- [ ] Reactiva filas soft-deleted de `UserRoleCompany` en vez de insertar duplicados.
- [ ] Invalida la caché solo de los usuarios con `outcome = "Updated"`.
- [ ] Un fallo de invalidación de caché no revierte las escrituras ni devuelve error.
- [ ] Todas las escrituras del lote pasan por un solo `SaveAsync`.
- [ ] La resolución de membresías y de roles existentes usa una query cada una, no una por usuario.
- [ ] Validator: `userIds` vacío; 201 usuarios (`BULK_TOO_MANY_USERS`); usuarios duplicados (`BULK_DUPLICATE_USER`); ambas listas de roles vacías (`BULK_NO_OP`); 41 roles combinados (`BULK_TOO_MANY_ROLES`); un `roleId` en ambas listas (`ROLE_IN_BOTH_LISTS`).

### F7 — Item 21: reporte paginado

- [ ] `GET /api/v1/Users/reports/my-company?pageNumber=1&pageSize=5` → 200 con `{items[], pageNumber, pageSize, totalCount, totalPages}`.
- [ ] `totalCount` cuenta pares `(UserId, CompanyId)` distintos que pasan los filtros, no filas de `user × company × role`.
- [ ] Recorrer todas las páginas devuelve cada par `(UserId, CompanyId)` exactamente una vez: ningún usuario partido entre páginas ni duplicado.
- [ ] Un usuario con 3 roles aparece con sus 3 roles en `Roles`, en la página que le corresponde.
- [ ] `search` matchea por `Email` y por nombre completo (`FirstName + ' ' + LastName`), case-insensitive, coincidencia parcial.
- [ ] El comodín del `LIKE` se arma en C#; el SQL usa `LIKE @Search` sin `+` ni `||`.
- [ ] `isActive = false` devuelve usuarios inactivos (el filtro global de EF no aplica porque el reporte va por Dapper).
- [ ] `isActive` ausente devuelve activos e inactivos.
- [ ] `pageNumber = 0` clampea a 1; `pageSize = 200` clampea a 50; `pageSize` ausente usa 10.
- [ ] `UserManagementReportDto` no cambia de forma.
- [ ] Tenant vacío → 400 `TENANT_REQUIRED`. **Ya no lanza `UnauthorizedAccessException` (401).**
- [ ] `GET /api/v1/Users/reports/system` no cambia de ruta, parámetros, shape de respuesta ni comportamiento.
- [ ] `UserManagementReportQueryHelper.ReadAsync` conserva su firma y sigue siendo la que usa `/reports/system`.
- [ ] La cláusula de paginación branchea entre `LIMIT/OFFSET` y `OFFSET … FETCH NEXT` según el proveedor.

### F8 — Controller

- [ ] `POST /{userId}/companies`, `DELETE /{userId}/companies/{companyId}` y `GET /{userId}/companies` llevan `[Authorize(Roles = "SuperAdmin")]` + `[SkipDynamicAuthorization]`.
- [ ] `GET /{userId}/effective-permissions` exige `CanRead` y `PUT /roles/bulk` exige `CanUpdate`, por el default de verbo HTTP, sin `[RequirePermission]`.
- [ ] `PUT /roles/bulk` está declarado antes de `PUT /{userId:guid}/roles` y resuelve correctamente: `PUT /api/v1/Users/roles/bulk` no se interpreta como `userId = "roles"`.
- [ ] `PUT /api/v1/Users/{guid}/roles` sigue resolviendo al endpoint single.
- [ ] El mapeo de códigos a HTTP de la tabla F8 está implementado tal cual.
- [ ] `PUT /{userId}/default-company/{companyId}`, `POST /login`, `POST /register`, `POST /refresh`, `POST /logout`, `GET /sidebar`, `POST /cleancache` y `GET /reports/system` no cambian de ruta, firma ni comportamiento.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test` → 0 fallidos, ≥ 46 tests nuevos o reescritos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate de CI).
- [ ] **Cero migraciones EF**: `dotnet ef migrations list` no muestra ninguna nueva.
- [ ] Ninguna escritura de esta spec toca `Security.UserRoles`.
- [ ] Toda reactivación de fila soft-deleted respeta los índices únicos `(UserId, CompanyId)` y `(UserId, RoleId, CompanyId)`.
- [ ] `CURL_REQUESTS.md` incluye los 4 endpoints nuevos y los 3 modificados.
- [ ] Ningún handler nuevo devuelve entidades de dominio; todos devuelven `Response<T>` con DTO.
- [ ] Ningún handler nuevo lanza excepción para fallas de negocio esperadas.

---

## Decisiones

### Corrección de H1 (roles en la tabla equivocada)

- **Sí:** la SPEC 28 arregla `PUT /Users/{userId}/roles` para que escriba `UserRoleCompany`. `PermissionService.cs:57` y `LoginCommandHandler.cs:64` leen esa tabla; la de Identity no participa de ninguna decisión de autorización en runtime. Hoy el endpoint le miente al operador: dice que asignó un rol y no cambia ningún permiso.
- **No:** dejarlo roto y migrar el front al bulk del item 20. Deja un endpoint activo que no hace lo que promete.
- **No:** arreglarlo en su propia spec. Es el hermano single del item 20; si aterrizan por separado, durante la ventana entre ambas hay dos endpoints de rol escribiendo en tablas distintas.
- **Sí:** el contrato de `PUT /Users/{userId}/roles` sigue aceptando **nombres** de rol. Cambiar a `Guid` rompería al front sin necesidad; los nombres se resuelven contra los roles del tenant.
- **No:** unificar el contrato del single y el bulk en `Guid`. Se acepta la fricción de tener uno por nombre y otro por ID a cambio de no romper lo que ya anda.
- **Sí:** `GET /Users` pasa a leer `UserRoleCompanies` y a filtrar por tenant. Si se arregla la escritura y no la lectura, la lista sigue mostrando los roles viejos de Identity y el operador ve algo distinto de lo que guardó.
- **No:** borrar o migrar `Security.UserRoles`. Queda sin escritores en producción pero con las filas del seed. Limpiarla exige migración y decidir qué pasa con `userManager.GetRolesAsync`. Spec aparte.
- **No:** tocar `DatabaseSeeder`, que sigue escribiendo la tabla de Identity. Fuera de alcance.

### Item 18 (membresía multi-empresa)

- **Sí:** los tres endpoints de `/companies` exigen SuperAdmin. Administrar a qué empresas pertenece un usuario es cross-tenant por naturaleza; el `GET` existente ya exponía empresas de otros tenants sin ningún filtro (H2), y acotarlo a SuperAdmin cierra el leak sin volverlo inútil.
- **No:** filtrar el `GET` al tenant del token. Devolvería 0 o 1 fila y el endpoint pierde sentido.
- **No:** dejar el `GET` como estaba y documentar el leak.
- **No:** una variante tenant-scoped del `POST` donde el `companyId` del body deba coincidir con el del token. Se reduciría al caso `MembershipAdded` que ya cubre `POST /Users/invite` de la SPEC 27.
- **Sí:** el `POST` valida los `roleIds` contra el `CompanyId` **del body**, no el del token. Es un endpoint SuperAdmin que opera sobre otra empresa; validar contra el token asignaría roles inexistentes en la empresa destino.
- **Sí:** el `POST` es idempotente. Si la membresía ya existe, reemplaza el set de roles y devuelve 200. Un 409 obligaría al front a consultar antes de escribir.
- **Sí:** toda re-alta **reactiva** la fila soft-deleted en vez de insertar. Los índices únicos `(UserId, CompanyId)` y `(UserId, RoleId, CompanyId)` **no filtran por `GcRecord`**, así que un insert a ciegas revienta con violación de índice único apenas alguien re-agrega algo dado de baja.
- **Sí:** `DELETE` sobre la empresa por defecto → `CANNOT_REMOVE_DEFAULT_COMPANY`. Hay que mover el default primero con el `PUT /default-company/{companyId}` existente.
- **No:** auto-promover otra empresa a default en el `DELETE`. Elegir por el operador cuál pasa a ser la principal es una decisión de negocio escondida en un borrado.
- **Sí:** `DELETE` de la última empresa → `CANNOT_REMOVE_LAST_COMPANY`. Un usuario sin ninguna empresa no puede loguearse y queda inaccesible salvo por SQL.
- **Sí:** el orden de guardas pone `CANNOT_REMOVE_LAST_COMPANY` antes que `CANNOT_REMOVE_DEFAULT_COMPANY`. Con una sola empresa, esa empresa es necesariamente la default, y el mensaje útil es el primero.

### Item 19 (permisos efectivos)

- **Sí:** `UserEffectivePermissionsDto` nuevo, con `RoleIds[]` y `RoleNames[]` explícitos.
- **No:** reusar `RoleSystemOptionMatrixDto` de SPEC 25 con `RoleName = "<Rol1> + N más"` (como planteaba la SPEC 26 para `my-permissions`). La unión de varios roles no es un rol; codificarla en un `string` obliga al front a parsear texto para saber cuántos roles hay.
- **Sí:** reusar `RoleSystemOptionMatrixModuleDto` y `RoleSystemOptionMatrixOptionDto` sin modificarlos. La grilla se renderiza igual que la de rol; solo cambia la cabecera.
- **Sí:** `Granted` es el OR de los flags de todos los roles. Es la semántica que ya aplica `PermissionService` al resolver permisos en runtime; el panel tiene que mostrar lo mismo que la autorización evalúa.
- **Sí:** devuelve la grilla completa aunque el usuario no tenga roles. Mismo criterio que `GET /RoleSystemOptions/matrix` de SPEC 25: la UI pinta todos los checkboxes y marca los concedidos.
- **Sí:** query fresca por Dapper, sin leer la caché de `PermissionService`. El panel muestra el estado real de la DB; un snapshot de hasta 30 minutos haría que el operador vea permisos que ya cambió.
- **No:** exponer `?companyId=`. SPEC 23 fijó que el tenant sale del token. El parámetro del backlog original se descarta; si hace falta la versión cross-tenant para SuperAdmin, va en spec aparte.

### Item 20 (bulk de roles)

- **Sí:** `addRoleIds[]` / `removeRoleIds[]` con `Guid`, renombrados respecto del backlog (`addRoles`/`removeRoles`). Los nombres de rol no son únicos entre tenants.
- **Sí:** semántica **delta**, no replace. Es lo que pide la UI de "seleccionar 20 filas y agregarles un rol": no se conoce ni interesa el set completo de cada usuario.
- **No:** semántica replace en el bulk. El replace por usuario ya lo cubre `PUT /Users/{userId}/roles`.
- **Sí:** un `userId` sin membresía devuelve `UserNotFound` en su ítem y **no** corta el lote. Una selección de 50 filas donde una quedó obsoleta no debería fallar entera.
- **Sí:** un `roleId` inválido **sí** corta el lote entero, antes de escribir. Es un error del operador o del front, no del dato; aplicarlo parcialmente dejaría un estado a medias imposible de explicar.
- **Sí:** resultado por usuario `{ userId, outcome, rolesAdded, rolesRemoved }` más los agregados `usersUpdated` / `usersSkipped`. La UI necesita mostrar "18 actualizados, 2 omitidos" sin recorrer el array.
- **Sí:** topes de 200 usuarios y 40 roles combinados. Acotan el payload y el tiempo de la transacción sin estorbar el caso real (una página de tabla son 50 filas).
- **Sí:** membresías y roles existentes se resuelven con una query cada una, no una por usuario. Con 200 usuarios, el patrón N+1 serían 400 round-trips.
- **Sí:** un solo `SaveAsync` para todo el lote.

### Item 21 (reporte paginado)

- **Sí:** paginar sobre pares `(UserId, CompanyId)` distintos con una CTE. El SQL devuelve una fila por `user × company × role`; un `OFFSET` sobre esas filas parte usuarios al medio y hace que un usuario con 3 roles aparezca en dos páginas.
- **No:** cambiar el contrato a una fila por usuario con las empresas anidadas. Sería más limpio pero rompe `UserManagementReportDto` y obliga a rehacer la tabla del front.
- **No:** paginar en memoria después de traer todo. Es exactamente lo que el item 21 viene a resolver.
- **Sí:** dos métodos en el helper (`ReadAsync` y `ReadPagedAsync`) en vez de uno con parámetros opcionales. El paginado devuelve `TotalCount` y el otro no; unificar ensucia el retorno de ambos.
- **Sí:** `GET /reports/system` no cambia. Sigue llamando `ReadAsync` con su firma actual.
- **No:** paginar también `/reports/system`. No lo pide el backlog y cambiaría el contrato de un endpoint SuperAdmin en uso.
- **Sí:** el comodín del `LIKE` se arma en C# (`$"%{search}%"`) y el SQL usa `LIKE @Search`. `'%' + @Search + '%'` es SQL Server; `||` es Postgres. Armarlo afuera es portable, que es lo que exige `CLAUDE.md`.
- **Sí:** `isActive` como filtro funciona porque el reporte va por Dapper. El filtro global de EF (`u.GcRecord == 0 && u.IsActive`) escondería los inactivos si esto fuera EF.
- **Sí:** el handler pasa de lanzar `UnauthorizedAccessException` a devolver `Response.Error("TENANT_REQUIRED")`. Es la convención de `CLAUDE.md` para fallas de negocio esperadas. Cambia el status de 401 a 400 en ese caso puntual.
- **Sí:** clamps de `PageSize` en `[1, 50]` con default 10, igual que `GetPersonsPagedQueryHandler`. Consistencia con los otros 34 handlers paginados del repo.

### Transversales

- **Sí:** la SPEC 28 depende de la 27. Los items 18 y 20 cortan con `Response.Error` después de escribir filas, así que sin el arreglo de `TransactionBehavior` (F0 de la 27) commitearían escrituras parciales. Además reusan `IUserAdminRepository`.
- **No:** duplicar el arreglo de `TransactionBehavior` y el repositorio para que la 28 sea autónoma. Dos copias del mismo arreglo divergen.
- **Sí:** cero migraciones EF. Todo el modelo necesario ya existe.
- **Sí:** las lecturas selectivas van por Dapper. `IGenericRepository<T>` no expone `GetAllAsync` con predicado — solo `GetAllAsync()`, `GetAsync(Guid)` y `GetAllWithPaginationAsync(int, int)` — así que filtrar por EF implicaría traer la tabla entera a memoria, que es el bug que ya tiene `SetDefaultCompanyCommandHandler`.
- **No:** arreglar `SetDefaultCompanyCommandHandler`. Carga todas las filas de `UserCompany` del sistema y filtra en C#. Bug de performance preexistente, ajeno a los items 18-21.
- **Sí:** `PUT /roles/bulk` se declara antes de `PUT /{userId:guid}/roles`. Con el orden inverso, ASP.NET intenta bindear `"roles"` como `Guid userId` y devuelve 400.
- **No:** cambiar la ruta del bulk a algo sin colisión (`PUT /bulk-roles`). La ruta del backlog es la que espera el front; el orden de declaración lo resuelve.

---

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Los índices únicos `(UserId, CompanyId)` y `(UserId, RoleId, CompanyId)` no filtran por `GcRecord` | Toda re-alta tiene que reactivar la fila soft-deleted, nunca insertar. Es la trampa más probable de la spec: un `InsertAsync` a ciegas pasa todos los tests con base limpia y revienta en producción la primera vez que alguien re-agrega algo dado de baja. Cubierto por criterios explícitos en F3, F4 y F6, y por el smoke de F10 paso 4. |
| `GET /Users/reports/my-company` cambia el shape de respuesta de array a `PagedResult<T>` | Breaking change del front, y es exactamente lo que pide el item 21. Coordinar el deploy con el front. `GET /reports/system` queda intacto como referencia del shape viejo. |
| `GET /Users` pasa a devolver solo usuarios del tenant | Hoy devuelve todos los usuarios del sistema sin filtro. Si el front usaba ese endpoint para algún listado global, se le vacía. Es la corrección de un leak, pero es observable. |
| `GET /Users/{userId}/companies` pasa a exigir SuperAdmin | Si el panel de un admin de empresa lo estaba llamando, empieza a recibir 403. Verificar con el front antes del deploy. |
| `Security.UserRoles` queda huérfana con las filas del seed | `userManager.GetRolesAsync` sigue devolviendo esos valores, que ya no representan nada. Ningún código de producción los consulta después de esta spec, pero un desarrollador que los use por costumbre obtiene datos falsos. Documentar. Limpieza en spec aparte. |
| El item 19 no lee caché y hace varios JOIN por request | Cada apertura del panel de permisos es una query completa sobre `SystemModules × SystemOptions × UserRoleCompanies × RoleSystemOptions`. Con el volumen de seed actual es despreciable. Si el catálogo crece por encima de ~1000 opciones, evaluar `FOR JSON PATH` o cachear con TTL corto. |
| La CTE de paginación duplica los filtros del SQL de detalle | El `WHERE` de fecha, rol y tenant vive en dos lugares dentro del mismo string. Si alguien agrega un filtro solo en uno, la página trae pares que el `COUNT` no cuenta. Mitigación: construir el bloque de `WHERE` una vez en C# e interpolarlo en ambos lugares. |
| El orden de declaración de rutas no está cubierto por unit tests | `PUT /roles/bulk` antes de `PUT /{userId}/roles` es un detalle de `UsersController` que ningún test de handler detecta. Queda en el smoke de F10 paso 5. Garantizarlo requiere test de integración (`tests/IntegrationTests`, SPEC 06), fuera del alcance de tests de esta spec. |
| `ReplaceUserRolesCommandHandlerTests` existentes hay que reescribirlos, no ampliarlos | Los mocks de `UserManager` / `RoleManager` desaparecen. Si se intenta conservarlos, los tests pasan probando código que ya no existe. |
| El bulk del item 20 con 200 usuarios × 40 roles genera hasta 8000 filas en una transacción | Escrituras vía `IUnitOfWork` fila por fila. Con los topes puestos es aceptable, pero si el caso real llega al máximo conviene medir. Si molesta, migrar a un `BulkUpsertAsync` Dapper como el de SPEC 25. |
| El `POST` del item 18 valida roles contra el `companyId` del body | Es deliberado (endpoint SuperAdmin cross-tenant), pero es la única escritura de todo el sistema donde el tenant no sale del token. Cualquier revisión de seguridad futura lo va a marcar como anomalía. Documentado en Decisiones. |
| Dependencia dura de la SPEC 27 | La 28 no se puede implementar antes. Si la 27 se demora, la 28 queda bloqueada. No hay mitigación razonable salvo duplicar el arreglo de `TransactionBehavior`, que es peor. |
| El filtro global `u.GcRecord == 0 && u.IsActive` sigue en pie | `GET /Users` (F2) usa Dapper, así que ve inactivos. Pero cualquier lectura EF de usuarios en el repo sigue ciega a ellos. Anotado en SPEC 27, sigue pendiente. |
| Cambio de 401 a 400 en `/reports/my-company` sin tenant | Observable para el front si distinguía ambos códigos. Alineación con `CLAUDE.md`. Documentar en `CURL_REQUESTS.md`. |

---

## Lo que NO entra en esta spec

- **Item 22** — `GET /api/v1/Audit/security`, la tabla `Security.AuditLogs` y el interceptor de diff. Va en la **SPEC 29**.
- **Items 15, 16 y 17** — invitación, cambio de estado y reseteo forzado. Van en la **SPEC 27**, que es pre-requisito de ésta.
- **Borrar o migrar `Security.UserRoles`** — queda sin escritores de producción pero con las filas del seed.
- **Cambios en `DatabaseSeeder`** — sigue escribiendo la tabla de Identity vía `AddToRoleAsync`.
- **Arreglar `SetDefaultCompanyCommandHandler`** — carga toda la tabla `UserCompany` en memoria. Bug preexistente.
- **Sacar `&& u.IsActive` del `HasQueryFilter` de `ApplicationUser`** — anotado desde la SPEC 27.
- **Paginación de `GET /Users`** — se le corrige la fuente de datos y el tenant, pero sigue devolviendo la colección completa.
- **Paginación de `GET /Users/reports/system`** — mantiene su contrato actual.
- **Permisos efectivos cross-tenant** (`?companyId=` para SuperAdmin) — siempre el tenant del JWT.
- **`GET /account/my-permissions`** (permisos del usuario logueado) — es SPEC 26.
- **Leer la caché de `PermissionService` en el item 19** — query fresca siempre.
- **Bulk de membresía multi-empresa** (N usuarios a una empresa de una vez) — el item 20 es bulk de roles.
- **Semántica replace en el item 20** — es delta.
- **Unificar el contrato de roles del single y el bulk** — uno por nombres, el otro por `Guid`.
- **Notificación por correo** de cambios de rol o de membresía.
- **Cambios sobre `PUT /Users/{userId}/default-company/{companyId}`**.
- **Test de integración del orden de rutas** — requiere `tests/IntegrationTests` (SPEC 06).
- **Migrar el bulk a Dapper** si el volumen lo justifica — arranca con `IUnitOfWork`.
- **Auto-promoción de empresa por defecto** al borrar la default.

Cada uno de esos, si aterriza, va en su propia spec.

---

## Archivos críticos

### Modify

- `src/2.Application/Interface/Persistence/Security/IUserAdminRepository.cs` — 5 métodos nuevos + record `UserCompanyMembershipInfo`.
- `src/3.Persistence/Repositories/Security/UserAdminRepository.cs` — implementar los 5.
- `src/2.Application/UseCases/Security/Users/Commands/ReplaceUserRoles/ReplaceUserRolesCommandHandler.cs` — reescritura completa.
- `src/2.Application/UseCases/Security/Users/Queries/GetUsersWithRoles/GetUsersWithRolesQueryHandler.cs` — SQL nuevo + tenant.
- `src/2.Application/UseCases/Security/Queries/GetMyCompanyUserReport/GetMyCompanyUserReportQuery.cs` — 4 parámetros + retorno `PagedResult<T>`.
- `src/2.Application/UseCases/Security/Queries/GetMyCompanyUserReport/GetMyCompanyUserReportQueryHandler.cs` — clamps + `ReadPagedAsync` + `TENANT_REQUIRED`.
- `src/2.Application/UseCases/Security/Queries/GetSystemWideUserReport/GetSystemWideUserReportQueryHandler.cs` — agregar `ReadPagedAsync` al helper y extraer los privados compartidos. El handler en sí no cambia.
- `src/4.Services.WebApi/Controllers/Security/UsersController.cs` — 4 endpoints nuevos, 3 modificados.
- `CURL_REQUESTS.md`.

### Create

**DTOs** (en `src/2.Application.DTO/Security/User/`):

- `UserEffectivePermissionsDto.cs`
- `AddUserCompanyRequestDto.cs`
- `AddUserCompanyResultDto.cs`
- `BulkUpdateUserRolesRequestDto.cs`
- `BulkUpdateUserRolesResultDto.cs`
- `BulkRoleOutcome.cs`

**Use cases:**

- `src/2.Application/UseCases/Security/UserCompanies/Commands/AddUserCompany/` (Command + Handler + Validator)
- `src/2.Application/UseCases/Security/UserCompanies/Commands/RemoveUserCompany/` (Command + Handler + Validator)
- `src/2.Application/UseCases/Security/Users/Commands/BulkUpdateUserRoles/` (Command + Handler + Validator)
- `src/2.Application/UseCases/Security/Users/Queries/GetUserEffectivePermissions/` (Query + Handler)

**Tests:**

- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/UserCompanies/Commands/AddUserCompany/AddUserCompanyCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/UserCompanies/Commands/AddUserCompany/AddUserCompanyCommandValidatorTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/UserCompanies/Commands/RemoveUserCompany/RemoveUserCompanyCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/Commands/BulkUpdateUserRoles/BulkUpdateUserRolesCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/Commands/BulkUpdateUserRoles/BulkUpdateUserRolesCommandValidatorTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/Queries/GetUserEffectivePermissions/GetUserEffectivePermissionsQueryHandlerTests.cs`

**Tests a reescribir:**

- `ReplaceUserRolesCommandHandlerTests` — los mocks de `UserManager`/`RoleManager` se van.
- `GetUsersWithRolesQueryHandlerTests` — el SQL y el tenant cambian.
- `GetMyCompanyUserReportQueryHandlerTests` — el retorno pasa a `PagedResult<T>`.
