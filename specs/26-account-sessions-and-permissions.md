# SPEC 26 — `account/sessions` revoke + `account/my-permissions`

> **Status:** Borrador
> **Depends on:** SPEC 18 (Roles), SPEC 22 (RoleSystemOption 7 flags), SPEC 23 (tenant from token), SPEC 25 (matrix shape)
> **Date:** 2026-08-15
> **Objective:** Cerrar los dos gaps de self-management del usuario autenticado en `AccountController` sin tocar los seis endpoints existentes: (a) permitir cerrar una sesión concreta (`DELETE /sessions/{sessionId}`) y todas las demás (`POST /sessions/revoke-others`) revokando `UserConnectionLog` + `UserRefreshToken` no-current con detección de "current" via claim JWT; (b) exponer `GET /my-permissions` con la matrix CRUD efectiva del usuario logueado (unión OR de todos sus roles en el tenant del JWT) reusando el shape `RoleSystemOptionMatrixDto` de SPEC 25. Cambios viven en `AccountController`, dos handlers nuevos, un claim JWT nuevo, y un método nuevo en `IPermissionService` para detectar el refresh token actual. No nuevos controllers, no migraciones de schema, no DTOs nuevos.

---

## Scope

**In:**

- `src/3.Infrastructure/Security/Jwt/JwtTokenGenerator.cs`: incluir claim `refresh_token_id` (Guid) en el JWT al emitir access token. Propagar al `RefreshTokenCommand` también.
- `src/2.Application/Interface/ICurrentUserService.cs` (+ impl en `3.Infrastructure`): nueva prop `Guid? RefreshTokenId` que lee el claim `refresh_token_id` del `HttpContext`.
- `src/2.Application/UseCases/Security/Account/Commands/RevokeMySession/`:
  - `RevokeMySessionCommand.cs` — `IRequest<Response<RevokeSessionResultDto>>` con `SessionId`.
  - `RevokeMySessionCommandHandler.cs` — búsqueda UNION en `UserConnectionLogs` + `UserRefreshTokens` por Id, valida que el row pertenece al `currentUser.UserId`, marca soft-revoke (uno o ambos según qué tabla lo contenga). Si ninguno de los dos lados devuelve fila → `SESSION_NOT_FOUND` (404). Si pertenece a otro user → `SESSION_NOT_FOUND` (no leak). Si es la sesión actual (refresh_token_id del claim) → `CANNOT_REVOKE_CURRENT` (400).
  - `RevokeMySessionCommandValidator.cs` — `SessionId` `NotEmpty`.
- `src/2.Application/UseCases/Security/Account/Commands/RevokeOtherMySessions/`:
  - `RevokeOtherMySessionsCommand.cs` — sin payload (el `currentUser` ya da el id y el `RefreshTokenId`).
  - `RevokeOtherMySessionsCommandHandler.cs` — soft-revoke de `UserConnectionLog` activos no-current + `UserRefreshToken` no-revoked no-expiry no-current del usuario. Devuelve contadores `{ revokedConnections, revokedTokens }`.
- `src/2.Application.DTO/Security/Account/RevokeSessionResultDto.cs` (nuevo) — `{ revokedConnections, revokedTokens }` reusado por ambos handlers.
- `src/2.Application/UseCases/Security/Account/Queries/GetMyPermissions/`:
  - `GetMyPermissionsQuery.cs` — sin payload.
  - `GetMyPermissionsQueryHandler.cs` — query fresh (un solo SELECT) que une `UserRoleCompanies` + `RoleSystemOptions` + `SystemOptions` + `SystemModules` para el `(userId, companyId)` del JWT, agrupado por módulo con `Supports` (de `SystemOptions`) y `Granted` (OR de los flags de los `RoleSystemOption` activos). Si el user no tiene roles en el tenant → `Modules = []` con 200 OK.
- `src/2.Application.DTO/Security/Account/MyPermissionsDto.cs` (nuevo) — wrapper `{ userId, companyId, roleIds[], modules[] }` reusando `RoleSystemOptionMatrixDto` de SPEC 25 sin modificarlo.
- `src/4.Services.WebApi/Controllers/Security/AccountController.cs`:
  - `DELETE /api/v1/account/sessions/{sessionId}` → `RevokeMySessionCommand`. Mapea `SESSION_NOT_FOUND` → 404, `CANNOT_REVOKE_CURRENT` → 400.
  - `POST /api/v1/account/sessions/revoke-others` → `RevokeOtherMySessionsCommand`. Devuelve 200 con contadores.
  - `GET /api/v1/account/my-permissions` → `GetMyPermissionsQuery`. Devuelve 200 con la matrix. Tenant del JWT (no query param).
- Tests en `tests/UnitTests/JOIN.Application.UnitTest/Security/Account/`:
  - `RevokeMySessionCommandHandlerTests`: 5 caminos (revoke de UserConnectionLog / revoke de UserRefreshToken / session ajena → 404 / session current → 400 / session inexistente → 404).
  - `RevokeOtherMySessionsCommandHandlerTests`: 4 caminos (sin sesiones / solo current / mixto / current inexistente en DB).
  - `GetMyPermissionsQueryHandlerTests`: 3 caminos (user con 2 roles y permisos en 2 módulos / user sin roles → 200 con modules vacío / tenant vacío → 400).

**Out of scope:**

- Endpoints 9 (MFA), 11 (confirm email change), 12 (verify phone), 13 (security activity log) — van en specs 27/28/29 separadas. Acordado en split inicial.
- Cambios sobre `GET /account/sessions` existente — el shape del DTO se mantiene idéntico; el `IsCurrent = true` se mantiene con la heurística actual (el frontend ya no la necesita porque ahora hay claim JWT, pero no se toca).
- `POST /account/change-password`, `POST /account/request-email-change`, `GET /account/profile`, `PUT /account/profile` — sin cambios.
- Invalidación de cache `permissions:v2:` tras revoke — un cambio de permisos no aplica acá (el endpoint no cambia flags de rol, solo sesiones). El cache de sidebar/permisos se mantiene.
- Tabla de `AuditLog` de eventos de security (login OK/fail, password change) — eso es spec 28.
- `my-permissions` cross-tenant (superadmin) — siempre tenant del JWT. Si hace falta, spec aparte.
- Paginación de `my-permissions` — la matrix es completa. Si el seed crece a >1000 options, spec aparte con `string_agg` o paginación.
- `revoke-others` con filtro por device/IP — fuera de scope. Solo "current vs others".
- Hard delete de `UserConnectionLog` / `UserRefreshToken` — soft-only (mantener auditoría). Las tablas tienen `GcRecord` o `IsActiveSession`/`IsRevoked` para esto.

---

## Data model

### `MyPermissionsDto` (nuevo, en `src/2.Application.DTO/Security/Account/`)

```csharp
public sealed record MyPermissionsDto(
    Guid UserId,
    Guid CompanyId,
    IReadOnlyList<Guid> RoleIds,
    RoleSystemOptionMatrixDto Matrix);
```

`RoleIds` = los `RoleId` activos del user en el tenant (de `UserRoleCompany` filtrado por `GcRecord = 0`). `Matrix` reusa el record de SPEC 25 tal cual. **El campo `RoleId` y `RoleName` del `RoleSystemOptionMatrixDto` se llenan con un "rol virtual" cuando hay múltiples**: el primer role por nombre alfabético, con `RoleName = "<RoleName1> + <N-1> más"`. Si hay un solo rol, `RoleName` real. Esta convención evita romper el shape de SPEC 25.

### `RevokeSessionResultDto` (nuevo, en `src/2.Application.DTO/Security/Account/`)

```csharp
public sealed record RevokeSessionResultDto(
    int RevokedConnections,
    int RevokedTokens);
```

`RevokeOtherMySessionsCommandHandler` devuelve esto. `RevokeMySessionCommandHandler` también lo devuelve, con uno o ambos counters en `0` según qué tabla contenía el `sessionId`.

### Claim JWT nuevo

- **Nombre:** `refresh_token_id`.
- **Tipo:** `Guid` (string en el payload, parseado en `ICurrentUserService`).
- **Set en:** `JwtTokenGenerator.GenerateAccessToken(...)` — el segundo argumento ya es el `RefreshToken` (o su `Id`); agregar el claim `new Claim("refresh_token_id", refreshTokenId.ToString())`.
- **Lectura en:** `ICurrentUserService.RefreshTokenId` (nueva prop, nullable `Guid?`). Si el claim falta o no parsea, devuelve `null` (token emitido antes de esta migration). Comportamiento: `revoke-others` con `RefreshTokenId == null` cae al fallback de "skip current" (no revoca nada que pueda ser current — más seguro). El `DELETE /sessions/{id}` individual sigue funcionando sin claim.

### Cambios en `ICurrentUserService`

```csharp
public interface ICurrentUserService
{
    // ... props existentes (UserId, CompanyId, etc.)
    Guid? RefreshTokenId { get; }  // NUEVO — lee claim "refresh_token_id"
}
```

### Cambios en `JwtTokenGenerator` (firma)

```csharp
public string GenerateAccessToken(ApplicationUser user, Guid refreshTokenId, ...);
// (antes: GenerateAccessToken(ApplicationUser user, ...))
```

Si la firma actual ya recibe el `RefreshToken` entity o su `Id`, el cambio es solo agregar un param. Si no, refactor mínimo: cambiar firma + actualizar 1-2 callers (`LoginCommandHandler`, `RefreshTokenCommandHandler`).

### Sin migraciones EF

- `UserConnectionLogs` ya tiene `IsActiveSession`, `DisconnectionDate`. Soft-revoke = `IsActiveSession = 0` + `DisconnectionDate = UtcNow` + `LastModified = UtcNow` + `UpdatedBy = currentUser.UserId`.
- `UserRefreshTokens` ya tiene `IsRevoked`, `LastModified`, `GcRecord`. Soft-revoke = `IsRevoked = 1` + `LastModified = UtcNow` (mantiene row para auditoría).
- No hay schema nuevo. No hay migration. El claim JWT es stateless (no se guarda en DB).

---

## Implementation plan

### F1 — Claim JWT `refresh_token_id`

1. `JwtTokenGenerator.GenerateAccessToken(ApplicationUser user, Guid refreshTokenId, IReadOnlyDictionary<string, string>? extraClaims = null)`:
   - Agregar al `JwtSecurityToken`: `new Claim("refresh_token_id", refreshTokenId.ToString())`.
   - Refactor firma: recibir `refreshTokenId` como param. Actualizar callers: `LoginCommandHandler` y `RefreshTokenCommandHandler` (2 sitios).
2. `ICurrentUserService`: añadir `Guid? RefreshTokenId { get; }`.
3. Implementación de `ICurrentUserService` (en `3.Infrastructure`): leer claim `refresh_token_id` del `ClaimsPrincipal`, parsear a `Guid?`, devolver `null` si falta o no parsea.
4. Build → 0 errores. Test manual: login → `jwt.io` decodifica el token → claim `refresh_token_id` presente y matchea el row de `UserRefreshTokens`.

### F2 — `RevokeMySession` (single)

1. DTO `RevokeSessionResultDto` (mostrado en data model).
2. `IRoleUserSessionRepository` (nuevo) — interface en `2.Application/Interface/Persistence/Security/` con tres métodos:
   - `Task<SessionLookupResult?> FindActiveByIdAsync(Guid sessionId, CancellationToken ct)`.
   - `Task<int> SoftRevokeRefreshTokensExceptAsync(Guid userId, Guid? currentRefreshTokenId, DateTime utcNow, CancellationToken ct)`.
   - `Task<int> SoftRevokeActiveConnectionsAsync(Guid userId, DateTime utcNow, CancellationToken ct)`.
   - `SessionLookupResult = record(Guid Id, SessionType Type)` con `SessionType { UserRefreshToken, UserConnectionLog }`.
3. `RoleUserSessionRepository` (nuevo) en `3.Persistence/Repositories/Security/` — Dapper, las 3 queries:
   - `FindActiveByIdAsync`: `UNION ALL` sobre las dos tablas, devuelve `(Id, Type)`.
   - `SoftRevokeRefreshTokensExceptAsync`: `UPDATE Security.UserRefreshTokens SET IsRevoked = 1, LastModified = @UtcNow WHERE UserId = @UserId AND IsRevoked = 0 AND ExpiryDate > @UtcNow AND GcRecord = 0 AND Id <> ISNULL(@CurrentRefreshTokenId, '00000000-0000-0000-0000-000000000000')` (cuando `currentRefreshTokenId` es null, el filtro no excluye nada).
   - `SoftRevokeActiveConnectionsAsync`: `UPDATE Security.UserConnectionLogs SET IsActiveSession = 0, DisconnectionDate = @UtcNow, LastModified = @UtcNow WHERE UserId = @UserId AND IsActiveSession = 1 AND GcRecord = 0`.
4. `RevokeMySessionCommand` + `Handler` + `Validator` (F2 scope).
5. Handler:
   ```csharp
   var session = await repo.FindActiveByIdAsync(request.SessionId, ct);
   if (session is null) return Response<RevokeSessionResultDto>.Error("SESSION_NOT_FOUND", [...]);
   // Cross-user check: FindActiveByIdAsync ya filtra activo pero no por user; el handler agrega verificación.
   var belongsTo = await repo.GetUserIdBySessionIdAsync(session, ct);
   if (belongsTo != currentUser.UserId) return Response<RevokeSessionResultDto>.Error("SESSION_NOT_FOUND", [...]); // no leak
   if (session.Type == SessionType.UserRefreshToken && session.Id == currentUser.RefreshTokenId)
       return Response<RevokeSessionResultDto>.Error("CANNOT_REVOKE_CURRENT", [...]);
   var (revConn, revTok) = await repo.SoftRevokeOneAsync(session, currentUser.UserId, DateTime.UtcNow, ct);
   return Response<RevokeSessionResultDto>.Success(new(revConn, revTok));
   ```
6. Validator: `RuleFor(x => x.SessionId).NotEmpty()`.
7. Build → 0 errores. Tests F4.

### F3 — `RevokeOtherMySessions` (bulk)

1. `RevokeOtherMySessionsCommand` (sin payload, factory o vacío).
2. Handler:
   ```csharp
   var utcNow = DateTime.UtcNow;
   var revTok = await repo.SoftRevokeRefreshTokensExceptAsync(currentUser.UserId, currentUser.RefreshTokenId, utcNow, ct);
   var revConn = await repo.SoftRevokeActiveConnectionsAsync(currentUser.UserId, utcNow, ct);
   return Response<RevokeSessionResultDto>.Success(new(revConn, revTok));
   ```
3. Sin validator (no hay payload). Build → 0 errores.

### F4 — Tests de sessions

1. `RevokeMySessionCommandHandlerTests`:
   - `Handle_WhenSessionIsUserConnectionLog_ShouldRevokeConnectionOnly` — mock `FindActiveByIdAsync` → `(Id, UserConnectionLog)`, mock `GetUserIdBySessionIdAsync` → userId match → mock `SoftRevokeOneAsync` → `(1, 0)` → result `RevokedConnections=1, RevokedTokens=0`.
   - `Handle_WhenSessionIsUserRefreshToken_ShouldRevokeTokenOnly` — simétrico, result `(0, 1)`.
   - `Handle_WhenSessionBelongsToOtherUser_ShouldReturnSessionNotFound` — mock → userId distinto → `SESSION_NOT_FOUND`, no se llama `SoftRevokeOneAsync`.
   - `Handle_WhenSessionIsCurrentRefreshToken_ShouldReturnCannotRevokeCurrent` — mock → `currentUser.RefreshTokenId == session.Id` → `CANNOT_REVOKE_CURRENT`.
   - `Handle_WhenSessionNotFound_ShouldReturnSessionNotFound` — mock → null → `SESSION_NOT_FOUND`.
2. `RevokeOtherMySessionsCommandHandlerTests`:
   - `Handle_WhenNoOtherSessions_ShouldReturnZeroCounters` — mock ambos `SoftRevoke*` → `0` → result `(0, 0)`.
   - `Handle_WhenOnlyCurrentSession_ShouldReturnZeroCounters` — `currentUser.RefreshTokenId` válido, los dos mocks devuelven `0` (current excluido, nada más).
   - `Handle_WhenMixedSessions_ShouldReturnCounters` — mock → `(3, 2)`.
   - `Handle_WhenCurrentRefreshTokenIdIsNull_ShouldStillRevokeOthersButKeepCurrent` — `RefreshTokenId` null → el `ISNULL` en SQL no excluye nada → mocks devuelven contadores; aclaración: tokens sin `currentRefreshTokenId` conocido se revocan también (más seguro, peor UX si el user se auto-revoca).
3. `dotnet test --filter "FullyQualifiedName~RevokeMySession|FullyQualifiedName~RevokeOtherMySessions"` → 0 fallidos.

### F5 — `GetMyPermissions` (matrix del user)

1. DTO `MyPermissionsDto` (mostrado en data model).
2. `IRoleUserSessionRepository` (mismo que F2) agregar:
   - `Task<MyPermissionsDto?> GetUserPermissionsMatrixAsync(Guid userId, Guid companyId, CancellationToken ct)`.
3. `RoleUserSessionRepository` — Dapper, **un solo SELECT** con JOINs:
   ```sql
   WITH user_roles AS (
       SELECT DISTINCT urc.RoleId
       FROM Security.UserRoleCompanies urc
       WHERE urc.UserId = @UserId
         AND urc.CompanyId = @CompanyId
         AND urc.GcRecord = 0
   ),
   effective_options AS (
       SELECT
           so.Id AS SystemOptionId,
           so.Name,
           so.Route,
           so.ModuleId,
           sm.Name AS ModuleName,
           so.CanRead AS SupportsRead, so.CanCreate AS SupportsCreate, so.CanUpdate AS SupportsUpdate,
           so.CanDelete AS SupportsDelete, so.CanDownload AS SupportsDownload, so.CanExport AS SupportsExport,
           so.CanExecute AS SupportsExecute,
           MAX(CASE WHEN rso.GcRecord = 0 THEN rso.CanRead END) AS GrantedRead,
           -- ... igual para los otros 6 flags
           MAX(rso.OrderMenu) AS OrderMenu
       FROM Security.SystemOptions so
       INNER JOIN Admin.SystemModules sm ON sm.Id = so.ModuleId
       LEFT JOIN Security.RoleSystemOptions rso
           ON rso.SystemOptionId = so.Id
          AND rso.CompanyId = @CompanyId
          AND rso.RoleId IN (SELECT RoleId FROM user_roles)
          AND rso.GcRecord = 0
       WHERE so.GcRecord = 0 AND sm.GcRecord = 0
       GROUP BY so.Id, so.Name, so.Route, so.ModuleId, sm.Name, /* 7 supports */
                so.OrderMenu
   )
   SELECT * FROM effective_options
   ORDER BY ModuleName, OrderMenu, Name;
   ```
   - Si no hay roles → el LEFT JOIN produce NULL en `Granted*` → 7 flags `false` (todos los SystemOptions aparecen con Granted = all false). Módulos siempre vienen.
   - Si no hay roles, `RoleIds = []` en el DTO.
4. Map en C#: agrupar por `ModuleId`/`ModuleName`, construir `RoleSystemOptionMatrixModuleDto`[] con `Options` cada uno con `Supports` + `Granted`. `RoleName` del `RoleSystemOptionMatrixDto` raíz: si `RoleIds.Count == 1` → nombre del rol; si `> 1` → `"<RoleName1> + <N-1> más"`.
5. `GetMyPermissionsQuery` + `Handler` (sin validator).
6. Handler:
   ```csharp
   var companyId = currentUser.CompanyId;
   if (companyId == Guid.Empty) return Response<MyPermissionsDto>.Error("TENANT_REQUIRED", [...]);
   var result = await repo.GetUserPermissionsMatrixAsync(currentUser.UserId, companyId, ct);
   return result is null
       ? Response<MyPermissionsDto>.Error("USER_NOT_FOUND", [...])
       : Response<MyPermissionsDto>.Success(result);
   ```
   - `null` solo si el user no existe en `AspNetUsers` (caso raro, defensa).
7. Build → 0 errores.

### F6 — Tests de `GetMyPermissions`

1. `GetMyPermissionsQueryHandlerTests`:
   - `Handle_WhenUserHasTwoRolesWithPermissionsInTwoModules_ShouldReturnGroupedMatrix` — mock → DTO con 2 módulos, 3 options, `Granted` mixto, `RoleIds` con 2 entries, `RoleName` = `"<Role1> + 1 más"`.
   - `Handle_WhenUserHasNoRoles_ShouldReturnEmptyMatrix` — mock → DTO con `RoleIds = []`, módulos poblados con `Granted` = all false.
   - `Handle_WhenTenantEmpty_ShouldReturnTenantRequiredError` — `companyId == Guid.Empty` → corta antes del repo.
2. `dotnet test --filter "FullyQualifiedName~GetMyPermissions"` → 0 fallidos.

### F7 — Controller

1. `AccountController.cs` — añadir tres endpoints (no romper los existentes):
   ```csharp
   [HttpDelete("sessions/{sessionId:guid}")]
   public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken ct) { ... }

   [HttpPost("sessions/revoke-others")]
   public async Task<IActionResult> RevokeOthers(CancellationToken ct) { ... }

   [HttpGet("my-permissions")]
   public async Task<IActionResult> GetMyPermissions(CancellationToken ct) { ... }
   ```
2. Mapping de errores:
   - `SESSION_NOT_FOUND` → 404.
   - `CANNOT_REVOKE_CURRENT` → 400.
   - `TENANT_REQUIRED` → 400.
3. Permisos: `[PermissionResource("Users")]` ya está en la clase. `DELETE → CanDelete`, `POST → CanCreate`, `GET → CanRead` (verb defaults). **No** añadir overrides.
4. Build → 0 errores.

### F8 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → `JOIN.Application` ≥ 90% (gate CI).
3. `CURL_REQUESTS.md` — añadir bloque `account/sessions` y `account/my-permissions` con los requests de los acceptance criteria.
4. Smoke test manual:
   - Login → JWT trae `refresh_token_id` → decodear en jwt.io y verificar.
   - `GET /api/v1/account/sessions` → 200 con N sesiones.
   - `DELETE /api/v1/account/sessions/{id}` con id de un row de UserConnectionLog → 200 con `{revokedConnections:1, revokedTokens:0}`. Repetir con id de UserRefreshToken.
   - `POST /api/v1/account/sessions/revoke-others` → 200 con counters. Verificar en DB que solo queda 1 `UserRefreshToken` activo (el current) y 0 `UserConnectionLog` activas.
   - `GET /api/v1/account/my-permissions` → 200 con la matrix agrupada.

---

## Acceptance criteria

### F1 — Claim JWT
- [ ] `JwtTokenGenerator.GenerateAccessToken` agrega claim `refresh_token_id` con el Guid del `UserRefreshToken` usado.
- [ ] `ICurrentUserService.RefreshTokenId` devuelve el Guid parseado del claim, o `null` si falta/no parsea.
- [ ] Tokens emitidos antes de esta spec siguen funcionando (claim ausente → `null`).
- [ ] `LoginCommandHandler` y `RefreshTokenCommandHandler` actualizados para pasar el `refreshTokenId`.

### F2 — `RevokeMySession`
- [ ] `DELETE /api/v1/account/sessions/{sessionId}` con id de `UserConnectionLog` del user → 200 con `{revokedConnections:1, revokedTokens:0}`. El row en DB tiene `IsActiveSession = 0`, `DisconnectionDate` = ahora.
- [ ] Mismo endpoint con id de `UserRefreshToken` del user → 200 con `{revokedConnections:0, revokedTokens:1}`. El row en DB tiene `IsRevoked = 1`.
- [ ] Mismo endpoint con id de un row de otro user → 404 con `SESSION_NOT_FOUND` (no leak cross-user).
- [ ] Mismo endpoint con id del current refresh token (claim JWT) → 400 con `CANNOT_REVOKE_CURRENT`.
- [ ] Mismo endpoint con id inexistente → 404 con `SESSION_NOT_FOUND`.
- [ ] Validator rechaza `sessionId` vacío con 400.

### F3 — `RevokeOtherMySessions`
- [ ] `POST /api/v1/account/sessions/revoke-others` con claim válido → 200 con contadores que reflejan los rows efectivamente revocados. Solo el current refresh token sobrevive.
- [ ] Si `RefreshTokenId` es null (token viejo) → revoca TODOS los `UserRefreshToken` activos (incluido el que emitió el token actual, si todavía está activo). El siguiente `POST /refresh` del cliente fallará con 401, forzando re-login. Documentado.
- [ ] Las `UserConnectionLog` activas del user se cierran todas (sin excluir current — el handler de ping re-abre con un row nuevo).
- [ ] Si el user no tiene otras sesiones → 200 con `{0, 0}`.

### F5 — `GetMyPermissions`
- [ ] `GET /api/v1/account/my-permissions` con tenant válido → 200 con `{userId, companyId, roleIds[], matrix: {roleId, roleName, modules[]}}`.
- [ ] User con 2 roles y permisos en 2 módulos → respuesta con 2 módulos, N options por módulo, `Granted` = OR de los flags de ambos roles, `roleIds` con 2 entries, `roleName` con formato `"<Rol1> + 1 más"`.
- [ ] User con 1 solo rol → `roleName` = nombre del rol, `roleIds` con 1 entry.
- [ ] User sin roles → 200 con `roleIds = []`, módulos poblados con todos los `SystemOptions` activos y `Granted` = all false (la matrix nunca es null).
- [ ] `companyId == Guid.Empty` → 400 con `TENANT_REQUIRED`. No se ignora el query param `?companyId=` (siempre del JWT).
- [ ] User inexistente en `AspNetUsers` → 404 con `USER_NOT_FOUND`.

### F7 — Controller
- [ ] Ningún endpoint existente (`GET/PUT profile`, `POST change-password`, `POST request-email-change`, `GET sessions`) cambia de ruta, firma o comportamiento.
- [ ] Los tres endpoints nuevos pasan por `[PermissionResource("Users")]` heredado. `DELETE /sessions/{id}` requiere `CanDelete`, `POST /sessions/revoke-others` requiere `CanCreate`, `GET /my-permissions` requiere `CanRead`. Sin `[RequirePermission]` overrides.
- [ ] Endpoints devuelven `200` en success, mappings de error documentados arriba.

### General
- [ ] Tests cubren los 5 caminos de `RevokeMySession`, 4 de `RevokeOtherMySessions`, 3 de `GetMyPermissions`, 1 de validator → 13 tests nuevos.
- [ ] `dotnet test --filter "FullyQualifiedName~Account"` → 0 fallidos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate CI).
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` actualizado con los 3 endpoints nuevos.
- [ ] No migraciones EF, no cambios en `UsersController`, `AuthController`, `RoleSystemOptionsController` u otros controllers.
- [ ] `RoleUserSessionRepository` reusa `ISqlConnectionFactory` y Dapper, no EF change tracker.

---

## Decisions taken and discarded

- **Repo unificado `IRoleUserSessionRepository`** (elegido) vs. métodos en `IUserConnectionLogRepository` + `IUserRefreshTokenRepository`. Los dos repos no existen hoy; crear uno solo que cubre los 3 métodos de sessions (`FindActiveByIdAsync`, `SoftRevokeRefreshTokensExceptAsync`, `SoftRevokeActiveConnectionsAsync`) + `GetUserPermissionsMatrixAsync` mantiene cohesión: "operaciones de self-management del account". Si en el futuro se necesitan CRUDs sobre estas tablas, van en sus propios repos.
- **Detección de current connection = cerrar todas** (elegido) vs. excluir por UserAgent+IP. Matchear UserAgent+IP es frágil (NAT, mobile IP rotation). Más simple y consistente: "revoke-others" cierra todas, la actual se reabre en el siguiente ping. Aceptable porque SignalR/WebSocket-style connections son de corta vida; un usuario que hace revoke-others y sigue usando la app obtiene una nueva fila en pocos segundos.
- **Soft-revoke en `UserConnectionLog` con `IsActiveSession = 0` + `DisconnectionDate = UtcNow`** (elegido) vs. hard delete. La tabla tiene `GcRecord` y `IsActiveSession` para auditoría. Hard delete rompe historial de conexiones.
- **Soft-revoke en `UserRefreshToken` con `IsRevoked = 1` + `LastModified = UtcNow`, sin tocar `GcRecord`** (elegido) vs. soft-delete vía `GcRecord`. `IsRevoked` ya tiene semántica de "revocado, no usar para refresh". `GcRecord` es para soft-delete del registro. Mezclar las dos es confuso.
- **`refresh_token_id` como claim JWT** (elegido) vs. header `X-Refresh-Token-Id`. Claim vive en el token, el cliente no tiene que gestionarlo. HMAC-signed → no se puede falsificar. Tradeoff: un token viejo (sin claim) tiene `RefreshTokenId = null` → revoke-others cierra TODOS los tokens (incluido el actual). Documentado en F3. Aceptable porque la ventana de tokens viejos es el periodo de deploy.
- **`RoleName` en `MyPermissionsDto` con formato `"<Rol1> + N más"` cuando hay múltiples roles** (elegido) vs. devolver un array de `{roleId, roleName, modules}` (un module por rol). El shape de SPEC 25 (`RoleSystemOptionMatrixDto`) tiene `roleId` + `roleName` únicos. Romper el shape para my-permissions fuerza al front a tener dos renderers distintos. El formato "X + N más" es feo pero la matrix ya está OR-merged → no se pierde info.
- **`MyPermissionsDto` con `RoleIds[]` aparte del `RoleName` resumen** (elegido) vs. solo `RoleName`. El front puede mostrar "Tus roles: Admin, Operador" sin parsear el string.
- **Query fresh en `GetUserPermissionsMatrixAsync` con CTE + GROUP BY** (elegido) vs. reusar `permissions:v2:{companyId}:{userId}` cache del `PermissionService`. El cache está en shape flat por Controller; para agrupar por Module hay que hacer un JOIN en memoria con un catálogo cacheado aparte. Fresh query con CTE es 1 round-trip, ~5ms, y devuelve exactamente el shape necesario sin acoplar a la estructura interna del PermissionService. La UI de "Mis accesos" se abre 1-2 veces por sesión; 5ms es invisible.
- **`CanRead` como `MAX(CASE WHEN rso.GcRecord = 0 THEN rso.CanRead END)`** (elegido) vs. `MAX(CAST(rso.CanRead AS INT))` o `OR_AGG`. CASE WHEN es portable SQL Server + Postgres (con ajuste mínimo). MAX bool no funciona en Postgres (no hay tipo bool agregado). CASE WHEN con NULL → 0/1 → MAX → siempre 0 o 1. Decisión portable.
- **`?companyId=` query param ignorado** (elegido) vs. override. SPEC 23 + CLAUDE.md son claros: tenant del JWT. Aceptar el param abre la puerta a leaks cross-tenant si el front lo manda mal. El front lee el companyId del header si lo necesita.
- **Cero migraciones EF** (elegido) vs. agregar columna `LastSeenUtc` o `DeviceId` a `UserConnectionLog`. No se necesita para este spec. Lo que ya existe (`IsActiveSession`, `DisconnectionDate`, `UserAgent`, `IpAddress`) cubre todo.
- **Validator solo en `RevokeMySessionCommand`** (elegido) vs. en `RevokeOtherMySessionsCommand` también. El segundo no tiene payload → no hay qué validar. Su validación es "user autenticado + tenant" que ya hace el handler.
- **Endpoint `revoke-others` como POST sin body** (elegido) vs. POST con body `{}` o POST con query `confirm=true`. Convención REST: POST sin body para "action" sobre el recurso current. ASP.NET lo soporta nativamente.
- **Mapeo `CANNOT_REVOKE_CURRENT` → 400** (elegido) vs. 409. 400 = "la request es inválida porque vos no podés cerrar tu sesión actual, primero cerrá la app o usá logout". 409 = "conflicto con el estado". 400 es más claro para el cliente: el problema está en la request, no en el estado del recurso.
- **Decisión diferida de cache invalidation tras revoke** (elegido) vs. invalidar `permissions:v2:`. Un revoke de session NO cambia los permisos del user (los roles siguen igual). El cache de permissions sigue válido. Invalidar sería overhead innecesario. Si en el futuro "revoke session" se extiende a "revoke + reasignar roles", se invalida.
- **Quick definition (rest of spec generado en una sola pasada)** — usuario pidió "procede con el spec 26" tras confirmar Header + Scope. Resto de secciones generadas en bloque respetando estructura del template; decisiones cerradas en Phase 2; un solo ciclo de confirmación al final.

---

## Identified risks

| Riesgo | Mitigación |
|---|---|
| Token viejo sin claim `refresh_token_id` → `revoke-others` cierra el token actual, cliente deslogueado | Aceptable. Ventana = tiempo de deploy. Documentar en `CURL_REQUESTS.md`. Solución definitiva: rotar todos los tokens activos en el deploy (migration data). |
| `MAX(CASE WHEN rso.GcRecord = 0 THEN rso.CanRead END)` no es portable a Postgres sin ajuste | Testear la query en CI contra Postgres cuando se agregue. Cambio mínimo: usar `BOOL_OR(rso.CanRead) FILTER (WHERE rso.GcRecord = 0)`. |
| CTE con LEFT JOIN a `RoleSystemOptions` puede ser lento con miles de `SystemOptions` y muchos `RoleSystemOption` | Índice en `Security.RoleSystemOptions (CompanyId, RoleId, SystemOptionId, GcRecord)`. Si sigue lento, denormalizar a tabla `UserEffectivePermissions` con trigger. |
| Race entre `revoke-others` y un login concurrente del mismo user | El INSERT del nuevo `UserRefreshToken` puede llegar después del UPDATE de `revoke-others` → el nuevo token sobrevive. Eso es lo correcto: el login del user legítimo debe funcionar. La ventana de carrera es <100ms. |
| Cross-user: el `FindActiveByIdAsync` no filtra por user; depende del handler para verificar | El handler llama `GetUserIdBySessionIdAsync` después. Si la verificación falla → 404. Dos queries, pero la defensa en profundidad es lo correcto. |
| `RoleName = "<Rol1> + N más"` es ugly para el front | Documentado. Si el front quiere ver todos los roleNames, agregamos `RoleNames[]` al DTO en spec futura. Por ahora, el `RoleIds[]` ya da la lista. |
| Cambio de `JwtTokenGenerator.GenerateAccessToken` firma: rompe callers no actualizados | Build falla → 0 escapes. Compilador detecta. |
| Soft-revoke de TODAS las connections sin excluir current: el cliente SignalR se desconecta brevemente hasta el próximo ping | Aceptable. El handler de SignalR reabre la connection. Latencia típica <1s. |
| Cache `permissions:v2:` no invalidado tras revoke → user ve permisos stale si cambió roles en la misma request | Imposible en este spec (no se cambian roles). Si en spec futura se hace, agregar `IPermissionService.InvalidateUserCacheAsync(companyId, userId)` (el mismo método de SPEC 25). |
| `GetUserPermissionsMatrixAsync` devuelve matrix vacía para user recién creado (sin roles asignados) | OK. Devuelve 200 con módulos poblados y `Granted = all false`. La UI puede mostrar "no tenés permisos asignados, contactá al admin". |

---

## What is NOT in this spec

- **Endpoints 9 (MFA), 11 (confirm email change), 12 (verify phone), 13 (security activity log)** — van en specs 27, 28, 29 separadas. Acordado en split inicial. MFA específicamente requiere app externa (TOTP authenticator) y se difiere a una spec propia.
- **Migración EF** — `UserConnectionLogs` y `UserRefreshTokens` ya tienen todos los campos necesarios. No se agregan columnas ni tablas. Cero migration.
- **Cambios en `GET /sessions`** — el shape del DTO se mantiene idéntico. La heurística `IsCurrent = true` por `LastActivityAtUtc DESC` sigue; el nuevo claim JWT es complementario, no reemplaza la heurística visible para el front.
- **`POST /account/change-password`, `POST /account/request-email-change`, `GET/PUT /account/profile`** — sin cambios. No se tocan otros endpoints del `AccountController`.
- **Cross-tenant `my-permissions`** (superadmin) — siempre tenant del JWT. Si se necesita, spec aparte.
- **Paginación de `my-permissions`** — la matrix es completa. Si el seed crece a >1000 options, spec aparte con `string_agg` o paginación.
- **`revoke-others` con filtro por device/IP** — fuera de scope. Solo "current vs others" (sin current detection en connections).
- **Hard delete de `UserConnectionLog` / `UserRefreshToken`** — soft-only. Las tablas tienen `IsActiveSession`/`IsRevoked` para esto.
- **Invalidación de cache `permissions:v2:` tras revoke** — un cambio de permisos no aplica acá. El cache sigue válido.
- **Audit log de eventos de security (login OK/fail, password change, role change)** — eso es spec 28.
- **Rotación masiva de tokens en deploy para evitar la ventana de tokens sin claim** — operacional, no es parte de la spec. Documentar como runbook.
