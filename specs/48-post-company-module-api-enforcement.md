# SPEC 48-post — Bloqueo de APIs y menú por módulo activo de la empresa (`CompanyModule`)

> **Status:** Borrador
> **Origen:** copia de SPEC 48 de `main` (estado allí al copiarla: Borrador), adaptada a PostgreSQL para el fork `main_postgresql`. Se implementa **desde cero** en el fork, sin traer commits de `main` (decisión del usuario, 2026-10-09).
> **Rama de trabajo:** `spec-48-post-company-module-api-enforcement`, creada desde `main_postgresql`; PR hacia `main_postgresql`, nunca hacia `main`.
> **Lectura en el fork:** dentro de esta spec, toda referencia a una spec de la serie copiada (40–49, 51, 99) se lee como su versión `-post` ("SPEC 41" = SPEC 41-post), y aplican las convenciones de `specs/README.md` → "Serie `-post`". Donde el texto copiado de `main` y la sección "Adaptación a PostgreSQL" difieren, prevalece esta última.
> **Depends on:** SPEC 43-post (`SystemModule.IsBase`, cada `SystemOption` conectada a su módulo real, menú filtrado, solo SuperAdmin habilita módulos).
> **Related:** SPEC 44 (el calendario deja de revisar el módulo por su cuenta), SPEC 45 (endpoints del agente).
> **Date:** 2026-10-09 (copia `-post`; original: 2026-10-01)
> **Objective:** Que una empresa no pueda usar las APIs de un módulo que no tiene activo en `Admin.CompanyModules`, respondiendo `403` con el código `MODULE_NOT_ENABLED`, con un único interruptor de configuración que enciende a la vez el bloqueo de APIs y el filtro del menú, y un endpoint de diagnóstico para revisar las empresas antes de encenderlo.

---

## Adaptación a PostgreSQL (fork `main_postgresql`)

| Tema | En `main` (SQL Server) | En esta versión |
|---|---|---|
| Consulta de módulos por recurso (sección B) | `CASE WHEN sm.IsActive = 1 AND cm.Id IS NOT NULL THEN 1 ELSE 0 END` | Expresión booleana directa `(sm.isactive AND cm.id IS NOT NULL) AS isenabled` y `= TRUE` (abajo). |
| `GetCompanyModulesDiagnosticsQuery` (sección F) | Dapper | Dapper en sintaxis PostgreSQL: booleanos `TRUE`/`FALSE`, `COUNT(DISTINCT ...)`, nombres en minúsculas; `EXPLAIN` contra la base migrada. |
| Cache e interruptor | `IMemoryCache`, `IOptions` | Igual (independiente del motor). |
| Tests | Integración sobre `CustomWebApplicationFactory` | `ModuleEnforcementPostgreSqlTests` sobre `PostgreSqlWebApplicationFactory`. El interruptor se enciende con `Modules__EnforceCompanyModules=true`; si la factory no admite configuración por clase de test, se agrega una factory derivada con esa variable. |

Consulta de la sección B en el fork:

```sql
SELECT so.controllername AS ControllerName,
       sm.name           AS ModuleName,
       (sm.isactive AND cm.id IS NOT NULL) AS IsEnabled
FROM security.systemoptions so
INNER JOIN admin.systemmodules sm ON sm.id = so.moduleid AND sm.gcrecord = 0
LEFT JOIN admin.companymodules cm ON cm.moduleid = sm.id AND cm.companyid = @CompanyId
                                 AND cm.isactive = TRUE AND cm.gcrecord = 0
WHERE so.gcrecord = 0 AND so.controllername IS NOT NULL AND so.controllername <> ''
```

Verificación con Docker (convención 12 del README): con `Modules__EnforceCompanyModules=true`, desactivar `Tickets` para la empresa privada → `GET /api/v1/Tickets` de un Manager responde 403 `MODULE_NOT_ENABLED`; reactivarlo → 200 sin reiniciar.

---

## Por qué existe esta spec

Con la SPEC 43, cada opción de menú queda conectada a su módulo y el menú se filtra por los módulos activos de la empresa. Pero las **APIs** siguen abiertas: `DynamicAuthorizationFilter` solo pregunta a `PermissionService` si el rol del usuario tiene el permiso sobre el recurso (`SystemOption.ControllerName`), sin mirar si la empresa tiene el módulo. Un usuario que conozca la URL puede usar un módulo que su empresa no contrató, si su rol tiene el permiso.

Hasta ahora, el Calendario (SPEC 44) resolvía esto por su cuenta con `CalendarModuleGuard`. Esta spec lo resuelve para **todos** los módulos en un solo lugar.

## Decisiones acordadas (2026-10-01)

| # | Decisión |
|---|---|
| B1 | Respuesta `403` con ProblemDetails y código **`MODULE_NOT_ENABLED`**, que incluye el nombre del módulo. El frontend puede mostrar "Tu empresa no tiene habilitado el módulo X" en lugar de un "sin permiso" genérico. |
| B2 | El **SuperAdmin no se bloquea**: sigue sin restricción, igual que su menú (SPEC 43 A8). |
| B3 | El Calendario **quita** su chequeo propio de módulo. Su guard se queda solo con lo que es del calendario: exigir `CalendarCompany` y devolver su contexto. Se ajusta la SPEC 44. |
| B4 | El bloqueo se enciende con un **interruptor de configuración**. Se despliega apagado, se revisan las empresas con un endpoint de diagnóstico y se enciende cuando todo está en orden. |
| B5 | **Un solo interruptor** controla el bloqueo de APIs **y** el filtro del menú de la SPEC 43. Encendido: menú filtrado y APIs bloqueadas. Apagado: todo como hoy. Menú y APIs nunca se contradicen. |

---

## Scope

**In:**

### A. Configuración

- `src/2.Application/Common/Settings/ModuleEnforcementSettings.cs` (nuevo), sección `"Modules"`:

```csharp
/// <summary>
/// Controls whether the system enforces Admin.CompanyModules: when true, a company can only see in the menu
/// and call the APIs of modules that are active for it (and active globally). When false, CompanyModules is
/// ignored for both menu and APIs (behavior before SPEC 48). SuperAdmin is never restricted.
/// Deploy with false, review GET /CompanyModules/diagnostics, then switch to true.
/// </summary>
public sealed class ModuleEnforcementSettings
{
    public const string SectionName = "Modules";
    public bool EnforceCompanyModules { get; init; }
}
```

- `appsettings.json`: `"Modules": { "EnforceCompanyModules": false }`. Se registra con `IOptions` como las demás settings (`PerformanceSettings`, `MfaOptions`).

### B. Resolución del módulo de un recurso

- `IPermissionService` agrega:

```csharp
/// <summary>
/// Resolves whether the module(s) that own a permission resource are enabled for the company.
/// A resource belongs to the modules of the SystemOptions whose ControllerName matches it
/// (same normalization as HasPermissionAsync). It is enabled if ANY of those modules is active
/// globally (SystemModule.IsActive) and for the company (CompanyModule.IsActive, not deleted).
/// A resource with no SystemOption is not module-scoped and returns Enabled.
/// </summary>
Task<ModuleAccessResult> GetModuleAccessAsync(Guid companyId, string resourceName, CancellationToken ct = default);
```

- `ModuleAccessResult` (en `2.Application/Interface`): `IsEnabled` (bool) y `ModuleName` (string?, el primer módulo dueño del recurso, para el mensaje de error).
- Implementación en `PermissionService` con cache por empresa: `company-modules:v1:{companyId}`, un diccionario `recurso normalizado → (módulos dueños, alguno habilitado)`. Se arma con una consulta:

```sql
SELECT so.controllername AS ControllerName,
       sm.name           AS ModuleName,
       (sm.isactive AND cm.id IS NOT NULL) AS IsEnabled
FROM security.systemoptions so
INNER JOIN admin.systemmodules sm ON sm.id = so.moduleid AND sm.gcrecord = 0
LEFT JOIN admin.companymodules cm ON cm.moduleid = sm.id AND cm.companyid = @CompanyId
                                 AND cm.isactive = TRUE AND cm.gcrecord = 0
WHERE so.gcrecord = 0 AND so.controllername IS NOT NULL AND so.controllername <> ''
```

  Mismos tiempos de expiración que el cache de permisos (30 minutos absolutos, 10 deslizantes).

### C. `DynamicAuthorizationFilter`

Nuevo paso **después** del bypass de SuperAdmin y del chequeo de `CompanyId`, y **antes** de `HasPermissionAsync`:

```csharp
if (_moduleSettings.Value.EnforceCompanyModules)
{
    var access = await _permissionService.GetModuleAccessAsync(parsedCompanyId, permissionResource, ct);
    if (!access.IsEnabled)
    {
        context.Result = new ObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Module not enabled",
            Detail = $"The company does not have the module '{access.ModuleName}' enabled.",
            Extensions = { ["code"] = "MODULE_NOT_ENABLED", ["module"] = access.ModuleName }
        }) { StatusCode = StatusCodes.Status403Forbidden };
        return;
    }
}
```

- El chequeo de módulo va antes del de permiso. Así, un usuario sin el módulo recibe siempre `MODULE_NOT_ENABLED`, tenga o no el permiso, y el mensaje es el mismo para todos los roles.
- Los endpoints `[AllowAnonymous]` y `[SkipDynamicAuthorization]` no pasan por este chequeo, igual que hoy no pasan por el de permisos.
- `instance` y `traceId` los completa la configuración global de ProblemDetails que ya existe en `Program.cs`.
- Con el interruptor apagado, el filtro se comporta exactamente como hoy.

### D. Menú lateral (ajuste a SPEC 43)

- El filtro por `SystemModules`/`CompanyModules` que agrega SPEC 43 en `GetSidebarMenuQueryHandler` se aplica **solo** si `EnforceCompanyModules = true`. Con el interruptor apagado, el menú se arma como hoy (B5).
- El SuperAdmin sigue viendo todo en ambos casos.

### E. Invalidación de cache

- Al crear, actualizar (activar o desactivar) o borrar un `CompanyModule`: se elimina `company-modules:v1:{companyId}` y, como ya define SPEC 43, el cache de menú y permisos de los usuarios de esa empresa.
- Al cambiar `SystemModule.IsActive` o el `ModuleId`/`ControllerName` de una `SystemOption` (SuperAdmin), el cambio afecta a todas las empresas. `IMemoryCache` no permite borrar por prefijo, así que se usa un **token de versión global**: `PermissionService` guarda un `CancellationTokenSource` compartido, todas las entradas `company-modules:*` se crean con `AddExpirationToken(...)`, y esos handlers llaman a `IPermissionService.InvalidateModuleCacheForAllCompanies()`, que cancela el token y crea uno nuevo.
- Cambiar el valor del interruptor requiere reiniciar la API (se lee con `IOptions`, no `IOptionsMonitor`): es un cambio de despliegue, no de operación diaria.

### F. Endpoint de diagnóstico

`GET /api/v1/CompanyModules/diagnostics` — `[Authorize(Roles = "SuperAdmin")]`.

```jsonc
{
  "enforceCompanyModules": false,
  "baseModules": ["Administration", "Customers", "Tickets", "Security"],
  "companies": [
    {
      "companyId": "guid",
      "companyName": "Empresa X",
      "missingBaseModules": ["Tickets"],                 // módulos base sin fila activa en CompanyModules
      "usersWithPermissionsInDisabledModules": 3          // usuarios con RoleSystemOptions en módulos inactivos de la empresa
    }
  ]
}
```

- Solo lista empresas activas con al menos un problema; si la lista viene vacía, se puede encender el interruptor sin cortar accesos a módulos base.
- `usersWithPermissionsInDisabledModules` indica a cuántos usuarios les cambiaría la experiencia al encender el bloqueo (verían `MODULE_NOT_ENABLED` donde hoy entran).
- Es solo lectura: completar los módulos que falten se hace a mano desde `CompanyModules` (SuperAdmin), porque la asignación automática es una etapa futura (SPEC 43 A6).

### G. Ajustes a SPEC 44 y SPEC 45

- **SPEC 44:** `CalendarModuleGuard` pasa a llamarse **`CalendarCompanyGuard`**. Ya no consulta `CompanyModule` ni devuelve `CALENDAR_MODULE_NOT_ENABLED`; solo exige `CalendarCompany` (`CALENDAR_COMPANY_NOT_CONFIGURED`) y devuelve su contexto. La regla R1 y los tests del guard se ajustan. El bloqueo por módulo lo hace el filtro global de esta spec.
- `CalendarUserProvisioner` (SPEC 44) **sigue** revisando si el módulo Calendar está activo antes de crear calendarios. No es una regla de autorización: decide si crear datos, y se ejecuta desde `InviteUser`/`AddUserCompany`, que no son endpoints del calendario.
- **SPEC 45:** las operaciones del agente pasan por el filtro global (recurso `CalendarChannelIntake`, módulo `Calendar`) y por `CalendarCompanyGuard`. `GET /CalendarChannelIntake/companies` (`[SkipDynamicAuthorization]`) sigue filtrando en su consulta las empresas con el módulo Calendar activo.

### H. Tests

- **Filtro (unitarios o integración liviana):** interruptor apagado → sin cambios de comportamiento; encendido + módulo activo → pasa al chequeo de permisos; encendido + módulo inactivo para la empresa → `403 MODULE_NOT_ENABLED` con el nombre del módulo, aunque el rol tenga el permiso; módulo inactivo globalmente (`SystemModule.IsActive = false`) → `403`; SuperAdmin con módulo inactivo → pasa; recurso sin `SystemOption` → no se bloquea por módulo; recurso presente en dos módulos con uno activo → pasa; `[SkipDynamicAuthorization]` → no se evalúa.
- **`GetModuleAccessAsync`:** normalización de nombres (mismos casos que `HasPermissionAsync`), cache por empresa, invalidación por empresa y global.
- **Menú:** con el interruptor apagado no filtra; encendido filtra (complementa los tests de SPEC 43).
- **Diagnóstico:** empresa sin un módulo base aparece; empresa completa no aparece; conteo de usuarios afectados; solo SuperAdmin.
- **Integración:** con el interruptor encendido, un usuario `Manager` de una empresa con `Tickets` desactivado recibe `403 MODULE_NOT_ENABLED` en `GET /api/v1/Tickets`, y al reactivarlo vuelve a `200` sin reiniciar (invalidación de cache).

**Out of scope:**

- Asignar automáticamente módulos base a empresas nuevas o existentes (SPEC 43 A6, etapa futura). El diagnóstico solo informa.
- Cambiar el interruptor en caliente sin reiniciar (`IOptionsMonitor`).
- Bloquear por módulo los endpoints `[AllowAnonymous]` o `[SkipDynamicAuthorization]`.
- Mensajes traducidos en el frontend (se definen en las specs del front).

---

## Implementation plan

### F1 — Configuración y contrato
`ModuleEnforcementSettings`, sección en `appsettings.json`, registro en DI, `ModuleAccessResult` y el método nuevo de `IPermissionService`.

### F2 — `PermissionService`
`GetModuleAccessAsync` con su cache por empresa, el token de versión global e `InvalidateModuleCacheForAllCompanies`.

### F3 — Filtro
Paso nuevo en `DynamicAuthorizationFilter` (sección C).

### F4 — Menú e invalidación
Condicionar el filtro de SPEC 43 al interruptor. Invalidación en `CompanyModules`, `SystemModules` y `SystemOptions` (sección E).

### F5 — Diagnóstico
`GetCompanyModulesDiagnosticsQuery` (Dapper) y endpoint en `CompanyModulesController`.

### F6 — SPEC 44 / 45
Renombrar y simplificar el guard del calendario en las specs (y en el código, si la SPEC 44 ya está implementada).

### F7 — Tests y verificación
Tests de la sección H. `dotnet build` sin warnings nuevos, `dotnet test` con el gate de 90%. En Development: con el interruptor apagado, todo igual; con el interruptor encendido, desactivar `Tickets` para la empresa privada y verificar `403 MODULE_NOT_ENABLED` y el menú sin tickets para un usuario no SuperAdmin. Detener la API al terminar.

---

## Acceptance criteria

- [ ] Con `Modules:EnforceCompanyModules = false`, menú y APIs se comportan exactamente como antes de esta spec.
- [ ] Con el interruptor encendido, ningún usuario no SuperAdmin puede usar las APIs de un módulo inactivo para su empresa o inactivo globalmente: recibe `403` con `code = "MODULE_NOT_ENABLED"` y el nombre del módulo.
- [ ] Con el interruptor encendido, el menú no muestra opciones de módulos inactivos, y nunca muestra una opción cuya API respondería `MODULE_NOT_ENABLED`.
- [ ] El SuperAdmin no se ve afectado en ningún caso.
- [ ] Activar o desactivar un módulo de una empresa se refleja en APIs y menú sin esperar a que venza el cache.
- [ ] `GET /CompanyModules/diagnostics` lista las empresas con módulos base faltantes y los usuarios afectados; solo lo usa el SuperAdmin.
- [ ] El Calendario ya no tiene un chequeo propio de módulo; `CALENDAR_MODULE_NOT_ENABLED` no existe.

---

## Decisions taken and discarded

- **Chequeo de módulo en el filtro global** (elegido) vs que cada módulo lo haga en sus handlers. Un solo lugar, la misma respuesta para todos los módulos y cero código repetido; es lo que la SPEC 44 dejó anotado como pendiente.
- **Módulo antes que permiso** (elegido): el mensaje correcto para "tu empresa no tiene el módulo" no depende del rol del usuario.
- **Un recurso habilitado si alguno de sus módulos está activo** (elegido): un mismo `ControllerName` puede aparecer en opciones de distintos módulos; bloquearlo si uno de ellos está inactivo cortaría accesos legítimos del otro.
- **Recurso sin `SystemOption` = no limitado por módulo** (elegido): el chequeo de permisos ya lo rechaza (no hay `RoleSystemOption` posible), así que bloquearlo también por módulo no agrega nada.
- **Interruptor único para menú y APIs** (B5) vs controles separados: evita estados en los que el menú muestra algo que la API rechaza, o al revés.
- **Token de versión global para el cache de módulos** (elegido) vs recorrer empresas: `IMemoryCache` no permite borrar por prefijo, y los cambios globales de módulos son poco frecuentes.

---

## Identified risks

| Riesgo | Mitigación |
|---|---|
| Encender el interruptor corta el acceso de empresas a las que les falta un módulo base en `CompanyModules`. | Se despliega apagado (B4). El diagnóstico (sección F) muestra las empresas y usuarios afectados; se completan a mano antes de encender. |
| Una opción de menú mal asociada a su módulo (si falló la corrección de SPEC 43) bloquea un recurso que debería estar habilitado. | La SPEC 43 corrige el `ModuleId` de todas las opciones en la seed y su prueba de integración lo verifica. El diagnóstico se revisa antes de encender. |
| El cache de módulos queda desactualizado en un despliegue con varias instancias (cache en memoria por instancia). | Mismo límite que ya tiene el cache de permisos (`permissions:v2`), con la misma expiración. Si el sistema pasa a varias instancias, ambos caches se mueven a un cache distribuido en una spec aparte. |
| Con el interruptor apagado, el Calendario ya no tiene control de módulo (B3). | Sigue protegido por permisos: fuera de `JOIN-001` no hay `RoleSystemOptions` del calendario hasta que se asignen a mano (SPEC 44). Encender el interruptor es el paso previo a usar el calendario como módulo contratable. |
