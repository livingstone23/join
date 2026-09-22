# SPEC 33 — Centralización de `PaginationSettings` y su uso en todos los query handlers

> **Status:** Draft
> **Depends on:** Ninguna spec previa. Es un refactor sobre código ya existente: `PaginationSettings` (`src/2.Application/Common/PaginationSettings.cs`), la sección `"AreaPagination"` de `appsettings.json` y los ~37 query handlers/validators que hoy pagina la API.
> **Date:** 2026-09-22
> **Objective:** Que exista una única fuente de verdad (`PaginationSettings`, sección de configuración `"Pagination"`) para los límites de paginación (`DefaultPageNumber`/`DefaultPageSize`/`MaxPageSize`/`MinPageSize`) y una lógica de saneamiento compartida (`Sanitize`), migrando a ella los handlers/validators que hoy hardcodean sus propias constantes y retrofiteando los que ya inyectan `PaginationSettings` pero repiten el clamping inline.

---

## Por qué existe esta spec

Se auditó el código real (no solo el nombre de la clase) antes de escribir esto:

- **`PaginationSettings`** (`src/2.Application/Common/PaginationSettings.cs`) ya existe, con `DefaultPageNumber=1`, `DefaultPageSize=10`, `MaxPageSize=50`. Se registra en `Program.cs:112-113` bajo la sección `"AreaPagination"` (nombre heredado — hoy la usan Tickets, Roles, Companies, TimeUnits, etc., no solo Areas).
- **24 query handlers** ya inyectan `IOptions<PaginationSettings>`, pero cada uno repite su propia versión del clamping inline (ver `GetAreasQueryHandler.cs:44-49`) — no hay un método compartido.
- **13 query handlers/helpers** (`GetPersonsPagedQueryHandler`, `GetCustomersPagedQueryHandler`, `GetCompaniesPagedQueryHandler`, `GetCountriesPagedQueryHandler`, `GetCommunicationChannelsPagedQueryHandler`, `GetStreetTypesPagedQueryHandler`, `GetRolesDetailedQueryHandler`, `GetUsersByRoleIdQueryHandler`, `GetRoleCompaniesPagedQueryHandler`, `GetSecurityActivityQueryHandler`, `GetSecurityAuditLogQueryHandler`, `GetMyCompanyUserReportQueryHandler`, `UserManagementReportQueryHelper`) **no usan `PaginationSettings` en absoluto** — definen sus propias constantes locales (`MaxPageSize = 50` o `100`, `DefaultPageSize = 10` o `20`, algunos con `MinPageSize = 1` separado), con valores que divergen entre sí y de `PaginationSettings`.
- **2 validators** (`GetSecurityActivityQueryValidator`, `GetSecurityAuditLogQueryValidator`) hardcodean `RuleFor(q => q.PageSize).InclusiveBetween(1, 100)` — rechazan en vez de clampear, con un `100` que no viene de ningún lado configurable.
- Hay un cambio **sin commitear** en el working tree (`GetPersonsPagedQueryHandler.cs`: `MaxPageSize` local `50 → 100`) que es exactamente el síntoma del problema: alguien ajustando a mano una constante duplicada en vez de tocar una configuración central.

---

## Scope

**In:**

1. Agregar `MinPageSize` (default `1`) a `PaginationSettings` y un método `Sanitize(int? pageNumber, int? pageSize)` que devuelve `(int PageNumber, int PageSize)`, con las mismas reglas que ya usa `GetAreasQueryHandler` (auto-corrige configuración inconsistente: si `MaxPageSize < DefaultPageSize` o `DefaultPageSize < MinPageSize`, no rompe, se autocorrige a un valor válido).
2. Renombrar la sección de configuración `"AreaPagination"` → `"Pagination"` en `Program.cs` y `appsettings.json`, agregando `"MinPageSize": 1`.
3. Migrar los 13 handlers/helper que hoy hardcodean constantes propias para que inyecten `IOptions<PaginationSettings>` y llamen a `Sanitize`, eliminando sus constantes locales.
4. Retrofitear los 24 handlers que ya inyectan `PaginationSettings` para que reemplacen su clamping inline por una llamada a `Sanitize` (refactor puro, sin cambio de comportamiento).
5. Actualizar `GetSecurityActivityQueryValidator` y `GetSecurityAuditLogQueryValidator` para que inyecten `IOptions<PaginationSettings>` y validen `PageSize` contra `MinPageSize`/`MaxPageSize` dinámicos en vez del `100` hardcodeado.
6. Unificar el límite/­default efectivo de **toda** la API a un único valor global: `DefaultPageNumber=1`, `DefaultPageSize=10`, `MaxPageSize=50`, `MinPageSize=1` (los valores que ya tiene `PaginationSettings` hoy). Esto es un cambio de comportamiento observable para los endpoints que hoy permiten hasta `100` (`GetPersonsPaged`, `GetRolesDetailed`, `GetUsersByRoleId`, `GetRoleCompaniesPaged`, `GetSecurityActivity`, `GetSecurityAuditLog`) y para los que tienen `DefaultPageSize=20` (`GetRolesDetailed`, `GetUsersByRoleId`, `GetRoleCompaniesPaged`) — confirmado, ver Decisiones.
7. El cambio sin commitear en `GetPersonsPagedQueryHandler.cs` (`MaxPageSize` local `50→100`) queda superado: al migrar el handler, la constante local desaparece por completo.
8. Actualizar todos los tests unitarios afectados (handlers migrados, handlers retrofit, y los 2 validators) para que inyecten `Options.Create(new PaginationSettings {...})` explícito donde corresponda y para que las aserciones reflejen los nuevos valores globales donde cambiaron.
9. Nuevo archivo de tests para `PaginationSettings.Sanitize` cubriendo los casos borde (page size negativo, cero, `null`, mayor a `MaxPageSize`, configuración inconsistente).

**Out of scope:**

- No se agregan nuevos endpoints ni nuevos parámetros de query — el contrato (`pageNumber`/`pageSize` en la request, `PagedResult<T>` en la response) no cambia.
- No se implementa override de límites por feature/endpoint (`"Pagination:Roles"` etc.) — se descartó explícitamente a favor de un único valor global.
- No se toca el frontend ni `CURL_REQUESTS.md`/`POSTMAN_CURL.txt` (no hay endpoints ni parámetros nuevos que documentar).
- No se agregan nuevos `SecurityEventType` ni instrumentación de auditoría.
- No se modifica el umbral de cobertura de CI (`Threshold=90`) ni el workflow de `.github/workflows/ci.yml`.
- No se buscan ni actualizan referencias a `AreaPagination` en pipelines/`.env`/`docker-compose.yml` externos a este repo — solo se audita dentro de este repo (ver Riesgos).

---

## Data model

No se introducen entidades ni tablas nuevas. Cambia únicamente la clase de configuración:

```csharp
// src/2.Application/Common/PaginationSettings.cs (MODIFICADO)
public class PaginationSettings
{
    public int DefaultPageNumber { get; set; } = 1;
    public int DefaultPageSize { get; set; } = 10;
    public int MaxPageSize { get; set; } = 50;

    /// <summary>Tamaño de página mínimo permitido. Nuevo.</summary>
    public int MinPageSize { get; set; } = 1;

    /// <summary>
    /// Sanea un pedido de paginación contra estos límites: pageNumber &lt; 1 (o null) usa
    /// DefaultPageNumber; pageSize &lt; MinPageSize (o null) usa DefaultPageSize; pageSize
    /// se topa a MaxPageSize. Si la configuración es inconsistente (MaxPageSize &lt; MinPageSize,
    /// o DefaultPageSize fuera de [MinPageSize, MaxPageSize]) se autocorrige antes de aplicar,
    /// igual que ya hace GetAreasQueryHandler hoy.
    /// </summary>
    public (int PageNumber, int PageSize) Sanitize(int? pageNumber, int? pageSize)
    {
        var minPageSize = MinPageSize < 1 ? 1 : MinPageSize;
        var maxPageSize = MaxPageSize < minPageSize ? minPageSize : MaxPageSize;
        var defaultPageSize = DefaultPageSize < minPageSize
            ? minPageSize
            : Math.Min(DefaultPageSize, maxPageSize);
        var defaultPageNumber = DefaultPageNumber < 1 ? 1 : DefaultPageNumber;

        var sanitizedPageNumber = pageNumber.GetValueOrDefault(defaultPageNumber);
        sanitizedPageNumber = sanitizedPageNumber < 1 ? defaultPageNumber : sanitizedPageNumber;

        var requestedPageSize = pageSize.GetValueOrDefault(defaultPageSize);
        var sanitizedPageSize = requestedPageSize < minPageSize
            ? defaultPageSize
            : Math.Min(requestedPageSize, maxPageSize);

        return (sanitizedPageNumber, sanitizedPageSize);
    }
}
```

```json
// appsettings.json (RENOMBRADO de "AreaPagination" a "Pagination", + MinPageSize nuevo)
"Pagination": {
  "DefaultPageNumber": 1,
  "DefaultPageSize": 10,
  "MaxPageSize": 50,
  "MinPageSize": 1
}
```

```csharp
// Program.cs (RENOMBRADO)
var paginationSection = builder.Configuration.GetSection("Pagination");
builder.Services.Configure<PaginationSettings>(paginationSection);
```

`UserManagementReportQueryHelper` es un helper **estático** (no tiene DI propia) usado por `GetMyCompanyUserReportQueryHandler` y `GetSystemWideUserReportQueryHandler`. Sigue el mismo molde que `RoleSystemOptionQuerySql.cs` (que ya recibe `PaginationSettings` como parámetro, no vía `IOptions`): su método pasa a aceptar `PaginationSettings paginationSettings` como parámetro, provisto por cada handler desde su propio `IOptions<PaginationSettings>` inyectado. Se eliminan sus constantes `MaxPageSize`/`DefaultPageSize` estáticas.

---

## Implementation plan

**F1 — Extender `PaginationSettings`.** Agregar `MinPageSize` y el método `Sanitize` (ver Data model), con tests unitarios propios cubriendo los casos borde.

**F2 — Renombrar la sección de configuración.** `Program.cs` (`"AreaPagination"` → `"Pagination"`) y `appsettings.json` (agregar `MinPageSize: 1`). `appsettings.Development.json` no tiene overrides de esta sección hoy — no requiere cambios.

**F3 — Migrar los 13 handlers/helper hardcodeados** (uno por uno, ver Archivos críticos): inyectar `IOptions<PaginationSettings>`, reemplazar el `Math.Min`/ternario local por `_paginationSettings.Sanitize(...)`, eliminar las constantes locales (`MaxPageSize`, `DefaultPageSize`, `MinPageSize` donde exista).

**F4 — Retrofit de los 24 handlers que ya usan `PaginationSettings`.** Reemplazar su clamping inline por `_paginationSettings.Sanitize(...)`. Refactor puro: mismos valores de configuración, mismo comportamiento observable, solo cambia la implementación interna.

**F5 — Actualizar los 2 validators.** `GetSecurityActivityQueryValidator` y `GetSecurityAuditLogQueryValidator` inyectan `IOptions<PaginationSettings>` por constructor y usan `.InclusiveBetween(settings.MinPageSize, settings.MaxPageSize)` en vez del `InclusiveBetween(1, 100)` hardcodeado.

**F6 — Actualizar tests de los 13 handlers migrados + 2 validators.** Ajustar las aserciones a los nuevos valores globales donde el límite efectivo cambió (100→50, o default 20→10), e inyectar `Options.Create(new PaginationSettings { ... })` explícito en el arrange de cada test en vez de depender de las constantes eliminadas.

**F7 — Verificar tests de los 24 handlers retrofit.** Deben seguir pasando sin cambios de aserciones — es refactor puro (F4). Si algún test rompe, es señal de que `Sanitize` no replica exactamente la lógica que tenía ese handler; corregir `Sanitize` antes de tocar el test.

**F8 — Verificación final.**
- `dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:Threshold=90 /p:ThresholdType=line` pasa.
- `grep -rn "MaxPageSize = \|DefaultPageSize = \|MinPageSize = " src/2.Application` no devuelve ninguna constante local de paginación (todas fueron eliminadas).
- Prueba manual con curl sobre al menos un endpoint migrado (ej. `GET /api/v1/persons?pageSize=500&pageNumber=1`) confirma que la respuesta trae `pageSize: 50`, no `500`.

---

## Acceptance criteria

- [ ] `PaginationSettings` expone `DefaultPageNumber`, `DefaultPageSize`, `MaxPageSize`, `MinPageSize` y el método `Sanitize`, con tests unitarios propios en `tests/UnitTests/JOIN.Application.UnitTest/Common/PaginationSettingsTests.cs`.
- [ ] `appsettings.json` tiene la sección `"Pagination"` (ya no existe `"AreaPagination"` en ningún archivo del repo).
- [ ] `grep -rn "private const int MaxPageSize\|private const int DefaultPageSize\|public const int MaxPageSize\|public const int DefaultPageSize" src/2.Application` no devuelve resultados.
- [ ] Los 13 handlers/helper migrados y los 24 retrofit invocan `_paginationSettings.Sanitize(...)` (o reciben `PaginationSettings` como parámetro, caso `UserManagementReportQueryHelper`).
- [ ] `GetSecurityActivityQueryValidator` y `GetSecurityAuditLogQueryValidator` rechazan `PageSize` fuera de `[MinPageSize, MaxPageSize]` leído de `PaginationSettings`, no de un literal `100`.
- [ ] Un request con `pageSize=1000` a cualquiera de los 37 endpoints paginados devuelve `pageSize` igual a `MaxPageSize` configurado (50 por defecto), nunca `1000`.
- [ ] `GetPersonsPaged`, `GetRolesDetailed`, `GetUsersByRoleId`, `GetRoleCompaniesPaged`, `GetSecurityActivity`, `GetSecurityAuditLog` ahora topan en `50` (antes `100`) — confirmado como cambio de comportamiento esperado.
- [ ] El archivo `GetPersonsPagedQueryHandler.cs` ya no tiene ninguna constante local `MaxPageSize`/`DefaultPageSize` (el diff pendiente sin commitear queda absorbido por la migración).
- [ ] `dotnet test` con el coverage gate de CI (`Threshold=90`) pasa sobre `JOIN.Application.UnitTest`.

---

## Decisiones

- **Confirmado:** alcance incluye migrar los 13 handlers hardcodeados **y** retrofitear los 24 que ya usan `PaginationSettings`, para que todos compartan la misma lógica de saneamiento (`Sanitize`), no solo la misma configuración.
- **Confirmado:** renombrar la sección de configuración `"AreaPagination"` → `"Pagination"`. Es config de servidor, no rompe contratos de API; el riesgo de despliegues que la sobreescriban por variable de entorno queda documentado en Riesgos.
- **Confirmado:** un único valor global (`DefaultPageSize=10`, `MaxPageSize=50`) para todos los endpoints paginados, en vez de overrides por feature. Implica reducir el tope de 6 endpoints que hoy permiten hasta 100 registros por página.
- **Confirmado:** agregar `MinPageSize` a `PaginationSettings` y centralizar el clamping en un método `Sanitize` compartido, en vez de que cada handler seguir repitiendo su propio ternario/`Math.Clamp`.
- **Confirmado:** `GetSecurityActivityQueryValidator`/`GetSecurityAuditLogQueryValidator` pasan a validar contra `MaxPageSize`/`MinPageSize` dinámicos vía `IOptions<PaginationSettings>` inyectado, en vez de su `InclusiveBetween(1, 100)` hardcodeado.
- **Confirmado:** el cambio sin commitear en `GetPersonsPagedQueryHandler.cs` (`MaxPageSize` local `50→100`) no se preserva como excepción — queda reemplazado por el valor global al eliminarse la constante local en F3.

---

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Bajar `MaxPageSize` de 100 a 50 en `Persons`, `RolesDetailed`, `UsersByRoleId`, `RoleCompaniesPaged`, `SecurityActivity`, `SecurityAuditLog` puede afectar clientes que ya piden `pageSize=100` — recibirán 50 en vez de 100, sin error, silenciosamente. | Avisar al equipo de frontend antes de mergear F3/F6; si algún consumidor depende de 100, es una señal para reconsiderar el valor global antes de mergear (no después). |
| Bajar `DefaultPageSize` de 20 a 10 en 3 endpoints implica más llamadas para traer el mismo listado si el cliente no manda `pageSize` explícito. | Mismo aviso que el punto anterior; el listado sigue siendo correcto, solo más paginado. |
| Renombrar `"AreaPagination"` → `"Pagination"` rompe cualquier despliegue que sobreescriba esa clave por variable de entorno (`AreaPagination__MaxPageSize`) fuera de este repo. | Buscar `AreaPagination` en `docker-compose.yml`, `.env*` y cualquier pipeline dentro de este repo antes de mergear F2; fuera del repo, comunicar el rename a quien administre los despliegues. |
| Tocar 37 handlers + 2 validators en un solo cambio es una superficie grande para el gate de cobertura del 90% — un solo handler mal migrado puede tirar CI. | Migrar y correr `dotnet test` incrementalmente por área (Admin, Common, Security, Messaging) durante la implementación, aunque el resultado final sea un solo PR. |
| `Sanitize` debe replicar exactamente el comportamiento de cada uno de los 24 handlers retrofit (F4) para no romper sus tests existentes — pequeñas diferencias (ej. orden de clamping, manejo de `null` vs `0`) ya variaban sutilmente entre handlers antes de esta spec. | F7 exige correr los tests existentes de los 24 handlers sin tocar sus aserciones; cualquier test que rompa se resuelve ajustando `Sanitize`, nunca relajando el test. |

---

## Archivos críticos

### Modify — Común / configuración

- `src/2.Application/Common/PaginationSettings.cs` — agrega `MinPageSize` + método `Sanitize`.
- `src/4.Services.WebApi/Program.cs` — renombra la sección `"AreaPagination"` → `"Pagination"`.
- `src/4.Services.WebApi/appsettings.json` — renombra la sección, agrega `MinPageSize`.

### Modify — 13 handlers/helper a migrar (hoy hardcodeados)

- `src/2.Application/UseCases/Admin/Persons/Queries/GetPersonsPaged/GetPersonsPagedQueryHandler.cs`
- `src/2.Application/UseCases/Admin/Customers/Queries/GetCustomersPaged/GetCustomersPagedQueryHandler.cs`
- `src/2.Application/UseCases/Common/Companies/Queries/GetCompaniesPaged/GetCompaniesPagedQueryHandler.cs`
- `src/2.Application/UseCases/Common/Countries/Queries/GetCountriesPaged/GetCountriesPagedQueryHandler.cs`
- `src/2.Application/UseCases/Common/CommunicationChannels/Queries/GetCommunicationChannelsPaged/GetCommunicationChannelsPagedQueryHandler.cs`
- `src/2.Application/UseCases/Common/StreetTypes/Queries/GetStreetTypesPaged/GetStreetTypesPagedQueryHandler.cs`
- `src/2.Application/UseCases/Security/Roles/Queries/GetRolesDetailed/GetRolesDetailedQueryHandler.cs`
- `src/2.Application/UseCases/Security/Roles/Queries/GetUsersByRoleId/GetUsersByRoleIdQueryHandler.cs`
- `src/2.Application/UseCases/Security/RoleCompanies/Queries/GetRoleCompaniesPaged/GetRoleCompaniesPagedQueryHandler.cs`
- `src/2.Application/UseCases/Security/Account/Queries/GetSecurityActivity/GetSecurityActivityQueryHandler.cs`
- `src/2.Application/UseCases/Security/Account/Queries/GetSecurityActivity/GetSecurityActivityQueryValidator.cs`
- `src/2.Application/UseCases/Security/Audit/Queries/GetSecurityAuditLog/GetSecurityAuditLogQueryHandler.cs`
- `src/2.Application/UseCases/Security/Audit/Queries/GetSecurityAuditLog/GetSecurityAuditLogQueryValidator.cs`
- `src/2.Application/UseCases/Security/Queries/GetMyCompanyUserReport/GetMyCompanyUserReportQueryHandler.cs`
- `src/2.Application/UseCases/Security/Queries/GetSystemWideUserReport/GetSystemWideUserReportQueryHandler.cs`
- `src/2.Application/UseCases/Security/Queries/GetSystemWideUserReport/UserManagementReportQueryHelper.cs`

### Modify — 24 handlers a retrofitear (ya usan `PaginationSettings`)

- `src/2.Application/UseCases/Admin/Areas/Queries/GetAreas/GetAreasQueryHandler.cs`
- `src/2.Application/UseCases/Admin/CompanyModules/Queries/GetCompanyModules/GetCompanyModulesQueryHandler.cs`
- `src/2.Application/UseCases/Admin/EntityStatuses/Queries/GetEntityStatus/GetEntityStatusQueryHandler.cs`
- `src/2.Application/UseCases/Admin/Genders/Queries/GetGenders/GetGendersQueryHandler.cs`
- `src/2.Application/UseCases/Admin/IdentificationTypes/Queries/GetIdentificationTypes/GetIdentificationTypesQueryHandler.cs`
- `src/2.Application/UseCases/Admin/IncomeRanges/Queries/GetIncomeRanges/GetIncomeRangesQueryHandler.cs`
- `src/2.Application/UseCases/Admin/Industries/Queries/GetIndustries/GetIndustriesQueryHandler.cs`
- `src/2.Application/UseCases/Admin/Projects/Queries/GetProjects/GetProjectsQueryHandler.cs`
- `src/2.Application/UseCases/Admin/SystemModules/Queries/GetSystemModules/GetSystemModulesQueryHandler.cs`
- `src/2.Application/UseCases/Admin/TaxRegimes/Queries/GetTaxRegimes/GetTaxRegimesQueryHandler.cs`
- `src/2.Application/UseCases/Common/Municipalities/Queries/GetMunicipalities/GetMunicipalitiesQueryHandler.cs`
- `src/2.Application/UseCases/Common/Provinces/Queries/GetProvinces/GetProvincesQueryHandler.cs`
- `src/2.Application/UseCases/Common/Regions/Queries/GetRegions/GetRegionsQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/TicketCompanyDefaults/Queries/GetSystemWideTicketCompanyDefaults/GetSystemWideTicketCompanyDefaultsQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/TicketComplexities/Queries/GetSystemWideTicketComplexities/GetSystemWideTicketComplexitiesQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/TicketComplexities/Queries/GetTicketComplexities/GetTicketComplexitiesQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/Tickets/Queries/GetSystemWideTickets/GetSystemWideTicketsQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/Tickets/Queries/GetTickets/GetTicketsQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/TicketStatuses/Queries/GetSystemWideTicketStatuses/GetSystemWideTicketStatusesQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/TicketStatuses/Queries/GetTicketStatuses/GetTicketStatusesQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/TimeUnits/Queries/GetSystemWideTimeUnits/GetSystemWideTimeUnitsQueryHandler.cs`
- `src/2.Application/UseCases/Messaging/TimeUnits/Queries/GetTimeUnits/GetTimeUnitsQueryHandler.cs`
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetRoleSystemOptionsPaged/GetRoleSystemOptionsPagedQueryHandler.cs`
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/GetSuperAdminAllRoleSystemOptionsPaged/GetSuperAdminAllRoleSystemOptionsPagedQueryHandler.cs`
- `src/2.Application/UseCases/Security/SystemOptions/Queries/GetSystemOptionsPaged/GetSystemOptionsPagedQueryHandler.cs`
- `src/2.Application/UseCases/Security/RoleSystemOptions/Queries/RoleSystemOptionQuerySql.cs` — ya recibe `PaginationSettings` como parámetro; ajustar para usar `Sanitize` en vez de su clamping propio.

### Modify — Tests

Todos los tests unitarios espejo de los archivos listados arriba, bajo su ruta equivalente en `tests/UnitTests/JOIN.Application.UnitTest/`. Para los 13 handlers migrados (F6): ajustar aserciones donde el límite efectivo cambió e inyectar `Options.Create(new PaginationSettings {...})` explícito. Para los 24 retrofit (F7): no deben requerir cambios de aserciones, solo compilar contra la firma nueva si cambia.

### Create

- `tests/UnitTests/JOIN.Application.UnitTest/Common/PaginationSettingsTests.cs` — tests unitarios de `PaginationSettings.Sanitize` (page size negativo, cero, `null`, mayor a `MaxPageSize`, configuración inconsistente).
