# SPEC 30 — Paridad de soft-delete en `UserConnectionLogs` y cobertura de integración del repo de sesiones

> **Status:** Aprobado
> **Depends on:** SPEC 06 (infraestructura `tests/IntegrationTests/` con Testcontainers.MsSql + WebApplicationFactory), SPEC 26 (AccountController session-revoke endpoints + `RoleUserSessionRepository`).
> **Date:** 2026-08-18
> **Objective:** Cerrar la grieta que dejó pasar el bug SQL 207 ("Invalid column name 'GcRecord'") en `RoleUserSessionRepository` corrigiendo la causa raíz — `Security.UserConnectionLogs` no tiene columna `GcRecord` aunque el resto del schema sí— y sumando cobertura de integración que ejecute SQL real contra SQL Server, porque los unit tests mockean el repo y CI no detecta desalineaciones modelo↔DDL.

---

## Por qué existe esta spec

El 2026-08-18, al ejercitar `DELETE /api/v1/account/sessions/{id}` con el fix inicial de SPEC 26, el endpoint devolvía HTTP 500 con `Invalid column name 'GcRecord'` (SQL Server error 207). Causa raíz: `RoleUserSessionRepository` filtraba por `GcRecord = 0` en **dos** tablas:

| Tabla | ¿Tiene `GcRecord`? | Hereda de |
|---|---|---|
| `Security.UserRefreshTokens` | Sí | `BaseAuditableEntity` (soft-delete) |
| `Security.UserConnectionLogs` | **No** | `BaseEntity` (sin soft-delete) |

La migración `20260514081921_InitialReset.cs:634-658` creó `UserConnectionLogs` con 8 columnas (`Id, UserId, IpAddress, Country, UserAgent, ConnectionDate, IsActiveSession, DisconnectionDate`) — sin `GcRecord`. La configuración EF (`UserConnectionLogConfiguration.cs`) tampoco la declara, coherente con la entidad.

Workaround aplicado el 2026-08-18 (commit a generar): quitar `AND GcRecord = 0` de las 4 queries Dapper que tocan `UserConnectionLogs` en `RoleUserSessionRepository.cs`. Eso elimina el 500 pero deja dos problemas sin resolver:

1. **Asimetría de schema.** Cada tabla `Security.*` soft-deletable del repo (Roles, Users, RoleSystemOptions, UserRoleCompanies, UserCompanies, UserRefreshTokens, PhoneVerificationCodes, UserMfaRecoveryCodes, SecurityEventLogs) tiene `GcRecord`. `UserConnectionLogs` queda como la única excepción. Cualquier query nueva que asuma `GcRecord` (futuro reporte de sesiones, GDPR delete, etc.) va a romper igual.
2. **Test gap.** Los unit tests de `RevokeMySessionCommandHandler` mockean `IRoleUserSessionRepository` (5 tests en `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Account/Commands/RevokeMySession/`). Ninguno ejecuta SQL real. El bug de SQL 207 pasó CI sin pena porque no hay aserción modelo↔DDL en el pipeline.

Esta spec cierra ambos. No entra en conflicto con SPECs 27/28/29: esas cubren ciclo de vida admin, membresía multi-empresa y bitácora de auditoría — todas sobre `Security.Users` / `Security.Roles` / tablas de memberships. Ninguna toca `UserConnectionLogs` ni `RoleUserSessionRepository`.

---

## Scope

**In:**

- **F1. Migración nueva `AddGcRecordToUserConnectionLogs`.** Agrega la columna `GcRecord INT NOT NULL CONSTRAINT DF_UserConnectionLogs_GcRecord DEFAULT 0` a `Security.UserConnectionLogs`. Backfill implícito vía DEFAULT para filas existentes. Nueva migración: `src/3.Persistence/Migrations/<timestamp>_AddGcRecordToUserConnectionLogs.cs` (+ Designer + snapshot update).
- **F2. Entidad + EF config.** Migrar `UserConnectionLog` de `BaseEntity` a `BaseAuditableEntity` para ganar `Created`/`CreatedBy`/`LastModified`/`LastModifiedBy`/`GcRecord`. Actualizar `UserConnectionLogConfiguration.cs` para declarar las 5 columnas nuevas (default `0` en `GcRecord`, default `DateTime.UtcNow` en `Created`).
- **F3. Restaurar filtro `GcRecord = 0` en `RoleUserSessionRepository`.** Revertir el workaround aplicado el 2026-08-18 — volver a poner `AND GcRecord = 0` en las 4 queries Dapper que tocan `UserConnectionLogs`. Tests deben pasar igual contra la DB con la columna presente.
- **F4. Tests de integración del repo.** Nueva clase `RoleUserSessionRepositoryIntegrationTests` en `tests/IntegrationTests/Security/RoleUserSessionRepositoryIntegrationTests.cs`, con `[Collection("IntegrationTests")]` para reusar el `CustomWebApplicationFactory` de SPEC 06. Cubre los 4 métodos mutantes del repo contra SQL Server real en Testcontainers: `FindActiveByIdAsync` (match en cada tabla, miss, soft-deleted), `GetUserIdBySessionIdAsync` (match + leak cross-user), `SoftRevokeSingleRefreshTokenAsync` (revoca, no toca current, idempotente), `SoftRevokeSingleConnectionAsync` (revoca, respeta `IsActiveSession = 1`), `SoftRevokeRefreshTokensExceptAsync`, `SoftRevokeActiveConnectionsAsync`. Mínimo 10 casos.
- **F5. Test end-to-end del endpoint.** Caso mínimo en `tests/IntegrationTests/Security/AccountSessionsRevokeTests.cs`: `DELETE /api/v1/account/sessions/{id}` con JWT válido → 200 + `RevokedTokens = 1`. Verifica que la cadena completa (filter + handler + repo Dapper + DB) cierra sin 500. Reusa el patrón de `RegisterEndpointTests` de SPEC 06.

**Out of scope (explícito):**

- Poblar `UserConnectionLogs` desde `LoginCommandHandler` (sesión "vieja" estilo cookie/header). Ese puente pertenece a una spec aparte — implica wiring de `ISecurityEventLogger`/`ICurrentUserService` a un nuevo side-effect, y SPEC 26 ya cerró el self-management sin requerirlo.
- Backfill de `Created`/`CreatedBy` en filas pre-existentes. Las filas viejas quedarán con `Created = DateTime.UtcNow` (default) y `CreatedBy = NULL`. Si en algún reporte se necesita el real, se aborda en spec de data-migration posterior.
- Soft-delete de filas de `UserConnectionLogs` (i.e. un endpoint admin que cierre sesiones masivamente por userId distinto del dueño). Ya hay `SoftRevokeActiveConnectionsAsync`; lo que falta es el handler/endpoint que lo invoque desde el controller de admin, fuera del scope de self-management de SPEC 26.
- Reescribir `LoginCommandHandler` para escribir una fila `UserConnectionLog` por login. Idem punto anterior.
- Tests de integración adicionales (LoginCommandHandler, queries de `GetMySessions`, etc.). Esta spec cubre solo el repo de sesiones + endpoint revoke. Otros endpoints se cubren cuando aparezca el spec que los implemente.
- Cambios en SPECs 27/28/29. Confirmado en lectura: ninguna referencia a `UserConnectionLogs` ni `RoleUserSessionRepository`.

---

## Data model

### Cambio en `UserConnectionLog` (modifica existente)

```csharp
// Antes
public class UserConnectionLog : BaseEntity

// Después
public class UserConnectionLog : BaseAuditableEntity
```

Gana las 5 propiedades de `BaseAuditableEntity`: `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `GcRecord`. La entidad ya tiene `UserId` (nullable Guid desde `20260725105425_MakeUserConnectionLogUserIdOptional`) — se mantiene.

### Cambio en EF config (`UserConnectionLogConfiguration.cs`)

Agregar:

```csharp
builder.Property(log => log.Created)
    .IsRequired();

builder.Property(log => log.CreatedBy)
    .HasMaxLength(256);   // convención del resto del schema

builder.Property(log => log.GcRecord)
    .IsRequired()
    .HasDefaultValue(BaseAuditableEntity.ActiveGcRecord);  // = 0
```

`LastModified`/`LastModifiedBy`/`IpAddress`/`Country`/`UserAgent`/`ConnectionDate`/`IsActiveSession`/`DisconnectionDate` sin cambios.

### Migración nueva

```csharp
migrationBuilder.AddColumn<int>(
    name: "GcRecord",
    schema: "Security",
    table: "UserConnectionLogs",
    type: "int",
    nullable: false,
    defaultValue: 0);

migrationBuilder.AddColumn<DateTime>(
    name: "Created",
    schema: "Security",
    table: "UserConnectionLogs",
    type: "datetime2",
    nullable: false,
    defaultValueSql: "GETUTCDATE()");  // o defaultValue: new DateTime(...)

migrationBuilder.AddColumn<string>(
    name: "CreatedBy",
    schema: "Security",
    table: "UserConnectionLogs",
    type: "nvarchar(256)",
    maxLength: 256,
    nullable: true);

migrationBuilder.AddColumn<DateTime>(
    name: "LastModified",
    schema: "Security",
    table: "UserConnectionLogs",
    type: "datetime2",
    nullable: true);

migrationBuilder.AddColumn<string>(
    name: "LastModifiedBy",
    schema: "Security",
    table: "UserConnectionLogs",
    type: "nvarchar(256)",
    maxLength: 256,
    nullable: true);
```

Sin DROP / RENAME de columnas existentes — la migration es ADITIVA. Idempotente contra el estado actual (todas las columnas target son nuevas).

### `RoleUserSessionRepository` (revierte workaround)

Re-aplicar `AND GcRecord = 0` en las 4 queries que tocan `UserConnectionLogs`:

```csharp
// FindActiveByIdAsync (línea ~31)
WHERE Id = @sessionId AND IsActiveSession = 1 AND GcRecord = 0

// GetUserIdBySessionIdAsync (línea ~55)
WHERE Id = @sessionId AND GcRecord = 0

// SoftRevokeActiveConnectionsAsync (línea ~92)
WHERE UserId = @userId AND IsActiveSession = 1 AND GcRecord = 0

// SoftRevokeSingleConnectionAsync (línea ~128)
WHERE Id = @connectionId AND IsActiveSession = 1 AND GcRecord = 0
```

`UserRefreshTokens` mantiene `GcRecord = 0` como ya estaba.

---

## Acceptance criteria

**F1–F3 (schema + repo):**

- `dotnet ef migrations add AddGcRecordToUserConnectionLogs --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi` produce una migration ADITIVA (solo `AddColumn`, ningún `DropColumn`/`AlterColumn`/`RenameColumn`).
- `dotnet ef database update` aplica la migration limpiamente sobre una DB existente con filas en `UserConnectionLogs`. Las filas pre-existentes quedan con `GcRecord = 0`, `Created = <utc now>`, `CreatedBy = NULL`.
- `dotnet build` 0 errores / 0 warnings nuevos.
- Unit tests `RevokeMySession` (5) + restantes de `Account` (43 total) siguen verdes.
- `RoleUserSessionRepository` re-compila con las 4 queries restauradas al filtro `GcRecord = 0`.

**F4 (integration tests repo):**

- `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj --filter "FullyQualifiedName~RoleUserSessionRepositoryIntegrationTests"` corre contra SQL Server en Testcontainers y pasa los 10 casos mínimos:
  1. `FindActiveByIdAsync` match en `UserRefreshTokens` (Type=0).
  2. `FindActiveByIdAsync` match en `UserConnectionLogs` (Type=1).
  3. `FindActiveByIdAsync` retorna null cuando el Id solo existe en `GcRecord > 0`.
  4. `FindActiveByIdAsync` retorna null para Guid inexistente.
  5. `GetUserIdBySessionIdAsync` retorna el UserId correcto para cada tabla.
  6. `SoftRevokeSingleRefreshTokenAsync` afecta 1 fila y respeta `Id <> @currentRefreshTokenId`.
  7. `SoftRevokeSingleRefreshTokenAsync` retorna 0 cuando ya estaba revocada (idempotente).
  8. `SoftRevokeSingleConnectionAsync` afecta 1 fila solo si `IsActiveSession = 1`.
  9. `SoftRevokeActiveConnectionsAsync` cierra todas las activas del user en una sola query.
  10. `SoftRevokeRefreshTokensExceptAsync` cierra todas las activas excepto la del param `@currentRefreshTokenId`.
- Cada test prepara su propio seed (user + connection log + refresh token) usando `ApplicationDbContext` resuelto del scope de `CustomWebApplicationFactory`, y limpia al final con `GcRecord` stamp sobre las filas que creó (no `DELETE` — preserva el patrón append-only / soft-delete del repo).
- CI workflow `.github/workflows/ci.yml` corre los integration tests en el step que ya existe (post-unitarios, sin gate de Coverlet), según SPEC 06.

**F5 (e2e endpoint):**

- `AccountSessionsRevokeTests.RevokeOtherUsersSession_Returns200_WhenSessionExists` ejercita el endpoint completo: register user → login → crear segunda sesión (login en otra request) → `DELETE /api/v1/account/sessions/{secondSessionId}` → assert `HttpStatusCode.OK` + body `RevokedTokens == 1`.
- Sin 500. Sin dependency de red externa (mocks de `IEmailService`/`ISmsService` ya están en `CustomWebApplicationFactory` desde SPEC 06).

---

## Risks

- **Backfill `Created = GETUTCDATE()`** asigna la fecha de la migration a filas históricas. Aceptable para una tabla de logs (la fecha relevante para reports suele ser `ConnectionDate`, no `Created`). Documentar en el commit message.
- **EF migration auto-diff** puede regenerar la migración con cambios no deseados si `ApplicationDbContextModelSnapshot` no se commitea fresco. Workaround: snapshot se actualiza con `dotnet ef migrations add`, luego commit de los 3 archivos (migration + Designer + snapshot) como un solo PR atómico.
- **Tests integration lentos** — Testcontainers levanta SQL Server por fixture. SPEC 06 ya eligió container-per-fixture; esta spec hereda esa decisión. Si el tiempo de CI se vuelve problema, se aborda en spec aparte con collection-fixture sharing.
- **Re-correr F4 contra la DB pre-migration** (si alguien corre los tests en una DB sin la columna) reproduce el bug original. Mitigación: el setup del test falla fast con `Assert.Fail` si `SELECT GcRecord FROM Security.UserConnectionLogs` no existe.

---

## Files touched (resumen)

```
src/1.Domain/Security/UserConnectionLog.cs                          [F2] BaseEntity → BaseAuditableEntity
src/3.Persistence/Configuration/Security/UserConnectionLogConfiguration.cs  [F2] agregar 5 props
src/3.Persistence/Repositories/Security/RoleUserSessionRepository.cs        [F3] restaurar GcRecord = 0 (4 sitios)
src/3.Persistence/Migrations/<ts>_AddGcRecordToUserConnectionLogs.cs        [F1] nueva migration
src/3.Persistence/Migrations/<ts>_AddGcRecordToUserConnectionLogs.Designer.cs [F1]
src/3.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs           [F1] snapshot update
tests/IntegrationTests/Security/RoleUserSessionRepositoryIntegrationTests.cs  [F4] nueva, 10 casos mínimos
tests/IntegrationTests/Security/AccountSessionsRevokeTests.cs                  [F5] nuevo, e2e mínimo
```

Sin cambios en `src/2.Application/`, `src/3.Infrastructure/`, `src/4.Services.WebApi/Controllers/`. Sin cambios en unit tests existentes.

---

## Out of SPECs 27/28/29 (confirmado)

- SPEC 27: ciclo de vida admin (invite, status, force-reset) + fix `TransactionBehavior` + `IUserAdminRepository`. No toca `UserConnectionLogs` ni `RoleUserSessionRepository`.
- SPEC 28: memberships multi-empresa, permisos efectivos, bulk roles, reporte paginado. No toca `UserConnectionLogs`.
- SPEC 29: tabla `Security.AuditLogs` + `IAuditLogger` + endpoint `GET /Audit/security`. No toca `UserConnectionLogs`.

Las tres son ortogonales a esta spec. Se pueden implementar en cualquier orden — esta spec **no depende** de ellas y ellas **no dependen** de esta.
