# SPEC 39 — Consolidación de query filters globales (soft-delete + tenant) en `ApplicationDbContext`

> **Status:** Borrador
> **Depends on:** SPEC 30 (`UserConnectionLog` ganó `GcRecord` ahí; esta spec cierra el lado EF que quedó pendiente). Establece la convención que deben seguir las specs 34 en adelante del módulo de tickets (`TicketUserCompany`, `TicketAttachmentSettings`, `TicketDocument`, `TicketInboundChannel`, `TicketStatusTransition` — ninguna existe todavía en el dominio).
> **Date:** 2026-09-28
> **Objective:** Mantener `ApplicationDbContext.ConfigureGlobalQueryFilters` como lista explícita (una línea `HasQueryFilter` por entidad, agrupada por sección), completarla con las entidades tenant-scoped que hoy no tienen filtro (incluida `Region`), alinear `UserConnectionLog` con SPEC 30, y agregar un test de guarda que falle cuando una entidad nueva quede sin filtro.

---

## Por qué existe esta spec

Al revisar el módulo de tickets (SPEC 34-38) se auditó `ConfigureGlobalQueryFilters` contra las entidades reales del dominio y aparecieron tres problemas:

1. **9 entidades `BaseTenantEntity` no tienen ningún filtro EF**: `Region`, `Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile`. Todas son datos propios de cada empresa: el `AuditableEntitySaveChangesInterceptor` les asigna el `CompanyId` del token al insertar, y sus índices únicos incluyen `CompanyId`. Solo les falta la línea en el método.
2. **`Region` tiene fuga real de tenant en lectura.** A diferencia de las otras 8 (cuyos handlers Dapper ya filtran `CompanyId = @TenantId`), `GetRegionsQueryHandler` y `GetRegionByIdQueryHandler` solo filtran `GcRecord = 0`: un Manager de la empresa A ve hoy las regiones de la empresa B. Además, `CreateRegionCommandHandler` valida unicidad de nombre/código con `GetAllAsync()` sobre **todas** las empresas, contradiciendo el índice único `(CompanyId, CountryId, Name, GcRecord)`.
3. **`UserConnectionLog` tiene un comentario y una línea comentada desactualizados.** El comentario asume que la entidad no tiene `GcRecord`; SPEC 30 (**Implementado**) la migró a `BaseAuditableEntity` y el sistema ya usa `GcRecord` para distinguir sesiones activas de revocadas.

Las tres cosas tienen la misma causa: nada falla cuando alguien agrega una entidad y se olvida la línea en `ConfigureGlobalQueryFilters`. Esta spec corrige lo que falta y agrega el test que lo detecta.

---

## Decisión de diseño: lista explícita, no reflexión

Se evaluó reemplazar el método por uno genérico basado en reflexión (derivar el filtro de `IAuditableEntity`/`BaseTenantEntity` con listas de exención). **Se descartó**:

- La lista explícita es el patrón que el proyecto ya usa, se lee de un vistazo y no depende de la firma de `IMutableEntityType.SetQueryFilter` en EF Core 10 (que cambió con los filtros nombrados de EF Core 9).
- Lo que la reflexión aportaba (que nadie olvide una entidad) se consigue igual con el test de guarda de F3, sin cambiar el mecanismo.

**Convención para specs futuras:** toda spec que agregue una entidad `IAuditableEntity` debe agregar su línea en la sección correspondiente de `ConfigureGlobalQueryFilters` y, si es una excepción, justificarla en la lista de excepciones del test de guarda.

---

## Modelo de tenant: SuperAdmin vs. usuarios de empresa

El filtro EF de tenant es **estricto**: `CompanyId == _currentUserService.CompanyId`, sin excepción por rol.

| Rol | Lectura (Dapper) | Escritura (EF) |
|---|---|---|
| Manager / Supervisor / UsuarioSimple | Solo su `CompanyId` del token. | Solo su `CompanyId` del token (filtro EF + interceptor). |
| SuperAdmin (rol Identity) | Su empresa activa por defecto; otra empresa solo si la pide explícitamente vía `TenantResolver.Resolve(currentUser, request.CompanyId)`. | Opera sobre su empresa activa; para operar en otra, `SwitchCompanyCommand` emite un token con otro `CompanyId`. |

**Se descarta** agregar `|| _currentUserService.IsInRole("SuperAdmin")` al filtro EF: todas las escrituras del SuperAdmin verían datos de todas las empresas (validaciones de unicidad cruzadas, `GetAsync(id)` resolviendo registros de otra empresa). El acceso entre empresas debe ser explícito por consulta, no un efecto implícito del filtro global.

---

## Scope

**In:**

### A. `ApplicationDbContext.ConfigureGlobalQueryFilters`

Agregar en la sección 1 (tenant + soft-delete), con el mismo patrón `e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId`:

- `Region` — se mueve desde la sección 2 (hoy solo tiene `GcRecord == 0`).
- `Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile` — hoy sin filtro.

Agregar en la sección 3 (solo soft-delete):

- `UserConnectionLog` → `e => e.GcRecord == 0`. Se elimina el comentario desactualizado.

Documentar inline, en la sección 3, por qué `UserCompany`, `RoleCompany` y `CompanyModule` son `BaseTenantEntity` pero **no** llevan filtro de tenant (lectura entre empresas a propósito: login antes de que exista el claim `CompanyId`, administración de la plataforma).

Documentar inline que `ApplicationUser` no lleva filtro a propósito (ver Out of scope).

### B. Queries Dapper de `Region`

- `GetRegionsQueryHandler` y `GetRegionByIdQueryHandler`: agregar el guard `currentUserService.CompanyId == Guid.Empty` → `Response<T>.Error(...)` y `AND r.CompanyId = @TenantId` al `WHERE`, igual que el resto de catálogos (`GetGendersQueryHandler`, `GetIndustriesQueryHandler`, etc.).
- `GetProvincesQueryHandler` y `GetProvinceByIdQueryHandler`: el `LEFT JOIN Admin.Regions r ON r.Id = p.RegionId` pasa a `... AND r.CompanyId = @TenantId AND r.GcRecord = 0`, para que una provincia global no muestre el nombre de una región de otra empresa (queda `RegionName = null`).

### C. `CreateRegionCommandHandler` / `UpdateRegionCommandHandler`

Sin cambio de código: con el filtro EF de A, `GetAllAsync()` pasa a devolver solo las regiones de la empresa actual y la validación de unicidad queda alineada con el índice `(CompanyId, CountryId, Name|Code, GcRecord)`. Se verifica con tests (F3).

### D. Tests

- Test de guarda (F3.1).
- Tests de integración de comportamiento (F3.2).
- Tests unitarios de los handlers de Region modificados en B (gate de cobertura de `JOIN.Application.UnitTest`).

**Out of scope:**

- **`Province` y `Municipality` siguen siendo catálogos globales** (`BaseAuditableEntity`, solo `GcRecord == 0`). No tiene sentido que cada empresa duplique la división territorial de un país.
- **Relación provincia/municipio ↔ región por empresa — spec futura.** Hoy `Province.RegionId` apunta a una `Region`, que con esta spec pasa a ser propia de cada empresa: una provincia global no puede pertenecer a la región privada de una empresa. `Province.RegionId` se mantiene tal cual (sin migración) como dato heredado, y la corrección de B solo evita que se muestre una región de otra empresa. La solución prevista es crear tablas intermedias con tenant — por ejemplo `ProvinceWithRegion (CompanyId, RegionId, ProvinceId)` y `MunicipalityWithRegion (CompanyId, RegionId, MunicipalityId)` — para que cada empresa arme sus propias regiones agrupando provincias y municipios globales. Cuando exista esa spec, `Province.RegionId` debería deprecarse.
- **Filtro de `ApplicationUser`.** Se mantiene sin filtro: `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` resuelven `CreatedByUserName`/`AssignedToUserName` con `GetRepository<ApplicationUser>().GetAsync(id)`, y un filtro haría que los tickets viejos de usuarios dados de baja muestren `null`. Decisión de producto pendiente.
- **Cambiar el mecanismo a reflexión o interfaces marcadoras** (`ICrossTenantEntity`, etc.) — descartado, ver Decisión de diseño.

---

## Implementation plan

### F0 — Verificación previa de datos

Antes de aplicar el filtro de tenant a las 9 entidades, confirmar en la base de desarrollo que no hay filas con `CompanyId = '00000000-0000-0000-0000-000000000000'` en `Admin.Regions`, `Admin.Customers`, `Admin.Genders`, `Admin.Industries`, `Admin.TaxRegimes`, `Admin.IncomeRanges`, `Admin.PersonBusinessProfiles`, `Admin.PersonEmployments`, `Admin.PersonFinancialProfiles`. Si existen (por ejemplo, regiones creadas antes de que el interceptor asignara `CompanyId`), dejarían de ser visibles para cualquier empresa: documentar el conteo y decidir si se reasignan con un script de datos antes de mergear.

### F1 — `ConfigureGlobalQueryFilters`

1. Mover `Region` de la sección 2 a la sección 1 con filtro de tenant.
2. Agregar las 8 entidades restantes a la sección 1.
3. Reemplazar el comentario de `UserConnectionLog` por la línea activa en la sección 3.
4. Comentarios inline para `UserCompany`/`RoleCompany`/`CompanyModule` (sin tenant a propósito) y `ApplicationUser` (sin filtro a propósito).
5. `dotnet build -c Release` → 0 errores.

### F2 — Queries Dapper de Region y Province

1. `GetRegionsQueryHandler` / `GetRegionByIdQueryHandler`: guard de `CompanyId` + `AND r.CompanyId = @TenantId`.
2. `GetProvincesQueryHandler` / `GetProvinceByIdQueryHandler`: condición de tenant en el `LEFT JOIN` a `Admin.Regions`.
3. Tests unitarios en `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Common/Regions/Queries/...` para el guard de `CompanyId == Guid.Empty`.

### F3 — Tests

**F3.1 — Test de guarda** (`tests/IntegrationTests/Persistence/GlobalQueryFiltersGuardTests.cs`): recorre `context.Model.GetEntityTypes()` y falla si:

- una entidad que implementa `IAuditableEntity` no tiene query filter, salvo que esté en la lista de excepciones `{ ApplicationUser }`;
- una entidad `BaseTenantEntity` tiene un filtro que no referencia `CompanyId`, salvo que esté en la lista `{ UserCompany, RoleCompany, CompanyModule }`.

El mensaje de error nombra la entidad y apunta a esta spec. La verificación de "referencia `CompanyId`" se hace inspeccionando el `LambdaExpression` del filtro (visitor que busca un `MemberExpression` sobre `CompanyId`).

**F3.2 — Tests de comportamiento** (`tests/IntegrationTests/Persistence/GlobalQueryFiltersIntegrationTests.cs`, SQL Server real vía `CustomWebApplicationFactory`):

1. `Region`: dos empresas con una región cada una; autenticado como A, `context.Regions.ToList()` solo devuelve la de A.
2. `Region`: `GET /regions` autenticado como Manager de A no devuelve regiones de B.
3. `Region`: la empresa B puede crear una región con el mismo nombre y país que una de A (antes fallaba con `REGION_NAME_IN_USE`).
4. `Gender` (muestra de las 8): una fila de B no aparece en `context.Genders` autenticado como A; `CreatePerson` con un `GenderId` de B devuelve error de referencia inválida.
5. `UserCompany`: filas de dos empresas visibles sin importar el tenant autenticado; una fila soft-deleted no aparece.
6. `Province`: visible para todas las empresas (catálogo global); si su `RegionId` apunta a una región de B, `GET /provinces` autenticado como A devuelve `RegionName = null`.
7. `UserConnectionLog`: una fila con `GcRecord != 0` no aparece en `context.UserConnectionLogs.ToList()`.
8. `ApplicationUser`: un usuario soft-deleted sigue siendo devuelto por `context.Users.Find(id)`.

### F3b — Impacto en tests existentes

1. `grep` de `ApplicationDbContext` y de los `DbSet` de las 9 entidades sobre `tests/` completo antes de implementar F1.
2. Cualquier test que siembre datos de estas entidades con un `CompanyId` distinto al del usuario autenticado y luego los lea vía EF va a cambiar de resultado — ajustarlo es parte de esta spec, no un efecto colateral: documentar cada test modificado en el PR.
3. El seeder (`DatabaseSeeder`) ya usa `.IgnoreQueryFilters()` con `CompanyId` explícito en `SeedGendersAsync`, `SeedTaxRegimesAsync`, `SeedIndustriesAsync` y `SeedIncomeRangesAsync` — confirmar que siga sembrando sin duplicar al arrancar dos veces.

### F4 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj /p:CollectCoverage=true /p:Threshold=90 /p:ThresholdType=line` → verde.
3. `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj` → 0 fallidos.
4. Smoke manual: Manager de empresa A y Manager de empresa B ven solo sus regiones, géneros, industrias y clientes.
5. Smoke manual: SuperAdmin ve su empresa activa por defecto y, tras `SwitchCompany`, la otra.
6. Smoke manual: revocar una sesión propia (`DELETE /api/v1/account/sessions/{id}`) sigue devolviendo 200.

---

## Acceptance criteria

- [ ] `Region`, `Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile` tienen filtro `GcRecord == 0 && CompanyId == tenant`.
- [ ] `UserConnectionLog` tiene filtro `GcRecord == 0`; el comentario desactualizado se eliminó.
- [ ] `UserCompany`, `RoleCompany`, `CompanyModule` mantienen solo `GcRecord == 0`, con comentario que justifica la excepción.
- [ ] `Province` y `Municipality` mantienen solo `GcRecord == 0` (globales).
- [ ] `ApplicationUser` sigue sin filtro, con comentario.
- [ ] Las queries Dapper de Region filtran por tenant; las de Province no exponen regiones de otra empresa.
- [ ] El test de guarda F3.1 pasa y falla si se borra cualquier línea de `ConfigureGlobalQueryFilters`.
- [ ] Los 8 casos de F3.2 pasan contra SQL Server real.
- [ ] Gate de cobertura de `JOIN.Application.UnitTest` ≥ 90%.
- [ ] Toda spec futura (34 en adelante) que agregue una entidad `IAuditableEntity` incluye su línea en `ConfigureGlobalQueryFilters`; el test de guarda lo exige.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| Filas existentes con `CompanyId = Guid.Empty` en alguna de las 9 entidades dejan de ser visibles. | F0 las cuenta antes de mergear; se reasignan con script de datos si hace falta. |
| Una lectura EF de estas entidades desde un proceso sin usuario (job en segundo plano, seeder) devuelve vacío porque `CompanyId == Guid.Empty`. | Mismo comportamiento que ya tienen `Ticket`/`Person`. Esos procesos deben usar Dapper o `.IgnoreQueryFilters()` con `CompanyId` explícito (ya es el caso del seeder). Relevante para la rutina de inactividad de SPEC 40. |
| `Province.RegionId` de una provincia global apunta a una región privada de una empresa. | Mitigado en lectura por F2.2 (`RegionName = null` para otras empresas). Resolución definitiva en la spec futura de `ProvinceWithRegion`/`MunicipalityWithRegion`. |
| El SuperAdmin espera ver todo sin cambiar de empresa en pantallas que leen vía EF. | Las lecturas son Dapper con `TenantResolver`; las escrituras operan sobre la empresa activa (`SwitchCompany`). Documentado en "Modelo de tenant". |
| Tests existentes que mezclaban tenants en estas entidades cambian de resultado. | F3b los identifica antes de implementar; cada ajuste se documenta en el PR. |

---

## What is **not** in this spec

- Tablas `ProvinceWithRegion` / `MunicipalityWithRegion` con `CompanyId` y deprecación de `Province.RegionId` — spec futura.
- Decidir si `ApplicationUser` debe filtrarse — decisión de producto diferida.
- Mecanismo de filtros por reflexión o interfaces marcadoras en el dominio.
- La rutina de notificación por inactividad de tickets — **SPEC 40**.
