# SPEC 32 — Desafío de login MFA (Email OTP + TOTP) y activación de Email OTP

> **Status:** Aprobado
> **Depends on:** SPEC 03/SPEC 26 (`AccountController.mfa/setup|enable|disable`, `IMfaTotpValidator`, `SecurityEventType`), SPEC 30 (`PhoneVerificationCode` + `IPhoneVerificationCodeRepository`, patrón que este spec clona para el email), repo hermano `join_frontb/specs/12-doble-factor-autenticacion.md` (spec de frontend que origina este pedido)
> **Date:** 2026-09-20
> **Objective:** Que `POST /Users/login` deje de emitir el JWT incondicionalmente: si el usuario tiene 1+ método de 2FA activo (`IsMfaEnabled` y/o el nuevo `IsEmailOtpEnabled`), el login se detiene en un "desafío" que solo libera el JWT tras verificar un código TOTP o un código enviado por email — y agregar el ciclo completo de activar/desactivar Email OTP como segundo método, paralelo al TOTP que ya existe.

---

## Por qué existe esta spec

`join_frontb/specs/12-doble-factor-autenticacion.md` (Aprobado, 2026-09-20) diseñó el frontend de esta feature completa, pero su Data model deja escrito, explícitamente, que **el backend no tiene nada de esto construido todavía** y que no se puede avanzar con los pasos de frontend hasta que exista. Se auditó el `openapi/v1.json` real (2026-09-20) y los controladores fuente en `src/4.Services.WebApi/Controllers/Security/` antes de escribir esta spec, no solo el spec del frontend:

- **`AccountController.cs` (405 líneas)** solo tiene `profile`, `change-password`, `request-email-change`/`confirm-email-change`, `sessions`, `my-permissions`, `mfa/setup|enable|disable` y `verify-phone/request|confirm`. No hay ningún método `EmailOtp*` ni `PreferredMethod`.
- **`AuthController.cs` (117 líneas)** solo tiene `setup-password`, `forgot-password`, `reset-password`. No existe ningún endpoint `mfa/challenge/*`.
- **`UsersController.Login` (línea 69-77)** llama a `_mediator.Send(command, ...)` y envuelve el resultado en `Response<LoginResponse>` con `IsSuccess = true` sin ninguna rama condicional — el JWT sale siempre, sin excepción.
- **`SecurityEventType` (SPEC 26)** ya define `LoginMfaRequired = 3`, pero **no se usa en ningún lado del código** (`grep` no encuentra otra referencia) — alguien anticipó este trabajo al diseñar el enum pero nunca lo conectó. Esta spec lo conecta por primera vez.

Es decir: nada de lo que pide `join_frontb` SPEC 12 existe. Esta spec traduce ese pedido a la arquitectura real del backend (CQRS + MediatR + Dapper para lecturas + EF para escrituras, ver `CLAUDE.md`), reutilizando exactamente el mismo patrón que `PhoneVerificationCode`/`IPhoneVerificationCodeRepository` (SPEC 30) ya estableció para códigos de un solo uso, en vez de inventar uno nuevo.

**Corrección a `join_frontb` SPEC 12:** su "Flujo de UX propuesto" describe que `EmailOtpCard.razor` "envía un código de prueba al correo" al presionar Activar, pero su Data model **no lista ningún endpoint que envíe ese código** — solo `email-otp/enable { code }`. Esta spec agrega el endpoint que falta (`email-otp/send-code`, ver Data model) y **conviene avisar a quien mantenga el spec de frontend para que lo sume ahí también** antes de implementar el paso 7 de su plan.

---

## Scope

**In:**

1. `POST /Users/login` deja de emitir siempre `Token`/`RefreshToken`: si el usuario tiene `IsMfaEnabled` y/o `IsEmailOtpEnabled` en `true`, la respuesta trae esos campos en `null` y en cambio trae `ChallengeToken`/`AvailableMethods`/`PreferredMethod` poblados. Sin ningún método activo, el comportamiento no cambia.
2. `POST /api/v1/auth/mfa/challenge/send` — reenvía el código por email para un desafío en curso (no aplica a `totp`).
3. `POST /api/v1/auth/mfa/challenge/verify` — valida el código (TOTP en vivo o email contra el hash guardado) y, si es correcto, recién ahí resuelve compañía/roles y emite el JWT real.
4. `POST /api/v1/account/email-otp/send-code` — envía (o reenvía) el código de prueba de 6 dígitos al correo confirmado del usuario autenticado. **Nuevo respecto a lo que pedía `join_frontb` SPEC 12** (ver arriba).
5. `POST /api/v1/account/email-otp/enable` / `disable` — activan/desactivan `IsEmailOtpEnabled` validando el código emitido por `send-code`.
6. `PUT /api/v1/account/mfa/preferred-method` — persiste `PreferredMfaMethod` en el usuario.
7. `GET /api/v1/account/profile` (`AccountProfileResponseDto`) gana `isEmailOtpEnabled` y `preferredMfaMethod`.
8. Nuevo setting `Mfa:ChallengeExpirationMinutes` (default `5`), atado a un `MfaOptions` igual que `SendGridOptions`/`JwtOptions`.
9. Límite de intentos de 5 fallos por `challengeToken` (igual criterio que `ConfirmPhoneVerificationCommandHandler`) — invalida todo el desafío, no solo el método usado, forzando un login nuevo desde cero.

**Out of scope (igual que `join_frontb` SPEC 12):**

- SMS como método de 2FA.
- "Recordar este dispositivo".
- 2FA obligatorio por rol.
- Códigos de recuperación para Email OTP.
- Tocar `request-email-change`/`confirm-email-change` (cambio de correo de cuenta, flujo distinto).
- Revocar sesiones existentes al desactivar el único método activo — **no se revocan**, mismo criterio que el resto de SPEC 03/26.

---

## Data model

### Forma de `LoginResponse` — confirmado (2026-09-20)

`LoginResponse` (`src/2.Application.DTO/Security/LoginResponse.cs`) hoy tiene `Token`/`RefreshToken`/`Expiration` **no anulables**. Esta spec los vuelve anulables y agrega los tres campos del desafío **al mismo tipo**, en vez de crear un `MfaChallengeResponse` separado que obligaría al controller a devolver `Response<object>` (perdiendo el tipado fuerte que hoy tiene `ProducesResponseType(typeof(Response<LoginResponse>), ...)`). Ver razonamiento completo en Decisiones.

```csharp
// src/2.Application.DTO/Security/LoginResponse.cs (MODIFICADO)
public record LoginResponse
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public Guid? CompanyId { get; init; }
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();

    // Anulables: null cuando la respuesta es un desafío, no un login exitoso.
    public string? Token { get; init; }
    public string? RefreshToken { get; init; }
    public DateTime? Expiration { get; init; }

    // Nuevos: null cuando el login es exitoso sin desafío.
    public string? ChallengeToken { get; init; }
    public IReadOnlyCollection<string>? AvailableMethods { get; init; }
    public string? PreferredMethod { get; init; }
}
```

`Roles` queda vacío (`[]`) en la rama de desafío — no tiene sentido revelar roles antes de completar el segundo factor.

### `MfaLoginChallenge` (nuevo, schema `Security`)

Vive server-side entre el `POST /Users/login` que lo crea y el `POST /auth/mfa/challenge/verify` que lo consume. Clona el patrón de `PhoneVerificationCode` (SPEC 30): `BaseAuditableEntity`, hash PBKDF2 vía `RecoveryCodeHasher`, `GcRecord` para soft-delete/invalidación.

```csharp
// src/1.Domain/Security/MfaLoginChallenge.cs (nuevo)
public class MfaLoginChallenge : BaseAuditableEntity
{
    public MfaLoginChallenge() { }
    public MfaLoginChallenge(Guid id) { Id = id; }

    public Guid UserId { get; set; }

    /// <summary>Igual que <c>LoginCommand.TargetCompanyId</c> — se reevalúa en <c>verify</c>, no se cachea el resultado.</summary>
    public Guid? TargetCompanyId { get; set; }

    /// <summary>Hash PBKDF2 del token opaco devuelto al cliente como <c>ChallengeToken</c>. Nunca se guarda en claro.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public int AttemptCount { get; set; }

    // --- Sub-estado del método email (un desafío = un código activo, no hace falta tabla hija) ---
    public string? EmailCodeHash { get; set; }
    public DateTime? EmailCodeExpiresAtUtc { get; set; }
    public DateTime? EmailSentAtUtc { get; set; }

    public virtual ApplicationUser? User { get; set; }
}
```

`AvailableMethods` no se persiste — se deriva en el momento de `send`/`verify` leyendo `user.IsMfaEnabled`/`user.IsEmailOtpEnabled` (misma fuente de verdad que al crear el desafío; evita que un cambio de método a mitad del desafío quede con datos viejos).

### `EmailOtpEnableCode` (nuevo, schema `Security`)

Clon exacto de `PhoneVerificationCode`, pero para el código de prueba de `email-otp/send-code` → `enable`/`disable` (flujo **autenticado**, no confundir con `MfaLoginChallenge.EmailCodeHash` que es pre-JWT).

```csharp
// src/1.Domain/Security/EmailOtpEnableCode.cs (nuevo)
public class EmailOtpEnableCode : BaseAuditableEntity
{
    public EmailOtpEnableCode() { }
    public EmailOtpEnableCode(Guid id) { Id = id; }

    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
    public int AttemptCount { get; set; }

    public virtual ApplicationUser? User { get; set; }
}
```

### Repositorios (Dapper, mismo molde que `IPhoneVerificationCodeRepository`)

```csharp
// src/2.Application/Interface/Persistence/Security/IMfaLoginChallengeRepository.cs (nuevo)
public interface IMfaLoginChallengeRepository
{
    Task InsertAsync(MfaLoginChallenge challenge, CancellationToken ct);
    /// <summary>Busca por hash del token opaco recibido del cliente; excluye consumidos/expirados.</summary>
    Task<MfaLoginChallenge?> GetActiveByTokenHashAsync(string tokenHash, DateTime utcNow, CancellationToken ct);
    Task UpdateEmailCodeAsync(Guid challengeId, string emailCodeHash, DateTime expiresAtUtc, DateTime sentAtUtc, CancellationToken ct);
    Task<int> IncrementAttemptAsync(Guid challengeId, DateTime utcNow, CancellationToken ct);
    Task<bool> MarkConsumedAsync(Guid challengeId, DateTime utcNow, CancellationToken ct);
    Task InvalidateAsync(Guid challengeId, DateTime utcNow, CancellationToken ct);
}

// src/2.Application/Interface/Persistence/Security/IEmailOtpEnableCodeRepository.cs (nuevo)
// Misma forma exacta que IPhoneVerificationCodeRepository (InvalidateActiveByUserAsync,
// InsertAsync, GetLatestActiveByUserAsync, MarkUsedAsync, IncrementAttemptAsync).
```

### `MfaOptions` (nuevo, igual molde que `SendGridOptions`)

```csharp
// src/2.Application/Common/Options/MfaOptions.cs (nuevo)
public sealed class MfaOptions
{
    /// <summary>Minutos que vive un challengeToken antes de expirar. Default 5.</summary>
    public int ChallengeExpirationMinutes { get; init; } = 5;
}
```

```json
// appsettings.json (agregar sección, junto a "Jwt"/"SendGrid")
"Mfa": {
  "ChallengeExpirationMinutes": 5
}
```

### Contratos nuevos (`JOIN.Application.DTO.Security.Auth` / `.Account`)

```csharp
public sealed record VerifyMfaChallengeRequestDto
{
    public required string ChallengeToken { get; init; }
    public required string Method { get; init; } // "email" | "totp"
    public required string Code { get; init; }
}

public sealed record SendMfaChallengeRequestDto
{
    public required string ChallengeToken { get; init; }
    public required string Method { get; init; } // solo "email" es válido hoy
}

public sealed record EnableEmailOtpRequestDto
{
    public required string Code { get; init; }
}

public sealed record DisableEmailOtpRequestDto
{
    public required string Code { get; init; }
}

public sealed record SetPreferredMfaMethodRequestDto
{
    public required string Method { get; init; } // "email" | "totp"
}
```

`AccountProfileResponseDto` gana:

```csharp
public bool IsEmailOtpEnabled { get; init; }
public string? PreferredMfaMethod { get; init; }
```

`ApplicationUser` gana:

```csharp
// src/1.Domain/Security/applicationuser.cs
public bool IsEmailOtpEnabled { get; set; } = false;
public string? PreferredMfaMethod { get; set; } // "email" | "totp" | null
```

### Endpoints — resumen

| Método | Ruta | Controller | Auth | Nota |
|---|---|---|---|---|
| POST | `/api/v1/auth/mfa/challenge/send` | `AuthController` | Anónimo | `[EnableRateLimiting("Strict")]`, igual que `login` |
| POST | `/api/v1/auth/mfa/challenge/verify` | `AuthController` | Anónimo | ídem |
| POST | `/api/v1/account/email-otp/send-code` | `AccountController` | JWT | Rechaza `EMAIL_NOT_CONFIRMED` si `EmailConfirmed == false` |
| POST | `/api/v1/account/email-otp/enable` | `AccountController` | JWT | |
| POST | `/api/v1/account/email-otp/disable` | `AccountController` | JWT | |
| PUT | `/api/v1/account/mfa/preferred-method` | `AccountController` | JWT | Rechaza si el método no está habilitado |

### Códigos de error nuevos (`AccountErrorCodes.Map`, reutilizado también por `AuthController`)

`AuthController` hoy **no traduce errores a códigos HTTP** (sus 3 acciones existentes hacen `return Ok(response)` directo, incluso en fallos). Los dos endpoints nuevos de `AuthController` **sí** deben usar `AccountErrorCodes.ToResult(response)` (la clase ya es `internal` dentro del mismo namespace `JOIN.Services.WebApi.Controllers.Security`, no hace falta moverla ni renombrarla) — las otras 3 acciones de `AuthController` quedan intactas, fuera de alcance.

```csharp
["CHALLENGE_NOT_FOUND"]              = StatusCodes.Status401Unauthorized,
["CHALLENGE_EXPIRED"]                = StatusCodes.Status401Unauthorized,
["CHALLENGE_INVALID_CODE"]           = StatusCodes.Status401Unauthorized,
["CHALLENGE_LOCKED"]                 = StatusCodes.Status429TooManyRequests,
["CHALLENGE_METHOD_NOT_AVAILABLE"]   = StatusCodes.Status400BadRequest,
["CHALLENGE_METHOD_NOT_RESENDABLE"]  = StatusCodes.Status400BadRequest, // method: "totp" en /send
["CHALLENGE_SEND_COOLDOWN"]          = StatusCodes.Status429TooManyRequests,
["EMAIL_NOT_CONFIRMED"]              = StatusCodes.Status400BadRequest,
["EMAIL_OTP_NOT_REQUESTED"]          = StatusCodes.Status400BadRequest, // enable/disable sin send-code previo
["EMAIL_OTP_CODE_EXPIRED"]           = StatusCodes.Status400BadRequest,
["EMAIL_OTP_CODE_INVALID"]           = StatusCodes.Status400BadRequest,
["EMAIL_OTP_CODE_LOCKED"]            = StatusCodes.Status429TooManyRequests,
["PREFERRED_METHOD_NOT_ENABLED"]     = StatusCodes.Status400BadRequest,
```

### `SecurityEventType` — conecta el valor muerto y agrega los que faltan

```csharp
// LoginMfaRequired = 3  ← YA EXISTE, nunca usado; esta spec lo dispara por primera vez
LoginMfaChallengeVerified = 19,
LoginMfaChallengeFailed = 20,
LoginMfaChallengeLocked = 21,
LoginMfaChallengeCodeSent = 22,
EmailOtpEnableCodeRequested = 23,
EmailOtpEnabled = 24,
EmailOtpDisabled = 25,
MfaPreferredMethodChanged = 26,
```

### Refactor obligatorio en `LoginCommandHandler`

Hoy `LoginCommandHandler.Handle` valida credenciales y, en el mismo método, resuelve compañía/roles y emite el JWT (líneas 66-139). Con el desafío, esa segunda mitad (resolución de compañía + roles + emisión de refresh token + JWT + log de `LoginSucceeded`) debe poder ejecutarse desde **dos** lugares: el login sin 2FA (como hoy) y `VerifyMfaChallengeCommandHandler` tras un código correcto. Extraer esa mitad a un servicio compartido, p. ej. `IAuthenticatedSessionIssuer.IssueAsync(ApplicationUser user, Guid? targetCompanyId, CancellationToken ct) : Task<LoginResponse>`, inyectado en ambos handlers. **No duplicar la lógica de resolución de compañía/roles entre los dos handlers.**

El punto de corte queda así en `LoginCommandHandler`:

```csharp
// tras CheckPasswordAsync exitoso, antes de ResolveEffectiveCompanyIdAsync:
if (user.IsMfaEnabled || user.IsEmailOtpEnabled)
{
    return await _mfaChallengeIssuer.IssueChallengeAsync(user, request.TargetCompanyId, cancellationToken);
    // Dispara SecurityEventType.LoginMfaRequired en vez de LoginSucceeded.
}
```

---

## Implementation plan

**F1 — Refactor de extracción.** Extraer la resolución de compañía/roles + emisión de JWT/refresh-token de `LoginCommandHandler` a `IAuthenticatedSessionIssuer` (nuevo, `src/2.Application/Interface/IAuthenticatedSessionIssuer.cs` + implementación en el mismo namespace de `Auth/Login`). Los tests existentes de `LoginCommandHandler` deben seguir pasando sin cambios de aserciones — es un refactor puro, sin cambio de comportamiento observable para usuarios sin 2FA.

**F2 — Dominio y persistencia.** `MfaLoginChallenge`, `EmailOtpEnableCode`, columnas nuevas en `ApplicationUser` (`IsEmailOtpEnabled`, `PreferredMfaMethod`), configuración EF (`Security.MfaLoginChallenges`, `Security.EmailOtpEnableCodes`), migración.

**F3 — Repositorios.** `IMfaLoginChallengeRepository`/`IEmailOtpEnableCodeRepository` + implementación Dapper, registrar en `3.Persistence/Configuration/ConfigureServices.cs`.

**F4 — `MfaOptions`.** Clase + bind en `Program.cs` + sección `Mfa` en `appsettings.json`/`appsettings.Development.json`.

**F5 — Login condicional.** Modificar `LoginCommandHandler` para bifurcar a un desafío cuando corresponda (usa F1). Modificar `LoginResponse` según la forma confirmada (ver Data model).

**F6 — `auth/mfa/challenge/send` + `verify`.** Nuevos comandos/handlers bajo `UseCases/Security/Auth/MfaChallenge/`, nuevas acciones en `AuthController` (anónimas, `EnableRateLimiting("Strict")`, usando `AccountErrorCodes.ToResult`). `verify` con método `totp` valida en vivo contra `user.MfaSecretKey` vía `IMfaTotpValidator` (sin tabla propia); con método `email` valida contra `MfaLoginChallenge.EmailCodeHash`. Lockout de 5 intentos invalida todo el `MfaLoginChallenge` (no solo el método).

**F7 — `account/email-otp/send-code|enable|disable`.** Nuevos comandos/handlers bajo `UseCases/Security/Account/Commands/EmailOtp*/`, clonando `RequestPhoneVerificationCommandHandler`/`ConfirmPhoneVerificationCommandHandler`. `send-code` rechaza `EMAIL_NOT_CONFIRMED` si `!user.EmailConfirmed`.

**F8 — `PUT account/mfa/preferred-method`.** Nuevo comando/handler, rechaza `PREFERRED_METHOD_NOT_ENABLED` si el método pedido no está activo para ese usuario.

**F9 — `GetMyProfileQuery`.** Sumar `IsEmailOtpEnabled`/`PreferredMfaMethod` al mapeo hacia `AccountProfileResponseDto`.

**F10 — Instrumentación de auditoría.** Disparar los `SecurityEventType` nuevos (incluyendo el ya existente `LoginMfaRequired`) desde cada handler tocado, siguiendo el patrón ya usado en `EnableMfaCommandHandler`/`ConfirmPhoneVerificationCommandHandler`.

**F11 — Tests.** Unit tests para los handlers nuevos + el refactor de F1 (cobertura ≥90% igual que el resto de `Application`, ver gate de CI). Integration test end-to-end: login con TOTP activo → desafío → verify → JWT; login con email activo → desafío → send (cooldown) → verify; lockout a los 5 intentos; expiración del `challengeToken`.

**F12 — Verificación final y documentación.** Actualizar `CURL_REQUESTS.md` con los 6 endpoints nuevos. Confirmar en vivo (como se hizo para auditar esta spec) que un usuario con `IsMfaEnabled=true` ya no recibe JWT directo en `POST /Users/login`.

---

## Acceptance criteria

- [ ] Usuario sin 2FA: `POST /Users/login` responde igual que hoy, con `Token`/`RefreshToken` poblados en el mismo paso (no regresión).
- [ ] Usuario con `IsMfaEnabled` y/o `IsEmailOtpEnabled`: `POST /Users/login` responde `200` con `Token`/`RefreshToken`/`Expiration` en `null` y `ChallengeToken`/`AvailableMethods` poblados; `Roles` vacío.
- [ ] Un solo método activo → `AvailableMethods` trae un solo valor.
- [ ] Ambos métodos activos → `AvailableMethods` trae los dos y `PreferredMethod` refleja lo persistido en `PUT mfa/preferred-method`.
- [ ] `verify` con código TOTP correcto → `200` con JWT real; código incorrecto → `401 CHALLENGE_INVALID_CODE`, sin JWT.
- [ ] `verify` con código email correcto → `200` con JWT real; con el `challengeToken` ya expirado (pasado `Mfa:ChallengeExpirationMinutes`) → `401 CHALLENGE_EXPIRED`.
- [ ] 5 intentos fallidos de `verify` sobre el mismo `challengeToken` → `429 CHALLENGE_LOCKED`; un 6º intento con el código correcto también falla (el desafío entero quedó invalidado).
- [ ] `send` con `method: "totp"` → `400 CHALLENGE_METHOD_NOT_RESENDABLE`.
- [ ] `send` repetido antes de que pasen 60s desde el envío anterior → `429 CHALLENGE_SEND_COOLDOWN`.
- [ ] `account/email-otp/send-code` con `EmailConfirmed == false` → `400 EMAIL_NOT_CONFIRMED`, no se envía nada.
- [ ] `account/email-otp/enable` con código correcto → `IsEmailOtpEnabled = true` en `GET account/profile`.
- [ ] `account/email-otp/disable` funciona igual que `enable` a la inversa; se puede desactivar TOTP y Email OTP en cualquier combinación y orden, sin ningún mínimo exigido — `PUT mfa/preferred-method` con el único método restante en `null`/inactivo → `400 PREFERRED_METHOD_NOT_ENABLED` si se intenta setear ese que ya no está activo.
- [ ] Desactivar el único método activo no revoca sesiones existentes (`GET account/sessions` sigue mostrando las mismas sesiones antes/después).
- [ ] `PUT mfa/preferred-method` en un dispositivo se refleja en el desafío de `POST /Users/login` iniciado desde otro dispositivo/navegador (confirma persistencia en servidor).

---

## Decisiones

- **Sí, confirmado por el dueño del producto (2026-09-20):** fusionar los campos del desafío dentro de `LoginResponse` (volviendo `Token`/`RefreshToken`/`Expiration` anulables) en vez de crear un `MfaChallengeResponse` separado. Razón: `Response<LoginResponse>` es el tipo que ya expone `ProducesResponseType` en `UsersController.Login`, y tanto Refit (frontend) como `System.Text.Json` (backend) no discriminan bien entre dos formas de éxito distintas sin un converter a medida — el propio `join_frontb` SPEC 12 ya decidió que el discriminador es "ausencia de `token`", lo cual encaja natural con un único tipo con campos anulables. La alternativa (tipo separado + `Response<object>`) es más invasiva y rompe el tipado fuerte del controller sin ganar nada a cambio. Ya no bloquea F5.
- **Sí, confirmado por el dueño del producto (2026-09-20):** 5 intentos fallidos de `verify` invalidan **todo** el `MfaLoginChallenge`, no solo el método usado en esos intentos (si el usuario prueba TOTP mal 5 veces, tampoco puede ya usar email con el mismo `challengeToken` — debe loguearse de cero). Razón: mismo modelo de amenaza que documenta el Riesgo de `join_frontb` SPEC 12 sobre un `challengeToken` interceptado; invalidar todo el desafío es la postura más conservadora. Ya no bloquea F6.
- **Sí, confirmado por el dueño del producto (2026-09-20):** cooldown de reenvío de 60s en `auth/mfa/challenge/send`, implementado con `MfaLoginChallenge.EmailSentAtUtc` + chequeo explícito (no con `[EnableRateLimiting]`, que es por IP/genérico y no le puede decir al frontend "faltan 42s"). **Corrección a lo asumido por `join_frontb` SPEC 12:** ese spec da por hecho que existe "el mismo patrón de cooldown/429 que ya usa `verify-phone/confirm`" — se verificó el código real (`ConfirmPhoneVerificationCommandHandler`) y **ese patrón no existe hoy**: el 429 ahí es por *lockout de 5 intentos fallidos*, no por cooldown de reenvío. No hay nada que clonar; se construye nuevo desde F6. Ya no bloquea F6.
- **Sí, confirmado (heredado de `join_frontb` SPEC 12):** `Mfa:ChallengeExpirationMinutes` default 5, configurable, no hardcodeado. Código de Email OTP numérico de 6 dígitos. `EmailConfirmed == true` obligatorio para activar Email OTP. Desactivar 2FA no revoca sesiones. Método preferido persiste en servidor. 2FA 100% opcional, sin mínimos.
- **Nuevo, agregado por esta spec:** `POST /account/email-otp/send-code` — endpoint que faltaba en el Data model de `join_frontb` SPEC 12 (ver "Por qué existe esta spec"). Recomendar actualizar ese spec para referenciarlo antes de que frontend implemente su paso 7.

---

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Cambiar `LoginResponse` (tipo usado en producción por `UsersController.Login` y `RefreshTokenCommandHandler`) a campos anulables puede romper código que asuma `Token`/`RefreshToken` no-nulos | `RefreshTokenCommandHandler` siempre puebla los tres campos con valores reales — sigue compilando y funcionando igual; auditar el resto del `src/` en busca de `.Token`/`.RefreshToken` sin null-check antes de mergear F5 |
| El refactor F1 (`IAuthenticatedSessionIssuer`) toca el corazón de `LoginCommandHandler`, que ya está en producción y tiene un comentario de un bug real ya corregido (líneas 254-263, `IsSuperAdminCompany`) — un refactor descuidado podría reintroducir ese bug | Mover el código tal cual, sin "aprovechar para limpiar"; correr los tests existentes de `LoginCommandHandler` antes y después del refactor y comparar 1:1 |
| Rate limiting `"Strict"` en los dos endpoints nuevos de `AuthController` es por IP — un atacante distribuido podría igual intentar fuerza bruta sobre `verify` desde múltiples IPs | El lockout de 5 intentos por `challengeToken` (no por IP) ya cubre este caso independientemente del rate limiter |
| Confirmado ya en `join_frontb` SPEC 12: SendGrid con `401 Maximum credits exceeded` — Email OTP no se puede probar en vivo hasta resolver el cupo | No bloquea el desarrollo de F1-F5 y F6 (rama `totp`) ni los unit tests (mockean `IEmailService`); sí bloquea la verificación end-to-end de F11/F12 para el método email |

---

## Lo que NO está en esta spec

SMS como método de 2FA, "recordar dispositivo", 2FA obligatorio por rol, códigos de recuperación para Email OTP, cambios a `request-email-change`/`confirm-email-change`, revocación de sesiones al desactivar 2FA.

---

## Archivos críticos

### Modify

- `src/2.Application.DTO/Security/LoginResponse.cs` — campos anulables + campos de desafío (pendiente confirmación, ver Decisiones).
- `src/2.Application/UseCases/Security/Auth/Login/LoginCommandHandler.cs` — extracción F1 + bifurcación F5.
- `src/1.Domain/Security/applicationuser.cs` — `IsEmailOtpEnabled`, `PreferredMfaMethod`.
- `src/1.Domain/Security/SecurityEventType.cs` — 7 valores nuevos (19-26).
- `src/4.Services.WebApi/Controllers/Security/AccountController.cs` — 4 acciones nuevas (`email-otp/send-code|enable|disable`, `mfa/preferred-method`).
- `src/4.Services.WebApi/Controllers/Security/AccountErrorCodes.cs` — 13 códigos nuevos.
- `src/4.Services.WebApi/Controllers/Security/AuthController.cs` — 2 acciones nuevas (`mfa/challenge/send|verify`).
- `src/2.Application.DTO/Security/Account/AccountProfileResponseDto.cs` — `isEmailOtpEnabled`, `preferredMfaMethod`.
- `src/2.Application/UseCases/Security/Account/Queries/GetMyProfile/*` — mapeo de los dos campos nuevos.
- `src/3.Persistence/Configuration/ConfigureServices.cs` — registrar los 2 repositorios nuevos.
- `src/4.Services.WebApi/Program.cs` — bind de `MfaOptions`.
- `src/4.Services.WebApi/appsettings.json` + `appsettings.Development.json` — sección `Mfa`.
- `CURL_REQUESTS.md` — 6 endpoints nuevos.

### Create

**Domain:**
- `src/1.Domain/Security/MfaLoginChallenge.cs`
- `src/1.Domain/Security/EmailOtpEnableCode.cs`

**Application — options/interfaces:**
- `src/2.Application/Common/Options/MfaOptions.cs`
- `src/2.Application/Interface/IAuthenticatedSessionIssuer.cs`
- `src/2.Application/Interface/Persistence/Security/IMfaLoginChallengeRepository.cs`
- `src/2.Application/Interface/Persistence/Security/IEmailOtpEnableCodeRepository.cs`

**Application — DTOs:**
- `src/2.Application.DTO/Security/Auth/VerifyMfaChallengeRequestDto.cs`
- `src/2.Application.DTO/Security/Auth/SendMfaChallengeRequestDto.cs`
- `src/2.Application.DTO/Security/Account/EnableEmailOtpRequestDto.cs`
- `src/2.Application.DTO/Security/Account/DisableEmailOtpRequestDto.cs`
- `src/2.Application.DTO/Security/Account/SetPreferredMfaMethodRequestDto.cs`

**Application — handlers:**
- `src/2.Application/UseCases/Security/Auth/Login/AuthenticatedSessionIssuer.cs` (implementación de F1)
- `src/2.Application/UseCases/Security/Auth/MfaChallenge/SendMfaChallenge/{SendMfaChallengeCommand,CommandHandler,CommandValidator}.cs`
- `src/2.Application/UseCases/Security/Auth/MfaChallenge/VerifyMfaChallenge/{VerifyMfaChallengeCommand,CommandHandler,CommandValidator}.cs`
- `src/2.Application/UseCases/Security/Account/Commands/EmailOtpSendCode/{...}.cs`
- `src/2.Application/UseCases/Security/Account/Commands/EnableEmailOtp/{...}.cs`
- `src/2.Application/UseCases/Security/Account/Commands/DisableEmailOtp/{...}.cs`
- `src/2.Application/UseCases/Security/Account/Commands/SetPreferredMfaMethod/{...}.cs`

**Persistence:**
- `src/3.Persistence/Configurations/Security/MfaLoginChallengeConfiguration.cs`
- `src/3.Persistence/Configurations/Security/EmailOtpEnableCodeConfiguration.cs`
- `src/3.Persistence/Repositories/Security/MfaLoginChallengeRepository.cs`
- `src/3.Persistence/Repositories/Security/EmailOtpEnableCodeRepository.cs`
- Migración EF nueva (`dotnet ef migrations add AddMfaLoginChallengeAndEmailOtp`).

**Tests:**
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Auth/Login/` (refactor + nuevo)
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Auth/MfaChallenge/`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Security/Account/EmailOtp*/`
- `tests/IntegrationTests/` — flujo end-to-end del desafío.
