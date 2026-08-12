# SPEC 20 — Preview de usuarios afectados por rol (`GET /api/v1/Roles/{id}/users`)

> **Status:** Implementado
> **Depends on:** SPEC 18 (Roles CRUD), SPEC 19 (RoleCompany junction), SPEC 17 (PermissionFlags)
> **Date:** 2026-08-11
> **Objective:** Exponer un endpoint paginado que liste los usuarios a los que un `ApplicationRole` está actualmente asignado dentro del tenant del caller (`Security.UserRoleCompanies` con `GcRecord = 0` y `CompanyId = token`), alimentando el preview "usuarios afectados" que el UI muestra antes de guardar cambios sobre el rol.

---

## Context

SPEC 19 (`RoleCompany` junction) cierra el catálogo de qué roles están disponibles por tenant, pero deja como riesgo implícito que un cambio sobre un rol (renombrar, retirar del catálogo, soft-delete) puede afectar a usuarios sin que el UI pueda avisar con quiénes cuenta. Este endpoint es el lado "lectura" del flujo: antes de hacer un cambio agresivo, el frontend pregunta "qué usuarios tengo con este rol" para que el SuperAdminCompany vea el blast-radius.

El endpoint vive en `RolesController` (no `RoleCompaniesController`) porque su ruta es `/api/v1/Roles/{id}/users` y el resource para `DynamicAuthorizationFilter` debe ser `"Roles"`.

---

## Scope

**In:**

- `src/2.Application.DTO/Security/RoleUsers/RoleAffectedUserDto.cs` (nuevo): record `sealed` con `init`-only:
  - `Guid Id`
  - `string FullName` (proyectado en SQL vía `CONCAT(u.FirstName, ' ', u.LastName) AS FullName`)
  - `bool IsActive`
  - `string UserName`
  - `string Email`
  - `string? PhoneNumber`
  - `DateTime Created`
  - `bool IsSuperAdmin`
  - `bool IsSuperAdminCompany`
  - `bool EmailConfirmed`
  - Sin `CompanyId` (siempre viene del token; exponerlo en el contrato es ruido).
- `src/2.Application/UseCases/Security/Roles/Queries/GetUsersByRoleId/GetUsersByRoleIdQuery.cs` (nuevo): `sealed record GetUsersByRoleIdQuery(Guid RoleId, int Page = 1, int PageSize = 20) : IRequest<Response<PagedResult<RoleAffectedUserDto>>>`. Sin validator (queries sin validator en este repo).
- `src/2.Application/UseCases/Security/Roles/Queries/GetUsersByRoleId/GetUsersByRoleIdQueryHandler.cs` (nuevo): primary constructor con `IRoleCompanyRepository`, `IRoleRepository`, `ICurrentUserService`. Constantes `MaxPageSize = 100`, `DefaultPageSize = 20`. Pasos:
  1. `tenantId = currentUserService.CompanyId`; si `Guid.Empty` → `Response<T>.Error("INVALID_COMPANY_ID", ["El token no contiene un CompanyId válido."])`.
  2. `if (!await roleRepository.ExistsAndActiveAsync(request.RoleId, ct))` → `Response<T>.Error("Rol no encontrado o inactivo.", ["El rol indicado no existe o se encuentra inactivo."])`.
  3. Page clamp (`page < 1 ? 1 : page`; `pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize)`).
  4. `(items, total) = await roleCompanyRepository.GetUsersByRoleIdPagedAsync(roleId, tenantId, page, pageSize, ct)`.
  5. Devolver `Response<PagedResult<RoleAffectedUserDto>>` con `IsSuccess = true`, `Message = "Users affected by role retrieved successfully."`.
- `src/2.Application/Interface/Persistence/Security/IRoleCompanyRepository.cs` (extender): añadir `Task<(IReadOnlyList<RoleAffectedUserDto> Items, int Total)> GetUsersByRoleIdPagedAsync(Guid roleId, Guid tenantId, int page, int pageSize, CancellationToken ct = default)`. Sibling de `GetPagedAsync`. Nuevo `using JOIN.Application.DTO.Security.RoleUsers;` en imports.
- `src/3.Persistence/Repositories/Security/RoleCompanyRepository.cs` (extender): implementar `GetUsersByRoleIdPagedAsync`. Dapper multi-result con dos queries sobre la misma conexión:
  - `SELECT COUNT(*) FROM [Security].[UserRoleCompanies] urc INNER JOIN [Security].[Users] u ON u.Id = urc.UserId {whereClause}` → `int total`.
  - `SELECT u.Id, CONCAT(u.FirstName, ' ', u.LastName) AS FullName, u.IsActive, u.UserName, u.Email, u.PhoneNumber, u.Created, u.IsSuperAdmin, u.IsSuperAdminCompany, u.EmailConfirmed FROM [Security].[UserRoleCompanies] urc INNER JOIN [Security].[Users] u ON u.Id = urc.UserId {whereClause} ORDER BY u.FirstName, u.LastName, u.Id {paginationClause}` → `IReadOnlyList<UserWithRolesDto>`.
  - `whereClause`: `WHERE urc.RoleId = @RoleId AND urc.CompanyId = @TenantId AND urc.GcRecord = 0 AND u.GcRecord = 0` (los filtros `GcRecord = 0` son obligatorios porque Dapper bypassa los query filters globales de EF; `urc.CompanyId = @TenantId` es defensa explícita porque `UserRoleCompany` no hereda `BaseTenantEntity`).
  - `paginationClause`: branch sobre `_dbContext.Database.ProviderName?.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase)`: Postgres → `LIMIT @pageSize OFFSET @offset`; SqlServer → `OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY`.
- `src/4.Services.WebApi/Controllers/Security/RolesController.cs` (extender): añadir acción `[HttpGet("{id:guid}/users")]` insertada entre `GetById` (línea 122) y `Create` (línea 124 aprox.). Atributos:
  - `[Authorize(Roles = "SuperAdminCompany")]`
  - `[ProducesResponseType(typeof(Response<PagedResult<RoleAffectedUserDto>>), 200)]`
  - `[ProducesResponseType(typeof(Response<object>), 400/401/403/404)]`
  - Parámetros: `Guid id`, `[FromQuery] int page = 1`, `[FromQuery] int pageSize = 20`, `CancellationToken ct`.
  - Switch de mapping en el return: `INVALID_COMPANY_ID` → 401, `"Rol no encontrado o inactivo."` → 404, default → 400. Reuso del mismo string-literal que ya usa `GetById` para mantener consistencia con `RolesController`.
  - No necesita `[PermissionResource]` propio — hereda el `[PermissionResource("Roles")]` a nivel de clase (línea 32); `DynamicAuthorizationFilter` lo resuelve por precedencia action→clase.
- Tests unitarios en `tests/UnitTests/JOIN.Application.UnitTest/Security/Roles/Queries/GetUsersByRoleId/GetUsersByRoleIdQueryHandlerTests.cs` (nuevo): 7 `[Fact]` cubriendo:
  1. `CompanyId == Guid.Empty` → `INVALID_COMPANY_ID`, repos nunca llamados.
  2. Rol inexistente / soft-deleted → `"Rol no encontrado o inactivo."`, `GetUsersByRoleIdPagedAsync` nunca llamado.
  3. Happy path con DTO seed.
  4. `pageSize > 100` → clamp a 100.
  5. `page < 1` → clamp a 1.
  6. `pageSize < 1` → fallback a 20.
  7. Resultado vacío (`TotalCount = 0`, `TotalPages = 0`) sigue siendo `IsSuccess = true`.
- Cobertura ≥ 90% en el handler nuevo.
- Documentación del endpoint en `CURL_REQUESTS.md` (bloque con curl happy-path + casos 401/404/403 + page clamp).

**Out of scope:**

- UI / modal que consuma el endpoint.
- Bulk reassign de usuarios al cambiar un rol.
- Historial de cambios sobre `UserRoleCompanies` (no hay tabla de auditoría hoy).
- Endpoint inverso "qué roles tiene este usuario" — ya existe `GetUsersWithRoles` en `src/2.Application/UseCases/Security/Users/Queries/GetUsersWithRoles/` y cubre esa dirección.
- Migración EF Core — endpoint es read-only sobre tablas existentes.
- Cambios a `SystemOption` / `ControllerName = "Roles"` — el seed actual ya cubre el resource.

---

## Data model

**Sin cambios.** Endpoint es solo lectura sobre:

- `Security.Roles` (existence + active check, vía `IRoleRepository.ExistsAndActiveAsync`).
- `Security.UserRoleCompanies` (filter source, `WHERE urc.RoleId = @RoleId AND urc.CompanyId = @TenantId AND urc.GcRecord = 0`).
- `Security.Users` (projection, `AND u.GcRecord = 0`).

`FullName` se calcula en SQL (`CONCAT()`) — `CONCAT` es portable entre SQL Server y Postgres, no usamos `+` (no portable a Postgres) ni `ISNULL`/`COALESCE` anidados.

---

## API contract

`GET /api/v1/Roles/{id:guid}/users?page={int}&pageSize={int}`

Headers:
- `Authorization: Bearer {jwt}` — token con claim `CompanyId` o header `X-Company-Id` (resolución ya implementada en `CurrentUserService.cs:42-72`).
- `Accept: application/json`

Respuestas:

| Status | Cuando | Cuerpo |
|---|---|---|
| 200 | OK, página de usuarios (posiblemente vacía) | `Response<PagedResult<RoleAffectedUserDto>>` |
| 400 | Error no esperado (regresión, default switch arm) | `Response<object>` con mensaje genérico |
| 401 | Token sin `CompanyId` claim/header | `Message = "INVALID_COMPANY_ID"` |
| 403 | Caller sin rol `SuperAdminCompany` | (sin body, ASP.NET default) |
| 404 | Rol no existe o está soft-deleted | `Message = "Rol no encontrado o inactivo."` |

Paged payload shape (mirror `GetRoleCompaniesPaged`):

```json
{
  "isSuccess": true,
  "message": "Users affected by role retrieved successfully.",
  "data": {
    "items": [
      {
        "id": "00000000-0000-0000-0000-000000000000",
        "fullName": "Livingstone Bravo",
        "isActive": true,
        "userName": "livingstone23@gmail.com",
        "email": "livingstone23@gmail.com",
        "phoneNumber": null,
        "created": "2026-08-08T12:00:00Z",
        "isSuperAdmin": false,
        "isSuperAdminCompany": true,
        "emailConfirmed": true
      }
    ],
    "pageNumber": 1,
    "pageSize": 20,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

---

## Implementation plan

1. Crear DTO `UserWithRolesDto.cs` (mirror `RoleCompanyListItemDto.cs` style: copyright header, init-only, default-empty strings).
2. Crear `GetUsersByRoleIdQuery.cs` (record con defaults `Page = 1`, `PageSize = 20`).
3. Crear `GetUsersByRoleIdQueryHandler.cs` (mirror `GetRoleCompaniesPagedQueryHandler` + pre-check via `IRoleRepository.ExistsAndActiveAsync`).
4. Extender `IRoleCompanyRepository.cs` con `GetUsersByRoleIdPagedAsync`.
5. Implementar `GetUsersByRoleIdPagedAsync` en `RoleCompanyRepository.cs` (Dapper, branch provider-aware).
6. Extender `RolesController.cs` con la acción `GetUsers`. Imports nuevos: `JOIN.Application.DTO.Security.UserWithRoles`, `JOIN.Application.UseCases.Security.Roles.Queries.GetUsersByRoleId`.
7. Crear `GetUsersByRoleIdQueryHandlerTests.cs` con los 7 `[Fact]` cases.
8. Crear `specs/20-role-users-preview-endpoint.md` (este archivo).
9. Añadir bloque curl en `CURL_REQUESTS.md`.
10. `dotnet build -c Release` + `dotnet test --filter "FullyQualifiedName~GetUsersByRoleIdQueryHandler"`.

---

## Acceptance criteria

- [ ] `GET /api/v1/Roles/{seedRoleId}/users?page=1&pageSize=20` con JWT de `livingstone23@gmail.com` (CompanyId = JOIN-001) devuelve 200 con al menos un usuario seed (Admin o SuperAdminCompany, los dos roles vinculados en `DatabaseSeeder.cs:480-541`).
- [ ] Cross-tenant: JWT con `CompanyId = PRIV-001` (no hay `UserRoleCompanies` para ese tenant) → 200 con `items: []`, `totalCount = 0`, `totalPages = 0`.
- [ ] `GET /api/v1/Roles/00000000-0000-0000-0000-000000000000/users` → 404 con `message = "Rol no encontrado o inactivo."`.
- [ ] JWT sin claim `CompanyId` ni header `X-Company-Id` → 401 con `message = "INVALID_COMPANY_ID"`.
- [ ] JWT con `role = Admin` (no `SuperAdminCompany`) → 403 desde `[Authorize(Roles = "SuperAdminCompany")]`.
- [ ] `?page=-1&pageSize=999` → 200 con `pageNumber = 1`, `pageSize = 100`.
- [ ] `dotnet test` con filtro `FullyQualifiedName~GetUsersByRoleIdQueryHandler` pasa 7/7.
- [ ] Cobertura Coverlet del handler nuevo ≥ 90%.

---

## Decisions

- **SPEC 20 nuevo, no amend SPEC 19.** SPEC 19 está `Status: Aprobado` y completa; amending una spec cerrada ensucia el audit trail. SPEC 20 cita a SPEC 19 como dependencia.
- **Endpoint en `RolesController`, no `RoleCompaniesController`.** La ruta es `/api/v1/Roles/{id}/users` (resource = `Roles`); el filtro `[PermissionResource("RoleCompanies")]` de `RoleCompaniesController` no aplica y la ruta no cuelga de su prefijo.
- **Repo method en `IRoleCompanyRepository`, no en `IRoleRepository` ni `IUserRepository`.** El filter source es `UserRoleCompanies`, que ya está en la jurisdicción del repo que maneja junctions del tenant. `IRoleRepository` queda enfocado en la entidad `ApplicationRole`.
- **Dapper + multi-result, no EF Core tracked.** Read-only, paginación cross-DB y filtros dinámicos: la convención del repo (`GetPagedAsync`).
- **`FullName` calculado en SQL con `CONCAT()`.** Portable entre SQL Server y Postgres. Evitamos `+` (no portable) y funciones date vendor.
- **Pre-check con `IRoleRepository.ExistsAndActiveAsync` en lugar de LEFT JOIN sobre `Security.Roles`.** Si el rol no existe, queremos 404 explícito en lugar de devolver una lista vacía silenciosa que confunde al UI (¿"no hay usuarios con este rol" o "el rol no existe"?).
- **Reuso del string-literal `"Rol no encontrado o inactivo."` que ya usa `GetById`.** Mantiene el contrato del controller uniforme — un solo switch en cualquier `RolesController` action cubre las 4 acciones que mapean este caso.

---

## Risks

- **Cross-tenant leak** si `urc.CompanyId = @TenantId` se olvida. Mitigado por: SQL explícito + handler short-circuit + tests cross-tenant.
- **Dapper bypass a filtros globales de EF** (`UserRoleCompany.GcRecord == 0`, `User.GcRecord == 0`). Mitigado por SQL explícito + tests con mocks que verifican que los filtros van al repo en cada llamada.
- **Inconsistencia entre `IRoleRepository.ExistsAndActiveAsync` y la proyección**: si el rol pasa el pre-check pero la query de usuarios devuelve 0, eso significa que el rol existe pero nadie lo tiene — respuesta 200 con `items: []`, no 404. Documentado en acceptance criteria.
- **Concurrencia**: el endpoint es read-only; sin race contra writes. Si el UI lo combina con un modal de delete/update, considerar `ETag`/`If-Match` — fuera de scope.
- **Escalabilidad**: query hace JOIN entre dos tablas (`UserRoleCompanies`, `Users`) sobre el índice único `(UserId, RoleId, CompanyId)` y PK de `Users`. Con 10K+ `UserRoleCompanies` por rol sigue siendo O(log n) por filtro — sin problema. Sin OFFSET grande: paginación se evalúa en SQL.

---

## What is not in this spec

- UI / vista previa en el frontend.
- Bulk unassign de usuarios al descontinuar un rol.
- Historial de cambios sobre `UserRoleCompanies` (no hay tabla de auditoría hoy).
- Endpoint simétrico "qué roles tiene este usuario" (ya cubierto por `GetUsersWithRoles`).
- Cambio a `ApplicationUser` para tener `CompanyId` propio — no se rediseña la arquitectura.
- Migración EF Core — sin cambios de schema.
