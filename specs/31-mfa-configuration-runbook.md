# SPEC 31 — MFA configuration runbook (user + admin guide)

> **Status:** Implementado
> **Depends on:** SPEC 26 (F6 MFA endpoints), `Otp.NET` validator, `Security.UserMfaRecoveryCodes` table, `Security.Users.MfaEnabledAtUtc` column.
> **Date:** 2026-08-18
> **Objective:** Procedimiento paso-a-paso para enrolar MFA desde el endpoint `AccountController`, con los curl exactos, respuesta esperada y resolución de los errores observados durante la prueba inicial (`POST /mfa/setup` → 500 por `BeginTransaction` sin `Open()`, audit log timeout 30s+).

---imp

## Prerequisites

- Cuenta activa (`IsActive=1`, `GcRecord=0`) en cualquier tenant.
- JWT válido (login previo o refresh). El bearer debe incluir claim `CompanyId` válido.
- Authenticator app instalada en el dispositivo destino (Google Authenticator, Microsoft Authenticator, Authy, 1Password, etc.).
- El usuario **no** debe tener MFA ya habilitado. Si ya lo tiene, primero `POST /mfa/disable`.

---

## End-to-end enrolment

### Step 1 — Generar bundle de enrolment

```bash
curl --location --request POST 'http://localhost:5267/api/v1/account/mfa/setup' \
  --header 'Authorization: Bearer <JWT>'
```

**Respuesta esperada (`200 OK`):**

```json
{
  "data": {
    "secret": "NLBGFSTAQNL5VSAXPL7OCBM76NC2AFRV",
    "qrCodeUri": "otpauth://totp/JOIN%3Alivingstone23%40gmail.com?secret=NLBGFSTAQNL5VSAXPL7OCBM76NC2AFRV&issuer=JOIN",
    "recoveryCodes": ["8SDQAXMHPT", "31AQEBW6X1", ...10 entries total]
  },
  "isSuccess": true,
  "message": "MFA enrolment bundle generated. Confirm with /mfa/enable to activate.",
  "errors": null
}
```

**Lo que devuelve:**
- `secret`: Base32 de 32 chars. Es la seed TOTP.
- `qrCodeUri`: provisioning URI estándar `otpauth://totp/...`. Se puede convertir a QR con cualquier librería (`qrcode.js`, etc.).
- `recoveryCodes`: 10 códigos alfanuméricos de 10 chars, mostrados **una sola vez**. El servidor solo guarda el PBKDF2-SHA256 hash.

**Lo que NO hace este endpoint:**
- No activa MFA todavía.
- No invalida sesiones existentes.
- Wipea códigos de recuperación previos del usuario si los hubiera.

### Step 2 — Escanear QR / introducir secret

En el authenticator app:
1. Seleccionar "Añadir cuenta" → "Otro" / "Basado en tiempo" (TOTP).
2. Escanear el QR (renderizar `qrCodeUri` como QR) o introducir `secret` manualmente.
3. Verificar que el app muestra códigos de 6 dígitos que rotan cada 30 segundos.

### Step 3 — Activar MFA con código TOTP

```bash
curl --location --request POST 'http://localhost:5267/api/v1/account/mfa/enable' \
  --header 'Authorization: Bearer <JWT>' \
  --header 'Content-Type: application/json' \
  --data '{"code":"123456"}'
```

Donde `code` es el código de 6 dígitos actual del app.

**Respuesta esperada (`200 OK`):**

```json
{
  "data": true,
  "isSuccess": true,
  "message": "MFA enabled.",
  "errors": null
}
```

Tras éxito:
- `Security.Users.IsMfaEnabled = true`
- `Security.Users.MfaEnabledAtUtc = <utcNow>`
- Audit row `MfaEnabled=9` en `Security.SecurityEventLogs`.

### Step 4 — Guardar recovery codes

Inmediatamente después de step 3, **el cliente debe persistir los 10 recovery codes en un gestor de contraseñas** (1Password, Bitwarden, KeePass, etc.) o imprimirlos. El servidor solo guarda el hash. **No hay endpoint para regenerarlos sin disable+setup de nuevo.**

---

## Login una vez MFA activado

Cuando MFA esté activo, el siguiente login del mismo usuario requerirá TOTP:

```
POST /api/v1/auth/login  →  200 con LoginMfaRequired flag
POST /api/v1/auth/login/mfa  { "userId", "code" }  →  200 con JWT
```

(Este flujo MFA-gate post-password está en scope de spec 30, no implementado todavía en la spec 26.)

---

## Disable MFA

```bash
curl --location --request POST 'http://localhost:5267/api/v1/account/mfa/disable' \
  --header 'Authorization: Bearer <JWT>' \
  --header 'Content-Type: application/json' \
  --data '{"code":"123456"}'
```

`code` puede ser TOTP actual **o** un recovery code no usado.

Tras éxito:
- `IsMfaEnabled=false`, `MfaSecretKey=null`, `MfaEnabledAtUtc=null`
- Recovery codes del usuario borrados (soft-delete via `GcRecord`)
- Audit row `MfaDisabled=10`.

---

## Errores observados y resoluciones

### E1. `POST /mfa/setup` → 500 "Operación no válida; se ha terminado la conexión"

**Causa:** `UserMfaRecoveryCodeRepository.InsertManyAsync` abría una `SqlConnection` y llamaba `BeginTransaction()` sin `connection.Open()` previo. El pool entregaba una conexión en estado `Terminated` que fallaba al auto-open.

**Fix aplicado** (`src/3.Persistence/Repositories/Security/UserMfaRecoveryCodeRepository.cs:38`):

```csharp
using var connection = _connectionFactory.CreateConnection();
connection.Open();                              // <-- añadido
using var transaction = connection.BeginTransaction();
```

Mismo patrón ya existía en `RoleSystemOptionsRepository.BulkUpsertAsync:302`. El fix alinea ambos repos.

**Estado:** resuelto. Re-test devuelve 200 OK con bundle completo.

### E2. Audit log timeout (~30-40s) en `SecurityEventRepository.InsertAsync`

**Síntoma:** respuesta 200 OK al cliente (el `catch` de `SecurityEventLogger.LogAsync` traga el error), pero el log emite `WRN: Failed to persist security event MfaSetupInitiated` con `Microsoft.Data.SqlClient.SqlException (0x80131904): Se agotó el tiempo de espera` y `Error Number:-2, Class:11` (WAIT_TIMEOUT).

**Causa:** SQL Server en `192.168.68.57,1433` no responde al INSERT de audit dentro del timeout por defecto de Dapper (30s). El delay es de red/SQL Server, no del código del repo.

**Mitigaciones pendientes:**
1. Aplicar `connection.Open()` por paridad (no resuelve el timeout de red, solo consistencia).
2. Reducir `commandTimeout` en `SecurityEventRepository.InsertAsync` para fallar rápido (ej. 5s) — sacrifica latencia por visibilidad del fallo.
3. Mover audit log a un `Channel<T>` en background (fire-and-forget) — el handler no espera, log nunca bloquea request.
4. Investigar SQL Server: `SELECT * FROM sys.dm_exec_requests WHERE status='running' ORDER BY cpu_time DESC` para detectar sesiones bloqueantes.

**Estado:** resuelto (opción 2 — `commandTimeout: 5s` en `SecurityEventRepository.InsertAsync`, aplicada en `src/3.Persistence/Repositories/Security/SecurityEventRepository.cs`).

---

## Audit events emitidos

| Endpoint                          | EventType                  | Result |
|-----------------------------------|----------------------------|--------|
| `POST /mfa/setup`                 | `MfaSetupInitiated=8`      | Success |
| `POST /mfa/enable` (valid TOTP)   | `MfaEnabled=9`             | Success |
| `POST /mfa/enable` (invalid code) | `MfaEnabled=9`             | Failure |
| `POST /mfa/disable` (valid)       | `MfaDisabled=10`           | Success |
| `POST /mfa/disable` (invalid)     | `MfaDisabled=10`           | Failure |
| Recovery code consumido           | `MfaRecoveryCodeUsed=11`   | Success |

Todos recuperables vía `GET /api/v1/account/security-activity?pageNumber=1&pageSize=20`.

---

## Out of scope spec 31

- Implementación del gate MFA post-password (spec 30).
- Regeneración de recovery codes sin disable completo.
- Soporte SMS como segundo factor.
- MFA para usuarios con rol `SuperAdmin` (actualmente bypass vía filtro dinámico).
- Persistencia del QR como imagen — el cliente lo renderiza.

---

## Quick reference (cheat sheet)

```
SETUP       POST /api/v1/account/mfa/setup          → secret + qrCodeUri + 10 codes
ENABLE      POST /api/v1/account/mfa/enable   { code }      → IsMfaEnabled=true
DISABLE     POST /api/v1/account/mfa/disable  { code|tcode|recoveryCode } → off
ACTIVITY    GET  /api/v1/account/security-activity?pageNumber=&pageSize=
```
