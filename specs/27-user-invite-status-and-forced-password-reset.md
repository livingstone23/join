# SPEC 27 — Alta administrada de usuarios (invite), activación/desactivación y reseteo forzado de contraseña

> **Status:** Aprobado
> **Depends on:** SPEC 17 (PermissionResource + flag override), SPEC 23 (tenant desde el token), SPEC 25 (`IPermissionService.InvalidateUserCacheAsync`)
> **Date:** 2026-08-15
> **Objective:** Cerrar el ciclo de vida administrado de un usuario de empresa —alta por invitación con correo de setup-password, activación/desactivación con motivo, y reseteo forzado de contraseña— corrigiendo de paso los cuatro defectos de infraestructura que hoy lo bloquean: los tres handlers de `Auth` que existen como comando sin handler, el `TransactionBehavior` que commitea las fallas de negocio, y el filtro global de EF que esconde a los usuarios inactivos.

---

## Por qué existe esta spec

El backlog original agrupaba ocho endpoints (items 15 a 22) bajo "Administración de usuarios de la empresa". Atraviesan cinco dominios distintos: invitación con correo, ciclo de vida del usuario, membresía multi-empresa, matriz de permisos efectivos y bitácora de auditoría. Se partió en tres specs:

| Spec | Items | Núcleo |
|---|---|---|
| **27** (esta) | 15, 16, 17 | Ciclo de vida admin: invite + status + force-reset, más los handlers de `Auth` faltantes. |
| **28** | 18, 19, 20, 21 | Membresía multi-empresa, permisos efectivos, roles bulk, reporte paginado. |
| **29** | 22 | Bitácora de seguridad real: tabla `Security.AuditLogs` + interceptor de diff + endpoint. |

Dependencias entre specs: 27 y 28 son independientes y se pueden implementar en paralelo. Ninguna depende de 29 para funcionar; los cambios de 27/28 empiezan a generar filas de auditoría recién cuando 29 aterrice. La SPEC 26 (`account/*` self-management) es independiente de las tres.

Dos hallazgos del relevamiento previo condicionan el alcance:

1. **`setup-password` no existe como flujo.** `SetupPasswordCommand`, `ForgotPasswordCommand` y `ResetPasswordCommand` existen como records, y `AuthController` los despacha, pero **no hay handlers**. MediatR falla en runtime. El item 15 no tiene a dónde apuntar el correo de invitación sin ellos, así que se implementan acá.
2. **No hay tabla de auditoría.** Solo existe `src/1.Domain/Audit/` con `IAuditableEntity` (`Created`/`CreatedBy`/`LastModified`/`LastModifiedBy`/`GcRecord`) y el `AuditableEntitySaveChangesInterceptor`. No se guardan valores viejo/nuevo. Por eso el `reason` del item 16 va a una columna propia y no a una bitácora.
3. **`TransactionBehavior` solo revierte ante excepción.** `src/2.Application/Common/TransactionBehavior.cs` hace `CommitAsync` en cuanto `next()` retorna, sin mirar el resultado. Un handler que devuelve `Response<T>.Error(...)` después de haber escrito filas **commitea esas filas**. Es un bug latente en todos los `ITransactionalCommand` del repo, y bloquea el requisito del item 15 de no dejar un usuario huérfano cuando falla el correo.
4. **El filtro global de EF esconde a los usuarios inactivos.** `src/3.Persistence/Configuration/Security/ApplicationUserConfiguration.cs:41` declara `builder.HasQueryFilter(u => u.GcRecord == 0 && u.IsActive)`. Un usuario con `IsActive = false` desaparece de toda consulta EF, así que `userManager.FindByIdAsync` devuelve null y reactivarlo por el item 16 sería imposible.

Los hallazgos 3 y 4 no estaban previstos al planificar el corte de specs. Se absorben acá porque los items 15 y 16 no son implementables sin ellos.

---

## Scope

**In:**

### A0. Correcciones de infraestructura (pre-requisito de los items 15 y 16)

- `src/2.Application/Common/TransactionBehavior.cs` — revertir la transacción cuando el handler devuelve un `Response<T>` con `IsSuccess == false`, en lugar de commitear siempre que no haya excepción. Detección por reflexión sobre la propiedad `IsSuccess` del `TResponse` (`Response<T>` no tiene interfaz común ni clase base):

  ```csharp
  var response = await next();
  if (IsFailedResponse(response))
  {
      await unitOfWork.RollbackAsync(cancellationToken);
      return response;
  }
  await unitOfWork.CommitAsync(cancellationToken);
  return response;
  ```

  `IsFailedResponse` devuelve `false` para cualquier `TResponse` que no exponga un `bool IsSuccess`, así que los comandos que no usan `Response<T>` mantienen el comportamiento actual.
- `src/3.Persistence/Configuration/Security/ApplicationUserConfiguration.cs` — **no se toca**. El filtro global `u.GcRecord == 0 && u.IsActive` se mantiene; el item 16 esquiva EF haciendo el flip de estado por Dapper (ver sección F).

### A. Handlers de `Auth` faltantes (pre-requisito de los items 15 y 17)

- `src/2.Application/UseCases/Security/Auth/ForgotPassword/ForgotPasswordCommandHandler.cs` + `ForgotPasswordCommandValidator.cs` (nuevos).
- `src/2.Application/UseCases/Security/Auth/ResetPassword/ResetPasswordCommandHandler.cs` + `ResetPasswordCommandValidator.cs` (nuevos).
- `src/2.Application/UseCases/Security/Auth/SetupPassword/SetupPasswordCommandHandler.cs` + `SetupPasswordCommandValidator.cs` (nuevos).
- `ResetPasswordCommand.cs` y `SetupPasswordCommand.cs`: agregar propiedad `Email`. Sin ella el token de Identity no permite resolver el usuario.
- `src/2.Application.DTO/Security/Auth/ResetPasswordRequestDto.cs` y `SetupPasswordRequestDto.cs`: agregar `Email`.

### B. Infraestructura de correo

- `src/2.Application/Common/Options/AppUrlsOptions.cs` (nuevo) — `{ FrontendBaseUrl }`. Bindeado en `Program.cs` desde la sección `AppUrls` de `appsettings.json`.
- `src/2.Application/Common/Email/AuthEmailTemplates.cs` (nuevo, static) — construye el HTML de los 3 correos:
  `BuildInvitation(firstName, companyName, link)`, `BuildForgotPassword(firstName, link)`, `BuildForcedReset(firstName, link, reason)`.
  HTML plano con `WebUtility.HtmlEncode`, mismo estilo que `RequestEmailChangeCommandHandler`. Sin motor de plantillas.
- Links generados: `{FrontendBaseUrl}/setup-password?email={urlEncoded}&token={urlEncoded}` y `{FrontendBaseUrl}/reset-password?email=…&token=…`.

### C. Item 15 — `POST /api/v1/Users/invite`

- `src/2.Application/UseCases/Security/Users/Commands/InviteUser/` — `InviteUserCommand.cs` (`ITransactionalCommand<Response<InviteUserResultDto>>`), handler, validator.
- `src/2.Application.DTO/Security/User/InviteUserRequestDto.cs` — `{ Email, FirstName, LastName, RoleIds[] }`. **Sin `companyId`** (sale del JWT, SPEC 23).
- `src/2.Application.DTO/Security/User/InviteUserResultDto.cs` — `{ UserId, Email, Outcome }` donde `Outcome ∈ { Created, MembershipAdded, InvitationResent }`.
- Alta: `UserManager.CreateAsync(user)` **sin password** → `IsActive = true`, `EmailConfirmed = false`.
- Escribe `UserCompany` (con `IsDefault = true` si es su primera empresa) + un `UserRoleCompany` por cada `roleId`.
- Token vía `UserManager.GeneratePasswordResetTokenAsync`. Correo con `AuthEmailTemplates.BuildInvitation`.

### D. Item 16 — `PUT /api/v1/Users/{userId}/status`

- `src/2.Application/UseCases/Security/Users/Commands/ChangeUserStatus/` — command (`IRequest<Response<bool>>`), handler, validator.
- `src/2.Application.DTO/Security/User/ChangeUserStatusRequestDto.cs` — `{ IsActive, Reason }`.
- `src/1.Domain/Security/applicationuser.cs`: nueva columna `string? StatusChangeReason`.
- **Todo el flujo va por Dapper, sin EF ni `UserManager`.** El filtro global `u.GcRecord == 0 && u.IsActive` de `ApplicationUserConfiguration` hace invisible al usuario inactivo para EF, así que ni la lectura previa ni el `UPDATE` pueden pasar por `UserManager`. Por eso el comando **no** es `ITransactionalCommand`: no muta entidades EF.
- Lectura previa: `IUserAdminRepository.GetAdminSnapshotAsync(userId, companyId)` — trae `{ UserId, Email, FirstName, IsActive, GcRecord, HasMembership }` en una sola query.
- Escritura: `IUserAdminRepository.SetUserActiveStatusAsync(userId, isActive, reason, modifiedBy, utcNow)` — un `UPDATE` que setea `IsActive`, `StatusChangeReason`, `LastModified` y `LastModifiedBy`. Cubre de paso que `ApplicationUser` no pasa por el `AuditableEntitySaveChangesInterceptor` (hereda de `IdentityUser<Guid>`, no de `BaseAuditableEntity`).
- Al desactivar: revoca `UserRefreshToken` activos, cierra `UserConnectionLog` abiertos, invalida `permissions:v2:{companyId}:{userId}` + `sidebar:{companyId}:{userId}`.

### E. Item 17 — `POST /api/v1/Users/{userId}/force-password-reset`

- `src/2.Application/UseCases/Security/Users/Commands/ForceUserPasswordReset/` — command (`IRequest<Response<bool>>`), handler, validator.
- `src/2.Application.DTO/Security/User/ForceUserPasswordResetRequestDto.cs` — `{ Reason }` (opcional).
- Genera token de reset, manda correo, revoca refresh tokens del usuario. **No** cambia la contraseña ni bloquea el login actual más allá de la revocación.

### F. Repositorio Dapper de administración

- `src/2.Application/Interface/Persistence/Security/IUserAdminRepository.cs` (nuevo):
  - `Task<int> RevokeActiveRefreshTokensAsync(Guid userId, DateTime utcNow, CancellationToken ct)`
  - `Task<int> CloseActiveConnectionsAsync(Guid userId, DateTime utcNow, CancellationToken ct)`
  - `Task<UserAdminSnapshot?> GetAdminSnapshotAsync(Guid userId, Guid companyId, CancellationToken ct)`
  - `Task<bool> SetUserActiveStatusAsync(Guid userId, bool isActive, string reason, string? modifiedBy, DateTime utcNow, CancellationToken ct)`
  - `Task<bool> HasCompanyMembershipAsync(Guid userId, Guid companyId, CancellationToken ct)`
  - `Task<bool> HasAnyCompanyAsync(Guid userId, CancellationToken ct)`
  - `Task<IReadOnlyList<Guid>> FilterExistingRoleIdsAsync(IReadOnlyList<Guid> roleIds, Guid companyId, CancellationToken ct)`
  - `Task<string?> GetCompanyNameAsync(Guid companyId, CancellationToken ct)`
- `src/3.Persistence/Repositories/Security/UserAdminRepository.cs` (nuevo) — Dapper + `ISqlConnectionFactory`. Registro en `src/3.Persistence/Configuration/ConfigureServices.cs`.

### G. Controller

- `src/4.Services.WebApi/Controllers/Security/UsersController.cs` — 3 endpoints nuevos bajo el `[PermissionResource("Users")]` ya presente a nivel de clase:
  - `POST /invite` → default `CanCreate`.
  - `PUT /{userId:guid}/status` → default `CanUpdate`.
  - `POST /{userId:guid}/force-password-reset` → **`[RequirePermission(PermissionFlags.CanExecute)]`** (override explícito: no es un "create", es una acción operativa).

### H. Migración

- Una sola migración EF: `AddUserStatusChangeReason` — columna `StatusChangeReason nvarchar(500) NULL` en `Security.Users`.

### I. Tests

- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Auth/…` para los 3 handlers de auth.
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/Commands/…` para los 3 comandos nuevos.

**Out of scope (para specs futuras):**

- **Items 18, 19, 20, 21** — SPEC 28 (membresía multi-empresa, permisos efectivos, roles bulk, reporte paginado).
- **Item 22 y la tabla `Security.AuditLogs`** — SPEC 29. Esta spec **no** escribe filas de auditoría; cuando 29 aterrice, las capta el interceptor sin tocar estos handlers.
- **Invite cross-tenant de SuperAdmin** — `companyId` sale siempre del JWT. Si hace falta, va en spec aparte.
- **Invitación masiva** (CSV / array de emails) — un invite por request.
- **Tabla de invitaciones pendientes** con expiración propia, y endpoint para listarlas/cancelarlas. Se usa el token de Identity y su vida útil por defecto.
- **Configurar la vida útil del token de Identity** (`DataProtectionTokenProviderOptions.TokenLifespan`) — queda el default. Si 24h resulta corto para una invitación, se ajusta en otra spec.
- **Motor de plantillas de correo** (Razor / Liquid / MJML) — HTML en `string` interpolado.
- **i18n de los correos** — solo inglés, como el correo ya existente de `RequestEmailChange`.
- **Gate de login para usuarios sin contraseña** — `LoginCommandHandler` no se toca; `CheckPasswordAsync` ya falla.
- **Rate limiting sobre `/invite`** — los 3 endpoints nuevos quedan fuera de la policy `Strict`.
- **Endpoint separado de "reenviar invitación"** — `POST /invite` sobre una invitación pendiente reenvía, es idempotente.
- **Cambios sobre `POST /Users/register`** — el auto-registro sigue igual, sin correo.
- **Cambios sobre `PUT /Users/{userId}/roles`** — sigue aceptando nombres de rol.

---

## Data model

### `ApplicationUser` (modify, `src/1.Domain/Security/applicationuser.cs`)

```csharp
/// <summary>Motivo del último cambio de estado (activación/desactivación). Null si nunca cambió.</summary>
public string? StatusChangeReason { get; set; }
```

Única columna nueva de la spec. `nvarchar(500)`, nullable. No se indexa (no se filtra por ella).

**Nota crítica de auditoría:** `ApplicationUser` implementa `IAuditableEntity` pero hereda de `IdentityUser<Guid>`, **no** de `BaseAuditableEntity`. El `AuditableEntitySaveChangesInterceptor` recorre `ChangeTracker.Entries<BaseAuditableEntity>()`, por lo que **no** toca `ApplicationUser`. Todo handler de esta spec que mute el usuario setea explícitamente:

```csharp
user.LastModified = DateTime.UtcNow;
user.LastModifiedBy = currentUserService.UserId;
```

### Configuración (`appsettings.json` + `appsettings.Development.json`)

```json
"AppUrls": {
  "FrontendBaseUrl": "https://localhost:4200"
}
```

```csharp
// src/2.Application/Common/Options/AppUrlsOptions.cs
public sealed class AppUrlsOptions
{
    public const string SectionName = "AppUrls";

    /// <summary>Base absoluta del front, sin barra final. Ej: https://app.join.com</summary>
    public string FrontendBaseUrl { get; set; } = string.Empty;
}
```

Bindeo en `Program.cs`: `builder.Services.Configure<AppUrlsOptions>(builder.Configuration.GetSection(AppUrlsOptions.SectionName));`. Los handlers la reciben como `IOptions<AppUrlsOptions>`. Si `FrontendBaseUrl` viene vacío, el handler devuelve `Response<T>.Error("FRONTEND_URL_NOT_CONFIGURED", …)` en vez de armar un link roto.

### Comandos de `Auth` modificados

```csharp
// src/2.Application/UseCases/Security/Auth/SetupPassword/SetupPasswordCommand.cs
public sealed record SetupPasswordCommand : IRequest<Response<bool>>
{
    public string Email { get; init; } = string.Empty;          // NUEVO
    public string Token { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
    public string ConfirmPassword { get; init; } = string.Empty;
}

// src/2.Application/UseCases/Security/Auth/ResetPassword/ResetPasswordCommand.cs
public sealed record ResetPasswordCommand : IRequest<Response<bool>>
{
    public string Email { get; init; } = string.Empty;          // NUEVO
    public string Token { get; init; } = string.Empty;
    public string NewPassword { get; init; } = string.Empty;
    public string ConfirmPassword { get; init; } = string.Empty;
}

// Sin cambios de forma
public sealed record ForgotPasswordCommand : IRequest<Response<bool>>
{
    public string Email { get; init; } = string.Empty;
}
```

`SetupPasswordRequestDto` y `ResetPasswordRequestDto` reciben el mismo campo `Email`.

### Item 15 — Invite

```csharp
// src/2.Application.DTO/Security/User/InviteUserRequestDto.cs
public sealed record InviteUserRequestDto(
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<Guid> RoleIds);

// src/2.Application.DTO/Security/User/InviteUserResultDto.cs
public sealed record InviteUserResultDto(
    Guid UserId,
    string Email,
    InviteOutcome Outcome);

// src/2.Application.DTO/Security/User/InviteOutcome.cs
public enum InviteOutcome
{
    /// <summary>Usuario nuevo creado sin password + correo de invitación.</summary>
    Created = 1,
    /// <summary>El usuario ya existía en otra empresa; se agregó UserCompany + UserRoleCompany + correo de aviso.</summary>
    MembershipAdded = 2,
    /// <summary>El usuario ya existía en esta empresa sin password fijada; se reenvió el correo con token nuevo.</summary>
    InvitationResent = 3
}

// src/2.Application/UseCases/Security/Users/Commands/InviteUser/InviteUserCommand.cs
public sealed record InviteUserCommand(
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<Guid> RoleIds)
    : ITransactionalCommand<Response<InviteUserResultDto>>;
```

**Detección de "invitación pendiente"**: `await userManager.HasPasswordAsync(user) == false`. No hay tabla de invitaciones; el estado "pendiente" es exactamente "existe sin `PasswordHash`".

**Árbol de decisión del handler** (email normalizado a lower-trim en los tres casos):

| Estado encontrado | Acción | `Outcome` | HTTP |
|---|---|---|---|
| No existe usuario | Crea + `UserCompany` + `UserRoleCompany` + correo invitación | `Created` | 200 |
| Existe, **sin** membresía en este `companyId` | Agrega `UserCompany` + `UserRoleCompany` + correo de aviso | `MembershipAdded` | 200 |
| Existe **con** membresía, `HasPasswordAsync == false` | Reemplaza `UserRoleCompany` por `RoleIds` + correo con token nuevo | `InvitationResent` | 200 |
| Existe **con** membresía, `HasPasswordAsync == true` | Ninguna | — | **409** `USER_ALREADY_EXISTS` |

`IsDefault` de `UserCompany` = `true` solo si `IUserAdminRepository.HasAnyCompanyAsync(userId, ct)` devuelve `false`.

### Item 16 — Change status

```csharp
// src/2.Application.DTO/Security/User/ChangeUserStatusRequestDto.cs
public sealed record ChangeUserStatusRequestDto(bool IsActive, string Reason);

// src/2.Application/UseCases/Security/Users/Commands/ChangeUserStatus/ChangeUserStatusCommand.cs
public sealed record ChangeUserStatusCommand(Guid UserId, bool IsActive, string Reason)
    : IRequest<Response<bool>>;
```

**No es `ITransactionalCommand`.** No muta entidades EF: lee y escribe por Dapper, porque el filtro global `u.GcRecord == 0 && u.IsActive` de `ApplicationUserConfiguration.cs:41` hace invisible al usuario inactivo para EF y `UserManager`.

Efectos al pasar a `IsActive = false`, en este orden:

1. `IUserAdminRepository.SetUserActiveStatusAsync(userId, false, reason, currentUser.UserId, utcNow, ct)` — un `UPDATE` que setea `IsActive`, `StatusChangeReason`, `LastModified`, `LastModifiedBy`.
2. `IUserAdminRepository.RevokeActiveRefreshTokensAsync(userId, utcNow, ct)`.
3. `IUserAdminRepository.CloseActiveConnectionsAsync(userId, utcNow, ct)`.
4. `IPermissionService.InvalidateUserCacheAsync(companyId, userId, ct)` — envuelto en `try/catch` con log warning; un fallo de caché no revierte el cambio de estado.

Al pasar a `IsActive = true`: solo el paso 1 más el paso 4. No se reactivan tokens revocados.

Los pasos 1 a 3 son tres `UPDATE` independientes sin transacción envolvente. Si el 2 o el 3 fallan, el usuario ya quedó desactivado pero conserva sesiones vivas. Aceptado: el reintento del mismo request es idempotente y el paso 1 corta con `STATUS_UNCHANGED` recién cuando el estado coincide, así que un segundo intento vuelve a ejecutar 2 y 3. Ver Riesgos.

### Item 17 — Force password reset

```csharp
// src/2.Application.DTO/Security/User/ForceUserPasswordResetRequestDto.cs
public sealed record ForceUserPasswordResetRequestDto(string? Reason);

// src/2.Application/UseCases/Security/Users/Commands/ForceUserPasswordReset/ForceUserPasswordResetCommand.cs
public sealed record ForceUserPasswordResetCommand(Guid UserId, string? Reason)
    : IRequest<Response<bool>>;
```

No es `ITransactionalCommand`: no muta entidades EF. Solo genera token, revoca refresh tokens vía Dapper y manda correo.

### `IUserAdminRepository` (nuevo)

```csharp
public sealed record UserAdminSnapshot(
    Guid UserId,
    string? Email,
    string? FirstName,
    bool IsActive,
    int GcRecord,
    bool HasMembership);

public interface IUserAdminRepository
{
    Task<int> RevokeActiveRefreshTokensAsync(Guid userId, DateTime utcNow, CancellationToken ct = default);
    Task<int> CloseActiveConnectionsAsync(Guid userId, DateTime utcNow, CancellationToken ct = default);
    Task<UserAdminSnapshot?> GetAdminSnapshotAsync(
        Guid userId, Guid companyId, CancellationToken ct = default);
    Task<bool> SetUserActiveStatusAsync(
        Guid userId, bool isActive, string reason, string? modifiedBy, DateTime utcNow,
        CancellationToken ct = default);
    Task<bool> HasCompanyMembershipAsync(Guid userId, Guid companyId, CancellationToken ct = default);
    Task<bool> HasAnyCompanyAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> FilterExistingRoleIdsAsync(
        IReadOnlyList<Guid> roleIds, Guid companyId, CancellationToken ct = default);
    Task<string?> GetCompanyNameAsync(Guid companyId, CancellationToken ct = default);
}
```

Todas las queries filtran `GcRecord = 0`. **Ninguna filtra por `IsActive`**: el repositorio existe justamente para poder ver y mutar usuarios inactivos, que EF esconde. `FilterExistingRoleIdsAsync` cruza `Security.Roles` con `Security.RoleCompanies` por `companyId` para que no se pueda asignar un rol de otro tenant; el handler compara el count devuelto contra el pedido y corta con `ROLE_NOT_FOUND` si no coinciden.

### `TransactionBehavior` (modify)

`src/2.Application/Common/TransactionBehavior.cs` pasa a revertir cuando el handler devuelve una falla de negocio:

```csharp
var response = await next();

if (IsFailedResponse(response))
{
    await unitOfWork.RollbackAsync(cancellationToken);
    return response;
}

await unitOfWork.CommitAsync(cancellationToken);
return response;
```

```csharp
private static bool IsFailedResponse(TResponse response)
{
    if (response is null) return false;
    var property = response.GetType().GetProperty(nameof(Response<object>.IsSuccess));
    if (property is null || property.PropertyType != typeof(bool)) return false;
    return property.GetValue(response) is false;
}
```

Reflexión porque `Response<T>` no tiene interfaz común ni clase base. Cualquier `TResponse` que no exponga un `bool IsSuccess` devuelve `false` y conserva el comportamiento actual (commit). El `catch` con `RollbackAsync` + `throw` se mantiene sin cambios.

### Nota sobre `Response<T>`

`src/2.Application/Common/Response.cs` expone **solo** la factory estática `Error(message, errors = null)`. **No existe `Response<T>.Success(...)`.** Los caminos felices se construyen con inicializador de objeto, como el resto del repo:

```csharp
return new Response<InviteUserResultDto>
{
    IsSuccess = true,
    Message = "Invitation sent.",
    Data = result
};
```

---

## Implementation plan

### F0 — `TransactionBehavior` revierte las fallas de negocio

1. Modificar `src/2.Application/Common/TransactionBehavior.cs` con el `IsFailedResponse` de la sección Data model.
2. `dotnet test` **antes de seguir**. Este cambio altera la semántica de todos los `ITransactionalCommand` del repo: los que hoy devuelven `Response<T>.Error(...)` después de escribir filas dejan de commitear. Si algún test existente falla, es porque dependía del commit indebido — revisar caso por caso antes de tocar el test.
3. Agregar `TransactionBehaviorTests` con 4 casos: request que no es `ITransactionalCommand` → no abre transacción; `Response<T>` con `IsSuccess = true` → commit; `Response<T>` con `IsSuccess = false` → rollback y devuelve la respuesta sin lanzar; excepción → rollback y propaga.
4. `dotnet build -c Release` → 0 errores.

### F1 — Config de URLs + plantillas de correo

1. Crear `src/2.Application/Common/Options/AppUrlsOptions.cs` con `SectionName` y `FrontendBaseUrl`.
2. Agregar la sección `AppUrls` a `appsettings.json` y `appsettings.Development.json`.
3. Registrar el bind en `Program.cs`: `builder.Services.Configure<AppUrlsOptions>(...)`.
4. Crear `src/2.Application/Common/Email/AuthEmailTemplates.cs` (static) con los 3 builders y un helper privado `BuildLink(baseUrl, path, email, token)` que hace `WebUtility.UrlEncode` de `email` y `token` y recorta la barra final de `baseUrl`.
5. `dotnet build -c Release` → 0 errores. Nada cambia en runtime todavía.

### F2 — Handlers de `Auth` (desbloquea los 3 endpoints que hoy devuelven 500)

1. Agregar `Email` a `SetupPasswordCommand`, `ResetPasswordCommand`, `SetupPasswordRequestDto`, `ResetPasswordRequestDto`.
2. `ForgotPasswordCommandHandler`: `FindByEmailAsync` → si null o `!IsActive` o `GcRecord != 0`, **devolver éxito igual** (no filtrar existencia de cuentas). Si existe: `GeneratePasswordResetTokenAsync` + `AuthEmailTemplates.BuildForgotPassword` + `SendEmailAsync`. Si el envío falla → `EMAIL_DELIVERY_FAILED`.
3. `ForgotPasswordCommandValidator`: `Email` `NotEmpty().EmailAddress()`.
4. `ResetPasswordCommandHandler`: `FindByEmailAsync` → null → devolver `INVALID_TOKEN` (mismo error que token inválido, no distinguir). `ResetPasswordAsync(user, token, newPassword)` → `!Succeeded` → `INVALID_TOKEN` con `result.Errors` mapeados.
5. `ResetPasswordCommandValidator`: `Email` email válido; `Token` `NotEmpty()`; `NewPassword` `NotEmpty().MinimumLength(8)`; `ConfirmPassword.Equal(x => x.NewPassword)`.
6. `SetupPasswordCommandHandler`: igual que reset, más `user.EmailConfirmed = true` al terminar bien, y `UpdateAsync`. Corta con `PASSWORD_ALREADY_SET` si `HasPasswordAsync(user) == true`.
7. `SetupPasswordCommandValidator`: mismas reglas que reset.
8. `dotnet build -c Release` → 0 errores. Smoke: `POST /auth/forgot-password` con un email semilla → 200 y correo en el log de SendGrid.

### F3 — Migración `StatusChangeReason`

1. Agregar `public string? StatusChangeReason { get; set; }` a `ApplicationUser`.
2. Configurar `nvarchar(500)` en la configuración EF de `ApplicationUser`.
3. `dotnet ef migrations add AddUserStatusChangeReason --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi`.
4. `dotnet ef database update` → aplica limpio. La app arranca sin cambio de comportamiento.

### F4 — `IUserAdminRepository` + implementación Dapper

1. Crear la interfaz en `src/2.Application/Interface/Persistence/Security/IUserAdminRepository.cs` con los 8 métodos y el record `UserAdminSnapshot`.
2. Crear `src/3.Persistence/Repositories/Security/UserAdminRepository.cs` con `ISqlConnectionFactory`. SQL de cada método:
   - `RevokeActiveRefreshTokensAsync`: `UPDATE Security.UserRefreshTokens SET IsRevoked = 1, LastModified = @UtcNow WHERE UserId = @UserId AND IsRevoked = 0 AND GcRecord = 0`.
   - `CloseActiveConnectionsAsync`: `UPDATE Security.UserConnectionLogs SET IsActiveSession = 0, DisconnectionDate = @UtcNow, LastModified = @UtcNow WHERE UserId = @UserId AND IsActiveSession = 1 AND GcRecord = 0`.
   - `GetAdminSnapshotAsync`: `SELECT u.Id AS UserId, u.Email, u.FirstName, u.IsActive, u.GcRecord, CASE WHEN EXISTS (SELECT 1 FROM Security.UserCompanies uc WHERE uc.UserId = u.Id AND uc.CompanyId = @CompanyId AND uc.GcRecord = 0) THEN 1 ELSE 0 END AS HasMembership FROM Security.Users u WHERE u.Id = @UserId AND u.GcRecord = 0`. **Sin filtro de `IsActive`** — es el punto de todo el repositorio.
   - `SetUserActiveStatusAsync`: `UPDATE Security.Users SET IsActive = @IsActive, StatusChangeReason = @Reason, LastModified = @UtcNow, LastModifiedBy = @ModifiedBy WHERE Id = @UserId AND GcRecord = 0`. Devuelve `rowsAffected == 1`.
   - `HasCompanyMembershipAsync`: `SELECT COUNT(1) FROM Security.UserCompanies WHERE UserId = @UserId AND CompanyId = @CompanyId AND GcRecord = 0`.
   - `HasAnyCompanyAsync`: idem sin el filtro de `CompanyId`.
   - `FilterExistingRoleIdsAsync`: `SELECT r.Id FROM Security.Roles r INNER JOIN Security.RoleCompanies rc ON rc.RoleId = r.Id AND rc.CompanyId = @CompanyId AND rc.GcRecord = 0 WHERE r.Id IN @RoleIds AND r.GcRecord = 0`.
   - `GetCompanyNameAsync`: `SELECT Name FROM Common.Companies WHERE Id = @CompanyId AND GcRecord = 0`.
3. Registrar en `src/3.Persistence/Configuration/ConfigureServices.cs`.
4. `dotnet build -c Release` → 0 errores.

### F5 — Item 15: `InviteUser`

1. Crear los DTOs: `InviteUserRequestDto`, `InviteUserResultDto`, `InviteOutcome`.
2. `InviteUserCommand` como `ITransactionalCommand<Response<InviteUserResultDto>>`.
3. `InviteUserCommandValidator`:
   - `Email` `NotEmpty().EmailAddress()`.
   - `FirstName` / `LastName` `NotEmpty().MaximumLength(100)`.
   - `RoleIds` `NotNull().NotEmpty()`, tope `<= 20` (`TOO_MANY_ROLES`), sin duplicados (`DUPLICATE_ROLE`), cada `Guid` `NotEmpty()`.
4. `InviteUserCommandHandler` — dependencias: `UserManager<ApplicationUser>`, `IUnitOfWork`, `IUserAdminRepository`, `ICurrentUserService`, `IEmailService`, `IOptions<AppUrlsOptions>`.
   Secuencia:
   1. `companyId == Guid.Empty` → `TENANT_REQUIRED`.
   2. `FrontendBaseUrl` vacío → `FRONTEND_URL_NOT_CONFIGURED`.
   3. `FilterExistingRoleIdsAsync` → count distinto al pedido → `ROLE_NOT_FOUND`.
   4. `FindByEmailAsync` → resolver la rama del árbol de decisión de la sección Data model.
   5. Escribir `UserCompany` / `UserRoleCompany` vía `IUnitOfWork.GetRepository<T>()`.
   6. `GeneratePasswordResetTokenAsync` + armar link + `SendEmailAsync`.
   7. Si el correo falla → `EMAIL_DELIVERY_FAILED`. La transacción **revierte** gracias a F0 (no queda un usuario huérfano sin forma de activarse). Sin F0 este paso commitearía las filas.
5. `dotnet build -c Release` → 0 errores.

### F6 — Item 16: `ChangeUserStatus`

1. Crear `ChangeUserStatusRequestDto` y `ChangeUserStatusCommand` (`IRequest<Response<bool>>`, **no** transaccional).
2. `ChangeUserStatusCommandValidator`: `UserId` `NotEmpty()`; `Reason` `NotEmpty().MaximumLength(500)`.
3. `ChangeUserStatusCommandHandler` — dependencias: `IUserAdminRepository`, `ICurrentUserService`, `IPermissionService`, `ILogger<ChangeUserStatusCommandHandler>`. **Sin `UserManager`**: el filtro global de EF no ve al usuario inactivo.
   Guardas, en orden:
   1. `companyId == Guid.Empty` → `TENANT_REQUIRED`.
   2. `userId == currentUser.UserId` → `CANNOT_CHANGE_OWN_STATUS`. Evita que un admin se auto-desactive y quede la empresa sin acceso.
   3. `GetAdminSnapshotAsync(userId, companyId)` null → `USER_NOT_FOUND`.
   4. `snapshot.HasMembership == false` → `USER_NOT_FOUND` (no filtrar usuarios de otros tenants).
   5. `snapshot.IsActive == request.IsActive` → `STATUS_UNCHANGED`.
   Luego aplica los 4 efectos de la sección Data model.
4. `dotnet build -c Release` → 0 errores. Smoke: desactivar un usuario, después **reactivarlo** — el segundo request debe devolver 200 y no 404.

### F7 — Item 17: `ForceUserPasswordReset`

1. Crear `ForceUserPasswordResetRequestDto` y `ForceUserPasswordResetCommand`.
2. `ForceUserPasswordResetCommandValidator`: `UserId` `NotEmpty()`; `Reason` `MaximumLength(500)` (opcional).
3. `ForceUserPasswordResetCommandHandler`: guardas 1, 3, 4 de F6 vía `GetAdminSnapshotAsync` (sin la de auto-referencia: forzarse el reseteo a uno mismo es válido), más una guarda propia: `snapshot.IsActive == false` → `USER_INACTIVE` (400). Es necesaria porque el token se genera con `UserManager.GeneratePasswordResetTokenAsync`, y el filtro global de EF no devuelve usuarios inactivos — sin esa guarda el `FindByIdAsync` daría null y el error sería engañoso. Luego `GeneratePasswordResetTokenAsync` → link → `AuthEmailTemplates.BuildForcedReset` → `SendEmailAsync` → `RevokeActiveRefreshTokensAsync`. Si el correo falla → `EMAIL_DELIVERY_FAILED` y **no** revoca tokens.
4. `dotnet build -c Release` → 0 errores.

### F8 — Controller

1. `UsersController` — 3 endpoints nuevos, con `[ProducesResponseType]` y XML doc como los existentes:
   - `POST /invite` `[FromBody] InviteUserRequestDto`.
   - `PUT /{userId:guid}/status` `[FromBody] ChangeUserStatusRequestDto`.
   - `POST /{userId:guid}/force-password-reset` `[FromBody] ForceUserPasswordResetRequestDto` + `[RequirePermission(PermissionFlags.CanExecute)]`.
2. Mapeo de errores a HTTP:

   | Código | HTTP |
   |---|---|
   | `TENANT_REQUIRED` | 400 |
   | `USER_NOT_FOUND` | 404 |
   | `ROLE_NOT_FOUND` | 404 |
   | `USER_ALREADY_EXISTS` | 409 |
   | `CANNOT_CHANGE_OWN_STATUS` | 409 |
   | `STATUS_UNCHANGED` | 409 |
   | `USER_INACTIVE` | 400 |
   | `EMAIL_DELIVERY_FAILED` | 502 |
   | `FRONTEND_URL_NOT_CONFIGURED` | 500 |
   | resto sin éxito | 400 |

3. `AuthController` no cambia de rutas; solo hereda los handlers nuevos de F2.
4. `dotnet build -c Release` → 0 errores.

### F9 — Tests (~42 casos)

Espejo de la ruta fuente, con AutoFixture + Moq + FluentAssertions.

- `TransactionBehaviorTests` — 4: request no transaccional no abre transacción; `IsSuccess = true` → commit; `IsSuccess = false` → rollback sin lanzar; excepción → rollback y propaga.
- `ForgotPasswordCommandHandlerTests` — 3: email inexistente devuelve éxito sin mandar correo; email válido manda correo; envío falla → `EMAIL_DELIVERY_FAILED`.
- `ResetPasswordCommandHandlerTests` — 3: token válido cambia password; token inválido → `INVALID_TOKEN`; email inexistente → `INVALID_TOKEN`.
- `SetupPasswordCommandHandlerTests` — 4: happy path setea `EmailConfirmed = true`; password ya fijada → `PASSWORD_ALREADY_SET`; token inválido; email inexistente.
- `SetupPasswordCommandValidatorTests` — 3: `ConfirmPassword` distinto falla; password corta falla; caso válido pasa.
- `InviteUserCommandHandlerTests` — 8: usuario nuevo → `Created` con `IsDefault = true`; usuario nuevo con empresa previa → `IsDefault = false`; existente en otra empresa → `MembershipAdded`; existente pendiente → `InvitationResent` con roles reemplazados; existente con password → `USER_ALREADY_EXISTS`; tenant vacío → `TENANT_REQUIRED`; rol de otro tenant → `ROLE_NOT_FOUND`; correo falla → `EMAIL_DELIVERY_FAILED` y devuelve `IsSuccess = false` (el rollback lo garantiza F0, verificado en `TransactionBehaviorTests`).
- `InviteUserCommandValidatorTests` — 4: email inválido; `RoleIds` vacío; 21 roles → `TOO_MANY_ROLES`; duplicados → `DUPLICATE_ROLE`.
- `ChangeUserStatusCommandHandlerTests` — 8: desactivar llama `SetUserActiveStatusAsync(false)` + revoca tokens + cierra conexiones + invalida caché; **reactivar un usuario inactivo devuelve 200** (snapshot por Dapper, no `FindByIdAsync`); activar no revoca tokens ni cierra conexiones; auto-desactivación → `CANNOT_CHANGE_OWN_STATUS`; snapshot null → `USER_NOT_FOUND`; `HasMembership = false` → `USER_NOT_FOUND`; mismo estado → `STATUS_UNCHANGED`; fallo de caché no revierte el cambio.
- `ForceUserPasswordResetCommandHandlerTests` — 5: happy path manda correo y revoca tokens; correo falla → no revoca; snapshot null → `USER_NOT_FOUND`; `HasMembership = false` → `USER_NOT_FOUND`; usuario inactivo → `USER_INACTIVE`.

`dotnet test --filter "FullyQualifiedName~InviteUser|FullyQualifiedName~ChangeUserStatus|FullyQualifiedName~Password|FullyQualifiedName~TransactionBehavior"` → 0 fallidos.

### F10 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → gate de 90% en `JOIN.Application` pasa.
3. `dotnet ef database update` sobre una base limpia → migración aplica y el seeder corre.
4. Smoke manual con curl: invitar un email nuevo → recibir correo → `POST /auth/setup-password` con el token → `POST /users/login` con la password nueva → 200.
5. Smoke manual: desactivar ese usuario → su refresh token deja de funcionar en `POST /users/refresh`.
6. `CURL_REQUESTS.md` — agregar los 3 endpoints nuevos + los 3 de `Auth` ya funcionales, con el header `X-Company-Id`.

---

## Acceptance criteria

### F0 — `TransactionBehavior`

- [ ] Un `ITransactionalCommand` cuyo handler devuelve `Response<T>` con `IsSuccess = false` produce `RollbackAsync` y **no** `CommitAsync`.
- [ ] Un `ITransactionalCommand` cuyo handler devuelve `Response<T>` con `IsSuccess = true` produce `CommitAsync`.
- [ ] Un `ITransactionalCommand` cuyo `TResponse` no expone `bool IsSuccess` sigue commiteando (comportamiento actual preservado).
- [ ] Un request que no implementa `ITransactionalCommand` no abre transacción.
- [ ] Una excepción sigue produciendo `RollbackAsync` + rethrow.
- [ ] `dotnet test` pasa con la suite completa **antes** de implementar el resto de la spec.

### F1 — Config y plantillas

- [ ] `AppUrlsOptions.SectionName == "AppUrls"` y la sección existe en `appsettings.json` y `appsettings.Development.json`.
- [ ] `AuthEmailTemplates.BuildInvitation/BuildForgotPassword/BuildForcedReset` devuelven HTML con `email` y `token` pasados por `WebUtility.UrlEncode` dentro del link, y los nombres por `WebUtility.HtmlEncode`.
- [ ] El link generado no tiene doble barra cuando `FrontendBaseUrl` termina en `/`.

### F2 — Handlers de `Auth`

- [ ] `POST /api/v1/auth/forgot-password` con un email existente → 200 y una llamada a `IEmailService.SendEmailAsync`.
- [ ] `POST /api/v1/auth/forgot-password` con un email inexistente → 200 y **cero** llamadas a `SendEmailAsync`.
- [ ] `POST /api/v1/auth/forgot-password` cuando `SendEmailAsync` devuelve `false` → 502 `EMAIL_DELIVERY_FAILED`.
- [ ] `POST /api/v1/auth/reset-password` con `{email, token, newPassword, confirmPassword}` válido → 200 y la password nueva sirve en `POST /users/login`.
- [ ] `POST /api/v1/auth/reset-password` con token inválido → 400 `INVALID_TOKEN`.
- [ ] `POST /api/v1/auth/reset-password` con email inexistente → 400 `INVALID_TOKEN` (mismo código que token inválido).
- [ ] `POST /api/v1/auth/reset-password` con `confirmPassword != newPassword` → 400 del validator, sin tocar `UserManager`.
- [ ] `POST /api/v1/auth/setup-password` válido → 200 y `user.EmailConfirmed == true` en DB.
- [ ] `POST /api/v1/auth/setup-password` sobre un usuario que ya tiene password → 400 `PASSWORD_ALREADY_SET`.
- [ ] Ninguno de los 3 endpoints devuelve 500 por falta de handler.

### F3 — Migración

- [ ] Existe una única migración nueva llamada `AddUserStatusChangeReason`.
- [ ] `Security.Users.StatusChangeReason` es `nvarchar(500) NULL`.
- [ ] `dotnet ef database update` sobre base limpia aplica sin error y el `DatabaseSeeder` corre después.

### F4 — `IUserAdminRepository`

- [ ] Los 8 métodos existen con la firma de la sección Data model y filtran `GcRecord = 0`.
- [ ] **Ningún método filtra por `IsActive`**: `GetAdminSnapshotAsync` devuelve el usuario aunque esté inactivo.
- [ ] `GetAdminSnapshotAsync` resuelve `HasMembership` en la misma query, sin round-trip extra.
- [ ] `SetUserActiveStatusAsync` setea `IsActive`, `StatusChangeReason`, `LastModified` y `LastModifiedBy` en un solo `UPDATE`, y devuelve `false` si no afectó filas.
- [ ] `FilterExistingRoleIdsAsync` no devuelve roles que no estén en `Security.RoleCompanies` para el `companyId` pasado.
- [ ] `RevokeActiveRefreshTokensAsync` deja `IsRevoked = 1` solo en filas que estaban en 0.
- [ ] `CloseActiveConnectionsAsync` setea `IsActiveSession = 0` y `DisconnectionDate` en las filas que estaban activas.
- [ ] La implementación usa `ISqlConnectionFactory` + Dapper, sin change tracker de EF.
- [ ] Está registrado en `src/3.Persistence/Configuration/ConfigureServices.cs`.

### F5 — Item 15: invite

- [ ] `POST /api/v1/Users/invite` con `{email, firstName, lastName, roleIds[]}` de un email nuevo → 200 con `outcome = "Created"`.
- [ ] El body **no** acepta `companyId`; el tenant sale del JWT.
- [ ] El usuario creado queda con `IsActive = true`, `EmailConfirmed = false` y sin `PasswordHash`.
- [ ] Se crea una fila `UserCompany` con `IsDefault = true` cuando el usuario no tenía ninguna otra empresa, y `false` cuando ya tenía.
- [ ] Se crea una fila `UserRoleCompany` por cada `roleId` del request, con el `companyId` del token.
- [ ] Email de un usuario que existe pero no pertenece a esta empresa → 200 con `outcome = "MembershipAdded"` y sin crear un `ApplicationUser` nuevo.
- [ ] Email de un usuario que pertenece a esta empresa y **no** tiene password → 200 con `outcome = "InvitationResent"`, y sus `UserRoleCompany` de esta empresa quedan **exactamente** los `roleIds` del request.
- [ ] Email de un usuario que pertenece a esta empresa y **sí** tiene password → 409 `USER_ALREADY_EXISTS`.
- [ ] `currentUserService.CompanyId == Guid.Empty` → 400 `TENANT_REQUIRED`, sin tocar DB ni correo.
- [ ] Un `roleId` que existe pero pertenece a otro tenant → 404 `ROLE_NOT_FOUND`, sin crear nada.
- [ ] `FrontendBaseUrl` vacío → 500 `FRONTEND_URL_NOT_CONFIGURED`, sin crear nada.
- [ ] `SendEmailAsync` devuelve `false` → 502 `EMAIL_DELIVERY_FAILED` y **cero filas** nuevas en `Security.Users`, `Security.UserCompanies` y `Security.UserRoleCompanies`.
- [ ] El correo de invitación contiene un link a `{FrontendBaseUrl}/setup-password?email=…&token=…`.
- [ ] Validator: email inválido, `roleIds` vacío, 21 roles (`TOO_MANY_ROLES`) y `roleIds` con duplicados (`DUPLICATE_ROLE`) fallan con 400.

### F6 — Item 16: status

- [ ] `PUT /api/v1/Users/{userId}/status` con `{isActive: false, reason}` → 200, `IsActive = false` y `StatusChangeReason = reason` en DB.
- [ ] **Reactivar después de desactivar** (`{isActive: true, reason}` sobre el mismo usuario) → 200, no 404. Éste es el criterio que verifica que el flujo esquiva el filtro global de EF.
- [ ] Al desactivar, todos los `UserRefreshToken` del usuario con `IsRevoked = 0` quedan en `1`.
- [ ] Al desactivar, todos los `UserConnectionLog` del usuario con `IsActiveSession = 1` quedan en `0` con `DisconnectionDate` seteado.
- [ ] Al desactivar, se llama `IPermissionService.InvalidateUserCacheAsync(companyId, userId)` exactamente una vez.
- [ ] Al activar (`isActive: true`), se invalida la caché y **no** se llama a revocar tokens ni cerrar conexiones.
- [ ] Después de desactivar, el refresh token del usuario devuelve 401 en `POST /api/v1/users/refresh`.
- [ ] `userId` igual al del token → 409 `CANNOT_CHANGE_OWN_STATUS`, sin mutar nada.
- [ ] `userId` de un usuario sin membresía en el tenant del token → 404 `USER_NOT_FOUND`.
- [ ] `isActive` igual al valor actual → 409 `STATUS_UNCHANGED`, sin mutar nada.
- [ ] `reason` vacío → 400 del validator.
- [ ] `LastModified` y `LastModifiedBy` quedan seteados por el `UPDATE` de Dapper (el interceptor no cubre `ApplicationUser`).
- [ ] El handler **no** depende de `UserManager<ApplicationUser>`.
- [ ] Si `InvalidateUserCacheAsync` lanza, la respuesta sigue siendo 200 y el cambio de estado persiste.

### F7 — Item 17: force-password-reset

- [ ] `POST /api/v1/Users/{userId}/force-password-reset` → 200, una llamada a `SendEmailAsync` y los refresh tokens del usuario revocados.
- [ ] El correo contiene un link a `{FrontendBaseUrl}/reset-password?email=…&token=…`.
- [ ] El token del correo funciona en `POST /api/v1/auth/reset-password`.
- [ ] `SendEmailAsync` devuelve `false` → 502 `EMAIL_DELIVERY_FAILED` y los refresh tokens **no** se revocan.
- [ ] `userId` de otro tenant → 404 `USER_NOT_FOUND`.
- [ ] `userId` de un usuario inactivo → 400 `USER_INACTIVE`, sin mandar correo ni revocar tokens.
- [ ] La contraseña actual del usuario **no** cambia hasta que él complete el reset.
- [ ] `userId` igual al del token → 200 (forzarse el reseteo a uno mismo es válido).

### F8 — Controller

- [ ] Los 3 endpoints nuevos viven en `UsersController` bajo el `[PermissionResource("Users")]` de clase.
- [ ] `POST /invite` exige `CanCreate`, `PUT /{userId}/status` exige `CanUpdate` (defaults por verbo HTTP, sin `[RequirePermission]`).
- [ ] `POST /{userId}/force-password-reset` lleva `[RequirePermission(PermissionFlags.CanExecute)]`.
- [ ] Un token sin el flag requerido devuelve 403; un request sin token o sin `CompanyId` devuelve 401.
- [ ] El mapeo de códigos a HTTP de la tabla F8 está implementado tal cual.
- [ ] Los 12 endpoints existentes de `UsersController` no cambian de ruta, firma ni comportamiento.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test` → 0 fallidos, ≥ 42 tests nuevos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate de CI).
- [ ] Una sola migración EF en toda la spec.
- [ ] `CURL_REQUESTS.md` incluye los 3 endpoints nuevos y los 3 de `Auth`, con `X-Company-Id`.
- [ ] Ningún handler nuevo devuelve una entidad de dominio; todos devuelven `Response<T>` con DTO.
- [ ] Ningún handler nuevo lanza excepción para fallas de negocio esperadas.

---

## Decisiones

### Alcance y corte

- **Sí:** partir los items 15–22 en tres specs (27: items 15–17, 28: items 18–21, 29: item 22). Ocho endpoints atravesando cinco dominios (invitación/correo, ciclo de vida, membresía multi-empresa, matriz de permisos, auditoría) no cabe en un plan de implementación ejecutable.
- **No:** una sola spec con los ocho items. El plan pasaría de 10 fases a más de 25 y ninguna sería commiteable de forma aislada.
- **No:** una spec previa solo para los handlers de `Auth` faltantes. El item 15 no tiene a dónde apuntar el correo sin `setup-password`, así que van juntos. Además hoy esos 3 endpoints devuelven 500 y arreglarlos aquí no agrega alcance.

### Correcciones de infraestructura descubiertas en el relevamiento

- **Sí:** arreglar `TransactionBehavior` para que revierta cuando el handler devuelve `Response<T>` con `IsSuccess == false`. Es la raíz del problema: hoy cualquier handler transaccional que corte con `Error(...)` después de escribir filas commitea esas filas. Sin este arreglo, el requisito del item 15 de no dejar usuarios huérfanos es inalcanzable.
- **No:** mandar el correo del invite después del commit y devolver `emailSent: false`. Evitaba tocar `TransactionBehavior` pero dejaba basura en `Security.Users` ante cualquier caída de SendGrid, y no resolvía el bug latente para el resto del repo.
- **No:** lanzar una excepción dedicada en el handler del invite y mapearla en `GlobalExceptionHandler`. Habría funcionado por el `catch` existente, pero rompe la convención de `CLAUDE.md` de no lanzar para fallas de negocio esperadas.
- **Riesgo asumido:** el arreglo cambia la semántica de los ~20 `ITransactionalCommand` existentes de golpe. Por eso F0 es la primera fase y exige correr la suite completa antes de seguir. Un test que falle ahí está documentando un commit indebido preexistente, no una regresión.
- **Sí:** detección de la falla por reflexión sobre `bool IsSuccess`. `Response<T>` no tiene interfaz común ni clase base, y agregarle una es un cambio transversal a todo el repo.
- **No:** introducir `IResponse` (o `Response` no genérica) como base de `Response<T>` para evitar la reflexión. Más limpio, pero toca todos los handlers y DTOs; va en spec aparte si molesta el costo de la reflexión.
- **Sí:** el item 16 hace el flip de estado por Dapper y esquiva el filtro global de EF. `ApplicationUserConfiguration.cs:41` declara `HasQueryFilter(u => u.GcRecord == 0 && u.IsActive)`, así que `userManager.FindByIdAsync` devuelve null para un usuario inactivo y reactivarlo por EF sería imposible.
- **No:** sacar `&& u.IsActive` del filtro global. Es la corrección de raíz y `LoginCommandHandler:45` ya chequea `IsActive` a mano, pero el filtro afecta a toda consulta EF sobre usuarios en el repo (reportes, sidebar, roles) y auditar cada una excede esta spec. Anotado como candidato a spec propia.
- **No:** `IgnoreQueryFilters()` en el handler del item 16. `UserManager<ApplicationUser>` no expone el `IQueryable` subyacente, así que habría que inyectar el `ApplicationDbContext` directo en la capa Application — peor que usar el repositorio Dapper que la spec ya introduce.
- **Consecuencia aceptada:** el item 17 gana la guarda `USER_INACTIVE` (400). El token se genera con `UserManager.GeneratePasswordResetTokenAsync`, que no ve usuarios inactivos; sin la guarda el error sería un `USER_NOT_FOUND` engañoso.

### Handlers de `Auth`

- **Sí:** agregar `Email` al body de `SetupPassword` y `ResetPassword`. El token de Identity no es auto-descriptivo: sin `Email` ni `UserId` no hay forma de resolver el usuario. Es un breaking change de un contrato que hoy es un 500, así que el costo real es cero.
- **No:** `UserId` en vez de `Email`. El usuario que llega desde un correo tiene su email a mano, no un `Guid`. `ForgotPassword` ya trabaja por email.
- **No:** tabla propia de tokens de invitación con expiración configurable. El `GeneratePasswordResetTokenAsync` de Identity ya resuelve generación y validación. Una tabla propia agrega migración, limpieza y superficie de bugs.
- **Sí:** `ForgotPassword` devuelve 200 aunque el email no exista. Evita enumeración de cuentas.
- **Sí:** `ResetPassword` devuelve `INVALID_TOKEN` tanto para email inexistente como para token inválido. Mismo motivo.
- **Sí:** `SetupPassword` corta con `PASSWORD_ALREADY_SET` si el usuario ya tiene password. Un token de reset sigue siendo válido para `reset-password`; el endpoint de setup es solo para la primera activación.

### Correos

- **Sí:** los 3 correos llevan un link a `{FrontendBaseUrl}/…?email=&token=`. Un alta por invitación con un token crudo pegado en el HTML es inusable para el destinatario.
- **No:** replicar el patrón de `RequestEmailChangeCommandHandler`, que pega el token crudo en el cuerpo. Se mantiene ese endpoint como está (fuera de scope), pero no se propaga el patrón.
- **Sí:** `AppUrlsOptions` en la capa `Application`, bindeada desde `Program.cs`. `Microsoft.Extensions.Options` no rompe la dirección de dependencias; un `IUrlBuilder` en `Infrastructure` sería una interfaz de un solo método sobre una concatenación de strings.
- **Sí:** HTML interpolado en un `static class AuthEmailTemplates`. `IEmailService.SendEmailAsync` recibe `htmlContent` crudo y no hay motor de plantillas en el repo.
- **No:** Razor / Liquid / MJML para las plantillas. Tres correos no justifican un motor. Si la cantidad crece, va en spec aparte.
- **No:** i18n de los correos. El correo existente está en inglés; se mantiene un solo idioma.

### Invitación (item 15)

- **Sí:** `companyId` sale del JWT y **no** se acepta en el body. SPEC 23 fijó esa regla; aceptarlo abre cross-tenant.
- **No:** variante SuperAdmin cross-tenant del invite. Va en spec aparte si aparece la necesidad.
- **Sí:** el invitado nace `IsActive = true` con `EmailConfirmed = false` y sin `PasswordHash`. `CheckPasswordAsync` ya lo bloquea en login.
- **No:** un tercer estado "pendiente" (`IsActive = false` hasta el setup, o una columna `Status`). Obligaría a tocar `LoginCommandHandler`, el reporte de usuarios y el item 16, que ya usa `IsActive` como único eje.
- **Sí:** el estado "invitación pendiente" se deriva de `HasPasswordAsync(user) == false`. Cero estado nuevo que mantener sincronizado.
- **Sí:** `roleIds` como `Guid[]` en los endpoints nuevos. Los nombres de rol no son únicos entre tenants.
- **No:** nombres de rol como en `UpdateUserRolesDto`. Se mantiene `PUT /Users/{userId}/roles` por nombres para no romper consumidores, pero lo nuevo va por `Guid`.
- **Sí:** invite sobre un usuario existente de otra empresa agrega la membresía (`MembershipAdded`). Es el caso real de una persona que trabaja para dos empresas del sistema.
- **No:** rechazar ese caso y obligar a usar el item 18. Item 18 vive en la SPEC 28 y el flujo de invitación no debería quedar bloqueado esperándola.
- **Sí:** invite sobre una invitación pendiente reenvía con token nuevo (`InvitationResent`), idempotente.
- **No:** un endpoint separado `POST /Users/{userId}/resend-invitation`. Mismo request, mismo efecto deseado; un segundo endpoint solo agrega superficie.
- **Sí:** `InvitationResent` **reemplaza** los `UserRoleCompany` del usuario en esa empresa por los `roleIds` del request. Un reenvío suele venir de haber cargado mal los roles.
- **No:** acumular roles en el reenvío. Sin forma de quitar un rol mal asignado desde el mismo flujo.
- **Sí:** si el correo falla, la transacción del invite revierte por completo. Un usuario sin password y sin correo enviado es invisible y no se puede activar.
- **No:** dejar el usuario creado y ofrecer reenvío. Deja basura en `Security.Users` ante cualquier caída de SendGrid.
- **Sí:** `IsDefault = true` en `UserCompany` solo si el usuario no tenía otra empresa. No se pisa la empresa por defecto de alguien que ya opera en el sistema.
- **Sí:** tope de 20 `roleIds` por invite. Cubre cualquier caso real y acota el payload.

### Cambio de estado (item 16)

- **Sí:** columna `StatusChangeReason` en `Security.Users`. Permite mostrar el motivo en la UI sin depender de la SPEC 29.
- **No:** mandar el `reason` a la bitácora del item 22. Acoplaría la SPEC 27 a la 29 y dejaría el motivo invisible hasta que 29 aterrice.
- **No:** loguear el `reason` solo con Serilog. No es consultable desde la aplicación.
- **Sí:** desactivar revoca refresh tokens, cierra conexiones activas e invalida la caché de permisos y sidebar. Sin eso, el usuario desactivado sigue operando hasta que expire su access token.
- **No:** invalidar el access token en curso. Requiere una blacklist de JWT; fuera de scope. La ventana es la vida útil del access token.
- **Sí:** activar **no** reactiva los refresh tokens revocados. El usuario vuelve a loguearse; reactivar sesiones viejas es un riesgo sin beneficio.
- **Sí:** `CANNOT_CHANGE_OWN_STATUS` bloquea la auto-desactivación. Un admin que se desactiva puede dejar la empresa sin acceso.
- **No:** bloquear "desactivar al último admin activo de la empresa". Requiere contar admins por flag de permiso y decidir qué es "admin"; fuera de scope.
- **Sí:** `STATUS_UNCHANGED` (409) cuando el estado ya es el pedido. Evita revocar sesiones y mandar efectos colaterales en un no-op.
- **Sí:** el fallo de `InvalidateUserCacheAsync` se traga con log warning. La DB ya está consistente; la caché se autovence por TTL (30 min absoluto / 10 min sliding).
- **Sí:** el `UPDATE` de Dapper setea `LastModified` / `LastModifiedBy` explícitamente. El `AuditableEntitySaveChangesInterceptor` recorre `ChangeTracker.Entries<BaseAuditableEntity>()` y `ApplicationUser` hereda de `IdentityUser<Guid>`, así que **nunca** pasa por ahí — y menos aún yendo por Dapper.
- **Sí:** los 3 `UPDATE` del flujo de desactivación corren sin transacción envolvente. El comando dejó de ser `ITransactionalCommand` al abandonar EF. Un fallo parcial se corrige reintentando el mismo request. Ver Riesgos.
- **No:** hacer que `ApplicationUser` herede de `BaseAuditableEntity`. Chocaría con `IdentityUser<Guid>` y es un refactor de alcance mucho mayor.

### Reseteo forzado (item 17)

- **Sí:** `ForceUserPasswordReset` manda correo y revoca refresh tokens, sin cambiar la contraseña.
- **No:** invalidar la contraseña actual de inmediato. Dejaría al usuario sin acceso si el correo se pierde.
- **No:** un flag `MustChangePasswordAtNextLogin` en `ApplicationUser`. Obliga a tocar `LoginCommandHandler` y a que el front maneje un estado intermedio de login; fuera de scope.
- **Sí:** si el correo falla, **no** se revocan los tokens. Revocar sin haber avisado deja al usuario afuera sin explicación.
- **Sí:** `IRequest<Response<bool>>` y no `ITransactionalCommand`. No muta entidades EF: solo genera token, revoca por Dapper y manda correo.
- **Sí:** `[RequirePermission(PermissionFlags.CanExecute)]`. Forzar un reseteo no es crear un recurso; el default por verbo (`CanCreate`) es semánticamente incorrecto.
- **Riesgo asumido:** si el seed no tiene `CanExecute` prendido en la opción de sistema de `Users`, el endpoint queda cerrado para todos salvo SuperAdmin hasta que se prenda. Anotado en Riesgos.

### Transversales

- **Sí:** `IUserAdminRepository` nuevo con Dapper para las mutaciones de sesiones y las consultas de membresía. Sigue el patrón del repo: escrituras masivas y lecturas de verificación por Dapper, no por change tracker.
- **No:** meter estos métodos en un repositorio existente. `IRoleRepository` y `IRoleSystemOptionsRepository` tienen otra responsabilidad.
- **Sí:** los caminos felices se construyen con inicializador de objeto. `Response<T>` expone **solo** la factory `Error(...)`; **no existe `Response<T>.Success(...)`** (las SPEC 25 y 26 la mencionan por error).
- **No:** agregar una factory `Success` a `Response<T>` en esta spec. Es un cambio transversal a todos los handlers del repo; va en spec aparte.
- **Sí:** los 3 endpoints nuevos quedan fuera de la rate limiting policy `Strict`. Son endpoints autenticados y con permiso; el abuso está acotado.
- **No:** tocar `POST /Users/register`. El auto-registro sigue existiendo sin correo, en paralelo al alta administrada.

---

## Riesgos

| Riesgo | Mitigación |
|---|---|
| `[RequirePermission(PermissionFlags.CanExecute)]` en force-password-reset queda cerrado si el seed no tiene `CanExecute` prendido en la `SystemOption` de `Users` | Verificar el seed durante F8. Si el flag no está, prenderlo en el `DatabaseSeeder` (idempotente) o revertir el endpoint al default `CanCreate`. Documentar en `CURL_REQUESTS.md` qué flag exige. |
| Vida útil del token de Identity (24h por defecto) es corta para una invitación | Fuera de scope ajustar `DataProtectionTokenProviderOptions.TokenLifespan`. Mitigación operativa: `POST /invite` sobre una invitación pendiente reenvía con token nuevo, así que un token vencido se resuelve reinvitando. |
| Breaking change en `SetupPasswordRequestDto` y `ResetPasswordRequestDto` (campo `Email` nuevo) | Impacto real nulo: ambos endpoints hoy devuelven 500 por falta de handler, así que no hay consumidor en producción. Verificar igual que el front no los llame. |
| `SendGridEmailAdapter.SendEmailAsync` devuelve `false` en vez de lanzar, incluso con la API key sin configurar | En Development sin `SendGrid:ApiKey`, **todo invite falla con 502 y revierte**. Documentar en `CURL_REQUESTS.md` que la key es obligatoria para ejercitar el flujo. Alternativa si molesta: un `NoOpEmailService` en Development, en spec aparte. |
| El arreglo de `TransactionBehavior` cambia la semántica de los ~20 `ITransactionalCommand` existentes | F0 es la primera fase y exige `dotnet test` completo antes de seguir. Un test que falle ahí documenta un commit indebido preexistente. Si aparecen fallas masivas, la alternativa de fallback es limitar el rollback a los comandos de esta spec con un marcador propio (`ITransactionalCommand` + interfaz nueva), pero se pierde el arreglo del bug latente. |
| `IsFailedResponse` por reflexión se ejecuta en cada request transaccional | `GetProperty` sobre un tipo cerrado es barato y el `PropertyInfo` es cacheable por `TResponse` (el behavior es genérico, así que un `static readonly` por instanciación genérica basta). Si el profiling lo marca, cachear. |
| Revertir la transacción del invite ante fallo de correo deja `UserManager.CreateAsync` a mitad de camino | `InviteUserCommand` es `ITransactionalCommand` y con F0 el rollback ocurre al devolver `Error`. Verificar en F5 que `UserManager` participe de la transacción de EF (comparte el mismo `DbContext`). Si no participa, el fallback es borrar el usuario explícitamente antes de devolver el error. |
| Los 3 `UPDATE` del item 16 corren sin transacción envolvente | Si falla el revoke de tokens o el cierre de conexiones, el usuario queda desactivado con sesiones vivas. Reintentar el mismo request vuelve a ejecutar los pasos 2 y 3 (`STATUS_UNCHANGED` solo corta cuando el estado ya coincide, y el reintento con el mismo `isActive` cortaría ahí). **Limitación real**: si el paso 1 tuvo éxito, el reintento corta con `STATUS_UNCHANGED` y **no** reintenta 2 y 3. Mitigación operativa: revocar por SQL, o mover los 3 pasos a una transacción Dapper explícita durante F6 si el caso preocupa. |
| El filtro global `u.GcRecord == 0 && u.IsActive` sigue en pie | Toda consulta EF sobre usuarios sigue ciega a los inactivos: reportes que van por EF, sidebar, resolución de roles. Esta spec solo esquiva el filtro en el item 16. Auditar el resto del repo va en spec aparte. |
| Cross-tenant: un admin de la empresa A invita un email que ya existe en la empresa B y ve `MembershipAdded` | Correcto por diseño, pero **filtra la existencia de la cuenta**. El admin descubre que ese email ya está en el sistema. Aceptado: el alta de un usuario a la propia empresa es una operación con permiso `CanCreate`, y el `outcome` no revela a qué empresa pertenece. |
| El access token del usuario desactivado sigue siendo válido hasta expirar | Sin blacklist de JWT. La ventana es la vida útil del access token. Si resulta inaceptable, hace falta un check de `IsActive` por request o una blacklist; ambos en spec aparte. |
| `ApplicationUser` no pasa por el `AuditableEntitySaveChangesInterceptor` | Cada handler que lo muta setea `LastModified`/`LastModifiedBy` a mano. Riesgo de olvido en handlers futuros. Anotado en la sección Data model y cubierto por acceptance criteria de F6. |
| Los snippets de las SPEC 25 y 26 usan `Response<T>.Success(...)`, que no existe | Esta spec construye los caminos felices con inicializador de objeto. Si la SPEC 25 ya está implementada, revisar cómo se resolvió ahí para no divergir. |
| Colisión con SPEC 26 sobre `Security.UserRefreshTokens` y `Security.UserConnectionLogs` | SPEC 26 define `IRoleUserSessionRepository` con `SoftRevokeRefreshTokensExceptAsync` / `SoftRevokeActiveConnectionsAsync`; esta spec define `IUserAdminRepository` con `RevokeActiveRefreshTokensAsync` / `CloseActiveConnectionsAsync`. Son operaciones distintas (self-service con exclusión del token actual vs. administrativa total) y pueden convivir. Si ambas specs aterrizan, evaluar unificar el SQL en un helper compartido. |
| `reason` del cambio de estado guarda solo el **último** motivo | Por diseño: es una columna, no un historial. El historial llega con la SPEC 29 (`Security.AuditLogs` captura el diff de `IsActive` y `StatusChangeReason`). |
| Invitar 20 roles genera 20 inserts fila por fila vía `IUnitOfWork` | Volumen despreciable (tope 20). No justifica un bulk Dapper. |
| El front no tiene todavía las rutas `/setup-password` y `/reset-password` | Los links quedarían apuntando a un 404 del front. Coordinar con el front antes de habilitar el invite en producción. `FrontendBaseUrl` es configurable por ambiente. |

---

## Lo que NO entra en esta spec

- **Items 18, 19, 20 y 21** — membresía multi-empresa (`POST`/`DELETE /Users/{userId}/companies`), permisos efectivos (`GET /Users/{userId}/effective-permissions`), asignación masiva de roles (`PUT /Users/roles/bulk`) y paginación/búsqueda de `GET /Users/reports/my-company`. Van en la **SPEC 28**.
- **Item 22** — `GET /api/v1/Audit/security`, la tabla `Security.AuditLogs` y el interceptor de diff. Van en la **SPEC 29**. Esta spec no escribe ninguna fila de auditoría.
- **Invite cross-tenant de SuperAdmin** — el `companyId` sale siempre del JWT.
- **Invitación masiva** (CSV o array de emails) — un invite por request.
- **Tabla de invitaciones pendientes** con expiración propia, listado y cancelación.
- **Ajustar `DataProtectionTokenProviderOptions.TokenLifespan`** — queda el default de Identity.
- **Motor de plantillas de correo** e **i18n** de los correos.
- **Gate de login para usuarios sin contraseña** — `LoginCommandHandler` no se toca.
- **Flag `MustChangePasswordAtNextLogin`** — el reseteo forzado no bloquea el login actual más allá de revocar refresh tokens.
- **Blacklist de access tokens** — desactivar un usuario no invalida su JWT en curso.
- **Bloquear la desactivación del último admin activo** de la empresa.
- **Rate limiting** sobre los 3 endpoints nuevos.
- **Sacar `&& u.IsActive` del `HasQueryFilter` de `ApplicationUser`** — corrección de raíz del hallazgo 4. Esta spec solo lo esquiva en el item 16, por Dapper.
- **Introducir `IResponse` / clase base de `Response<T>`** para evitar la reflexión en `TransactionBehavior`.
- **Factory `Response<T>.Success(...)`** — cambio transversal a todo el repo.
- **`NoOpEmailService` para Development** — hoy sin `SendGrid:ApiKey` el invite falla.
- **Cambios sobre `POST /Users/register`**, `PUT /Users/{userId}/roles`, `GET /Users`, `GET /Users/reports/system` ni ningún otro endpoint existente de `UsersController`.
- **Cambios sobre `RequestEmailChangeCommandHandler`** — sigue mandando el token crudo en el cuerpo del correo.

Cada uno de esos, si aterriza, va en su propia spec.

---

## Archivos críticos

### Modify

- `src/1.Domain/Security/applicationuser.cs` — columna `StatusChangeReason`.
- `src/2.Application/Common/TransactionBehavior.cs` — rollback ante `Response<T>` con `IsSuccess = false`.
- `src/2.Application/UseCases/Security/Auth/ResetPassword/ResetPasswordCommand.cs` — campo `Email`.
- `src/2.Application/UseCases/Security/Auth/SetupPassword/SetupPasswordCommand.cs` — campo `Email`.
- `src/2.Application.DTO/Security/Auth/ResetPasswordRequestDto.cs` — campo `Email`.
- `src/2.Application.DTO/Security/Auth/SetupPasswordRequestDto.cs` — campo `Email`.
- `src/3.Persistence/Configuration/ConfigureServices.cs` — registrar `IUserAdminRepository`.
- `src/4.Services.WebApi/Controllers/Security/UsersController.cs` — 3 endpoints nuevos.
- `src/4.Services.WebApi/Program.cs` — bind de `AppUrlsOptions`.
- `src/4.Services.WebApi/appsettings.json` + `appsettings.Development.json` — sección `AppUrls`.
- `CURL_REQUESTS.md` — 3 endpoints nuevos + 3 de `Auth`.

### Create

**Application — options y plantillas:**

- `src/2.Application/Common/Options/AppUrlsOptions.cs`
- `src/2.Application/Common/Email/AuthEmailTemplates.cs`

**Application — interfaces:**

- `src/2.Application/Interface/Persistence/Security/IUserAdminRepository.cs`

**DTOs:**

- `src/2.Application.DTO/Security/User/InviteUserRequestDto.cs`
- `src/2.Application.DTO/Security/User/InviteUserResultDto.cs`
- `src/2.Application.DTO/Security/User/InviteOutcome.cs`
- `src/2.Application.DTO/Security/User/ChangeUserStatusRequestDto.cs`
- `src/2.Application.DTO/Security/User/ForceUserPasswordResetRequestDto.cs`

**Handlers de `Auth`:**

- `src/2.Application/UseCases/Security/Auth/ForgotPassword/ForgotPasswordCommandHandler.cs`
- `src/2.Application/UseCases/Security/Auth/ForgotPassword/ForgotPasswordCommandValidator.cs`
- `src/2.Application/UseCases/Security/Auth/ResetPassword/ResetPasswordCommandHandler.cs`
- `src/2.Application/UseCases/Security/Auth/ResetPassword/ResetPasswordCommandValidator.cs`
- `src/2.Application/UseCases/Security/Auth/SetupPassword/SetupPasswordCommandHandler.cs`
- `src/2.Application/UseCases/Security/Auth/SetupPassword/SetupPasswordCommandValidator.cs`

**Use cases de `Users`:**

- `src/2.Application/UseCases/Security/Users/Commands/InviteUser/InviteUserCommand.cs`
- `src/2.Application/UseCases/Security/Users/Commands/InviteUser/InviteUserCommandHandler.cs`
- `src/2.Application/UseCases/Security/Users/Commands/InviteUser/InviteUserCommandValidator.cs`
- `src/2.Application/UseCases/Security/Users/Commands/ChangeUserStatus/ChangeUserStatusCommand.cs`
- `src/2.Application/UseCases/Security/Users/Commands/ChangeUserStatus/ChangeUserStatusCommandHandler.cs`
- `src/2.Application/UseCases/Security/Users/Commands/ChangeUserStatus/ChangeUserStatusCommandValidator.cs`
- `src/2.Application/UseCases/Security/Users/Commands/ForceUserPasswordReset/ForceUserPasswordResetCommand.cs`
- `src/2.Application/UseCases/Security/Users/Commands/ForceUserPasswordReset/ForceUserPasswordResetCommandHandler.cs`
- `src/2.Application/UseCases/Security/Users/Commands/ForceUserPasswordReset/ForceUserPasswordResetCommandValidator.cs`

**Persistence:**

- `src/3.Persistence/Repositories/Security/UserAdminRepository.cs`
- `src/3.Persistence/Migrations/20260815xxxx_AddUserStatusChangeReason.cs` (+ Designer + ModelSnapshot)

**Tests:**

- `tests/UnitTests/JOIN.Application.UnitTest/Common/TransactionBehaviorTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Auth/ForgotPassword/ForgotPasswordCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Auth/ResetPassword/ResetPasswordCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Auth/SetupPassword/SetupPasswordCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Auth/SetupPassword/SetupPasswordCommandValidatorTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/Commands/InviteUser/InviteUserCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/Commands/InviteUser/InviteUserCommandValidatorTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/Commands/ChangeUserStatus/ChangeUserStatusCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Users/Commands/ForceUserPasswordReset/ForceUserPasswordResetCommandHandlerTests.cs`
