# SPEC 51 — Invalidación de la caché de permisos con la clave `permissions:v2`

> **Status:** Borrador
> **Depends on:** SPEC 17 (cambió la clave de la caché de permisos a `permissions:v2`), SPEC 25 (agregó `IPermissionService.InvalidateUserCacheAsync` y documentó este bug).
> **Date:** 2026-10-08
> **Objective:** Que `POST /Users/sidebar/cache/invalidate` (y su alias `POST /Users/cleancache`) borre de verdad el snapshot de permisos del usuario cuando se pide `permission`, `permissions` o `all`.

---

## Por qué existe esta spec

`PermissionService` guarda el snapshot de permisos con la clave `permissions:v2:{companyId}:{userId}` (`src/3.Infrastructure/Security/PermissionService.cs:24`, prefijo cambiado en SPEC 17). `CleanCacheCommandHandler` (`src/2.Application/UseCases/Security/Users/Commands/InvalidateSidebarCache/InvalidateSidebarCacheCommandHandler.cs:36` y `:41`) sigue borrando la clave vieja `permissions:{companyId}:{userId}`, que ya no existe:

- Con `cacheKey` = `permission`, `permissions` o `all` la respuesta es `IsSuccess = true` ("Cache cleaned successfully"), pero el snapshot de permisos queda intacto hasta que vence. El usuario sigue con los permisos anteriores.
- `sidebar` sí funciona (la clave `sidebar:{companyId}:{userId}` no cambió).
- SPEC 25 ya lo había detectado y lo dejó documentado en `IPermissionService.InvalidateUserCacheAsync` ("A future spec will fix that command"). Los handlers nuevos (SPEC 25, SPEC 41) usan ese método y no tienen el problema; el que falla es solo este endpoint, que usa el frontend (FE-07, FE-11).

Detectado al revisar los pendientes de SPEC 41 (2026-10-08).

---

## Scope

**In:**

- `CleanCacheCommandHandler`: para `permission`, `permissions` y `all`, invalidar mediante `IPermissionService.InvalidateUserCacheAsync(companyId, userId)` (borra `permissions:v2:{companyId}:{userId}` y `sidebar:{companyId}:{userId}`), en lugar de armar la clave a mano. `sidebar` sigue borrando solo la clave del menú.
- Unit tests del handler (hoy solo existe `CleanCacheCommandValidatorTests`): cada `cacheKey` válido invalida lo que corresponde y una clave desconocida devuelve `INVALID_CACHE_KEY`.
- Quitar del `<remarks>` de `IPermissionService.InvalidateUserCacheAsync` la nota del bug.

**Out of scope:**

- Cambiar la ruta, el contrato (`CleanCacheCommand`) o la autorización del endpoint (`SuperAdmin`): el frontend no cambia.
- Otras cachés o un mecanismo de invalidación distribuido (la caché es `IMemoryCache` por instancia).

---

## Plan de implementación

1. Inyectar `IPermissionService` en `CleanCacheCommandHandler` (constructor primario) y usarlo para `permission`/`permissions`/`all`.
2. Unit tests del handler en `tests/UnitTests/.../Security/Users/Commands/InvalidateSidebarCache/`.
3. Actualizar el `<remarks>` de `IPermissionService`.
4. Build Release, gate de cobertura 90 % y suite de integración.

---

## Acceptance criteria

- [ ] Después de `POST /Users/sidebar/cache/invalidate` con `cacheKey = "permission"` (o `"all"`), el siguiente request del usuario reconstruye el snapshot de permisos desde la base.
- [ ] `cacheKey = "sidebar"` sigue borrando solo la caché del menú.
- [ ] Una clave no soportada sigue devolviendo `INVALID_CACHE_KEY`.
- [ ] Ningún archivo arma a mano la clave `permissions:{companyId}:{userId}`.

---

## Preguntas abiertas

> **Decisión del usuario (2026-10-08):** las preguntas abiertas se responden cuando se trabaje esta spec; hasta entonces queda en `Borrador` y no bloquea las siguientes.

- Con `cacheKey = "permission"`, `InvalidateUserCacheAsync` también borra la caché del menú (el menú depende de los permisos). ¿Se acepta, o se agrega un método que borre solo el snapshot?
