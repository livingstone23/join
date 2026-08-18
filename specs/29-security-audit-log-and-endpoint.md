# SPEC 29 — Bitácora de seguridad: tabla de auditoría con diff y endpoint de consulta

> **Status:** Borrador
> **Depends on:** SPEC 17 (PermissionResource + flag override), SPEC 23 (tenant desde el token), SPEC 25 (`RoleSystemOptionsRepository.BulkUpsertAsync`, camino Dapper a instrumentar), **SPEC 27** (flip de estado por Dapper a instrumentar), **SPEC 28** (`ReplaceUserRoles` reescrito, `BulkUpdateUserRoles`, `AddUserCompany`, `RemoveUserCompany`, todos a instrumentar)
> **Date:** 2026-08-15
> **Objective:** Dar respuesta consultable a "quién cambió qué permiso y cuándo" creando la tabla `Security.AuditLogs` con diff de valores viejo/nuevo, un servicio `IAuditLogger` invocado explícitamente desde cada handler que muta roles, permisos, usuarios o membresías —único mecanismo que cubre tanto los caminos EF como los Dapper—, y el endpoint `GET /api/v1/Audit/security` para leerla.

---

## Por qué existe esta spec

Tercera y última de las specs en que se partió el bloque "Administración de usuarios de la empresa" del backlog (items 15 a 22). Cubre el item 22.

| Spec | Items | Núcleo |
|---|---|---|
| 27 | 15, 16, 17 | Ciclo de vida admin: invite + status + force-reset. |
| 28 | 18, 19, 20, 21 | Membresía multi-empresa, permisos efectivos, roles bulk, reporte paginado. |
| **29** (esta) | 22 | Bitácora de seguridad: tabla + diff + endpoint. |

Es la última de la cadena **27 → 28 → 29**: instrumenta handlers que las dos specs anteriores crean o reescriben.

### El hallazgo que define el diseño

El planteo obvio del item 22 sería extender el `AuditableEntitySaveChangesInterceptor` existente para que capture valores viejo/nuevo. **No funciona**, por dos motivos independientes y cada uno suficiente.

**Motivo 1: el interceptor no ve dos de las tres entidades que el item 22 nombra.** `AuditableEntitySaveChangesInterceptor.cs:52` recorre `ChangeTracker.Entries<BaseAuditableEntity>()`:

| Entidad del item 22 | Herencia | ¿La ve el interceptor? |
|---|---|---|
| `RoleSystemOption` | `BaseTenantEntity : BaseAuditableEntity` | Sí |
| `ApplicationRole` | `IdentityRole<Guid>, IAuditableEntity` | **No** |
| `ApplicationUser` | `IdentityUser<Guid>, IAuditableEntity` | **No** |

**Motivo 2: los caminos que más importan no pasan por EF.**

- `RoleSystemOptionsRepository.BulkUpsertAsync` (SPEC 25, ya implementado) es Dapper con transacción explícita. Es **la** vía por la que el front guarda la matriz de permisos de un rol. Ningún interceptor de `SaveChanges` la ve.
- El cambio de estado de usuario de la SPEC 27 es Dapper por diseño, para esquivar el filtro global `u.GcRecord == 0 && u.IsActive` de `ApplicationUserConfiguration`.

O sea: un interceptor de EF auditaría casi nada de lo que el item 22 quiere registrar. De ahí la decisión de fondo de esta spec: **captura explícita vía `IAuditLogger` invocado desde los handlers**, que funciona igual con EF y con Dapper.

### Estado previo de la auditoría en el repo

No hay ninguna tabla de auditoría. `src/1.Domain/Audit/` contiene solo `IAuditableEntity`, `BaseEntity`, `BaseAuditableEntity` y `BaseTenantEntity`, y lo que se persiste son stamps (`Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `GcRecord`). Esos campos dicen **quién tocó la fila por última vez**, no **qué cambió**. No existe `JOIN.Contracts/Audit` — el árbol `src/` solo tiene los seis proyectos del layer map.

---

## Scope

**In:**

### A. Dominio

- `src/1.Domain/Audit/AuditLog.cs` (nuevo) — entidad de la bitácora. **No** hereda de `BaseAuditableEntity`: es append-only y no tiene sentido auditar la auditoría ni soft-deletearla. Hereda de `BaseEntity` (solo `Id`).
- `src/1.Domain/Audit/AuditedEntity.cs` (nuevo) — enum de las entidades auditadas: `Role = 1, User = 2, RoleSystemOption = 3, UserRoleCompany = 4, UserCompany = 5, RoleCompany = 6`.
- `src/1.Domain/Audit/AuditAction.cs` (nuevo) — enum `Created = 1, Updated = 2, Deleted = 3`.

### B. Servicio de escritura

- `src/2.Application/Interface/IAuditLogger.cs` (nuevo) — interfaz con `LogAsync` y `LogManyAsync` (firmas en la sección Data model).
- `src/3.Infrastructure/Audit/AuditLogger.cs` (nuevo) — implementación. Resuelve `ChangedBy` e `IpAddress` desde `ICurrentUserService`, el `CompanyId` del token, y persiste vía `IAuditLogRepository`. Scoped.
- `src/3.Infrastructure/Audit/AuditDiffBuilder.cs` (nuevo, static) — arma los pares `OldValuesJson` / `NewValuesJson`, **incluyendo solo las claves que cambiaron** y **excluyendo siempre** una lista negra de campos sensibles.
- Registro en `src/3.Infrastructure/DependencyInjection.cs`.

### C. Persistencia

- `src/3.Persistence/Configuration/Audit/AuditLogConfiguration.cs` (nuevo) — tabla `AuditLogs` en el schema `Security`. **Sin query filter global** (no hay `GcRecord`).
- `src/3.Persistence/Contexts/ApplicationDbContext.cs` — `DbSet<AuditLog>` nuevo.
- Una sola migración EF: `AddSecurityAuditLog`, con la tabla y dos índices:
  - `IX_AuditLogs_Entity` sobre `(EntityName, EntityId, ChangedAtUtc DESC)`.
  - `IX_AuditLogs_Company_ChangedAt` sobre `(CompanyId, ChangedAtUtc DESC)`.
- `src/2.Application/Interface/Persistence/Audit/IAuditLogRepository.cs` (nuevo) — `InsertAsync`, `InsertManyAsync`, `ListPagedAsync`.
- `src/3.Persistence/Repositories/Audit/AuditLogRepository.cs` (nuevo) — Dapper + `ISqlConnectionFactory`. Registro en `src/3.Persistence/Configuration/ConfigureServices.cs`.

### D. Instrumentación de los handlers

Cada handler que muta una de las seis entidades auditadas llama `IAuditLogger` **después** de que la escritura tuvo éxito y **antes** de devolver el `Response<T>` exitoso. Los nombres exactos de los handlers se confirman al implementar; el listado por área es:

- **Roles** (SPEC 18): create, update y delete de `ApplicationRole` → `AuditedEntity.Role`.
- **RoleCompanies** (SPEC 19): alta y baja de disponibilidad de un rol en un tenant → `AuditedEntity.RoleCompany`.
- **RoleSystemOptions**: `POST` (single create), `PUT /{id}` (single update), `DELETE /{id}` → `AuditedEntity.RoleSystemOption`.
- **RoleSystemOptions bulk** (SPEC 25, `BulkUpsertAsync`, **Dapper**): el handler ya recibe de vuelta `{ created, updated, removed }`. Se registra **una fila de auditoría por cada `SystemOptionId` afectado**, con la acción correspondiente. Es el caso que motiva el item 22.
- **Users** (SPEC 27): `InviteUser` (alta → `AuditedEntity.User`, más una fila `UserRoleCompany` por rol asignado y una `UserCompany` por la membresía), `ChangeUserStatus` (**Dapper** → `AuditedEntity.User`, con el `reason` en `Metadata`).
- **Users roles** (SPEC 28): `ReplaceUserRoles` y `BulkUpdateUserRoles` → una fila `UserRoleCompany` por rol agregado o quitado.
- **UserCompanies** (SPEC 28): `AddUserCompany` y `RemoveUserCompany` → `AuditedEntity.UserCompany`, más las filas `UserRoleCompany` correspondientes.
- **UserCompanies** (existente): `SetDefaultCompany` → `AuditedEntity.UserCompany` con el diff de `IsDefault`.

### E. Lectura

- `src/2.Application/UseCases/Security/Audit/Queries/GetSecurityAuditLog/` — query (`IRequest<Response<PagedResult<SecurityAuditLogItemDto>>>`), handler, validator.
- `src/2.Application.DTO/Security/Audit/SecurityAuditLogItemDto.cs` (nuevo).
- `src/2.Application.DTO/Security/Audit/AuditFieldChangeDto.cs` (nuevo) — `{ Field, OldValue, NewValue }`. El handler deserializa `OldValuesJson`/`NewValuesJson` y los cruza en una lista de cambios por campo, para que el front no tenga que parsear JSON crudo.
- Filtros: `entity`, `entityId`, `changedBy`, `action`, `fromDate`, `toDate`, `pageNumber`, `pageSize`, `allTenants`.
- Tenant-scoped por el `CompanyId` del token. `allTenants = true` solo lo respeta un SuperAdmin; para el resto se ignora silenciosamente y se sigue filtrando por su tenant.
- Clamps: `PageNumber >= 1`, `PageSize` en `[1, 100]`, default 20. Orden fijo `ChangedAtUtc DESC, Id DESC`.
- Branching de la cláusula de paginación entre `LIMIT/OFFSET` y `OFFSET … FETCH NEXT`, como el resto de los handlers paginados.

### F. Controller

- `src/4.Services.WebApi/Controllers/Security/AuditController.cs` (nuevo) — `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/[controller]")]`, `[Produces("application/json")]`, `[PermissionResource("Audit")]`.
- `GET /api/v1/Audit/security` — un solo endpoint. Default por verbo HTTP → `CanRead`.
- `src/3.Persistence/Seed/DatabaseSeeder.cs` — agregar la `SystemOption` de `Audit` (con `ControllerName = "Audit"` y `CanRead`) al seed idempotente de menú y permisos. **Sin esto el endpoint devuelve 403 para todos salvo SuperAdmin**, porque el `DynamicAuthorizationFilter` falla cerrado.

### G. Tests

- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Audit/Queries/GetSecurityAuditLog/GetSecurityAuditLogQueryHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/Audit/AuditDiffBuilderTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/Audit/AuditLoggerTests.cs`
- Ampliar los tests de los handlers instrumentados con un caso cada uno: "el camino feliz llama `IAuditLogger` con la entidad, la acción y el diff esperados".

**Out of scope (para specs futuras):**

- **Interceptor de EF para capturar el diff automáticamente.** Descartado: `ApplicationRole` y `ApplicationUser` no heredan de `BaseAuditableEntity` y el interceptor actual solo recorre `ChangeTracker.Entries<BaseAuditableEntity>()`; además los caminos que más importan (`BulkUpsertAsync` de SPEC 25, flip de estado de SPEC 27) son Dapper y no pasan por `SaveChanges`.
- **Triggers de base de datos.** Capturarían todo, pero exigen dos implementaciones (SQL Server y Postgres) y rompen la portabilidad que exige `CLAUDE.md`.
- **Job de retención / particionado / archivado.** Sin límite de crecimiento en esta spec, por decisión explícita.
- **`Security.SecurityEventLog` de la SPEC 26** (login, MFA, verificación de teléfono). Es un feed de **eventos de sesión**, no un diff de entidades. Las dos tablas coexisten con propósitos distintos.
- **Auditoría de lecturas** (quién consultó qué). Solo mutaciones.
- **Auditoría de las entidades de negocio** (`Person`, `Customer`, `Ticket`, etc.). Solo las seis entidades de seguridad.
- **Exportar la bitácora** a CSV o Excel (`CanExport`).
- **Endpoint de detalle** `GET /Audit/security/{id}`. El listado ya trae el diff completo de cada fila.
- **Rollback / "deshacer"** un cambio desde la bitácora.
- **Firma criptográfica o encadenamiento hash** de las filas para detectar manipulación.
- **Diff de propiedades de navegación o de colecciones**. Solo columnas planas; las relaciones se auditan como filas propias de su entidad (`UserRoleCompany`, `UserCompany`).
- **Alertas** ante cambios sensibles.
- **Backfill histórico.** La bitácora arranca vacía; lo anterior a esta spec no existe y no se puede reconstruir.
- **Modificar `AuditableEntitySaveChangesInterceptor`.** Sigue haciendo lo que hace (stamps + tenant + soft delete), sin tocar.

---

## Data model

### `AuditLog` (nuevo, schema `Security`)

```csharp
// src/1.Domain/Audit/AuditLog.cs
public class AuditLog : BaseEntity
{
    /// <summary>Entidad afectada. Se persiste el nombre del enum como string, no el int.</summary>
    public string EntityName { get; set; } = string.Empty;      // nvarchar(64)

    /// <summary>Id de la fila afectada.</summary>
    public Guid EntityId { get; set; }

    /// <summary>Etiqueta legible del sujeto: nombre del rol, email del usuario, ruta de la opción.</summary>
    public string? EntityLabel { get; set; }                    // nvarchar(256)

    public string Action { get; set; } = string.Empty;          // nvarchar(16) — Created | Updated | Deleted

    /// <summary>Tenant en cuyo contexto se hizo el cambio, tomado del token de quien lo hizo.</summary>
    public Guid CompanyId { get; set; }

    /// <summary>UserId de quien hizo el cambio, o "System" para procesos internos.</summary>
    public string ChangedBy { get; set; } = string.Empty;       // nvarchar(64)

    public DateTime ChangedAtUtc { get; set; }

    public string? IpAddress { get; set; }                      // nvarchar(45)

    /// <summary>Solo las propiedades que cambiaron. Null en Created.</summary>
    public string? OldValuesJson { get; set; }                  // nvarchar(max)

    /// <summary>Solo las propiedades que cambiaron. Null en Deleted.</summary>
    public string? NewValuesJson { get; set; }                  // nvarchar(max)

    /// <summary>Contexto libre: motivo del cambio, id del lote, etc.</summary>
    public string? MetadataJson { get; set; }                   // nvarchar(max)
}
```

**No hereda de `BaseAuditableEntity`.** Hereda de `BaseEntity` (solo `Id`). La bitácora es append-only: no se modifica, no se soft-deletea, y no tiene sentido auditar la auditoría. Por eso tampoco lleva `GcRecord` ni query filter global.

`CompanyId` **no** es el de la entidad afectada sino el del token de quien hizo el cambio. `Security.Roles` y `Security.Users` no tienen `CompanyId` propio, así que el tenant de la entidad no siempre existe; el del actor siempre sí, y es lo que responde "quién, desde qué empresa".

`EntityName` y `Action` se persisten como **string**, no como int. Una bitácora se lee a mano en SQL cuando hay un incidente; `'RoleSystemOption'` es legible y `3` no.

### Enums

```csharp
// src/1.Domain/Audit/AuditedEntity.cs
public enum AuditedEntity
{
    Role = 1,
    User = 2,
    RoleSystemOption = 3,
    UserRoleCompany = 4,
    UserCompany = 5,
    RoleCompany = 6
}

// src/1.Domain/Audit/AuditAction.cs
public enum AuditAction
{
    Created = 1,
    Updated = 2,
    Deleted = 3
}
```

El query param `?entity=` acepta los nombres del enum, case-insensitive. Un valor fuera del enum corta con `INVALID_ENTITY` (400) en el validator, no devuelve una lista vacía.

### Configuración EF

```csharp
// src/3.Persistence/Configuration/Audit/AuditLogConfiguration.cs
builder.ToTable("AuditLogs", "Security");
builder.HasKey(a => a.Id);
builder.Property(a => a.EntityName).HasMaxLength(64).IsRequired();
builder.Property(a => a.EntityLabel).HasMaxLength(256);
builder.Property(a => a.Action).HasMaxLength(16).IsRequired();
builder.Property(a => a.ChangedBy).HasMaxLength(64).IsRequired();
builder.Property(a => a.IpAddress).HasMaxLength(45);
builder.HasIndex(a => new { a.EntityName, a.EntityId, a.ChangedAtUtc })
       .HasDatabaseName("IX_AuditLogs_Entity");
builder.HasIndex(a => new { a.CompanyId, a.ChangedAtUtc })
       .HasDatabaseName("IX_AuditLogs_Company_ChangedAt");
// Sin HasQueryFilter: la tabla no tiene GcRecord.
```

Sin claves foráneas a `Users`, `Roles` ni `Companies`. Una fila de bitácora tiene que sobrevivir al borrado de la entidad que registra; una FK con `Restrict` bloquearía el borrado y una con `Cascade` destruiría la evidencia.

### `IAuditLogger`

```csharp
// src/2.Application/Interface/IAuditLogger.cs
public interface IAuditLogger
{
    Task LogAsync(
        AuditedEntity entity,
        Guid entityId,
        AuditAction action,
        string? entityLabel = null,
        IReadOnlyDictionary<string, object?>? oldValues = null,
        IReadOnlyDictionary<string, object?>? newValues = null,
        string? metadataJson = null,
        CancellationToken ct = default);

    Task LogManyAsync(
        IEnumerable<AuditLogEntryRequest> entries,
        CancellationToken ct = default);
}

public sealed record AuditLogEntryRequest(
    AuditedEntity Entity,
    Guid EntityId,
    AuditAction Action,
    string? EntityLabel = null,
    IReadOnlyDictionary<string, object?>? OldValues = null,
    IReadOnlyDictionary<string, object?>? NewValues = null,
    string? MetadataJson = null);
```

`LogManyAsync` existe para los casos de lote: el bulk de la matriz de permisos (SPEC 25) puede generar cientos de filas y llamarlas de a una sería un round-trip por fila.

La implementación resuelve por su cuenta `CompanyId`, `ChangedBy`, `IpAddress` y `ChangedAtUtc` desde `ICurrentUserService`; el handler no los pasa. `ChangedBy` cae a `"System"` cuando `ICurrentUserService.UserId` es null, igual que hace el interceptor existente.

### `AuditDiffBuilder`

```csharp
// src/3.Infrastructure/Audit/AuditDiffBuilder.cs
public static class AuditDiffBuilder
{
    private static readonly HashSet<string> Redacted = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
        "MfaSecretKey", "Token", "RefreshToken", "CodeHash"
    };

    /// <summary>
    /// Devuelve (oldJson, newJson) con SOLO las claves cuyo valor cambió.
    /// Las claves de la lista negra se descartan por completo, no se enmascaran.
    /// Si no cambió nada, devuelve (null, null).
    /// </summary>
    public static (string? OldJson, string? NewJson) Build(
        IReadOnlyDictionary<string, object?>? oldValues,
        IReadOnlyDictionary<string, object?>? newValues);
}
```

Comparación con `Equals` sobre el valor, tratando `null` y `string.Empty` como distintos. Serialización con `System.Text.Json` y opciones por defecto.

Los campos de la lista negra **se descartan**, no se enmascaran con `"***"`. Un `"***"` en la bitácora sugiere que el valor está guardado en algún lado; descartarlo deja claro que nunca se registró.

Cuando `Build` devuelve `(null, null)` en una acción `Updated`, no se escribe la fila: un update que no cambió nada no es un evento.

### `IAuditLogRepository`

```csharp
// src/2.Application/Interface/Persistence/Audit/IAuditLogRepository.cs
public interface IAuditLogRepository
{
    Task<int> InsertAsync(AuditLog entry, CancellationToken ct = default);

    Task<int> InsertManyAsync(IEnumerable<AuditLog> entries, CancellationToken ct = default);

    Task<(IReadOnlyList<AuditLog> Items, int TotalCount)> ListPagedAsync(
        Guid? companyId,              // null = todos los tenants (solo SuperAdmin)
        string? entityName,
        Guid? entityId,
        string? changedBy,
        string? action,
        DateTime? fromUtc,
        DateTime? toUtcExclusive,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default);
}
```

`ListPagedAsync` devuelve página y total en un `QueryMultipleAsync`. Orden fijo `ChangedAtUtc DESC, Id DESC` — el `Id` desempata filas del mismo lote, que comparten timestamp al milisegundo.

### DTOs de lectura

```csharp
// src/2.Application.DTO/Security/Audit/AuditFieldChangeDto.cs
public sealed record AuditFieldChangeDto(
    string Field,
    string? OldValue,
    string? NewValue);

// src/2.Application.DTO/Security/Audit/SecurityAuditLogItemDto.cs
public sealed record SecurityAuditLogItemDto(
    Guid Id,
    string EntityName,
    Guid EntityId,
    string? EntityLabel,
    string Action,
    string ChangedBy,
    string? ChangedByName,
    DateTime ChangedAtUtc,
    string? IpAddress,
    IReadOnlyList<AuditFieldChangeDto> Changes,
    string? Metadata);
```

El handler deserializa `OldValuesJson` y `NewValuesJson` y los cruza en `Changes`: unión de las claves de ambos diccionarios, con `OldValue` o `NewValue` en null cuando la clave falta de un lado. El front recibe una lista de cambios por campo y no tiene que parsear JSON crudo.

`ChangedByName` se resuelve con un `LEFT JOIN` a `Security.Users` por `ChangedBy` en la misma query del listado. Queda null si el usuario fue borrado o si `ChangedBy` es `"System"`.

### Query

```csharp
// src/2.Application/UseCases/Security/Audit/Queries/GetSecurityAuditLog/GetSecurityAuditLogQuery.cs
public sealed record GetSecurityAuditLogQuery(
    string? Entity = null,
    Guid? EntityId = null,
    string? ChangedBy = null,
    string? Action = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int PageNumber = 1,
    int PageSize = 20,
    bool AllTenants = false)
    : IRequest<Response<PagedResult<SecurityAuditLogItemDto>>>;
```

Guardas del handler, en orden:

1. `companyId == Guid.Empty` → `TENANT_REQUIRED` (400).
2. `Entity` con valor que no parsea al enum `AuditedEntity` → `INVALID_ENTITY` (400).
3. `Action` con valor que no parsea al enum `AuditAction` → `INVALID_ACTION` (400).
4. `AllTenants = true` sin rol SuperAdmin → se **ignora** y se sigue filtrando por el tenant del token. No es un error: el parámetro simplemente no aplica.
5. Clamps de `PageNumber` y `PageSize`, y normalización de `ToDate` a exclusivo (`ToDate.Date.AddDays(1)`), igual que hace `UserManagementReportQueryHelper.NormalizeDateRange`.

### Ejemplo de fila

Guardar la matriz de permisos de un rol quitándole `CanDelete` sobre una opción produce:

```json
{
  "entityName": "RoleSystemOption",
  "entityId": "9f1c…",
  "entityLabel": "Contador → Personas",
  "action": "Updated",
  "changedBy": "3a2b…",
  "changedByName": "Ana Pérez",
  "changedAtUtc": "2026-08-15T14:22:07Z",
  "ipAddress": "190.12.4.8",
  "changes": [
    { "field": "CanDelete", "oldValue": "True", "newValue": "False" }
  ],
  "metadata": "{\"bulkOperationId\":\"7c4e…\"}"
}
```

`bulkOperationId` es un `Guid` que el handler del bulk genera una vez por request y repite en las N filas del lote, para que la UI pueda agrupar "este operador guardó la matriz y cambió 12 permisos de una vez" en lugar de mostrar 12 eventos sueltos.

---

## Implementation plan

Pre-requisito: **SPEC 27 y SPEC 28 implementadas.** Las fases F7 y F8 instrumentan handlers que esas specs crean o reescriben.

### F1 — Dominio

1. Crear `src/1.Domain/Audit/AuditedEntity.cs` y `src/1.Domain/Audit/AuditAction.cs`.
2. Crear `src/1.Domain/Audit/AuditLog.cs` heredando de `BaseEntity`.
3. `dotnet build -c Release` → 0 errores. Nada cambia en runtime.

### F2 — Persistencia y migración

1. Crear `src/3.Persistence/Configuration/Audit/AuditLogConfiguration.cs` con la configuración de la sección Data model. **Sin `HasQueryFilter`** y **sin claves foráneas**.
2. Agregar `DbSet<AuditLog> AuditLogs` a `ApplicationDbContext`.
3. `dotnet ef migrations add AddSecurityAuditLog --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi`.
4. Verificar en el archivo generado que están los dos índices y que **no** hay FK a `Users`, `Roles` ni `Companies`.
5. `dotnet ef database update` → aplica limpio.

### F3 — Repositorio

1. Crear `src/2.Application/Interface/Persistence/Audit/IAuditLogRepository.cs` con los 3 métodos.
2. Crear `src/3.Persistence/Repositories/Audit/AuditLogRepository.cs` con `ISqlConnectionFactory`:
   - `InsertAsync`: `INSERT INTO Security.AuditLogs (…) VALUES (…)`.
   - `InsertManyAsync`: un solo `ExecuteAsync` con la lista de parámetros (Dapper hace el batch).
   - `ListPagedAsync`: `QueryMultipleAsync` con el `SELECT` paginado más `SELECT COUNT(*)`. Filtros con el patrón `(@Param IS NULL OR Columna = @Param)` de `UserManagementReportQueryHelper`. `LEFT JOIN Security.Users` por `ChangedBy` para traer `ChangedByName` con `CONCAT(u.FirstName, ' ', u.LastName)`. Orden `ChangedAtUtc DESC, Id DESC`. Cláusula de paginación con el branching de proveedor.
3. Registrar en `src/3.Persistence/Configuration/ConfigureServices.cs`.
4. `dotnet build -c Release` → 0 errores.

### F4 — `AuditDiffBuilder` + `IAuditLogger`

1. Crear `src/3.Infrastructure/Audit/AuditDiffBuilder.cs` con la lista negra y el `Build` de la sección Data model.
2. Crear `src/2.Application/Interface/IAuditLogger.cs` con `LogAsync`, `LogManyAsync` y el record `AuditLogEntryRequest`.
3. Crear `src/3.Infrastructure/Audit/AuditLogger.cs`. Resuelve `CompanyId`, `ChangedBy` (con caída a `"System"`), `IpAddress` y `ChangedAtUtc` desde `ICurrentUserService`; llama `AuditDiffBuilder.Build`; descarta la entrada si la acción es `Updated` y el diff quedó vacío; persiste vía `IAuditLogRepository`.
4. **Toda llamada al repositorio se envuelve en `try/catch` con log warning dentro del propio `AuditLogger`**, no en cada handler. Un fallo de la bitácora no puede tumbar la operación de negocio que ya se ejecutó.
5. Registrar `IAuditLogger` como Scoped en `src/3.Infrastructure/DependencyInjection.cs`.
6. Agregar `AuditDiffBuilderTests` (6 casos: solo cambios; sin cambios devuelve `(null, null)`; campo de la lista negra se descarta; clave presente solo en `newValues`; `null` vs `string.Empty` cuenta como cambio; `Created` con `oldValues` null).
7. `dotnet build -c Release` → 0 errores.

### F5 — Instrumentar Roles, RoleCompanies y RoleSystemOptions single

1. Confirmar los nombres exactos de los handlers de `src/2.Application/UseCases/Security/Roles/Commands/`, `RoleCompanies/Commands/` y `RoleSystemOptions/Commands/`.
2. En cada handler de create: `LogAsync(entity, id, AuditAction.Created, label, newValues: dict)`.
3. En cada handler de update: capturar el estado previo **antes** de mutar (el handler ya carga la entidad para actualizarla) y pasar `oldValues` + `newValues`.
4. En cada handler de delete: `LogAsync(entity, id, AuditAction.Deleted, label, oldValues: dict)`.
5. `EntityLabel`: nombre del rol para `Role`; `"<RoleName> → <SystemOptionName>"` para `RoleSystemOption`; `"<RoleName> @ <CompanyName>"` para `RoleCompany`.
6. La llamada va **después** del `SaveAsync` exitoso y **antes** del `return` del `Response<T>` exitoso. En los caminos que cortan con `Response.Error` no se registra nada.
7. `dotnet build -c Release` → 0 errores.

### F6 — Instrumentar el bulk de la matriz de permisos (SPEC 25, Dapper)

1. `BulkUpsertRoleSystemOptionsCommandHandler` ya recibe `{ created, updated, removed }` de `BulkUpsertAsync`. Eso alcanza para saber **qué** filas cambiaron, pero **no** los valores previos de las actualizadas.
2. Para tener el diff de las actualizadas, el handler llama `GetActiveByRoleAndCompanyAsync(roleId, companyId)` **antes** de `BulkUpsertAsync` y guarda el snapshot en memoria. Ese método ya existe (SPEC 25) y ya se usa; verificar si el handler actual lo invoca y reutilizar la lectura en vez de duplicarla.
3. Generar un `bulkOperationId = Guid.NewGuid()` una vez por request.
4. Armar la lista de `AuditLogEntryRequest`: una entrada por `SystemOptionId` de `created` (acción `Created`), de `updated` (acción `Updated`, con el diff contra el snapshot) y de `removed` (acción `Deleted`, con `oldValues` del snapshot). Las `updated` cuyo diff quede vacío **se descartan**.
5. Un solo `LogManyAsync` con toda la lista.
6. `MetadataJson` de cada entrada: `{"bulkOperationId":"<guid>"}`.
7. `dotnet build -c Release` → 0 errores. Smoke: guardar la matriz de un rol cambiando 2 flags → 1 fila de bitácora por opción afectada, todas con el mismo `bulkOperationId`.

### F7 — Instrumentar Users (SPEC 27)

1. `InviteUserCommandHandler`: según el `InviteOutcome`, registrar
   - `Created` → una fila `User` (`Created`, con `Email`/`FirstName`/`LastName`/`IsActive`), una `UserCompany` (`Created`) y una `UserRoleCompany` (`Created`) por rol.
   - `MembershipAdded` → una `UserCompany` (`Created`) y las `UserRoleCompany` correspondientes. **Sin** fila `User`: el usuario no se creó.
   - `InvitationResent` → solo las `UserRoleCompany` que efectivamente cambiaron.
2. `ChangeUserStatusCommandHandler` (**Dapper**): una fila `User` (`Updated`) con el diff de `IsActive` y de `StatusChangeReason`, y `MetadataJson` = `{"reason":"<texto>"}`. Es la única instrumentación de la spec sobre un handler que no toca EF.
3. `ForceUserPasswordResetCommandHandler`: **no se instrumenta**. No muta ninguna de las seis entidades auditadas; es un evento de sesión y su lugar natural es el `SecurityEventLog` de la SPEC 26.
4. `dotnet build -c Release` → 0 errores.

### F8 — Instrumentar roles y membresías de usuario (SPEC 28)

1. `ReplaceUserRolesCommandHandler`: una fila `UserRoleCompany` por rol agregado (`Created`) y por rol quitado (`Deleted`). `EntityLabel` = `"<Email> → <RoleName>"`. Un solo `LogManyAsync`.
2. `BulkUpdateUserRolesCommandHandler`: ídem, con `bulkOperationId` compartido en `MetadataJson`, y solo para los usuarios con `Outcome = Updated`.
3. `AddUserCompanyCommandHandler`: una fila `UserCompany` (`Created`) más las `UserRoleCompany` correspondientes.
4. `RemoveUserCompanyCommandHandler`: una fila `UserCompany` (`Deleted`) más una `UserRoleCompany` (`Deleted`) por cada rol dado de baja.
5. `SetDefaultCompanyCommandHandler`: una fila `UserCompany` (`Updated`) con el diff de `IsDefault`, por cada fila que cambió (la que pierde el default y la que lo gana).
6. `dotnet build -c Release` → 0 errores.

### F9 — Query de lectura

1. Crear `AuditFieldChangeDto` y `SecurityAuditLogItemDto`.
2. Crear `GetSecurityAuditLogQuery` y `GetSecurityAuditLogQueryValidator` (`PageNumber >= 1`; `PageSize` entre 1 y 100; `Entity` y `Action` parseables al enum si vienen con valor; `FromDate <= ToDate`).
3. Crear `GetSecurityAuditLogQueryHandler`: las 5 guardas de la sección Data model, resolución de `AllTenants` según SuperAdmin, llamada a `ListPagedAsync`, y mapeo de cada `AuditLog` a `SecurityAuditLogItemDto` cruzando los dos JSON en `Changes`.
4. La deserialización de los JSON va en un helper privado con `try/catch`: una fila con JSON corrupto devuelve `Changes` vacío y no tumba la página entera.
5. `dotnet build -c Release` → 0 errores.

### F10 — Controller y seed del permiso

1. Crear `src/4.Services.WebApi/Controllers/Security/AuditController.cs` con `[PermissionResource("Audit")]` a nivel de clase y un único `GET security`.
2. Mapeo de errores a HTTP:

   | Código | HTTP |
   |---|---|
   | `TENANT_REQUIRED` | 400 |
   | `INVALID_ENTITY` | 400 |
   | `INVALID_ACTION` | 400 |
   | resto sin éxito | 400 |

3. Agregar al `DatabaseSeeder` la `SystemOption` de auditoría, de forma **idempotente**: `ControllerName = "Audit"`, `CanRead = true`, el resto de los flags en `false`, dentro del módulo de seguridad. Sin esto el `DynamicAuthorizationFilter` falla cerrado y el endpoint devuelve 403 para todos salvo SuperAdmin.
4. Verificar que el seed corre en una base ya migrada.
5. `dotnet build -c Release` → 0 errores.

### F11 — Tests (~24 casos)

- `AuditDiffBuilderTests` — 6 casos (ya agregados en F4).
- `GetSecurityAuditLogQueryHandlerTests` — 9: página con items y `totalCount`; `pageNumber = 0` clampea a 1; `pageSize = 500` clampea a 100; `entity` inválida → `INVALID_ENTITY`; `action` inválida → `INVALID_ACTION`; tenant vacío → `TENANT_REQUIRED`; `allTenants = true` sin SuperAdmin filtra por el tenant del token; `allTenants = true` con SuperAdmin pasa `companyId = null` al repositorio; fila con `OldValuesJson` corrupto devuelve `Changes` vacío sin lanzar.
- `AuditLoggerTests` — 4: arma la fila con `CompanyId`/`ChangedBy`/`IpAddress` del `ICurrentUserService`; `ChangedBy = "System"` cuando `UserId` es null; `Updated` con diff vacío **no** persiste; una excepción del repositorio se traga con log warning y no propaga.
- Casos agregados a los handlers instrumentados — 1 por handler, verificando que el camino feliz llama `IAuditLogger` con la entidad, la acción y el diff esperados, y que los caminos de error **no** lo llaman.
- `BulkUpsertRoleSystemOptionsCommandHandlerTests` — ampliar con 2 casos: N opciones afectadas producen N entradas con el mismo `bulkOperationId`; una opción cuyo flag no cambió no genera entrada.

`dotnet test --filter "FullyQualifiedName~Audit"` → 0 fallidos.

### F12 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → gate de 90% en `JOIN.Application` pasa.
3. `dotnet ef database update` sobre base limpia → la migración aplica y el seeder corre con la `SystemOption` de `Audit`.
4. Smoke manual del caso que motiva el item 22: loguearse como operador → `PUT /RoleSystemOptions/bulk` quitando un permiso a un rol → `GET /Audit/security?entity=RoleSystemOption` → la fila aparece con `changedBy` del operador, `action = "Updated"` y `changes` mostrando el flag que pasó de `True` a `False`.
5. Smoke manual de aislamiento de tenant: repetir el cambio con un operador de otra empresa y verificar que ninguno de los dos ve los eventos del otro.
6. Smoke manual de SuperAdmin: `GET /Audit/security?allTenants=true` devuelve los eventos de ambas empresas.
7. Smoke manual de resiliencia: con la tabla renombrada a mano, ejecutar un cambio de permisos → la operación de negocio **devuelve 200** y el warning queda en el log.
8. `CURL_REQUESTS.md` — agregar el endpoint con todos sus query params y una nota de que exige `CanRead` sobre el recurso `Audit`.

---

## Acceptance criteria

### F1 — Dominio

- [ ] `AuditLog` hereda de `BaseEntity`, **no** de `BaseAuditableEntity`, y no tiene `GcRecord`.
- [ ] `AuditedEntity` tiene exactamente 6 miembros: `Role`, `User`, `RoleSystemOption`, `UserRoleCompany`, `UserCompany`, `RoleCompany`.
- [ ] `AuditAction` tiene exactamente 3 miembros: `Created`, `Updated`, `Deleted`.

### F2 — Persistencia y migración

- [ ] Existe una única migración nueva llamada `AddSecurityAuditLog`.
- [ ] La tabla se crea como `Security.AuditLogs`.
- [ ] `EntityName` es `nvarchar(64)`, `Action` `nvarchar(16)`, `ChangedBy` `nvarchar(64)`, `EntityLabel` `nvarchar(256)`, `IpAddress` `nvarchar(45)`.
- [ ] `OldValuesJson`, `NewValuesJson` y `MetadataJson` son `nvarchar(max)` nullable.
- [ ] Existen los índices `IX_AuditLogs_Entity` sobre `(EntityName, EntityId, ChangedAtUtc)` e `IX_AuditLogs_Company_ChangedAt` sobre `(CompanyId, ChangedAtUtc)`.
- [ ] La tabla **no** tiene claves foráneas a `Security.Users`, `Security.Roles` ni `Common.Companies`.
- [ ] La configuración EF **no** declara `HasQueryFilter`.
- [ ] `dotnet ef database update` sobre base limpia aplica sin error.

### F3 — Repositorio

- [ ] `InsertManyAsync` persiste N filas en un solo `ExecuteAsync`, no N round-trips.
- [ ] `ListPagedAsync` devuelve items y `TotalCount` en un solo `QueryMultipleAsync`.
- [ ] `companyId = null` devuelve filas de todos los tenants; con valor, filtra por ese tenant.
- [ ] Cada filtro opcional (`entityName`, `entityId`, `changedBy`, `action`, `fromUtc`, `toUtcExclusive`) se ignora cuando viene en null.
- [ ] El orden es `ChangedAtUtc DESC, Id DESC`.
- [ ] `ChangedByName` se resuelve con `LEFT JOIN` y queda null si el usuario no existe o si `ChangedBy` es `"System"`.
- [ ] La cláusula de paginación branchea entre `LIMIT/OFFSET` y `OFFSET … FETCH NEXT` según el proveedor.
- [ ] Usa `ISqlConnectionFactory` + Dapper, sin change tracker de EF.

### F4 — `AuditDiffBuilder` e `IAuditLogger`

- [ ] `Build` devuelve solo las claves cuyo valor cambió; las iguales no aparecen en ninguno de los dos JSON.
- [ ] `Build` devuelve `(null, null)` cuando no cambió nada.
- [ ] Las claves de la lista negra (`PasswordHash`, `SecurityStamp`, `ConcurrencyStamp`, `MfaSecretKey`, `Token`, `RefreshToken`, `CodeHash`) **se descartan por completo**, no se enmascaran con `"***"`.
- [ ] Una clave presente solo en `newValues` aparece con `OldValue` null.
- [ ] `null` y `string.Empty` se consideran valores distintos.
- [ ] `AuditLogger` resuelve `CompanyId`, `ChangedBy`, `IpAddress` y `ChangedAtUtc` por su cuenta desde `ICurrentUserService`; ningún handler los pasa.
- [ ] `ChangedBy` es `"System"` cuando `ICurrentUserService.UserId` es null.
- [ ] Una acción `Updated` cuyo diff quedó vacío **no** persiste ninguna fila.
- [ ] **Una excepción del repositorio se traga con log warning y no propaga.** El `try/catch` vive dentro de `AuditLogger`, no en los handlers.
- [ ] `IAuditLogger` está registrado como Scoped.

### F5 — Instrumentación de Roles, RoleCompanies y RoleSystemOptions single

- [ ] Crear un rol registra una fila `Role` con `action = "Created"` y `newValues` con los campos del rol.
- [ ] Modificar un rol registra `action = "Updated"` con `oldValues` y `newValues` de **solo** los campos que cambiaron.
- [ ] Borrar un rol registra `action = "Deleted"` con `oldValues`.
- [ ] `POST`, `PUT /{id}` y `DELETE /{id}` de `RoleSystemOptions` registran fila con `entityName = "RoleSystemOption"`.
- [ ] Alta y baja de `RoleCompany` registran fila con `entityName = "RoleCompany"`.
- [ ] `EntityLabel` de `RoleSystemOption` tiene el formato `"<RoleName> → <SystemOptionName>"`.
- [ ] Los caminos que cortan con `Response.Error` **no** registran ninguna fila.

### F6 — Instrumentación del bulk de la matriz

- [ ] `PUT /RoleSystemOptions/bulk` que afecta N opciones registra N filas de bitácora.
- [ ] Las N filas comparten el mismo `bulkOperationId` en `MetadataJson`.
- [ ] Las opciones de `created` quedan con `action = "Created"`; las de `updated` con `"Updated"` y diff contra el estado previo; las de `removed` con `"Deleted"` y `oldValues`.
- [ ] Una opción incluida en el payload cuyos 7 flags no cambiaron **no** genera fila.
- [ ] Las N filas se persisten con un solo `LogManyAsync`.
- [ ] El snapshot previo se obtiene con `GetActiveByRoleAndCompanyAsync` antes del upsert, reutilizando la lectura si el handler ya la hacía.

### F7 — Instrumentación de Users

- [ ] `POST /Users/invite` con `outcome = "Created"` registra una fila `User` (`Created`), una `UserCompany` (`Created`) y una `UserRoleCompany` (`Created`) por rol.
- [ ] `outcome = "MembershipAdded"` registra `UserCompany` y `UserRoleCompany` pero **ninguna** fila `User`.
- [ ] `outcome = "InvitationResent"` registra solo las `UserRoleCompany` que efectivamente cambiaron.
- [ ] Ninguna fila de invitación contiene `PasswordHash` ni `SecurityStamp` en los JSON.
- [ ] `PUT /Users/{userId}/status` (camino **Dapper**) registra una fila `User` con `action = "Updated"`, diff de `IsActive` y `StatusChangeReason`, y `metadata` con el `reason`.
- [ ] `POST /Users/{userId}/force-password-reset` **no** registra ninguna fila de bitácora.

### F8 — Instrumentación de roles y membresías

- [ ] `PUT /Users/{userId}/roles` registra una fila `UserRoleCompany` por rol agregado (`Created`) y por rol quitado (`Deleted`), en un solo `LogManyAsync`.
- [ ] `EntityLabel` tiene el formato `"<Email> → <RoleName>"`.
- [ ] `PUT /Users/roles/bulk` registra filas solo de los usuarios con `outcome = "Updated"`, todas con el mismo `bulkOperationId`.
- [ ] `POST /Users/{userId}/companies` registra una fila `UserCompany` (`Created`) más las `UserRoleCompany` correspondientes.
- [ ] `DELETE /Users/{userId}/companies/{companyId}` registra una fila `UserCompany` (`Deleted`) más una `UserRoleCompany` (`Deleted`) por rol dado de baja.
- [ ] `PUT /Users/{userId}/default-company/{companyId}` registra una fila `UserCompany` (`Updated`) por cada fila que cambió de `IsDefault`: la que lo pierde y la que lo gana.

### F9 — Query de lectura

- [ ] `GET /api/v1/Audit/security` → 200 con `{items[], pageNumber, pageSize, totalCount, totalPages}`.
- [ ] Cada item trae `{id, entityName, entityId, entityLabel, action, changedBy, changedByName, changedAtUtc, ipAddress, changes[], metadata}`.
- [ ] `changes[]` es la unión de las claves de los dos JSON, con `oldValue` o `newValue` en null cuando la clave falta de un lado. El front **no** recibe JSON crudo.
- [ ] `?entity=RoleSystemOption` filtra por esa entidad, case-insensitive.
- [ ] `?entity=Cualquiera` → 400 `INVALID_ENTITY`, no una lista vacía.
- [ ] `?action=Inventada` → 400 `INVALID_ACTION`.
- [ ] `?entityId=`, `?changedBy=`, `?fromDate=` y `?toDate=` filtran correctamente y son combinables.
- [ ] `toDate` se normaliza a exclusivo: un evento del mismo día que `toDate` aparece en el resultado.
- [ ] `pageNumber = 0` clampea a 1; `pageSize = 500` clampea a 100; `pageSize` ausente usa 20.
- [ ] `fromDate > toDate` → 400 del validator.
- [ ] Tenant vacío → 400 `TENANT_REQUIRED`.
- [ ] Una fila con `OldValuesJson` corrupto devuelve `changes` vacío y **no** tumba la página.

### F10 — Controller, permisos y aislamiento

- [ ] `AuditController` existe en `src/4.Services.WebApi/Controllers/Security/` con `[PermissionResource("Audit")]` a nivel de clase.
- [ ] `GET /Audit/security` exige `CanRead` por el default de verbo HTTP, sin `[RequirePermission]`.
- [ ] El `DatabaseSeeder` crea la `SystemOption` de `Audit` con `ControllerName = "Audit"` y `CanRead`, de forma idempotente.
- [ ] Correr el seeder dos veces no duplica la `SystemOption`.
- [ ] Un usuario con `CanRead` sobre `Audit` recibe 200; uno sin el flag recibe 403; un request sin token o sin `CompanyId` recibe 401.
- [ ] Un operador de la empresa A **no** ve ningún evento de la empresa B.
- [ ] `?allTenants=true` sin rol SuperAdmin se **ignora**: devuelve 200 filtrado por el tenant del token, no 403 ni 400.
- [ ] `?allTenants=true` con rol SuperAdmin devuelve eventos de todos los tenants.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test` → 0 fallidos, ≥ 24 tests nuevos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate de CI).
- [ ] Una sola migración EF en toda la spec.
- [ ] **`AuditableEntitySaveChangesInterceptor` no se modifica.**
- [ ] Ningún handler registra bitácora en un camino que devuelve `Response<T>` con `IsSuccess = false`.
- [ ] Con la tabla `Security.AuditLogs` inaccesible, toda operación de negocio sigue devolviendo su código de éxito habitual y el fallo queda como warning en el log.
- [ ] Ningún JSON de la bitácora contiene valores de `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp`, `MfaSecretKey`, `Token`, `RefreshToken` ni `CodeHash`.
- [ ] `CURL_REQUESTS.md` documenta el endpoint con sus 9 query params.

---

## Decisiones

### Mecanismo de captura

- **Sí:** `IAuditLogger` invocado explícitamente desde cada handler. Es el único mecanismo que cubre a la vez los caminos EF y los Dapper, y los caminos Dapper son precisamente los que importan: `RoleSystemOptionsRepository.BulkUpsertAsync` (SPEC 25) es la vía por la que el front guarda la matriz de permisos, y el flip de estado de la SPEC 27 es Dapper por diseño.
- **No:** extender `AuditableEntitySaveChangesInterceptor` para capturar el diff automáticamente. Dos razones independientes, cada una suficiente: (a) el interceptor recorre `ChangeTracker.Entries<BaseAuditableEntity>()` y ni `ApplicationRole` ni `ApplicationUser` heredan de `BaseAuditableEntity` — implementan `IAuditableEntity` sobre `IdentityRole<Guid>` / `IdentityUser<Guid>`; (b) ningún camino Dapper pasa por `SaveChanges`, así que el bulk de permisos sería invisible.
- **No:** un híbrido de interceptor extendido a `IAuditableEntity` más instrumentación manual de los caminos Dapper. Dos mecanismos conviviendo sobre las mismas entidades es la receta para doble registro en algunos casos y huecos en otros.
- **No:** triggers de base de datos. Capturan todo sin excepción, incluidos los `UPDATE` manuales por SQL, pero exigen dos implementaciones (SQL Server y Postgres) y rompen la portabilidad que exige `CLAUDE.md`.
- **Costo asumido:** la instrumentación es manual y toca unos 12 handlers. Un handler nuevo que alguien escriba mañana no se audita solo. Anotado en Riesgos.

### Alcance de lo auditado

- **Sí:** seis entidades, no las tres del backlog. `Role`, `User` y `RoleSystemOption` son las que nombra el item 22; se agregan `UserRoleCompany`, `UserCompany` y `RoleCompany`. Sin `UserRoleCompany` la pregunta "quién le dio este permiso a este usuario" no tiene respuesta, porque asignar un rol es un cambio de permisos aunque no toque `RoleSystemOptions`.
- **No:** auditar entidades de negocio (`Person`, `Customer`, `Ticket`). El item 22 es una bitácora de seguridad.
- **No:** auditar lecturas. Solo mutaciones.
- **No:** instrumentar `ForceUserPasswordReset`. No muta ninguna de las seis entidades; es un evento de sesión y su lugar es el `SecurityEventLog` de la SPEC 26.
- **Sí:** `Security.AuditLogs` y el `Security.SecurityEventLog` de la SPEC 26 coexisten. Uno registra **diffs de entidades**, el otro **eventos de sesión** (login, MFA, verificación de teléfono). Unificarlos forzaría un esquema que no le sirve bien a ninguno de los dos.

### Forma de la tabla

- **Sí:** `AuditLog` hereda de `BaseEntity`, no de `BaseAuditableEntity`. Es append-only: no se modifica, no se borra, y auditar la auditoría no tiene sentido. Por eso tampoco lleva `GcRecord` ni query filter global.
- **Sí:** `EntityName` y `Action` se persisten como **string**, no como int del enum. Una bitácora se lee a mano en SQL durante un incidente; `'RoleSystemOption'` es legible y `3` obliga a ir a buscar el enum.
- **Sí:** sin claves foráneas a `Users`, `Roles` ni `Companies`. Una fila de bitácora tiene que sobrevivir al borrado de lo que registra: una FK con `Restrict` bloquearía el borrado y una con `Cascade` destruiría la evidencia.
- **Sí:** `CompanyId` es el tenant **del token de quien hizo el cambio**, no el de la entidad afectada. `Security.Roles` y `Security.Users` no tienen `CompanyId` propio, así que el tenant de la entidad no siempre existe; el del actor siempre sí, y es lo que permite el aislamiento en la lectura.
- **Sí:** solo dos índices. Cubren los dos accesos reales: "historial de esta entidad" y "qué pasó en este tenant".
- **No:** job de retención, particionado ni archivado. Decisión explícita: el volumen no preocupa. Si algún día preocupa, se agrega en spec aparte.
- **No:** firma criptográfica o encadenamiento hash de las filas. Detectaría manipulación de la bitácora, pero es un nivel de garantía que nadie pidió.

### Diff

- **Sí:** `OldValuesJson` / `NewValuesJson` guardan **solo las propiedades que cambiaron**. Es lo que hace legible la bitácora: un snapshot completo obliga a diffear a ojo en la UI.
- **No:** snapshot completo de la entidad. Además de ilegible, arrastraría `PasswordHash` y `MfaSecretKey` de `ApplicationUser` a una tabla de consulta.
- **Sí:** los campos sensibles **se descartan**, no se enmascaran con `"***"`. Un `"***"` sugiere que el valor está guardado en algún lado; descartarlo deja claro que nunca se registró. Consecuencia visible: un cambio de contraseña registra el evento sobre `User` con `changes` vacío.
- **Sí:** un `Updated` cuyo diff queda vacío no persiste fila. Un update que no cambió nada no es un evento.
- **No:** diff de propiedades de navegación o de colecciones. Solo columnas planas; las relaciones se auditan como filas propias de su entidad.
- **Sí:** `bulkOperationId` en `MetadataJson`, compartido por todas las filas de un mismo request de lote. Sin él, guardar la matriz de un rol produce N eventos indistinguibles de N cambios individuales hechos a lo largo del día.

### Lectura

- **Sí:** el handler cruza los dos JSON en una lista `changes[]` de `{field, oldValue, newValue}`. El front recibe datos estructurados y no parsea JSON crudo.
- **Sí:** una fila con JSON corrupto devuelve `changes` vacío en lugar de tumbar la página entera. Una bitácora ilegible en una fila no debería impedir leer las otras.
- **Sí:** tenant-scoped por defecto, con `?allTenants=true` respetado solo para SuperAdmin.
- **Sí:** `allTenants = true` sin SuperAdmin **se ignora** en silencio en lugar de devolver 403. El parámetro no aplica a ese usuario; devolver un error convertiría un filtro opcional en una trampa.
- **Sí:** `?entity=` con un valor fuera del enum devuelve `INVALID_ENTITY` (400) y no una lista vacía. Una lista vacía haría creer que no hay eventos cuando el problema es un typo.
- **Sí:** un solo endpoint. El listado ya trae el diff completo de cada fila, así que un `GET /Audit/security/{id}` de detalle no agregaría nada.
- **Sí:** orden fijo `ChangedAtUtc DESC, Id DESC`. El `Id` desempata las filas de un mismo lote, que comparten timestamp al milisegundo.
- **No:** exportar a CSV o Excel (`CanExport`). Spec aparte si hace falta.

### Resiliencia y permisos

- **Sí:** el `try/catch` que traga los fallos de la bitácora vive **dentro** de `AuditLogger`, no en cada handler. Ponerlo en los handlers significa 12 oportunidades de olvidarse.
- **Sí:** un fallo de la bitácora **no** tumba la operación de negocio. El cambio ya se ejecutó y se committeó; hacer fallar el request dejaría al operador creyendo que no se aplicó.
- **Riesgo asumido:** eso implica que la bitácora puede tener huecos silenciosos si la tabla se cae. Queda el warning en el log. La alternativa — hacer fallar la operación — es peor.
- **Sí:** la `SystemOption` de `Audit` va en el seed dentro de esta spec. Sin ella el `DynamicAuthorizationFilter` falla cerrado (SPEC 17) y el endpoint devuelve 403 a todos salvo SuperAdmin, lo que parece un bug de permisos en lugar de una configuración faltante.
- **Sí:** el endpoint es un `GET` con `CanRead`, sin `[RequirePermission]`. El default por verbo HTTP ya da el flag correcto.
- **No:** hacerlo SuperAdmin-only como los `/companies` de la SPEC 28. La bitácora del propio tenant es información que un administrador de empresa necesita; el aislamiento lo da el filtro por `CompanyId`.

### Alcance temporal

- **Sí:** la bitácora arranca vacía. No hay backfill.
- **No:** reconstruir historia a partir de los `LastModifiedBy` / `LastModified` existentes. Esos campos dicen quién tocó la fila por última vez, no qué cambió; inventar filas de bitácora a partir de ellos sería fabricar evidencia.
- **Sí:** la spec es la última de la cadena 27 → 28 → 29. Depende de las dos anteriores porque instrumenta handlers que ellas crean o reescriben.

---

## Riesgos

| Riesgo | Mitigación |
|---|---|
| La instrumentación es manual: un handler nuevo no se audita solo | No hay mitigación técnica dentro de esta spec. Paliativos: dejar el listado de handlers instrumentados en la propia spec como checklist, y agregar a `CLAUDE.md` la regla "todo handler que mute Role, User, RoleSystemOption, UserRoleCompany, UserCompany o RoleCompany llama `IAuditLogger`". Un test que recorra por reflexión los handlers de esas áreas y falle si no dependen de `IAuditLogger` es posible pero frágil; queda fuera. |
| Un fallo de la bitácora produce huecos silenciosos | Deliberado: el `try/catch` dentro de `AuditLogger` traga la excepción con log warning. La alternativa (hacer fallar la operación) dejaría al operador creyendo que su cambio no se aplicó cuando sí se committeó. Monitorear el warning en Serilog. |
| F6 puede agregar un round-trip al guardado de la matriz | El bulk devuelve qué filas cambiaron, no sus valores previos. Si `BulkUpsertRoleSystemOptionsCommandHandler` no llama ya `GetActiveByRoleAndCompanyAsync`, hay que agregar esa lectura antes del upsert. Verificar en F6 antes de duplicarla. |
| Sin `SystemOption` de `Audit` en el seed, el endpoint devuelve 403 a todos | Está dentro del scope (F10 paso 3) justamente por eso. El `DynamicAuthorizationFilter` falla cerrado por diseño (SPEC 17), así que la ausencia se manifiesta como un 403 inexplicable en lugar de un error de configuración. |
| Los JSON pueden filtrar datos sensibles de una entidad futura | La lista negra de `AuditDiffBuilder` es una constante hardcodeada. Si mañana `ApplicationUser` gana una columna sensible con otro nombre, se registra en claro. Mitigación: la lista está en un solo lugar y hay un criterio de aceptación explícito. Revisar la lista al agregar columnas a las entidades auditadas. |
| `nvarchar(max)` en tres columnas de una tabla que crece sin retención | Los índices no incluyen esas columnas, así que el impacto es de espacio, no de consulta. Sin job de retención por decisión explícita. Si el espacio pasa a molestar, la retención va en spec aparte. |
| `EntityLabel` se calcula al momento del cambio y queda congelado | Si un rol se renombra, los eventos viejos siguen mostrando el nombre anterior. Es lo correcto para una bitácora (registra lo que se veía entonces), pero puede confundir a quien lea. Documentar. |
| `ChangedByName` queda null si el usuario fue borrado | El `LEFT JOIN` no encuentra la fila. `ChangedBy` conserva el `UserId`, así que la trazabilidad no se pierde, solo el nombre legible. Aceptado. |
| El `bulkOperationId` no está en ninguna columna indexada | Vive dentro de `MetadataJson`. Agrupar por lote en la UI requiere traer la página y agrupar en el cliente; filtrar por `bulkOperationId` en SQL exigiría parsear JSON. Si la UI necesita filtrar por lote, promoverlo a columna propia en spec aparte. |
| Depende de las SPEC 27 y 28 | Es la última de la cadena. Si alguna de las dos anteriores se demora, esta queda bloqueada. Alternativa: implementar F1 a F6 y F9 a F12 (que solo dependen de SPEC 25) y dejar F7 y F8 para cuando aterricen. Eso sí es viable y vale como plan B. |
| `AuditedEntity` como enum obliga a tocar el dominio para auditar algo nuevo | Un `string` libre sería más flexible pero permitiría typos silenciosos que rompen el filtro `?entity=`. Se prefiere el enum. |
| Doble registro si un handler instrumentado llama a otro instrumentado | Hoy no ocurre: los handlers no se invocan entre sí. Si mañana alguno lo hace vía MediatR, el evento se registra dos veces. Revisar al agregar composición entre handlers. |
| El smoke más importante (F12 paso 7) es manual | Verificar de punta a punta que un fallo de la bitácora no tumba la operación requiere romper la tabla a mano. El unit test de `AuditLoggerTests` cubre el comportamiento del servicio, no la integración completa. |

---

## Lo que NO entra en esta spec

- **Interceptor de EF para el diff automático** — descartado por dos motivos independientes: `ApplicationRole` y `ApplicationUser` no heredan de `BaseAuditableEntity`, y los caminos Dapper no pasan por `SaveChanges`.
- **Triggers de base de datos** — capturarían todo, incluidos los `UPDATE` manuales, pero exigen dos implementaciones y rompen la portabilidad.
- **Modificar `AuditableEntitySaveChangesInterceptor`** — sigue haciendo stamps, tenant y soft delete, sin tocar.
- **Job de retención, particionado o archivado** de `Security.AuditLogs`.
- **Firma criptográfica o encadenamiento hash** de las filas.
- **Backfill histórico** — la bitácora arranca vacía y lo anterior no se puede reconstruir.
- **Auditoría de lecturas** (quién consultó qué).
- **Auditoría de entidades de negocio** (`Person`, `Customer`, `Ticket`, …) — solo las seis de seguridad.
- **Instrumentar `ForceUserPasswordReset`** — es un evento de sesión, va al `SecurityEventLog` de la SPEC 26.
- **Unificar `Security.AuditLogs` con el `Security.SecurityEventLog` de la SPEC 26** — propósitos distintos, coexisten.
- **`GET /Audit/security/{id}`** de detalle — el listado ya trae el diff completo.
- **Exportar la bitácora** a CSV o Excel (`CanExport`).
- **Filtrar por `bulkOperationId` en SQL** — vive en `MetadataJson`, sin columna ni índice propios.
- **Rollback o "deshacer"** un cambio desde la bitácora.
- **Diff de propiedades de navegación o colecciones** — solo columnas planas.
- **Alertas** ante cambios sensibles.
- **Test por reflexión** que exija `IAuditLogger` en todo handler de las áreas auditadas.
- **Promover `EntityLabel` a un cálculo en tiempo de lectura** — queda congelado al momento del cambio, a propósito.

Cada uno de esos, si aterriza, va en su propia spec.

---

## Archivos críticos

### Modify

- `src/3.Persistence/Contexts/ApplicationDbContext.cs` — agregar `DbSet<AuditLog> AuditLogs`.
- `src/3.Persistence/Seed/DatabaseSeeder.cs` — agregar la `SystemOption` de `Audit` (idempotente).
- `src/3.Infrastructure/DependencyInjection.cs` — registrar `IAuditLogger` como Scoped.
- Los **handlers** que se instrumentan en F5, F6, F7 y F8: unas 12 clases entre `Roles`, `RoleCompanies`, `RoleSystemOptions` (single), `RoleSystemOptions/BulkUpsert`, `Users/Invite`, `Users/ChangeUserStatus`, `Users/ReplaceUserRoles`, `Users/BulkUpdateUserRoles`, `UserCompanies/AddUserCompany`, `UserCompanies/RemoveUserCompany`, `UserCompanies/SetDefaultCompany`. Cada uno gana una dependencia `IAuditLogger` en su ctor.
- `src/3.Persistence/Configuration/ConfigureServices.cs` — registrar `IAuditLogRepository`.

### Create

**Dominio:**

- `src/1.Domain/Audit/AuditLog.cs`
- `src/1.Domain/Audit/AuditedEntity.cs`
- `src/1.Domain/Audit/AuditAction.cs`

**Application — interfaces:**

- `src/2.Application/Interface/IAuditLogger.cs`
- `src/2.Application/Interface/Persistence/Audit/IAuditLogRepository.cs`

**Application — DTOs:**

- `src/2.Application.DTO/Security/Audit/AuditFieldChangeDto.cs`
- `src/2.Application.DTO/Security/Audit/SecurityAuditLogItemDto.cs`

**Application — use case:**

- `src/2.Application/UseCases/Security/Audit/Queries/GetSecurityAuditLog/GetSecurityAuditLogQuery.cs`
- `src/2.Application/UseCases/Security/Audit/Queries/GetSecurityAuditLog/GetSecurityAuditLogQueryHandler.cs`
- `src/2.Application/UseCases/Security/Audit/Queries/GetSecurityAuditLog/GetSecurityAuditLogQueryValidator.cs`

**Infrastructure:**

- `src/3.Infrastructure/Audit/AuditLogger.cs`
- `src/3.Infrastructure/Audit/AuditDiffBuilder.cs`

**Persistence:**

- `src/3.Persistence/Configuration/Audit/AuditLogConfiguration.cs`
- `src/3.Persistence/Repositories/Audit/AuditLogRepository.cs`
- `src/3.Persistence/Migrations/20260815xxxx_AddSecurityAuditLog.cs` (+ Designer + ModelSnapshot)

**WebApi:**

- `src/4.Services.WebApi/Controllers/Security/AuditController.cs`

**Tests:**

- `tests/UnitTests/JOIN.Application.UnitTest/Audit/AuditDiffBuilderTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/Audit/AuditLoggerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Audit/Queries/GetSecurityAuditLog/GetSecurityAuditLogQueryHandlerTests.cs`
- Ampliar los tests de los ~12 handlers instrumentados con un caso cada uno.
