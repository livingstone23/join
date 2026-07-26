# SPEC 17 — `PermissionResource` opcional y override per-action con flags extendidos

> **Status:** Implementado
> **Depends on:** [[12-role-system-option-canexport-canexecute]], [[16-add-canexport-canexecute-to-systemoption]] (los flags `CanExport`/`CanExecute` ya existen en `RoleSystemOption` y `SystemOption`; este spec los conecta al enforcement).
> **Date:** 2026-07-26
> **Objective:** Hacer que `[PermissionResource]` funcione sin argumentos (auto-inferir el nombre desde la clase controller), introducir un atributo `[RequirePermission(PermissionFlags)]` para override per-action que permita mapear endpoints específicos a flags no cubiertos por el HTTP verb (CanRead/Create/Update/Delete), extender `PermissionService.HasPermissionAsync` para evaluar los 7 flags (`CanRead/Create/Update/Delete/Download/Export/Execute`) y respetar el bypass de `[AllowAnonymous]`/`[SkipDynamicAuthorization]`, manteniendo la semántica HTTP 401 (sin token / sin CompanyId) y 403 (token OK + flag false)./

---

## Scope

**In:**

- `src/4.Services.WebApi/Filters/PermissionResourceAttribute.cs`: agregar constructor sin parámetros; `ResourceName` pasa a `string?`. Documentar que la inferencia automática se hace en el filter.
- `src/4.Services.WebApi/Filters/RequirePermissionAttribute.cs` (nuevo): atributo a nivel de acción con un `PermissionFlags` enum, usado para override explícito de la regla HTTP verb.
- `src/1.Domain/Security/PermissionFlags.cs` (nuevo): enum `[Flags]` con los 7 bits (`CanRead / CanCreate / CanUpdate / CanDelete / CanDownload / CanExport / CanExecute`) + `None`.
- `src/4.Services.WebApi/Filters/DynamicAuthorizationFilter.cs`:
  - Resolución de `resourceName` ampliada: action-level → class-level → `descriptor.ControllerName` strip "Controller" → fail-closed (ForbidResult).
  - Lectura de `RequirePermissionAttribute` desde `EndpointMetadata`; pasar flag explícito al service.
  - Mantener bypass de `AllowAnonymousAttribute` y `SkipDynamicAuthorizationAttribute` (líneas 35-40 actuales).
  - Mantener SuperAdmin bypass (líneas 62-67 actuales).
  - Mantener 401 (sin userId / sin companyId) y 403 (flag false) — sin cambios de status code.
- `src/3.Infrastructure/Application/Interface/IPermissionService.cs`: nuevo overload `HasPermissionAsync(userId, companyId, resourceName, actionType, PermissionFlags? explicitFlag = null)`.
- `src/3.Infrastructure/Security/PermissionService.cs`:
  - `PermissionFlags` record struct interno pasa de 4 bool a 7 bool.
  - Query EF proyecta los 7 flags (`CanRead/Create/Update/Delete/Download/Export/Execute`).
  - Switch: si `explicitFlag != null` → evaluar flag puntual; else → HTTP verb default (backward compat).
  - Invalidación de cache existente: la nueva firma del dict invalida el cache viejo (cambio de shape); documentar que la próxima implementación pública debe bump-ear la clave de cache p.ej. `permissions:v2:{companyId}:{userId}` o limpiar en startup.
- `tests/UnitTests/JOIN.Application.UnitTest/Security/PermissionServiceTests.cs` (nuevo o extendido): cubrir 7 flags, override explícito, HTTP verb fallback, cache hit/miss, normalización controller names.
- `tests/UnitTests/JOIN.Application.UnitTest/Security/DynamicAuthorizationFilterTests.cs` (nuevo o extendido): cubrir `[AllowAnonymous]` bypass, `[SkipDynamicAuthorization]` bypass, SuperAdmin bypass, sin token → 401, sin CompanyId → 401, flag false → 403, inferencia de resource name desde controller, override per-action.

**Out of scope (tickets separados):**

- TS-17.1: Remover los 33 literales `[PermissionResource("...")]` existentes y migrar a attribute sin argumento. La auditoría de drift (paso 6 del plan) precede este ticket.
- TS-17.2: Crear archivo `src/4.Services.WebApi/Security/PermissionResources.cs` con constantes centralizadas (`PermissionResources.Users`, `PermissionResources.Persons`, etc.) y reemplazar literales por constantes.
- TS-17.3: Cambiar 403 → 401 (la conversación lo propuso; este spec mantiene semántica RFC).
- TS-17.4: Endpoint de export/ejecución concretos en `PersonsController`/`CommunicationChannelsController` decorados con `[RequirePermission(CanExport)]` / `[RequirePermission(CanExecute)]`. Queda como ejemplo de uso, no parte de este spec.
- TS-17.5: Backfill de `Security.RoleSystemOptions.CanDownload/CanExport/CanExecute` para tenants ya existentes fuera de `JOIN-001` (heredado de SPEC 12, fuera de alcance).
- TS-17.6: Exponer los 7 flags en DTOs o mappers — sin caso de uso CQRS para `RoleSystemOption` hoy.

---

## Data model

Este spec no introduce entidades nuevas ni columnas nuevas. Reorganiza dos archivos existentes y agrega dos nuevos.

### `src/1.Domain/Security/PermissionFlags.cs` (nuevo)

```csharp
namespace JOIN.Domain.Security;

[Flags]
public enum PermissionFlags
{
    None       = 0,
    CanRead    = 1 << 0,
    CanCreate  = 1 << 1,
    CanUpdate  = 1 << 2,
    CanDelete  = 1 << 3,
    CanDownload= 1 << 4,
    CanExport  = 1 << 5,
    CanExecute = 1 << 6,
}
```

### `src/4.Services.WebApi/Filters/PermissionResourceAttribute.cs` (extendido)

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionResourceAttribute : Attribute
{
    public string? ResourceName { get; }

    public PermissionResourceAttribute() { ResourceName = null; }
    public PermissionResourceAttribute(string resourceName) { ResourceName = resourceName; }
}
```

### `src/4.Services.WebApi/Filters/RequirePermissionAttribute.cs` (nuevo)

```csharp
using JOIN.Domain.Security;

namespace JOIN.Services.WebApi.Filters;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequirePermissionAttribute(PermissionFlags flag) : Attribute
{
    public PermissionFlags Flag { get; } = flag;
}
```

### `src/3.Infrastructure/Security/PermissionService.cs` (extendido)

```csharp
private readonly record struct PermissionFlagsSnapshot(
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload,
    bool CanExport,
    bool CanExecute);

public async Task<bool> HasPermissionAsync(
    string userId,
    string companyId,
    string resourceName,
    string actionType,
    PermissionFlags? explicitFlag = null)
{
    if (!Guid.TryParse(userId, out var parsedUserId) ||
        !Guid.TryParse(companyId, out var parsedCompanyId))
        return false;

    var cacheKey = $"permissions:{companyId}:{userId}"; // ver TS-17.0 sobre bump
    var permissions = await _memoryCache.GetOrCreateAsync(cacheKey, async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
        entry.SlidingExpiration = TimeSpan.FromMinutes(10);

        var roleIds = await _dbContext.UserRoleCompanies
            .AsNoTracking()
            .Where(link => link.UserId == parsedUserId &&
                           link.CompanyId == parsedCompanyId &&
                           link.GcRecord == 0)
            .Select(link => link.RoleId)
            .Distinct()
            .ToArrayAsync();

        if (roleIds.Length == 0)
            return new Dictionary<string, PermissionFlagsSnapshot>(StringComparer.OrdinalIgnoreCase);

        var rows = await (from roleOption in _dbContext.RoleSystemOptions.AsNoTracking()
                          join systemOption in _dbContext.SystemOptions.AsNoTracking()
                              on roleOption.SystemOptionId equals systemOption.Id
                          where roleIds.Contains(roleOption.RoleId)
                                && roleOption.CompanyId == parsedCompanyId
                                && roleOption.GcRecord == 0
                                && systemOption.GcRecord == 0
                          select new
                          {
                              ControllerName = systemOption.ControllerName ?? string.Empty,
                              roleOption.CanRead,
                              roleOption.CanCreate,
                              roleOption.CanUpdate,
                              roleOption.CanDelete,
                              roleOption.CanDownload,
                              roleOption.CanExport,
                              roleOption.CanExecute
                          })
            .ToListAsync();

        var map = new Dictionary<string, PermissionFlagsSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var normalizedNames = NormalizeControllerNames(row.ControllerName);
            foreach (var name in normalizedNames)
            {
                map.TryGetValue(name, out var current);
                map[name] = new PermissionFlagsSnapshot(
                    current.CanRead    || row.CanRead,
                    current.CanCreate  || row.CanCreate,
                    current.CanUpdate  || row.CanUpdate,
                    current.CanDelete  || row.CanDelete,
                    current.CanDownload|| row.CanDownload,
                    current.CanExport  || row.CanExport,
                    current.CanExecute || row.CanExecute);
            }
        }
        return map;
    });

    var normalizedResourceNames = NormalizeControllerNames(resourceName);
    foreach (var name in normalizedResourceNames)
    {
        if (!permissions!.TryGetValue(name, out var flags))
            continue;

        if (explicitFlag is { } requested)
            return EvaluateFlag(flags, requested);

        return actionType.ToUpperInvariant() switch
        {
            "GET" or "HEAD" => flags.CanRead,
            "POST"          => flags.CanCreate,
            "PUT" or "PATCH"=> flags.CanUpdate,
            "DELETE"        => flags.CanDelete,
            _               => false
        };
    }
    return false;
}

private static bool EvaluateFlag(PermissionFlagsSnapshot flags, PermissionFlags flag) => flag switch
{
    PermissionFlags.CanRead     => flags.CanRead,
    PermissionFlags.CanCreate   => flags.CanCreate,
    PermissionFlags.CanUpdate   => flags.CanUpdate,
    PermissionFlags.CanDelete   => flags.CanDelete,
    PermissionFlags.CanDownload => flags.CanDownload,
    PermissionFlags.CanExport   => flags.CanExport,
    PermissionFlags.CanExecute  => flags.CanExecute,
    _ => false
};

private static IReadOnlyCollection<string> NormalizeControllerNames(string controllerName) { /* sin cambios */ }
```

### `src/4.Services.WebApi/Filters/DynamicAuthorizationFilter.cs` (extendido)

```csharp
public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
{
    // 1. Bypass [AllowAnonymous] / [SkipDynamicAuthorization] — sin cambios.
    if (context.ActionDescriptor.EndpointMetadata.Any(em =>
            em is AllowAnonymousAttribute || em is SkipDynamicAuthorizationAttribute))
    {
        return;
    }

    if (context.ActionDescriptor is not ControllerActionDescriptor descriptor) return;

    var permissionResource = ResolvePermissionResourceName(descriptor);
    if (string.IsNullOrWhiteSpace(permissionResource))
    {
        context.Result = new ForbidResult(); // fail-closed
        return;
    }

    var explicitFlag = context.ActionDescriptor.EndpointMetadata
        .OfType<RequirePermissionAttribute>()
        .FirstOrDefault()?.Flag;

    string httpMethod = context.HttpContext.Request.Method;
    var user = context.HttpContext.User;
    var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
    var companyId = user.FindFirst("CompanyId")?.Value;

    if (string.IsNullOrEmpty(userId))
    {
        context.Result = new UnauthorizedResult();
        return;
    }

    if (roles.Any(r => string.Equals(r, SuperAdminRoleName, StringComparison.OrdinalIgnoreCase)))
        return;

    if (string.IsNullOrEmpty(companyId))
    {
        context.Result = new UnauthorizedResult();
        return;
    }

    bool hasAccess = await _permissionService.HasPermissionAsync(
        userId, companyId, permissionResource, httpMethod, explicitFlag);

    if (!hasAccess) context.Result = new ForbidResult();
}

private static string? ResolvePermissionResourceName(ControllerActionDescriptor descriptor)
{
    var actionResource = descriptor.MethodInfo
        .GetCustomAttributes(inherit: true)
        .OfType<PermissionResourceAttribute>()
        .FirstOrDefault()?.ResourceName;

    if (!string.IsNullOrWhiteSpace(actionResource))
        return actionResource;

    var controllerResource = descriptor.ControllerTypeInfo
        .GetCustomAttributes(inherit: true)
        .OfType<PermissionResourceAttribute>()
        .FirstOrDefault()?.ResourceName;

    if (!string.IsNullOrWhiteSpace(controllerResource))
        return controllerResource;

    var controllerName = descriptor.ControllerName;
    if (controllerName.EndsWith("Controller", StringComparison.OrdinalIgnoreCase))
        controllerName = controllerName[..^"Controller".Length];

    return string.IsNullOrWhiteSpace(controllerName) ? null : controllerName;
}
```

---

## Implementation plan

Plan ordenado; cada fase debe compilar y pasar `dotnet test` antes de la siguiente.

### F1 — Modelo de dominio y atributos (sin lógica nueva)

1. Crear `src/1.Domain/Security/PermissionFlags.cs` con el enum `[Flags]`.
2. Crear `src/4.Services.WebApi/Filters/RequirePermissionAttribute.cs` con el atributo que consume `PermissionFlags`.
3. Extender `src/4.Services.WebApi/Filters/PermissionResourceAttribute.cs` con constructor sin parámetros; `ResourceName` pasa a `string?`. Marcar con `// backward compat` el constructor string.
4. Compilar: `dotnet build -c Release`. 0 errores.

### F2 — `PermissionService` extendido

1. En `src/3.Infrastructure/Application/Interface/IPermissionService.cs`, agregar el overload con `PermissionFlags? explicitFlag`.
2. En `src/3.Infrastructure/Security/PermissionService.cs`:
   - Renombrar `PermissionFlags` record struct a `PermissionFlagsSnapshot` (evitar colisión con el nuevo enum).
   - Proyectar los 7 flags en la query EF.
   - Extender el switch con `explicitFlag` precedence.
   - Agregar `EvaluateFlag` helper.
3. **Bump de cache key**: cambiar `permissions:{companyId}:{userId}` → `permissions:v2:{companyId}:{userId}` para invalidar caches preexistentes (4 bool) sin necesidad de flush manual. Documentar el bump en commit message.
4. Compilar + `dotnet test` (Application layer).

### F3 — `DynamicAuthorizationFilter` integrado

1. Extender `ResolvePermissionResourceName` con fallback a `descriptor.ControllerName` (strip "Controller") y fail-closed si vacío.
2. Leer `RequirePermissionAttribute` desde `EndpointMetadata` y pasarlo al service.
3. No tocar bypass ni status codes.
4. Compilar + tests de filter.

### F4 — Tests unitarios

1. `PermissionServiceTests`:
   - `HasPermissionAsync` con `CanRead/Create/Update/Delete/Download/Export/Execute` (mock `RoleSystemOption` + `SystemOption`).
   - Override explícito: `explicitFlag = CanExport` sobre `GET /export` exige `CanExport`, no `CanRead`.
   - HTTP verb fallback: sin `explicitFlag`, `GET` exige `CanRead`.
   - Cache hit: segunda llamada no vuelve a DB.
   - Snapshot/OR merging: rol A con `CanExport=true` + rol B con `CanExport=false` → resultado `true`.
   - `NormalizeControllerNames` ya tiene cobertura; revisar.
2. `DynamicAuthorizationFilterTests`:
   - `[AllowAnonymous]` → filter retorna sin tocar service.
   - `[SkipDynamicAuthorization]` → filter retorna sin tocar service.
   - Rol `SuperAdmin` → filter retorna sin tocar service.
   - Sin `userId` → `UnauthorizedResult`.
   - Sin `CompanyId` → `UnauthorizedResult`.
   - Flag false → `ForbidResult`.
   - Inferencia: controller sin `[PermissionResource]` resuelve a `descriptor.ControllerName` sin "Controller".
   - Multi-attribute: action-level `[PermissionResource("X")]` pisa class-level.
3. Verificar `dotnet test` con `--collect:"XPlat Code Coverage"` y cobertura ≥ 90% en `JOIN.Application` + `DynamicAuthorizationFilter`.

### F5 — Auditoría de drift (no remover literals aún)

1. Generar CSV:
   ```
   controller, [PermissionResource literal], descriptor.ControllerName, SystemOption.ControllerName en DB (seed)
   ```
2. Marcar cada controller:
   - `OK` — literal o class name matchean `SystemOption.ControllerName`.
   - `ALIAS` — literal difiere del class name (p.ej. `PersonContactController` → `"Persons"`). Mantener literal.
   - `DRIFT` — `descriptor.ControllerName` no matchea ningún `SystemOption.ControllerName`. Requiere intervención manual.
3. Esperado: ~24 controllers `OK`, ~8 `ALIAS`, 0 `DRIFT` (verificar).
4. Salida: `specs/17-permissionresource-optional-and-flag-override-drift-audit.csv` (committed) o pegada en PR description.

### F6 — Documentación

1. `CURL_REQUESTS.md`: agregar sección con ejemplos de `[RequirePermission(CanExport)]` aplicado a `GET /api/v1/persons/export` y `[RequirePermission(CanExecute)]` aplicado a `POST /api/v1/persons/run-profile`.
2. `CLAUDE.md`: actualizar la sección sobre Auth & security con el nuevo atributo y el fallback de `PermissionResource`.

### F7 — Verificación final

1. `dotnet build -c Release` → 0 errores.
2. `dotnet test` con coverage → ≥ 90% Application.
3. Arranque manual de la API en Development → `MigrateAsync()` + seed idempotente sin errores.
4. Smoke test con `CURL_REQUESTS.md`: login → token → GET `/persons/{id}` (200) → POST `/persons` (200) → GET `/persons/export` con `CanExport=false` (403) → con `CanExport=true` (200).
5. Antes de merge: PR incluye `dotnet test` verde + link al CSV de auditoría.

---

## Acceptance criteria

- [ ] Existe `src/1.Domain/Security/PermissionFlags.cs` con enum `[Flags]` y los 7 valores (`CanRead / CanCreate / CanUpdate / CanDelete / CanDownload / CanExport / CanExecute`) + `None`.
- [ ] Existe `src/4.Services.WebApi/Filters/RequirePermissionAttribute.cs` con un único ctor `(PermissionFlags flag)`.
- [ ] `PermissionResourceAttribute` expone dos constructores: `()` y `(string resourceName)`. `ResourceName` es `string?`.
- [ ] `[PermissionResource]` sin argumento en un controller `PersonsController` resuelve a `"Persons"` en runtime.
- [ ] `[PermissionResource("Custom")]` sigue funcionando como override (backward compat).
- [ ] `[RequirePermission(CanExport)]` en `GET /export` bloquea roles con `CanExport=false` aunque el método sea GET.
- [ ] Sin `[RequirePermission]` + `GET` → exige `CanRead` (backward compat).
- [ ] `[AllowAnonymous]` en `AuthController.Login` continúa bypasseando el filter.
- [ ] `[SkipDynamicAuthorization]` en `UsersController.GetSidebarMenu` continúa bypasseando el filter.
- [ ] Usuario con rol `SuperAdmin` continúa bypasseando el filter.
- [ ] Request sin `NameIdentifier` claim → `UnauthorizedResult` (401).
- [ ] Request sin `CompanyId` claim → `UnauthorizedResult` (401).
- [ ] Request con token OK + flag `CanExecute=false` sobre endpoint `[RequirePermission(CanExecute)]` → `ForbidResult` (403).
- [ ] `PermissionService.HasPermissionAsync` proyecta los 7 flags en su query EF (verificable con `SqlServer profiler` o assert sobre `DbContext` mockeado).
- [ ] Cache key bump: `permissions:v2:{companyId}:{userId}`. Caches preexistentes no contaminan el nuevo shape.
- [ ] OR-merge entre roles: dos roles `CanExport=true` y `CanExport=false` → resultado `true`.
- [ ] Auditoría `specs/17-...-drift-audit.csv` committed con 0 entries `DRIFT`.
- [ ] `dotnet test` con coverage ≥ 90% en Application layer.
- [ ] `dotnet build -c Release` sin errores ni warnings nuevos.
- [ ] `CURL_REQUESTS.md` documenta el patrón de `[RequirePermission]`.
- [ ] `CLAUDE.md` actualizado con la nueva convención.

---

## Decisions taken and discarded

- **Atributo nuevo `[RequirePermission]` vs. extender `[PermissionResource]` con un enum `RequiredAction`**: se eligió atributo separado porque el dominio del primero es "qué flag exige este endpoint" (per-action), mientras que el dominio del segundo es "qué recurso protege este controller" (per-class o per-action). Mezclar ambos en un solo atributo haría el routing semántico del filter más opaco y obligaría a repetir el resource name en cada action override.
- **Constructor sin parámetros en `PermissionResourceAttribute` vs. atributo totalmente distinto (p.ej. `[AutoPermissionResource]`)**: se eligió sobrecargar el constructor porque (a) el usuario lo pidió como "opción", no como reemplazo, (b) la firma del atributo no cambia significativamente, (c) un nombre nuevo requeriría migrar controllers que sí tienen literal. Backward compat gana.
- **Inefrier nombre desde `descriptor.ControllerName` strip "Controller" vs. mantener `descriptor.ControllerName` tal cual**: se eligió strip porque la convención ASP.NET y los seeds (`SystemOption.ControllerName`) ya omiten el sufijo. Sin strip, `NormalizeControllerNames` recibiría `"PersonsController"` y aún funcionaría (heurística +s/+y/ies), pero el match contra `SystemOption.ControllerName = "Persons"` dependería de la heurística en lugar de ser directo. Más frágil.
- **Fail-closed (ForbidResult) cuando no se puede resolver resource name vs. fail-open (allow)**: fail-closed. La justificación es que un controller sin `[PermissionResource]` y sin nombre inferible es bug, no feature. Permitir silenciosamente abriría una superficie de ataque por olvido.
- **Bump de cache key `permissions:v2:` vs. flush manual en startup**: bump. Razón: el flush manual requiere un `IHostedService` o un `app.Use(...)` adicional en `Program.cs`; el bump es una línea de cambio y se autorresuelve con el absolute expiration de 30 min.
- **Mantener 403 vs. unificar a 401**: se mantiene 403 (semántica RFC). El spec original proponía unificar pero la conversación revisó: 401 = "no autenticado", 403 = "autenticado sin permiso". Mezclar haría indistinguible para el frontend el caso "token expirado" del caso "permiso revocado". Documentado en TS-17.3 por si negocio lo requiere.
- **Auditoría de drift como parte de este spec vs. como ticket aparte**: se eligió mantenerla dentro del spec porque el filtro ya quedó cambiable y la auditoría es pre-requisito lógico para que el filtro no rompa 8 controllers. Sin auditoría, la F4 (tests) pasaría localmente pero prod rompería.
- **No remover los 33 literales `[PermissionResource("...")]` en este spec**: se deja como TS-17.1 porque requiere audit previa por controller y no es bloqueante para el enforcement.

---

## Identified risks

- **Drift silencioso al activar fallback automático**: si F5 (auditoría) no se ejecuta antes de mergear, controllers con alias (`PersonContactController` → `"Persons"`) serán matcheados contra `"PersonContact"` en el resolver, que no existe en `SystemOption.ControllerName`, y el usuario recibirá 403 sin razón aparente. Mitigación: F5 con resultado `0 DRIFT` es criterio de aceptación.
- **Cache cross-version**: durante deploy rolling, instancias nuevas (con `permissions:v2:`) coexisten con instancias viejas (con `permissions:`). Mientras `MemoryCache` es per-instance, no hay impacto. Si después se mueve a `Redis`, el bump debe limpiar el namespace completo. Documentar.
- **Performance de la query EF con 7 columnas**: el query EF proyecta 7 columnas booleanas junto a `ControllerName` y se materializa a `ToListAsync`. En `JOIN-001` con ~30 system options y ~4 roles, el set es < 200 filas. Index `RoleSystemOptions(RoleId, CompanyId, GcRecord)` ya existe. Negligible. Medir con `SqlServer profiler` en F7.
- **`[RequirePermission]` olvida default HTTP verb**: si un desarrollador decora todos los endpoints de un controller con `[RequirePermission(CanRead)]`, no rompe — pero si olvida ponerlo en un endpoint que requiere `CanExport`, el HTTP verb default (`GET` → `CanRead`) lo abrirá. Mitigación: tests negativos (endpoint con `[RequirePermission(CanExport)]` + rol con `CanRead=true, CanExport=false` → 403) en F4.
- **SuperAdmin bypass y nuevos flags**: si SuperAdmin tiene `CanExport=false` en BD (no debería, pero conceivable), el bypass lo ignora. Consistente con el diseño actual (SuperAdmin siempre pasa). Documentar.
- **Cambio de shape del record `PermissionFlags` interno**: el rename a `PermissionFlagsSnapshot` puede romper tests existentes que importen el nombre viejo. Mitigación: el record era `private readonly`, no expuesto. Verificar en F2.
- **Enum `PermissionFlags` en `Domain` vs. `Application`**: si `PermissionFlags` vive en `Domain.Security`, el `RequirePermissionAttribute` en `WebApi` depende de `Domain` (vía `JOIN.Domain.Security`). Aceptable según la regla de dirección de dependencias actual (WebApi ya depende de Application+Infrastructure+Persistence; Domain no tiene deps externos). Sin impacto.
- **Attribute `RequirePermission` con `Multiple = false`**: si un endpoint debiera exigir `CanRead AND CanExport`, el atributo actual no lo expresa. Mitigación: documentar que `RequirePermission` es single-flag; para multi-flag, dejar como TS futuro. Alternativa inmediatamente disponible: usar dos atributos custom (`[RequireAll(CanRead | CanExport)]`), pero fuera de scope.
