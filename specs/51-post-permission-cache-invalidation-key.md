# SPEC 51-post — Invalidación de la caché de permisos con la clave `permissions:v2`

> **Status:** Implementado
> **Origen:** copia de SPEC 51 de `main` (estado allí al copiarla: Borrador), adaptada a PostgreSQL para el fork `main_postgresql`. Se implementa **desde cero** en el fork, sin traer commits de `main` (decisión del usuario, 2026-10-09).
> **Rama de trabajo:** `spec-51-post-permission-cache-invalidation-key`, creada desde `main_postgresql`; PR hacia `main_postgresql`, nunca hacia `main`.
> **Lectura en el fork:** dentro de esta spec, toda referencia a una spec de la serie copiada (40–49, 51, 00) se lee como su versión `-post` ("SPEC 41" = SPEC 41-post), y aplican las convenciones de `specs/README.md` → "Serie `-post`". Donde el texto copiado de `main` y la sección "Adaptación a PostgreSQL" difieren, prevalece esta última.
> **Depends on:** SPEC 17 (cambió la clave de la caché de permisos a `permissions:v2`), SPEC 25 (agregó `IPermissionService.InvalidateUserCacheAsync` y documentó este bug). Ambas ya están en el fork.
> **Date:** 2026-10-09 (copia `-post`; original: 2026-10-08)
> **Objective:** Que `POST /Users/sidebar/cache/invalidate` (y su alias `POST /Users/cleancache`) borre de verdad el snapshot de permisos del usuario cuando se pide `permission`, `permissions` o `all`.

---

## Adaptación a PostgreSQL (fork `main_postgresql`)

Sin diferencias de motor: la caché es `IMemoryCache` y el arreglo es solo de Application. Verificado el 2026-10-09 que el fork tiene el mismo defecto: `InvalidateSidebarCacheCommandHandler.cs:36` y `:41` borran `permissions:{companyId}:{userId}`, mientras `PermissionService` usa el prefijo `permissions:v2` (`PermissionService.cs:24`). Solo cambian la rama de trabajo y que la suite de integración del paso 4 es la PostgreSQL (`~PostgreSql`).

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
4. Build Release, gate de cobertura 90 % y suite de integración PostgreSQL (`~PostgreSql`).

---

## Acceptance criteria

- [x] Después de `POST /Users/sidebar/cache/invalidate` con `cacheKey = "permission"` (o `"all"`), el siguiente request del usuario reconstruye el snapshot de permisos desde la base. *(integración `CleanCachePostgreSqlTests`, también por el alias `/Users/cleancache`)*
- [x] `cacheKey = "sidebar"` sigue borrando solo la caché del menú.
- [x] Una clave no soportada sigue devolviendo `INVALID_CACHE_KEY` (desde el handler). *Ajuste (2026-10-13): por HTTP la rechaza antes `CleanCacheCommandValidator` con 400 ("CacheKey must be one of…"), igual que antes de esta spec; `INVALID_CACHE_KEY` queda como respaldo defensivo del handler, cubierto por unit tests.*
- [x] Ningún archivo arma a mano la clave `permissions:{companyId}:{userId}` (la única construcción es `PermissionService.CacheKeyPrefix`).

---

## Preguntas abiertas

> **Decisión del usuario (2026-10-08):** las preguntas abiertas se responden cuando se trabaje esta spec; hasta entonces queda en `Borrador` y no bloquea las siguientes.

- Con `cacheKey = "permission"`, `InvalidateUserCacheAsync` también borra la caché del menú (el menú depende de los permisos). ¿Se acepta, o se agrega un método que borre solo el snapshot?
  - **Decisión del usuario (2026-10-13): opción A, se acepta.** Es lo que ya dice el Scope, el menú se arma a partir de los permisos (un menú cacheado con permisos viejos sería incoherente) y reconstruirlo cuesta una consulta. No se agrega un método nuevo a `IPermissionService`.

---

## Registro de implementación (2026-10-13)

Rama `spec-51-post-permission-cache-invalidation-key`.

- `InvalidateSidebarCacheCommandHandler.cs` (`CleanCacheCommandHandler`): recibe `IPermissionService` por constructor primario; `permission`/`permissions` llaman a `InvalidateUserCacheAsync`; `all` llama a `InvalidateUserCacheAsync` y además borra `sidebar:` explícitamente; `sidebar` sin cambios. Mensajes y contrato sin cambios (el texto de `Errors` para `INVALID_CACHE_KEY` se mantuvo idéntico).
- `IPermissionService.InvalidateUserCacheAsync`: quitada la nota del bug del `<remarks>`; el `<summary>` indica que es el punto de entrada que usa `CleanCacheCommandHandler`.
- Unit tests: `tests/UnitTests/.../Security/Users/Commands/InvalidateSidebarCache/CleanCacheCommandHandlerTests.cs` (6 casos: `sidebar` borra solo el menú, `permission`, `permissions`, `all` borra snapshot + menú, clave desconocida → `INVALID_CACHE_KEY`, normalización de mayúsculas/espacios).
- Integración: `tests/IntegrationTests/Security/CleanCachePostgreSqlTests.cs` (5 casos sobre la `IMemoryCache` real: la clave `permissions:v2` desaparece y el siguiente request la reconstruye, `sidebar` conserva el snapshot, clave no soportada → 400).
- Verificación: build Release sin errores; unit tests 3190 OK (2 skipped), cobertura de líneas 94,48 % (gate 90 %); integración `~PostgreSql` 209/209 OK.
- Frontend: sin cambios de ruta, contrato ni permisos, así que no hay que actualizar specs FE (FE-07, FE-11).
