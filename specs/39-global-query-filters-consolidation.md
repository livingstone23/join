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
| Manager / Supervisor / UsuarioSimple / SuperAdminCompany | Solo su `CompanyId` del token. | Solo su `CompanyId` del token (filtro EF + interceptor). |
| SuperAdmin (rol Identity) | **Una empresa por request**: su empresa activa por defecto, u otra si la pide explícitamente vía `TenantResolver.Resolve(currentUser, request.CompanyId)`. **Única excepción:** el catálogo de empresas (`GET /companies`), donde ve todas. | Una empresa por request: su empresa activa (o `SwitchCompanyCommand` para cambiarla). En los casos de uso que ya aceptan una empresa explícita vía `TenantResolver` (hoy `UpdateRoleCompany`), el repositorio usa `.IgnoreQueryFilters()` **siempre** con el filtro explícito `CompanyId == tenantId && GcRecord == 0`. |

**Se descarta** agregar `|| _currentUserService.IsInRole("SuperAdmin")` al filtro EF: todas las escrituras del SuperAdmin verían datos de todas las empresas (validaciones de unicidad cruzadas, `GetAsync(id)` resolviendo registros de otra empresa). El acceso entre empresas debe ser explícito por consulta, no un efecto implícito del filtro global.

---

## Scope

**In:**

### A. `ApplicationDbContext.ConfigureGlobalQueryFilters`

Agregar en la sección 1 (tenant + soft-delete), con el mismo patrón `e => e.GcRecord == 0 && e.CompanyId == _currentUserService.CompanyId`:

- `Region` — se mueve desde la sección 2 (hoy solo tiene `GcRecord == 0`).
- `Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile` — hoy sin filtro.
- `RoleCompany`, `CompanyModule` — se mueven desde la sección 3 (hoy solo `GcRecord == 0`). Ver sección E.

Agregar en la sección 3 (solo soft-delete):

- `UserConnectionLog` → `e => e.GcRecord == 0`. Se elimina el comentario desactualizado.

Documentar inline, en la sección 3, por qué `UserCompany` es `BaseTenantEntity` pero **no** lleva filtro de tenant: es la única excepción del sistema. En el login todavía no existe el claim `CompanyId`, y hay que leer las membresías del usuario en todas sus empresas para elegir la empresa por defecto (también en refresh y `SwitchCompany`). No es visible para todos: se lee siempre filtrada por `UserId` (sección E).

Documentar inline que `ApplicationUser` no lleva filtro a propósito (ver Out of scope).

### B. Queries Dapper de `Region`

- `GetRegionsQueryHandler` y `GetRegionByIdQueryHandler`: agregar el guard `currentUserService.CompanyId == Guid.Empty` → `Response<T>.Error(...)` y `AND r.CompanyId = @TenantId` al `WHERE`, igual que el resto de catálogos (`GetGendersQueryHandler`, `GetIndustriesQueryHandler`, etc.).
- `GetProvincesQueryHandler` y `GetProvinceByIdQueryHandler`: el `LEFT JOIN Admin.Regions r ON r.Id = p.RegionId` pasa a `... AND r.CompanyId = @TenantId AND r.GcRecord = 0`, para que una provincia global no muestre el nombre de una región de otra empresa (queda `RegionName = null`).
- `PersonAddressQuerySql` y `GetPersonByIdQueryHandler` (sección de direcciones): el `LEFT JOIN Admin.Regions re ON a.RegionId = re.Id` pasa a `... AND re.CompanyId = @TenantId`. Es el único JOIN a un catálogo de empresa que hoy no lleva condición de tenant — `Genders`, `Industries`, `TaxRegimes` e `IncomeRanges` ya la tienen en todos sus JOINs.

### C. `CreateRegionCommandHandler` / `UpdateRegionCommandHandler`

Sin cambio de código: con el filtro EF de A, `GetAllAsync()` pasa a devolver solo las regiones de la empresa actual y la validación de unicidad queda alineada con el índice `(CompanyId, CountryId, Name|Code, GcRecord)`. Se verifica con tests (F3).

### D. Empresas y módulos por empresa — `SuperAdminCompany` limitado a su propia empresa

**Decisión (2026-09-28):** solo el rol `SuperAdmin` ve y opera sobre todas las empresas. `SuperAdminCompany` (administrador de una empresa) solo ve y opera sobre la suya (`CompanyId` del token). Hoy hay fugas de lectura **y de escritura**:

| Endpoint | Rol hoy | Problema verificado | Corrección |
|---|---|---|---|
| `GET /companies` | `SuperAdminCompany` | `GetCompaniesPagedQueryHandler` solo filtra `GcRecord = 0`: lista todas las empresas de la plataforma. | Si no es `SuperAdmin` → `WHERE c.Id = @TenantId`. |
| `GET /companies/{id}` | `SuperAdminCompany` | `GetCompanyByIdQueryHandler` filtra solo por `c.Id = @Id`: lee cualquier empresa por id. | Si no es `SuperAdmin` y `id != CompanyId` del token → `NOT_FOUND`. |
| `DELETE /companies/{id}` | `SuperAdminCompany` | `DeleteCompanyCommandHandler` hace `GetAsync(request.Id)` sin chequear empresa: **un SuperAdminCompany puede borrar otra empresa**. | **Solo `SuperAdmin`** (`[Authorize(Roles = "SuperAdmin")]`). Decisión 2026-09-28: ni siquiera su propia empresa, porque dejaría sin acceso a todos sus usuarios. |
| `POST`, `PUT`, `DELETE /company-modules` | `SuperAdminCompany` | Un administrador de empresa puede activarse o desactivarse módulos (funcionalidades contratadas). Además, `DeleteCompanyModulesCommandHandler` busca el id en todas las empresas: **puede borrar un módulo de otra empresa**. | **Solo `SuperAdmin`** (decisión 2026-09-28). La empresa destino se indica explícitamente y se resuelve con `TenantResolver`; el handler exige `entity.CompanyId == tenantId`. |

`GET /company-modules` y `GET /company-modules/{id}` siguen disponibles para `SuperAdminCompany`, forzando el `CompanyId` del token (como hoy): puede ver qué módulos tiene su empresa, no modificarlos. `GET /by-admin` sigue siendo solo `SuperAdmin`.

Tests (integración): un `SuperAdminCompany` de la empresa A recibe `404` al leer la empresa B, `403` al intentar borrar cualquier empresa o crear/modificar/borrar cualquier módulo, y los datos de B no cambian; el `SuperAdmin` sí puede.

### E. Excepciones de tenant: solo queda `UserCompany`

**Decisión (2026-09-28):** solo las entidades globales son visibles entre empresas. De las tres excepciones que existían, dos pasan a filtrar por tenant y una queda como excepción justificada.

**`RoleCompany` → filtro de tenant.**
- `RoleCompanyRepository` es Dapper para lecturas, pero `GetByIdForUpdateAsync` lee con EF para `UpdateRoleCompanyCommandHandler`, que resuelve el tenant con `TenantResolver` (el SuperAdmin puede editar la configuración de roles de otra empresa indicándola explícitamente — join_frontb specs/10-role-companies.md).
- Con el filtro nuevo, esa lectura quedaría limitada a la empresa del token y rompería esa funcionalidad. Corrección: `GetByIdForUpdateAsync` agrega `.IgnoreQueryFilters()`, conservando el filtro explícito que ya tiene (`CompanyId == tenantId && GcRecord == 0`). Es seguro porque `tenantId` viene de `TenantResolver`: para cualquier rol que no sea SuperAdmin es siempre el `CompanyId` del token.
- Revisar `ExistsActiveLinkAsync` y cualquier otra lectura EF de `RoleCompany` con el mismo criterio.

**`CompanyModule` → filtro de tenant.**
- Lecturas: `GET` de `SuperAdminCompany` usan el `CompanyId` del token; `GET /by-admin` (SuperAdmin) es Dapper y no se ve afectado.
- Escrituras (solo SuperAdmin, sección D): la empresa destino viene de `TenantResolver`. Como el filtro EF limitaría las lecturas a la empresa del token, `CreateCompanyModulesCommandHandler` (chequeo de asignación duplicada) y `Update`/`Delete` (carga del registro) pasan de `GetAllAsync()` a un repositorio nombrado con `.IgnoreQueryFilters()` **y** filtro explícito `CompanyId == tenantId && GcRecord == 0` — el mismo patrón que `RoleCompany`.

**`UserCompany` → sigue sin filtro de tenant (única excepción), pero leída siempre por usuario.**
- Hoy `AuthenticatedSessionIssuer`, `RefreshTokenCommandHandler`, `SwitchCompanyCommandHandler`, `CreateTicketCommandHandler` y `UpdateTicketCommandHandler` hacen `GetRepository<UserCompany>().GetAllAsync()`: cargan **toda la tabla** (membresías de todos los usuarios de todas las empresas) y filtran en memoria. No se expone al cliente, pero es innecesario y no escala.
- Corrección: repositorio nombrado `IUserCompanyRepository` (Persistence), accesible como `_unitOfWork.UserCompanies`, con:
  - `GetActiveByUserIdAsync(Guid userId)` — login, refresh, `SwitchCompany`.
  - `IsActiveMemberAsync(Guid userId, Guid companyId)` — `CreateTicket`/`UpdateTicket` (validar que el usuario asignado pertenece a la empresa).
- Revisar también `CreateCustomerCommandHandler`, `AddUserCompanyCommandHandler`, `RemoveUserCompanyCommandHandler`, `SetDefaultCompanyCommandHandler` y `CustomUserClaimsPrincipalFactory` (`src/3.Persistence/Identity`): toda lectura de `UserCompany` debe filtrar por `UserId` (y por `CompanyId` cuando aplique) en la query, nunca en memoria.

Tests:
- `RoleCompany`: un Manager de A no lee ni modifica la configuración de roles de B; el SuperAdmin sí puede actualizar la de B indicándola explícitamente (regresión de la funcionalidad existente).
- `CompanyModule`: un `SuperAdminCompany` de A ve solo los módulos de A y no puede modificar ninguno; el SuperAdmin puede crear/modificar/borrar módulos de B indicándola explícitamente, y el chequeo de duplicado se hace contra los módulos de B.
- `UserCompany`: login, refresh y `SwitchCompany` siguen funcionando para un usuario con membresías en dos empresas.

### F. Tests

- Test de guarda (F3.1).
- Tests de integración de comportamiento (F3.2).
- Tests unitarios de los handlers de Region modificados en B (gate de cobertura de `JOIN.Application.UnitTest`).

**Out of scope:**

- **`Province` y `Municipality` siguen siendo catálogos globales** (`BaseAuditableEntity`, solo `GcRecord == 0`). No tiene sentido que cada empresa duplique la división territorial de un país: Madrid existe una sola vez y todas las empresas ven el mismo registro, tanto en los listados como al elegirlo en una dirección.
- **Relación provincia/municipio ↔ región por empresa — spec futura.** Hoy `Province.RegionId` apunta a una `Region`, que con esta spec pasa a ser propia de cada empresa: una provincia global no puede pertenecer a la región privada de una empresa. `Province.RegionId` se mantiene tal cual (sin migración) como dato heredado, y la corrección de B solo evita que se muestre una región de otra empresa. La solución prevista es crear tablas intermedias con tenant — por ejemplo `ProvinceWithRegion (CompanyId, RegionId, ProvinceId)` y `MunicipalityWithRegion (CompanyId, RegionId, MunicipalityId)` — para que cada empresa arme sus propias regiones agrupando provincias y municipios globales. Cuando exista esa spec, `Province.RegionId` debería deprecarse.
- **Filtro de `ApplicationUser`.** Se mantiene sin filtro: `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` resuelven `CreatedByUserName`/`AssignedToUserName` con `GetRepository<ApplicationUser>().GetAsync(id)`, y un filtro haría que los tickets viejos de usuarios dados de baja muestren `null`. Decisión de producto pendiente.
- **Cambiar el mecanismo a reflexión o interfaces marcadoras** (`ICrossTenantEntity`, etc.) — descartado, ver Decisión de diseño.

---

## Implementation plan

### F0 — Verificación previa de datos

Antes de aplicar el filtro de tenant a las 9 entidades, confirmar en la base de desarrollo que no hay filas con `CompanyId = '00000000-0000-0000-0000-000000000000'` en `Admin.Regions`, `Admin.Customers`, `Admin.Genders`, `Admin.Industries`, `Admin.TaxRegimes`, `Admin.IncomeRanges`, `Admin.PersonBusinessProfiles`, `Admin.PersonEmployments`, `Admin.PersonFinancialProfiles`. Si existen (por ejemplo, regiones creadas antes de que el interceptor asignara `CompanyId`), dejarían de ser visibles para cualquier empresa: documentar el conteo y decidir si se reasignan con un script de datos antes de mergear.

**Referencias cruzadas entre empresas.** Hasta hoy, `CreatePerson`/`UpdatePerson`, `CreatePersonBusinessProfile`/`Update...` y `CreatePersonFinancialProfile`/`Update...` validaban las FK con `GetRepository<T>().GetAsync(id)` **sin** filtro de tenant, así que una persona de la empresa A pudo quedar apuntando a un catálogo de la empresa B. Contar, con una query de auditoría por relación, las filas donde `CompanyId` del registro ≠ `CompanyId` del catálogo referenciado:

| Registro | FK | Catálogo |
|---|---|---|
| `Admin.Persons` | `GenderId` | `Admin.Genders` |
| `Admin.PersonAddresses` | `RegionId` | `Admin.Regions` |
| `Admin.PersonBusinessProfiles` | `IndustryId`, `TaxRegimeId` | `Admin.Industries`, `Admin.TaxRegimes` |
| `Admin.PersonFinancialProfiles` | `IncomeRangeId` | `Admin.IncomeRanges` |

Con esta spec esas filas cambian de comportamiento: en los `LEFT JOIN` el nombre del catálogo sale `null`, y en los `INNER JOIN` con `CompanyId` (perfiles de negocio y financieros) **el perfil completo deja de aparecer**. Si el conteo es > 0, decidir antes de mergear si se reasignan a un catálogo equivalente de la propia empresa o se dejan en `null`. Si es 0, dejar constancia en el PR.

### F1 — `ConfigureGlobalQueryFilters`

1. Mover `Region` de la sección 2 a la sección 1 con filtro de tenant.
2. Agregar las 8 entidades restantes a la sección 1.
3. Reemplazar el comentario de `UserConnectionLog` por la línea activa en la sección 3.
4. Mover `RoleCompany` y `CompanyModule` a la sección 1; comentarios inline para `UserCompany` (única excepción de tenant) y `ApplicationUser` (sin filtro a propósito).
6. Aplicar la sección E (`IgnoreQueryFilters` en `RoleCompanyRepository.GetByIdForUpdateAsync`, `IUserCompanyRepository`).
5. `dotnet build -c Release` → 0 errores.

### F2 — Queries Dapper de Region y Province

1. `GetRegionsQueryHandler` / `GetRegionByIdQueryHandler`: guard de `CompanyId` + `AND r.CompanyId = @TenantId`.
2. `GetProvincesQueryHandler` / `GetProvinceByIdQueryHandler`: condición de tenant en el `LEFT JOIN` a `Admin.Regions`.
3. Tests unitarios en `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Common/Regions/Queries/...` para el guard de `CompanyId == Guid.Empty`.

### F3 — Tests

**F3.1 — Test de guarda** (`tests/IntegrationTests/Persistence/GlobalQueryFiltersGuardTests.cs`): recorre `context.Model.GetEntityTypes()` y falla si:

- una entidad que implementa `IAuditableEntity` no tiene query filter, salvo que esté en la lista de excepciones `{ ApplicationUser }`;
- una entidad `BaseTenantEntity` tiene un filtro que no referencia `CompanyId`, salvo que esté en la lista `{ UserCompany }`.

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
- [ ] `RoleCompany` y `CompanyModule` tienen filtro `GcRecord == 0 && CompanyId == tenant`; la edición de `RoleCompany` de otra empresa por el SuperAdmin sigue funcionando.
- [ ] `UserCompany` es la única `BaseTenantEntity` sin filtro de tenant, con comentario que lo justifica, y ninguna lectura suya carga la tabla completa.
- [ ] `Province` y `Municipality` mantienen solo `GcRecord == 0` (globales).
- [ ] `ApplicationUser` sigue sin filtro, con comentario.
- [ ] Las queries Dapper de Region filtran por tenant; las de Province, `PersonAddressQuerySql` y `GetPersonById` no exponen regiones de otra empresa.
- [ ] La auditoría de F0 (filas con `CompanyId = Guid.Empty` y referencias cruzadas entre empresas) está documentada en el PR, con la decisión tomada si hubo casos.
- [ ] `SuperAdminCompany` solo ve su propia empresa y los módulos de su empresa; no puede borrar empresas ni crear/modificar/borrar módulos. Solo `SuperAdmin` puede (sección D).
- [ ] El test de guarda F3.1 pasa y falla si se borra cualquier línea de `ConfigureGlobalQueryFilters`.
- [ ] Los 8 casos de F3.2 pasan contra SQL Server real.
- [ ] Gate de cobertura de `JOIN.Application.UnitTest` ≥ 90%.
- [ ] Toda spec futura (34 en adelante) que agregue una entidad `IAuditableEntity` incluye su línea en `ConfigureGlobalQueryFilters`; el test de guarda lo exige.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| Filas existentes con `CompanyId = Guid.Empty` en alguna de las 9 entidades dejan de ser visibles. | F0 las cuenta antes de mergear; se reasignan con script de datos si hace falta. |
| Registros que ya apuntan a un catálogo de otra empresa (género, región, industria, régimen fiscal, rango de ingresos) pierden el dato o, en perfiles con `INNER JOIN`, desaparecen del listado. | Auditoría de referencias cruzadas en F0; decisión explícita antes de mergear. |
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
