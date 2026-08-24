# SPEC 26 — `account/*` self-management (sessions, MFA, email change, phone verify, activity log)

> **Status:** Implementado (expanded)
> **Depends on:** SPEC 18 (Roles), SPEC 22 (RoleSystemOption 7 flags), SPEC 23 (tenant from token), SPEC 25 (matrix shape), `Otp.NET` NuGet.
> **Date:** 2026-08-15
> **Objective:** Cerrar el set completo de self-management del usuario autenticado en `AccountController`. Originalmente SPEC 26 cubría solo items 10 (sessions revoke) + 14 (my-permissions); items 9 (MFA), 11 (confirm email change), 12 (verify phone), 13 (security activity log) se difirieron a specs 27/28/29. Decisión: reabsorber los 4 items en esta spec para entregar el surface completo de `AccountController` en una sola entrega. Cambios viven en `AccountController`, 8 handlers nuevos, 3 tablas nuevas (`Security.SecurityEventLog`, `Security.UserMfaRecoveryCode`, `Security.PhoneVerificationCode`), 1 columna nueva en `Security.Users` (`MfaEnabledAtUtc`), 4 repositorios Dapper nuevos, 2 servicios de dominio nuevos (`ISecurityEventLogger`, `ISmsService`), refactor de `JwtTokenGenerator.GenerateToken` para incluir claim `refresh_token_id`, expansión de `ICurrentUserService` con `RefreshTokenId`/`IpAddress`/`UserAgent`, refactor de `LoginCommandHandler` + `RefreshTokenCommandHandler` para persistir `UserRefreshToken` antes de emitir access token. No nuevos controllers, no cambios en los 6 endpoints existentes.

---

## Scope

**In (10 nuevos endpoints en `AccountController`):**

- `DELETE /api/v1/account/sessions/{sessionId}` (item 10) — revoca una sesión concreta (UserConnectionLog o UserRefreshToken).
- `POST /api/v1/account/sessions/revoke-others` (item 10) — revoca todas las sesiones excepto la actual.
- `GET /api/v1/account/my-permissions` (item 14) — matrix CRUD efectiva del usuario logueado.
- `POST /api/v1/account/mfa/setup` (item 9) — genera Base32 secret + QR provisioning URI + 10 recovery codes. NO habilita MFA.
- `POST /api/v1/account/mfa/enable { code }` (item 9) — valida TOTP, persiste `IsMfaEnabled=true`, `MfaEnabledAtUtc`.
- `POST /api/v1/account/mfa/disable { code }` (item 9) — valida TOTP o recovery code, persiste `IsMfaEnabled=false`, limpia `MfaSecretKey`, borra recovery codes.
- `POST /api/v1/account/confirm-email-change { newEmail, token }` (item 11) — llama `userManager.ChangeEmailAsync` con el token enviado por `request-email-change`.
- `POST /api/v1/account/verify-phone/request { phoneNumber }` (item 12) — genera 6-digit code, hashea + persiste, envía vía `ISmsService` (NoOp log).
- `POST /api/v1/account/verify-phone/confirm { code }` (item 12) — valida code, set `PhoneNumberConfirmed=true`.
- `GET /api/v1/account/security-activity?pageNumber&pageSize` (item 13) — feed paginado de eventos de seguridad del usuario.

**Out of scope (explícito):**

- Items ya diferidos a spec 30: SMS provider real (Twilio), MFA login gate (challenge TOTP post-password), rotación masiva de tokens, recovery code regeneration, audit log retention background job, admin read endpoint del audit log.
- Hard delete de sesiones (soft-only via `IsRevoked` / `IsActiveSession=0`).
- Cross-tenant `my-permissions` (superadmin).
- Cambios sobre los 6 endpoints existentes (`GET/PUT profile`, `POST change-password`, `POST request-email-change`, `GET sessions`).
- Tabla propia para tokens de cambio de email — usa `AspNetUserTokens` built-in de Identity.
- Paginación cursor-based del activity log — solo offset/page.

---

## Data model

### `SecurityEventLog` (nuevo, schema `Security`)

```csharp
public class SecurityEventLog : BaseEntity
{
    public Guid? UserId { get; set; }                 // nullable — sobrevive user delete
    public int EventType { get; set; }                // SecurityEventType (int)
    public DateTime OccurredAtUtc { get; set; }
    public string? IpAddress { get; set; }            // nvarchar(45)
    public string? UserAgent { get; set; }            // nvarchar(500)
    public int Result { get; set; }                   // SecurityEventResult (int)
    public string? MetadataJson { get; set; }         // nvarchar(max), contexto opcional
    public virtual ApplicationUser? User { get; set; }
}
```

No soft-delete (`GcRecord`). Audit trail append-only. Index `(UserId, OccurredAtUtc DESC)` para activity feed. Sin query filter global.

`SecurityEventType` enum (`src/1.Domain/Security/SecurityEventType.cs`):
`LoginSucceeded=1, LoginFailed=2, LoginMfaRequired=3, PasswordChanged=4, EmailChangeRequested=5, EmailChangeConfirmed=6, EmailChangeFailed=7, MfaSetupInitiated=8, MfaEnabled=9, MfaDisabled=10, MfaRecoveryCodeUsed=11, PhoneVerificationRequested=12, PhoneVerificationConfirmed=13, PhoneVerificationFailed=14, PhoneVerificationLocked=15, SessionRevoked=16, SessionsRevokedOthers=17, RoleChanged=18`.

`SecurityEventResult` enum (`src/1.Domain/Security/SecurityEventResult.cs`):
`Success=1, Failure=2, Denied=3`.

### `UserMfaRecoveryCode` (nuevo, schema `Security`)

```csharp
public class UserMfaRecoveryCode : BaseEntity
{
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;       // PBKDF2-SHA256
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public virtual ApplicationUser? User { get; set; }
}
```

Index `(UserId, UsedAtUtc)`. Sin soft-delete — used rows quedan para audit.

### `PhoneVerificationCode` (nuevo, schema `Security`)

```csharp
public class PhoneVerificationCode : BaseEntity
{
    public Guid UserId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;   // E.164
    public string CodeHash { get; set; } = string.Empty;       // PBKDF2-SHA256
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public virtual ApplicationUser? User { get; set; }
}
```

Index `(UserId, ExpiresAtUtc DESC)`.

### `ApplicationUser` (modify)

Agregar columna `MfaEnabledAtUtc` (DateTime?, nullable). Se agrega en la misma migration.

### Sin cambios a tablas existentes

- `UserConnectionLog` mantiene `IsActiveSession` + `DisconnectionDate` para session revoke.
- `UserRefreshToken` mantiene `IsRevoked` para revoke.
- `AspNetUsers` recibe cambio de email vía `userManager.ChangeEmailAsync` (Identity built-in usa `AspNetUserTokens` internamente).

---

## JWT + ICurrentUserService changes

### `ICurrentUserService` (modify, `src/2.Application/Interface/ICurrencyUserService.cs`)

```csharp
public interface ICurrentUserService
{
    string? UserId { get; }
    Guid CompanyId { get; }
    bool IsAuthenticated { get; }
    Guid? RefreshTokenId { get; }    // NUEVO — lee claim "refresh_token_id"
    string? IpAddress { get; }       // NUEVO — X-Forwarded-For, sino Connection.RemoteIpAddress
    string? UserAgent { get; }       // NUEVO — User-Agent header
}
```

### `CurrentUserService` impl (modify, `src/4.Services.WebApi/Services/CurrentUserService.cs`)

- `RefreshTokenId`: `User.FindFirstValue("refresh_token_id")` → `Guid?`. Null si falta/no parsea (tokens emitidos antes de esta spec).
- `IpAddress`: primero `Request.Headers["X-Forwarded-For"]` (split comma, primero first), sino `Connection.RemoteIpAddress?.ToString()`.
- `UserAgent`: `Request.Headers.UserAgent.FirstOrDefault()`.

### `IJwtTokenGenerator` + `JwtTokenGenerator` (modify)

Refactor de signature:

```csharp
// Antes
(string Token, string RefreshToken, DateTime Expiration, DateTime RefreshTokenExpiration)
    GenerateToken(ApplicationUser user, Guid? companyId, IEnumerable<string> roles);

// Después
(string Token, string RefreshToken, DateTime Expiration, DateTime RefreshTokenExpiration)
    GenerateToken(ApplicationUser user, Guid? companyId, IEnumerable<string> roles, Guid refreshTokenId);

// + exponer helper externo
string GenerateRefreshTokenString();
```

Claim nuevo en el payload:

```csharp
new Claim("refresh_token_id", refreshTokenId.ToString())
```

### Refactor de callers (Login + Refresh)

```csharp
// 1. Generar par refresh-token ANTES de tocar JWT
var refreshTokenId = Guid.NewGuid();
var refreshTokenString = _jwtTokenGenerator.GenerateRefreshTokenString();
var refreshTokenExpiration = DateTime.UtcNow.AddDays(refreshTokenExpirationDays);

// 2. Persistir fila UserRefreshToken PRIMERO
await _unitOfWork.GetRepository<UserRefreshToken>().InsertAsync(new UserRefreshToken
{
    Id = refreshTokenId,
    UserId = user.Id,
    Token = refreshTokenString,
    ExpiryDate = refreshTokenExpiration,
    IsRevoked = false
});
await _unitOfWork.SaveChangesAsync(cancellationToken);

// 3. ENTONCES generar access token con el Id de la fila persistida
var (accessToken, refreshToken, expiration, _) =
    _jwtTokenGenerator.GenerateToken(user, effectiveCompanyId, roleNames, refreshTokenId);
```

Si `_unitOfWork.SaveChangesAsync` falla → caller retorna `UnauthorizedAccessException` (login) / `UnauthorizedAccessException` (refresh) sin exponer access token.

---

## Repositories (Dapper)

Patrón establecido: interface en `src/2.Application/Interface/Persistence/Security/`, impl en `src/3.Persistence/Repositories/Security/`, usa `ISqlConnectionFactory` + Dapper.

### `IRoleUserSessionRepository` (sessions + my-permissions)

```csharp
Task<SessionLookupResult?> FindActiveByIdAsync(Guid sessionId, CancellationToken ct);
Task<int> SoftRevokeRefreshTokensExceptAsync(Guid userId, Guid? currentRefreshTokenId, DateTime utcNow, CancellationToken ct);
Task<int> SoftRevokeActiveConnectionsAsync(Guid userId, DateTime utcNow, CancellationToken ct);
Task<Guid?> GetUserIdBySessionIdAsync(Guid sessionId, CancellationToken ct);    // cross-user leak prevention
Task<MyPermissionsDto?> GetUserPermissionsMatrixAsync(Guid userId, Guid companyId, CancellationToken ct);
```

`SessionLookupResult = record(Guid Id, SessionType Type)` con `SessionType { UserRefreshToken, UserConnectionLog }`.

### `ISecurityEventRepository`

```csharp
Task<int> InsertAsync(SecurityEventLog entry, CancellationToken ct);
Task<IReadOnlyList<SecurityEventLog>> ListByUserPagedAsync(Guid userId, int pageNumber, int pageSize, CancellationToken ct);
```

### `IUserMfaRecoveryCodeRepository`

```csharp
Task<int> InsertManyAsync(IEnumerable<UserMfaRecoveryCode> codes, CancellationToken ct);
Task<IReadOnlyList<UserMfaRecoveryCode>> ListUnusedByUserAsync(Guid userId, CancellationToken ct);
Task<bool> MarkUsedAsync(Guid codeId, DateTime utcNow, CancellationToken ct);
Task<int> DeleteAllByUserAsync(Guid userId, CancellationToken ct);
```

### `IPhoneVerificationCodeRepository`

```csharp
Task<int> InvalidateActiveByUserAsync(Guid userId, CancellationToken ct);
Task InsertAsync(PhoneVerificationCode code, CancellationToken ct);
Task<PhoneVerificationCode?> GetLatestActiveByUserAsync(Guid userId, CancellationToken ct);
Task<bool> MarkUsedAsync(Guid codeId, DateTime utcNow, CancellationToken ct);
Task<bool> IncrementAttemptAsync(Guid codeId, CancellationToken ct);
```

---

## Servicios de dominio

### `ISecurityEventLogger` (nuevo)

```csharp
public interface ISecurityEventLogger
{
    Task LogAsync(
        SecurityEventType eventType,
        SecurityEventResult result,
        Guid? userId,
        string? ipAddress,
        string? userAgent,
        string? metadataJson = null,
        CancellationToken ct = default);
}
```

Impl `SecurityEventLogger` en `src/3.Infrastructure/Security/SecurityEventLogger.cs`. Una línea: build entity + `ISecurityEventRepository.InsertAsync`. Scoped.

### `ISmsService` (nuevo, stub)

```csharp
public interface ISmsService
{
    Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken ct = default);
}
```

Impl `NoOpSmsService` en `src/3.Infrastructure/Messaging/Sms/NoOpSmsService.cs`. Loguea con `ILogger<NoOpSmsService>` `LogInformation`. Retorna `true`. Spec 30 reemplaza con Twilio.

### `IMfaTotpValidator` (nuevo)

```csharp
public interface IMfaTotpValidator
{
    string GenerateSecret();                                  // Base32, 32 chars
    string BuildQrCodeUri(string secret, string accountName, string issuer);
    bool ValidateCode(string secret, string code, int windowSteps = 1);
}
```

Impl `MfaTotpValidator` en `src/3.Infrastructure/Security/Mfa/MfaTotpValidator.cs`. Wrapper sobre `Otp.NET`.

### `RecoveryCodeHasher` (static, PBKDF2 helper)

```csharp
public static class RecoveryCodeHasher
{
    public static string GenerateCode();      // 10-char alphanumeric (Crockford base32)
    public static string Hash(string code);   // PBKDF2-SHA256, 100k iter, 16-byte salt, base64 salt:hash
    public static bool Verify(string code, string storedHash);
}
```

Usado para recovery codes MFA + phone verification codes.

---

## Plan de implementación

### F1 — JWT `refresh_token_id` claim + `ICurrentUserService` props

1. Agregar `Guid? RefreshTokenId`, `string? IpAddress`, `string? UserAgent` a `ICurrentUserService`.
2. Implementar en `CurrentUserService`.
3. Agregar `string GenerateRefreshTokenString()` a `IJwtTokenGenerator` + impl (extraer de `JwtTokenGenerator.GenerateSecureRefreshToken` private).
4. Refactor signature `IJwtTokenGenerator.GenerateToken` para aceptar `Guid refreshTokenId`; agregar claim.
5. Refactor `LoginCommandHandler` + `RefreshTokenCommandHandler` según patrón de la sección anterior.
6. Build → 0 errors. Smoke: login → JWT en jwt.io → claim `refresh_token_id` presente, matchea `UserRefreshTokens.Id`.

### F2 — `RevokeMySession` (single) + F3 `RevokeOtherMySessions` (bulk)

Handlers en `src/2.Application/UseCases/Security/Account/Commands/RevokeMySession/` + `Commands/RevokeOtherMySessions/`. Dapper queries en `RoleUserSessionRepository`:
- `FindActiveByIdAsync`: `UNION ALL` sobre `UserConnectionLogs` + `UserRefreshTokens`, devuelve `(Id, Type)`.
- `SoftRevokeRefreshTokensExceptAsync`: `UPDATE Security.UserRefreshTokens SET IsRevoked = 1, LastModified = @UtcNow WHERE UserId = @UserId AND IsRevoked = 0 AND ExpiryDate > @UtcNow AND GcRecord = 0 AND Id <> ISNULL(@CurrentRefreshTokenId, '00000000-0000-0000-0000-000000000000')`.
- `SoftRevokeActiveConnectionsAsync`: `UPDATE Security.UserConnectionLogs SET IsActiveSession = 0, DisconnectionDate = @UtcNow, LastModified = @UtcNow WHERE UserId = @UserId AND IsActiveSession = 1 AND GcRecord = 0`.

Hook `ISecurityEventLogger.LogAsync(SessionRevoked | SessionsRevokedOthers, Success, ...)` en éxito.

### F4 — `GetMyPermissions`

Query fresh con CTE + GROUP BY en `RoleUserSessionRepository.GetUserPermissionsMatrixAsync`. Single SELECT que une `UserRoleCompanies` + `RoleSystemOptions` + `SystemOptions` + `SystemModules` para `(userId, companyId)` del JWT. Shape: `RoleSystemOptionMatrixDto` de SPEC 25, con `RoleName = "<RoleName1> + <N-1> más"` cuando hay múltiples roles. Sin security event logged.

### F5 — `SecurityEventLog` infrastructure + `ISecurityEventLogger`

1. EF migration `AddSecurityEventLogAndMfaAndPhoneVerify` (incluye las 3 tablas + `MfaEnabledAtUtc`).
2. `SecurityEventLog` entity + `SecurityEventLogConfiguration` (sin query filter, sin soft-delete).
3. `SecurityEventType` + `SecurityEventResult` enums.
4. `ISecurityEventRepository` + `SecurityEventRepository` (Dapper).
5. `ISecurityEventLogger` + `SecurityEventLogger` impl.
6. DI registration en `src/3.Infrastructure/DependencyInjection.cs`.
7. Hook en handlers existentes (sin cambio de comportamiento, solo log):
   - `LoginCommandHandler` → log `LoginSucceeded` (Success) o `LoginFailed` (Failure).
   - `RefreshTokenCommandHandler` → log `LoginSucceeded`.
   - `ChangeMyPasswordCommandHandler` → log `PasswordChanged`.
   - `RequestEmailChangeCommandHandler` → log `EmailChangeRequested`.

### F6 — MFA setup + enable + disable

1. `UserMfaRecoveryCode` entity + config + `DbSet`.
2. `IUserMfaRecoveryCodeRepository` + Dapper impl.
3. `IMfaTotpValidator` + `MfaTotpValidator` impl (`Otp.NET`). DI registration.
4. `RecoveryCodeHasher` static class.
5. **SetupMfaCommand** (`ITransactionalCommand<Response<SetupMfaResponseDto>>`):
   - Generate Base32 secret.
   - Generate 10 recovery codes, hash + persist.
   - Persist `user.MfaSecretKey` (NO `IsMfaEnabled`).
   - Build QR: `otpauth://totp/JOIN:{user.Email}?secret={secret}&issuer=JOIN`.
   - Return `{ Secret, QrCodeUri, RecoveryCodes[] }`.
   - Log `MfaSetupInitiated`.
6. **EnableMfaCommand** (`ITransactionalCommand<Response<bool>>`):
   - Si `user.MfaSecretKey` null → `MFA_NOT_CONFIGURED`.
   - `IMfaTotpValidator.ValidateCode(secret, code)` → fail → `MFA_INVALID_CODE`.
   - Set `user.IsMfaEnabled = true`, `MfaEnabledAtUtc = UtcNow`. `UserManager.UpdateAsync`.
   - Log `MfaEnabled`.
7. **DisableMfaCommand** (`ITransactionalCommand<Response<bool>>`):
   - Si `user.IsMfaEnabled` false → `MFA_NOT_CONFIGURED`.
   - TOTP path: `IMfaTotpValidator.ValidateCode`. Recovery path: iterar `ListUnusedByUserAsync`, `RecoveryCodeHasher.Verify`, marcar used.
   - Set `user.IsMfaEnabled = false`, `MfaSecretKey = null`, `MfaEnabledAtUtc = null`.
   - `IUserMfaRecoveryCodeRepository.DeleteAllByUserAsync`.
   - Log `MfaDisabled` (o `MfaRecoveryCodeUsed` si fue recovery).
8. Controller endpoints:
   - `POST /mfa/setup` → SetupMfaCommand.
   - `POST /mfa/enable` → EnableMfaCommand.
   - `POST /mfa/disable` → DisableMfaCommand.
   - Heredan `[PermissionResource("Users")]` → POST default `CanCreate`.

### F7 — Confirm email change

1. **ConfirmEmailChangeCommand** (`IRequest<Response<bool>>`, no transaccional — single Identity call):
   - `userManager.FindByIdAsync(currentUser.UserId)`.
   - `userManager.ChangeEmailAsync(user, newEmail, token)`.
   - Si `!result.Succeeded` → `EMAIL_CHANGE_FAILED`. Log `EmailChangeFailed`.
   - Success → log `EmailChangeConfirmed`.
2. `ConfirmEmailChangeRequestDto { NewEmail, Token }`.
3. Controller: `POST /confirm-email-change`. POST default → `CanCreate`. Validator: `[FromBody]`, `NewEmail` valid email, `Token` not empty.

### F8 — Verify phone

1. `PhoneVerificationCode` entity + config + `DbSet`.
2. `IPhoneVerificationCodeRepository` + Dapper impl.
3. `ISmsService` + `NoOpSmsService` impl. DI registration.
4. **RequestPhoneVerificationCommand** (`IRequest<Response<bool>>`, sends SMS):
   - Validate phone format (E.164 regex `^\+[1-9]\d{1,14}$`).
   - `IPhoneVerificationCodeRepository.InvalidateActiveByUserAsync`.
   - Generate 6-digit numeric code.
   - `RecoveryCodeHasher.Hash`. Persist con `ExpiresAtUtc = UtcNow + 10min`.
   - `ISmsService.SendSmsAsync(phoneNumber, "Your JOIN verification code: {code}. Expires in 10 minutes.")`.
   - Log `PhoneVerificationRequested`.
5. **ConfirmPhoneVerificationCommand** (`ITransactionalCommand<Response<bool>>`):
   - `GetLatestActiveByUserAsync` → null → `PHONE_CODE_EXPIRED`.
   - `IncrementAttemptAsync`. Si `> 5` → invalidate + log `PhoneVerificationLocked` + `PHONE_CODE_LOCKED`.
   - `RecoveryCodeHasher.Verify`. Fail → log `PhoneVerificationFailed` + `PHONE_CODE_INVALID`.
   - `MarkUsedAsync`.
   - Set `user.PhoneNumber = code.PhoneNumber`, `user.PhoneNumberConfirmed = true`. `UserManager.UpdateAsync`.
   - Log `PhoneVerificationConfirmed`.
6. DTOs: `RequestPhoneVerificationRequestDto { PhoneNumber }`, `ConfirmPhoneVerificationRequestDto { Code }`.
7. Controller: `POST /verify-phone/request`, `POST /verify-phone/confirm`.

### F9 — Security activity log query

1. **GetSecurityActivityQuery** (`IRequest<Response<SecurityActivityPageDto>>`):
   - Clamp `pageNumber >= 1`, `pageSize 1..100`.
   - `ISecurityEventRepository.ListByUserPagedAsync`.
   - Map a `SecurityActivityEventDto { OccurredAtUtc, Event (string enum), IpAddress, Device (UserAgent), Result (string enum) }`.
   - Wrap en `SecurityActivityPageDto { Items, PageNumber, PageSize, TotalCount }`. Evaluar reuse de `PagedResult<T>` si shape matchea.
2. Controller: `GET /security-activity?pageNumber&pageSize`. GET default → `CanRead`.

### F10 — EF migration

Una sola migration `AddSecurityEventLogAndMfaAndPhoneVerify`:
- Add column `Security.MfaEnabledAtUtc` (DateTime?, nullable) on `Security.Users`.
- Create `Security.SecurityEventLogs` (Id, UserId, EventType, OccurredAtUtc, IpAddress, UserAgent, Result, MetadataJson).
- Create `Security.UserMfaRecoveryCodes` (Id, UserId, CodeHash, CreatedAtUtc, UsedAtUtc).
- Create `Security.PhoneVerificationCodes` (Id, UserId, PhoneNumber, CodeHash, ExpiresAtUtc, UsedAtUtc, AttemptCount).
- Indexes: `IX_SecurityEventLogs_UserId_OccurredAtUtc` DESC, `IX_UserMfaRecoveryCodes_UserId_UsedAtUtc`, `IX_PhoneVerificationCodes_UserId_ExpiresAtUtc` DESC.

### F11 — Controller wiring

10 endpoints nuevos en `AccountController`, todos bajo `[PermissionResource("Users")]` heredado. Sin `[RequirePermission]` overrides. Error mapping:

| Error code | HTTP |
|---|---|
| `SESSION_NOT_FOUND` | 404 |
| `CANNOT_REVOKE_CURRENT` | 400 |
| `TENANT_REQUIRED` | 400 |
| `USER_NOT_FOUND` | 404 |
| `MFA_NOT_CONFIGURED` | 400 |
| `MFA_INVALID_CODE` | 400 |
| `EMAIL_CHANGE_FAILED` | 400 |
| `PHONE_INVALID_FORMAT` | 400 |
| `PHONE_CODE_EXPIRED` | 400 |
| `PHONE_CODE_INVALID` | 400 |
| `PHONE_CODE_LOCKED` | 429 |

### F12 — Tests (~30 tests nuevos)

Mirror source path: `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Account/...`. Pattern: AutoFixture, Moq, FluentAssertions, FakeConnection/FakeResultSet test doubles.

Casos por handler:

- `RevokeMySessionCommandHandlerTests` — 5 casos (UserConnectionLog revoke, UserRefreshToken revoke, cross-user 404, current 400, not found 404).
- `RevokeOtherMySessionsCommandHandlerTests` — 4 casos (no sessions, only current, mixed, currentRefreshTokenId null).
- `GetMyPermissionsQueryHandlerTests` — 3 casos (2 roles 2 módulos, no roles matrix vacía, tenant empty 400).
- `SetupMfaCommandHandlerTests` — 3 casos (happy path, ya tiene MFA → error, recovery codes persisted hashed).
- `EnableMfaCommandHandlerTests` — 4 casos (valid TOTP, invalid TOTP, no secret, recovery codes intactos).
- `DisableMfaCommandHandlerTests` — 5 casos (valid TOTP, valid recovery code, invalid code, no MFA, recovery codes deleted).
- `ConfirmEmailChangeCommandHandlerTests` — 3 casos (valid token, invalid token, mismatched newEmail).
- `RequestPhoneVerificationCommandHandlerTests` — 3 casos (valid phone, invalid format, SMS fail logs event).
- `ConfirmPhoneVerificationCommandHandlerTests` — 5 casos (valid, invalid, expired, locked, used).
- `GetSecurityActivityQueryHandlerTests` — 3 casos (paged results, pageNumber clamp, pageSize clamp).
- `MfaTotpValidatorTests` — 3 casos (RFC 6238 vector, secret length, QR URI format).
- `RecoveryCodeHasherTests` — 3 casos (Generate charset, Hash verify, Verify wrong).

`dotnet test --filter "FullyQualifiedName~Account"` → 0 failed. Coverage ≥ 90% JOIN.Application.

### F13 — Verificación

1. `dotnet build -c Release` → 0 errors, 0 new warnings.
2. `dotnet ef database update --project src/3.Persistence --startup-project src/4.Services.WebApi` → migration applies cleanly.
3. `dotnet test --collect:"XPlat Code Coverage"` → gate 90% pasa.
4. Smoke manual (curl) — ejercitar los 10 endpoints contra WebApi corriendo.
5. JWT decode (jwt.io) post-login → claim `refresh_token_id` presente, matchea `UserRefreshTokens.Id`.
6. `CURL_REQUESTS.md` actualizado con los 10 endpoints + `X-Company-Id` header.

---

## Acceptance criteria

### F1 — JWT
- [ ] `JwtTokenGenerator.GenerateToken` agrega claim `refresh_token_id` con el Guid del row persistido.
- [ ] `ICurrentUserService.RefreshTokenId` devuelve Guid parseado del claim, null si falta.
- [ ] `ICurrentUserService.IpAddress` resuelve `X-Forwarded-For` primero, sino `RemoteIpAddress`.
- [ ] `ICurrentUserService.UserAgent` resuelve header `User-Agent`.
- [ ] `IJwtTokenGenerator.GenerateRefreshTokenString()` expuesto y usado por Login + Refresh.
- [ ] `LoginCommandHandler` y `RefreshTokenCommandHandler` persisten `UserRefreshToken` ANTES de llamar `GenerateToken`. Si persist falla, no se emite access token.
- [ ] Tokens emitidos antes de esta spec siguen funcionando (claim ausente → null).

### F2 — RevokeMySession
- [ ] `DELETE /api/v1/account/sessions/{id}` con id UserConnectionLog → 200 `{revokedConnections:1, revokedTokens:0}`. Row: `IsActiveSession=0`, `DisconnectionDate=now`.
- [ ] Mismo con id UserRefreshToken → 200 `{revokedConnections:0, revokedTokens:1}`. Row: `IsRevoked=1`.
- [ ] Cross-user id → 404 `SESSION_NOT_FOUND` (no leak).
- [ ] Current refresh token (claim JWT) → 400 `CANNOT_REVOKE_CURRENT`.
- [ ] Inexistente → 404 `SESSION_NOT_FOUND`.
- [ ] Validator rechaza `sessionId` empty con 400.
- [ ] Security event `SessionRevoked` logged on success.

### F3 — RevokeOtherMySessions
- [ ] `POST /api/v1/account/sessions/revoke-others` → 200 con counters que reflejan rows revoked. Solo el current refresh token sobrevive.
- [ ] Si `RefreshTokenId` null (token viejo) → revoke TODOS los UserRefreshToken activos.
- [ ] UserConnectionLog activas se cierran todas (sin excluir current).
- [ ] Si no hay otras sesiones → 200 `{0, 0}`.
- [ ] Security event `SessionsRevokedOthers` logged.

### F4 — GetMyPermissions
- [ ] `GET /api/v1/account/my-permissions` tenant válido → 200 `{userId, companyId, roleIds[], matrix: {roleId, roleName, modules[]}}`.
- [ ] 2 roles 2 módulos → 2 módulos, N options por módulo, `Granted` = OR de los flags, `roleIds` con 2 entries, `roleName` formato `"<Rol1> + 1 más"`.
- [ ] 1 solo rol → `roleName` = nombre real.
- [ ] Sin roles → 200 con `roleIds = []`, módulos poblados con `Granted` all false.
- [ ] `companyId == Guid.Empty` → 400 `TENANT_REQUIRED`. Ignora query param `?companyId=`.

### F6 — MFA
- [ ] `POST /mfa/setup` → 200 `{secret, qrCodeUri, recoveryCodes[10]}`. Recovery codes persisted en DB con hash (no plaintext).
- [ ] `POST /mfa/enable { code }` con TOTP válido → 200. `user.IsMfaEnabled=true`, `MfaEnabledAtUtc=now`.
- [ ] `POST /mfa/enable` con TOTP inválido → 400 `MFA_INVALID_CODE`. `IsMfaEnabled` unchanged.
- [ ] `POST /mfa/enable` sin secret previo → 400 `MFA_NOT_CONFIGURED`.
- [ ] `POST /mfa/disable { code }` con TOTP válido → 200. `user.IsMfaEnabled=false`, `MfaSecretKey=null`, recovery codes borrados.
- [ ] `POST /mfa/disable` con recovery code válido → 200 + row marcado used + log `MfaRecoveryCodeUsed`.
- [ ] `POST /mfa/disable` con code inválido → 400 `MFA_INVALID_CODE`.

### F7 — ConfirmEmailChange
- [ ] `POST /confirm-email-change { newEmail, token }` con token válido de `request-email-change` → 200. Email cambia, `EmailConfirmed=true`.
- [ ] Token inválido → 400 `EMAIL_CHANGE_FAILED`. Log `EmailChangeFailed`.

### F8 — VerifyPhone
- [ ] `POST /verify-phone/request { phoneNumber }` E.164 → 200. Code 6-digit generated + persisted + SMS logged (NoOp).
- [ ] PhoneNumber con formato inválido → 400 `PHONE_INVALID_FORMAT`.
- [ ] `POST /verify-phone/confirm { code }` válido → 200. `user.PhoneNumberConfirmed=true`.
- [ ] Code inválido → 400 `PHONE_CODE_INVALID`. Log `PhoneVerificationFailed`.
- [ ] Code expirado → 400 `PHONE_CODE_EXPIRED`.
- [ ] 5+ intentos fallidos → 429 `PHONE_CODE_LOCKED`. Code invalidado. Log `PhoneVerificationLocked`.

### F9 — SecurityActivity
- [ ] `GET /security-activity?pageNumber=1&pageSize=20` → 200 con items DESC por `OccurredAtUtc`.
- [ ] `pageNumber=0` clamps a 1.
- [ ] `pageSize=200` clamps a 100.
- [ ] Cada item: `{occurredAtUtc, event (string), ipAddress, device, result (string)}`.

### F11 — Controller
- [ ] Los 6 endpoints existentes sin cambio de ruta/firma/comportamiento.
- [ ] 10 endpoints nuevos pasan por `[PermissionResource("Users")]` heredado. Sin `[RequirePermission]` overrides.
- [ ] HTTP status mapping documentado.

### General
- [ ] `dotnet test --filter "FullyQualifiedName~Account|FullyQualifiedName~Mfa"` → 0 fallidos, ≥ 30 tests nuevos.
- [ ] `JOIN.Application` ≥ 90% line coverage (CI gate).
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` actualizado con los 10 endpoints.
- [ ] 1 sola EF migration: `AddSecurityEventLogAndMfaAndPhoneVerify`.
- [ ] Repositories usan `ISqlConnectionFactory` + Dapper, no EF change tracker.

---

## Decisiones tomadas

- **Spec 26 absorbe items 9/11/12/13** (elegido) vs. mantenerlos en specs 27/28/29 separadas. User pidió replan. Justificación: `AccountController` self-management surface es cohesivo; dividir specs incrementaría overhead sin ganancia arquitectural. Spec 30 absorberá SMS provider, MFA login gate, retention, admin read.
- **MFA con `Otp.NET`** (elegido) vs. `GoogleAuthenticator`. Otp.NET es MIT, minimal deps, RFC 6238 compliance verificado.
- **Recovery codes hasheados PBKDF2-SHA256** (elegido) vs. plaintext + DB encryption. PBKDF2 portable cross-DB, no depende de TDE.
- **Recovery codes en tabla aparte** (elegido) vs. JSON column en `AspNetUsers`. Tabla permite índices y cleanup selectivo.
- **`ISmsService` stub con `NoOpSmsService`** (elegido) vs. Twilio ahora. SMS provider no bloquea esta entrega; spec 30 lo cambia.
- **`SecurityEventLog` tabla separada** (elegido) vs. extender `UserConnectionLog`. Eventos de seguridad son semánticamente distintos a connections; mezclar dificulta queries.
- **Audit trail append-only, sin soft-delete** (elegido) vs. `GcRecord`. Eventos no se "deshacen"; soft-delete agregaría complejidad innecesaria.
- **LoginCommandHandler refactor: persist row ANTES de GenerateToken** (elegido) vs. emitir token primero y rollback. La fila DEBE existir antes que el JWT que la referencia, si no el claim `refresh_token_id` puede apuntar a row inexistente. Si la persistencia falla, no se emite access token.
- **`refresh_token_id` claim vs. header `X-Refresh-Token-Id`** (elegido). Claim vive en token firmado, no manipulable por cliente. Tradeoff: token viejo sin claim → revoke-others cierra el current. Aceptable durante ventana de deploy.
- **`RecoveryCodeHasher` static class** (elegido) vs. interfaz `IRecoveryCodeHasher`. Solo lógica stateless, no necesita DI/mocking. Tests directos.
- **`MyPermissionsDto` con `RoleName = "<Rol1> + N más"`** (elegido) vs. array de roles. Mantiene shape de `RoleSystemOptionMatrixDto` de SPEC 25; un solo renderer en front.
- **`ITransactionalCommand` para MFA enable/disable + phone confirm** (elegido) vs. `IRequest<Response<T>>`. Las operaciones mutan múltiples tablas (User + Recovery codes + Phone code + Security event) — EF transaction garantiza atomicidad.
- **`IRequest<Response<T>>` para setup MFA, request phone, confirm email change** (elegido). Setup solo persiste User + Recovery codes; el QR URI es derivado del secret. Request phone solo escribe 1 row + SMS externo. Confirm email change es una sola llamada a Identity.
- **Auth handlers tiran `UnauthorizedAccessException`** (elegido, comportamiento existente). Account handlers retornan `Response<T>.Error`. Cada capa mantiene su convención.
- **`PagedResult<T>` reusable para `SecurityActivityPageDto`** (elegido condicional). Si shape matchea, reuse; sino crear nuevo. Decidir durante F9.
- **Error mapping `PHONE_CODE_LOCKED → 429`** (elegido) vs. 400. 429 = "Too Many Requests" semánticamente correcto para lock por intentos.
- **Endpoint count = 10** (verificado): 3 MFA + 1 confirm-email + 2 phone + 1 activity + 1 my-permissions + 2 sessions = 10. (Original spec draft decía "8"; revisado a 10.)

---

## Riesgos identificados

| Riesgo | Mitigación |
|---|---|
| Refactor `JwtTokenGenerator.GenerateToken` signature rompe Login + Refresh callers | Ambos callers refactoreados juntos. Save row first → luego GenerateToken. Test verifica el orden en ambos handlers. |
| `refresh_token_id` claim ausente en tokens pre-deploy → revoke-others cierra current token | Documentado. Ventana = tiempo de deploy. Runbook: rotar todos los refresh tokens activos vía SQL UPDATE en deploy. |
| `Security.SecurityEventLog` crece sin límite | Out of scope. Spec 30 agrega background job de retención (delete events > 90 días). |
| `NoOpSmsService` traga SMS — producción no recibe código real | Documentado en CURL_REQUESTS.md. Spec 30 swap a Twilio. README nota que producción DEBE configurar Twilio. |
| `Otp.NET` clock drift en server | Default window = 1 step (±30s). Aceptable. Si drift se reporta, widen a 2 steps. |
| PBKDF2 100k iter demasiado lento | Cold path (setup/disable). No login hot path. Aceptable. |
| `ConfirmEmailChange` token leak en logs | Force token via body only. Validator enforces `[FromBody]` + Token not empty. Document. |
| Cross-user MFA attack: user A intenta `confirm-phone` con code enviado a A | Code lookup usa `(UserId, UsedAtUtc IS NULL)` → no reusable cross-user. |
| Activity log expone PII (IP, user-agent) | Self-only en spec 26. Admin read endpoint en spec 30. Production debe proteger log PII. |
| Migración grande (3 tablas + 1 columna) puede tardar en aplicar con `Database.MigrateAsync` al startup | Out of scope. Si DB grande, hacer en ventana de mantenimiento. |
| `RecoveryCodeHasher` PBKDF2 iterations constante hardcodeada (100k) | Aceptable. Si se necesita bump, cambiar constante + invalidar todos los recovery codes existentes (forzar re-setup). |

---

## What is NOT in this spec

- **SMS provider real (Twilio)** — `NoOpSmsService` placeholder. Spec 30.
- **MFA login gate** — TOTP challenge post-password en login. Spec 30.
- **Rotación masiva de refresh tokens en deploy** — operacional, runbook, no código.
- **Recovery code regeneration endpoint** — `POST /account/mfa/recovery-codes/regenerate` con re-auth. Spec 30.
- **Audit log retention background job** — IHostedService que borra events > 90 días. Spec 30.
- **Admin read endpoint del audit log** — `GET /admin/users/{id}/security-activity` con filtros. Spec 30.
- **Audit log filtering por event type / result / date range** — solo paged por user ahora. Spec 30.
- **Cambios en `GET /account/sessions`** — el shape del DTO se mantiene idéntico.
- **Cross-tenant `my-permissions`** (superadmin) — siempre tenant del JWT. Si se necesita, spec aparte.
- **Paginación cursor-based del activity log** — solo offset/page.
- **Hard delete de sesiones** — soft-only via flags.
- **Invalidación de cache `permissions:v2:` tras revoke** — revoke de session NO cambia permisos del user. Cache sigue válido.
- **Migración de datos para tokens pre-deploy** — operacional.

---

## Archivos críticos

### Modify
- `src/2.Application/Interface/ICurrencyUserService.cs` — add 3 props.
- `src/4.Services.WebApi/Services/CurrentUserService.cs` — impl 3 props.
- `src/2.Application/Interface/IJwtTokenGenerator.cs` — new param + `GenerateRefreshTokenString()`.
- `src/3.Infrastructure/Security/Jwt/JwtTokenGenerator.cs` — impl new param, add claim, expose helper.
- `src/1.Domain/Security/applicationuser.cs` — add `MfaEnabledAtUtc`.
- `src/3.Persistence/Contexts/ApplicationDbContext.cs` — 3 new DbSets.
- `src/3.Persistence/Configuration/ConfigureServices.cs` — register 4 new repositories.
- `src/3.Infrastructure/DependencyInjection.cs` — register `ISmsService`, `ISecurityEventLogger`, `IMfaTotpValidator`.
- `src/2.Application/JOIN.Application.csproj` — add `Otp.NET` package.
- `src/4.Services.WebApi/Controllers/Security/AccountController.cs` — 10 new endpoints.
- `src/2.Application/UseCases/Security/Auth/Login/LoginCommandHandler.cs` — refactor + log event.
- `src/2.Application/UseCases/Security/Auth/Refresh/RefreshTokenCommandHandler.cs` — refactor + log event.
- `src/2.Application/UseCases/Security/Account/Commands/ChangeMyPassword/ChangeMyPasswordCommandHandler.cs` — log event.
- `src/2.Application/UseCases/Security/Account/Commands/RequestEmailChange/RequestEmailChangeCommandHandler.cs` — log event.

### Create

**Domain enums + entities:**
- `src/1.Domain/Security/SecurityEventType.cs`
- `src/1.Domain/Security/SecurityEventResult.cs`
- `src/1.Domain/Security/SecurityEventLog.cs`
- `src/1.Domain/Security/UserMfaRecoveryCode.cs`
- `src/1.Domain/Security/PhoneVerificationCode.cs`

**EF configurations:**
- `src/3.Persistence/Configuration/Security/SecurityEventLogConfiguration.cs`
- `src/3.Persistence/Configuration/Security/UserMfaRecoveryCodeConfiguration.cs`
- `src/3.Persistence/Configuration/Security/PhoneVerificationCodeConfiguration.cs`

**Application interfaces:**
- `src/2.Application/Interface/ISmsService.cs`
- `src/2.Application/Interface/ISecurityEventLogger.cs`
- `src/2.Application/Interface/IMfaTotpValidator.cs`
- `src/2.Application/Interface/Persistence/Security/IRoleUserSessionRepository.cs`
- `src/2.Application/Interface/Persistence/Security/ISecurityEventRepository.cs`
- `src/2.Application/Interface/Persistence/Security/IUserMfaRecoveryCodeRepository.cs`
- `src/2.Application/Interface/Persistence/Security/IPhoneVerificationCodeRepository.cs`

**Application DTOs** (en `src/2.Application.DTO/Security/Account/`):
- `SetupMfaResponseDto.cs`
- `EnableMfaRequestDto.cs`
- `DisableMfaRequestDto.cs`
- `ConfirmEmailChangeRequestDto.cs`
- `RequestPhoneVerificationRequestDto.cs`
- `ConfirmPhoneVerificationRequestDto.cs`
- `SecurityActivityEventDto.cs`
- `SecurityActivityPageDto.cs`

**Use case handlers** (cada folder: Command + Handler + Validator):
- `src/2.Application/UseCases/Security/Account/Commands/RevokeMySession/`
- `src/2.Application/UseCases/Security/Account/Commands/RevokeOtherMySessions/`
- `src/2.Application/UseCases/Security/Account/Commands/SetupMfa/`
- `src/2.Application/UseCases/Security/Account/Commands/EnableMfa/`
- `src/2.Application/UseCases/Security/Account/Commands/DisableMfa/`
- `src/2.Application/UseCases/Security/Account/Commands/ConfirmEmailChange/`
- `src/2.Application/UseCases/Security/Account/Commands/RequestPhoneVerification/`
- `src/2.Application/UseCases/Security/Account/Commands/ConfirmPhoneVerification/`
- `src/2.Application/UseCases/Security/Account/Queries/GetMyPermissions/`
- `src/2.Application/UseCases/Security/Account/Queries/GetSecurityActivity/`

**Infrastructure impls:**
- `src/3.Infrastructure/Repositories/Security/RoleUserSessionRepository.cs`
- `src/3.Infrastructure/Repositories/Security/SecurityEventRepository.cs`
- `src/3.Infrastructure/Repositories/Security/UserMfaRecoveryCodeRepository.cs`
- `src/3.Infrastructure/Repositories/Security/PhoneVerificationCodeRepository.cs`
- `src/3.Infrastructure/Security/SecurityEventLogger.cs`
- `src/3.Infrastructure/Security/Mfa/MfaTotpValidator.cs`
- `src/3.Infrastructure/Security/Mfa/RecoveryCodeHasher.cs`
- `src/3.Infrastructure/Messaging/Sms/NoOpSmsService.cs`

**EF migration:**
- `src/3.Persistence/Migrations/20260815xxxx_AddSecurityEventLogAndMfaAndPhoneVerify.cs` (+ Designer + ModelSnapshot updates).

**Tests** (mirror source path `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Account/...` y `tests/UnitTests/JOIN.Application.UnitTest/Security/...`):
- `Commands/RevokeMySession/RevokeMySessionCommandHandlerTests.cs`
- `Commands/RevokeOtherMySessions/RevokeOtherMySessionsCommandHandlerTests.cs`
- `Commands/SetupMfa/SetupMfaCommandHandlerTests.cs`
- `Commands/EnableMfa/EnableMfaCommandHandlerTests.cs`
- `Commands/DisableMfa/DisableMfaCommandHandlerTests.cs`
- `Commands/ConfirmEmailChange/ConfirmEmailChangeCommandHandlerTests.cs`
- `Commands/RequestPhoneVerification/RequestPhoneVerificationCommandHandlerTests.cs`
- `Commands/ConfirmPhoneVerification/ConfirmPhoneVerificationCommandHandlerTests.cs`
- `Queries/GetMyPermissions/GetMyPermissionsQueryHandlerTests.cs`
- `Queries/GetSecurityActivity/GetSecurityActivityQueryHandlerTests.cs`
- `Security/Mfa/MfaTotpValidatorTests.cs`
- `Security/Mfa/RecoveryCodeHasherTests.cs`