# SPEC 41 — Visibilidad de registros borrados y restauración para SuperAdmin

> **Status:** Implementado
> **Depends on:** SPEC 38 (query filters completos — define qué es "de empresa" y qué es "global"), SPEC 40 (índices únicos filtrados — la unicidad debe aplicar solo a activos para poder restaurar sin colisiones).
> **Date:** 2026-09-28
> **Historia (2026-10-08, Etapa 4):** vuelve a Borrador por cambio de lógica al planificar la Etapa 4: `Ticket` pasa a ser padre de composición de `TicketDocument` (cascada), los tickets de seguimiento activos bloquean el borrado de un ticket, se agrega `LogType.Restoration` y se filtra el índice único de `TicketCompanyDefault`. Las etapas 1 a 3 ya están implementadas y no cambian.
> **Historia (2026-10-08):** vuelve a Borrador por cambio de lógica del borrado (sección E, "Borrado en cascada de composición"). Las etapas 1 y 2 ya implementadas se ajustan cuando la spec se vuelva a aprobar.
> **Historia (2026-10-07):** al planificar la Etapa 1, el usuario decidió que un hijo no se restaura mientras su padre siga borrado, incluidas las referencias a catálogos (reemplaza la decisión del 2026-09-28 que lo permitía), y que los endpoints `system-wide` de SuperAdmin quedan sin cambios. Se agregaron el plan y el inventario de la Etapa 1.
> **Objective:** Permitir que el rol Identity `SuperAdmin` vea registros activos **y** borrados (`GcRecord > 0`) de todas las entidades, y que pueda restaurar un registro borrado (`GcRecord → 0`), sin abrir ninguna fuga de datos entre empresas para el resto de los roles. Se implementa por etapas.

---

## Por qué existe esta spec

Hoy ningún rol puede ver ni recuperar un registro borrado:

- Los filtros globales de EF (SPEC 38) y todas las queries Dapper excluyen `GcRecord > 0` sin excepción, incluso para el SuperAdmin (por ejemplo `GetUsersWithRolesQueryHandler`, `UserManagementReportQueryHelper`: `WHERE u.GcRecord = 0`).
- Los métodos `Reactivate()` que existen (`Person`, `Gender`, `Industry`, `Customer`, etc.) cambian `IsActive`, **no** `GcRecord`. "Inactivo" (visible, marcado como no activo) y "borrado" (oculto) son conceptos distintos, y el segundo no tiene vuelta atrás.
- `GenericRepository.GetIncludingDeletedAsync` (`src/3.Persistence/Repositories/GenericRepository.cs:58`) ya existe, pero ignora **todos** los filtros — también el de tenant. Usarlo sin controles es una fuga.

---

## Reglas de visibilidad (invariantes del sistema)

| Rol | Datos de empresa | Datos globales | Borrados |
|---|---|---|---|
| Cualquier rol excepto `SuperAdmin` | Solo su `CompanyId` del token | Todos los activos | Nunca |
| `SuperAdmin` (rol Identity) | **Una empresa por request**: su empresa activa, u otra si la pide explícitamente (`TenantResolver`). Única excepción: el catálogo de empresas, donde ve todas. | Todos | Solo si lo pide explícitamente (`IncludeDeleted = true`) |

- "Global" significa exactamente las entidades sin `CompanyId` que SPEC 38 clasifica como catálogo global: `Company`, `Country`, `Province`, `Municipality`, `StreetType`, `CommunicationChannel`, `IdentificationType`, `EntityStatus`, `SystemModule`, `SystemOption`. Nada más es visible entre empresas.
- `IncludeDeleted` se **ignora silenciosamente** para cualquier rol que no sea `SuperAdmin` (mismo criterio que `TenantResolver` con `CompanyId`): el request no puede ampliar lo que el token permite.
- El rol `SuperAdminCompany` (administrador de una empresa) **no** es `SuperAdmin`: no ve borrados ni otras empresas.
- Mostrar borrados es opt-in por request, no un comportamiento por defecto del SuperAdmin: así los selectores de uso diario ("asignar ticket a…", "elegir género") nunca muestran registros borrados, ni siquiera a él.

---

## Diseño

### A. Dominio

- `BaseAuditableEntity.Restore()` → `GcRecord = ActiveGcRecord`. Idempotente.
- `ApplicationUser.Restore()` y `ApplicationRole.Restore()` equivalentes (no heredan `BaseAuditableEntity`: ya extienden `IdentityUser<Guid>`/`IdentityRole<Guid>`).
- `BaseAuditableEntity.IsDeleted` (propiedad calculada, no mapeada) → `GcRecord != ActiveGcRecord`.

### B. Application — piezas comunes (`2.Application/Common`)

- **`SoftDeleteVisibility.IncludeDeleted(ICurrentUserService, bool? requested)`** — estático, mismo estilo que `TenantResolver`: devuelve `true` solo si `requested == true && currentUserService.IsInRole("SuperAdmin")`.
- **`SoftDeleteRestorer`** — clase DI (constructor primario) con la lógica común de restauración, para que cada handler no la reimplemente:
  1. Carga con `GetIncludingDeletedAsync(id)`.
  2. Si no existe → `Response.Error("NOT_FOUND")`.
  3. **Tenant:** si la entidad es `BaseTenantEntity`, compara `entity.CompanyId` con `TenantResolver.Resolve(currentUser, request.CompanyId)`; si no coincide → `NOT_FOUND` (no `FORBIDDEN`, para no confirmar la existencia de un registro de otra empresa).
  4. Si ya está activa → `Response.Error("NOT_DELETED")`.
  5. **Padre borrado:** ejecuta el chequeo que le pasa el handler (por ejemplo, un `PersonContact` no se restaura si su `Person` está borrada) → `PARENT_DELETED`.
  6. **Duplicado activo:** ejecuta el chequeo que le pasa el handler contra la misma clave natural del índice único filtrado de SPEC 40 → `ACTIVE_DUPLICATE_EXISTS` (error de negocio, nunca un 500 por violación de índice).
  7. `entity.Restore()` + `SaveChangesAsync`.

### C. Application — por entidad (convención CQRS)

Por cada entidad restaurable, en `UseCases/<Area>/<Feature>/Commands/Restore<Entity>/`:

- `Restore<Entity>Command(Guid Id, Guid? CompanyId)` : `IRequest<Response<Guid>>`, `ITransactionalCommand<...>`.
- `Restore<Entity>CommandValidator` — `Id` no vacío.
- `Restore<Entity>CommandHandler` — guard `CompanyId == Guid.Empty`, guard `IsInRole("SuperAdmin")`, y delega en `SoftDeleteRestorer` pasándole los chequeos de padre y de duplicado propios de la entidad.

Queries de listado y por id de cada entidad:

- Parámetro opcional `IncludeDeleted` (bool, por defecto `false`).
- `WHERE (@IncludeDeleted = 1 OR x.GcRecord = 0)`, con `@IncludeDeleted` calculado por `SoftDeleteVisibility`, nunca tomado directo del request.
- Los JOINs a tablas relacionadas mantienen `GcRecord = 0` (un registro borrado muestra sus relaciones activas; no se mezclan borrados de varias tablas).
- DTO: se agregan `IsDeleted` (bool) y `DeletedOn` (`DateOnly?`, derivado del sello `yyyyMMdd` de `GcRecord`).

### D. WebApi

- `POST /api/v1/<resource>/{id}/restore` en el controller existente de cada entidad, con `[Authorize(Roles = "SuperAdmin")]` (mismo patrón que `CompaniesController`). **Decisión (2026-09-28):** basta con el rol; no se exige un flag de permiso adicional por recurso.
- `GET` de listado y por id aceptan `?includeDeleted=true`.

### E. Modelo de borrado: solo lógico, sin cascada hacia arriba

Verificado en el código (2026-09-28):

- **No hay borrado físico.** Ningún caso de uso llama a `GenericRepository.DeleteAsync` (el único método que hace `Remove()`), no hay `DELETE` en SQL crudo ni llamadas a `UserManager.DeleteAsync`/`RoleManager.DeleteAsync`. Las FK con `DeleteBehavior.Cascade` (usuario → membresías, tokens, códigos MFA; ticket → `TicketLog`) solo se disparan con un borrado físico, que nunca ocurre.
- **Borrar un hijo nunca afecta al padre.** `DeletePersonAddressCommandHandler` marca solo la dirección; si era la default, `PersonAddressDefaultCoordinator.PromoteNextDefaultAsync` promueve otra, y si no queda ninguna simplemente no hay default: se puede borrar la última dirección y la persona sigue activa. Igual para contactos (`PersonContactPrimaryCoordinator`).
- **Borrar una persona sí borra (lógicamente) algunos hijos.** `DeletePersonCommandHandler` marca la persona y sus direcciones y contactos activos. **No** marca `Customer`, `PersonEmployment`, `PersonBusinessProfile` ni `PersonFinancialProfile`, y sus queries no cruzan con `Admin.Persons`: quedan activos y visibles apuntando a una persona borrada. Con la regla de abajo, este comportamiento cambia.

**Decisión (2026-09-28), vigente solo para referencias (ver decisión 2026-10-08 más abajo): un padre con hijos activos no se puede borrar.** El único borrado válido en el sistema es el lógico (`GcRecord`). Si el padre tiene hijos activos, el borrado falla con un error de negocio que lista qué registros activos tiene; el usuario debe borrar primero los hijos y recién entonces puede borrar el padre. No hay borrado en cascada ni confirmación que lo fuerce.

- Código de error: el patrón existente `<ENTIDAD>_IN_USE` (por ejemplo `PERSON_IN_USE`), con el detalle de los hijos activos en `errors` (por ejemplo `"3 direcciones activas"`, `"2 contactos activos"`, `"1 empleo activo"`).
- ~~**`DeletePerson` deja de borrar direcciones y contactos junto con la persona.** Direcciones, contactos, cliente, empleos, perfil de negocio y perfil financiero activos bloquean el borrado de la persona. Es un cambio de comportamiento respecto de hoy: los tests existentes de `DeletePersonCommandHandler` que esperan el borrado conjunto se actualizan como parte de la Etapa 2.~~ Reemplazada el 2026-10-08: `DeletePerson` borra en cascada sus 5 hijos de composición; `Customer`, `Ticket` y `UserPerson` activos bloquean.
- Borrar un hijo sigue sin afectar al padre (sin cambios).

Este patrón ya existe en la mayoría de los catálogos: `DeleteGender`, `DeleteIndustry`, `DeleteRegion`, `DeleteTaxRegime`, `DeleteIncomeRange`, `DeleteProject`, `DeleteTicketStatus` y `DeleteProvince` devuelven `<ENTIDAD>_IN_USE` si el registro está referenciado. **No** lo tienen hoy: `DeleteArea`, `DeleteCountry`, `DeleteCompany` y `DeletePerson`. Esta spec lo extiende a todo padre con hijos: cada etapa, antes de implementar, inventaría las relaciones padre → hijo de sus entidades y alinea sus handlers `Delete<Entidad>`. `DeleteCompany` (solo SuperAdmin, SPEC 38) queda bloqueado mientras la empresa tenga membresías o registros activos.

**Decisión (2026-10-08), reemplaza a la del 2026-09-28 para la composición: borrado en cascada (soft delete) de los hijos propios.** Se distinguen dos tipos de relación padre → hijo:

- **Composición** (el hijo no existe sin el padre): borrar el padre marca como borrados (`GcRecord`) al padre y a sus hijos activos, en la misma operación y con el mismo sello de fecha. Aplica a:
  - `Person` → `PersonAddress`, `PersonContact`, `PersonEmployment`, `PersonBusinessProfile`, `PersonFinancialProfile`.
  - `SystemModule` → `SystemOption` → `SystemOption` hijas (`ParentId`), en todos los niveles.
  - `Ticket` → `TicketDocument` (decisión 2026-10-08, Etapa 4).
- **Referencia** (el hijo apunta a un catálogo o a otra entidad con vida propia): sigue la regla anterior, el padre con referencias activas no se borra → `<ENTIDAD>_IN_USE`. Aplica a los catálogos (`Gender`, `Country`, `TimeUnit`…) y, para `Person`, a `Customer`, `Ticket` y `UserPerson`; para `SystemOption`, a `RoleSystemOption`; para `SystemModule`, a `CompanyModule`; para `Ticket`, a sus tickets de seguimiento (`PrecedentTicketId`).
- **Orden de los chequeos al borrar un padre de composición:** primero las referencias (si hay alguna activa en el padre o en cualquier hijo que se borraría → `_IN_USE` y no se borra nada); después la cascada.
- **Permiso:** basta `CanDelete` sobre el recurso del padre (los hijos de `Person` comparten el recurso `Persons`).
- **Restaurar un padre de composición** restaura con él a los hijos que se borraron en la misma cascada (mismo sello `GcRecord` que el padre), aplicando el chequeo de duplicado activo y las marcas default/principal/actual de cada hijo. Los hijos borrados antes, uno por uno, quedan borrados. **Limitación:** `GcRecord` guarda solo la fecha (`yyyyMMdd`), así que un hijo borrado individualmente el mismo día que el padre también se restaura con él.
- Restaurar un hijo sigue siendo individual y exige el padre activo (`PARENT_DELETED`).

Reglas para esta spec:

- Se elimina `DeleteAsync` de `IGenericRepository`/`GenericRepository` (borrado físico sin uso), para que ningún caso de uso futuro pueda introducirlo.
- ~~**Restaurar es siempre individual.**~~ Reemplazada el 2026-10-08 para los padres de composición: restaurar el padre restaura los hijos de su misma cascada (ver arriba). Restaurar un hijo sigue siendo individual.
- **Restaurar un hijo exige el padre activo (decisión 2026-10-07, reemplaza a la del 2026-09-28).** Padre e hijo se definen igual que para el borrado: si un registro activo bloquea el borrado de otro, es su hijo. Un hijo no se restaura mientras su padre siga borrado → `PARENT_DELETED`; primero se restaura el padre. Esto incluye las referencias a catálogos (por ejemplo, una `Person` no se restaura si su `Gender` está borrado).

### F. Entidades que nunca se restauran

Registros de seguridad y auditoría: restaurarlos reabriría sesiones, códigos o tokens revocados, o alteraría el historial.

`UserConnectionLog`, `UserRefreshToken`, `EmailOtpEnableCode`, `MfaLoginChallenge`, `PhoneVerificationCode`, `UserMfaRecoveryCode`, `TicketLog`.

No tienen endpoint de restauración. Ver sus registros borrados queda fuera de esta spec.

---

## Etapas

Cada etapa es un PR independiente, con sus propios tests. Solo se restauran entidades que hoy tienen un caso de uso de borrado (F0 confirma el inventario).

### Etapa 1 — Infraestructura común + catálogos

- Piezas A, B y el test de guarda de visibilidad (ver Tests).
- Catálogos de empresa: `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `Region`, `Area`, `Project`, `TicketStatus`, `TicketComplexity`, `TimeUnit`.
- Catálogos globales: `Country`, `Province`, `Municipality`, `StreetType`, `CommunicationChannel`, `IdentificationType`, `EntityStatus`, `SystemModule`, `SystemOption`.

Son las entidades más simples: sin padre (salvo `Province → Country`, `Municipality → Province`, `Region → Country`), y la validación de duplicado activo es la clave natural del índice.

#### Etapa 1 — Plan de implementación (Ajuste por SPEC 41, 2026-10-07)

La spec no traía pasos numerados; este plan los fija para la Etapa 1 sin cambiar su lógica.

- **F0 — Inventario.** Tabla de abajo: queries, padre, hijos que bloquean el borrado y clave de duplicado activo de cada entidad.
- **F1 — Dominio (A).** `BaseAuditableEntity.Restore()` / `IsDeleted` (no mapeada), `ApplicationUser.Restore()`, `ApplicationRole.Restore()` + unit tests.
- **F2 — Piezas comunes (B).** `SoftDeleteVisibility` y `SoftDeleteRestorer` en `2.Application/Common`, registro en DI + unit tests.
- **F3 — Borrado (E).** Eliminar `DeleteAsync` de `IGenericRepository`/`GenericRepository`; alinear los 19 `Delete<Entidad>CommandHandler` con la columna "Hijos activos" (error `<ENTIDAD>_IN_USE` con el detalle de los hijos activos en `errors`) + unit tests.
- **F4 — Restore + `includeDeleted` (C, D)**, por lotes: **F4a** `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `Area`, `Project` · **F4b** `TicketStatus`, `TicketComplexity`, `TimeUnit` · **F4c** `Country`, `Province`, `Municipality`, `Region` · **F4d** `StreetType`, `CommunicationChannel`, `IdentificationType`, `EntityStatus`, `SystemModule`, `SystemOption`. Por entidad: `Restore<Entity>Command`/`Validator`/`Handler`, `POST /{id}/restore`, `IncludeDeleted` (y `CompanyId?` vía `TenantResolver` en las de empresa) en listado y por id, `IsDeleted`/`DeletedOn` en el DTO, unit tests.
- **F5 — Integración.** Test de guarda de visibilidad sobre los 19 listados + `POST /{id}/restore` (Manager → 403; SuperAdmin → 200 y reaparece en el listado).
- **F6 — Verificación.** Build Release, unit tests con gate 90 %, suite de integración completa.

#### Etapa 1 — Inventario (F0, verificado en el código 2026-10-07)

Todas las queries son Dapper. Las de empresa filtran hoy por `currentUserService.CompanyId` (ninguna usa `TenantResolver`).

| Entidad | Ámbito | Listado / por id | Padre (`PARENT_DELETED`) | Hijos activos que bloquean el borrado (hoy chequeados → **faltan**) | Duplicado activo (clave del índice) |
|---|---|---|---|---|---|
| `Gender` | Empresa | `GetGenders` / `GetGenderById` | — | `Person` | `UX_Genders_Company_Code`, `UX_Genders_Company_Name` |
| `Industry` | Empresa | `GetIndustries` / `GetIndustryById` | — | `PersonBusinessProfile` | `UX_Industries_Company_Code`, `…_Name` |
| `TaxRegime` | Empresa | `GetTaxRegimes` / `GetTaxRegimeById` | — | `PersonBusinessProfile` | `UX_TaxRegimes_Company_Code`, `…_Name` |
| `IncomeRange` | Empresa | `GetIncomeRanges` / `GetIncomeRangeById` | — | `PersonFinancialProfile` | `UX_IncomeRanges_Company_DisplayName`, `…_DisplayOrder` |
| `Area` | Empresa | `GetAreas` / `GetAreaById` | `EntityStatus` | **`Ticket`, `TicketCompanyDefault`** | Sin índice único → no aplica |
| `Project` | Empresa | `GetProjects` / `GetProjectById` | `EntityStatus` | `Ticket`, **`TicketCompanyDefault`** | Sin índice único → no aplica |
| `TicketStatus` | Empresa | `GetTicketStatuses` / `GetTicketStatusById` (+ `GetSystemWideTicketStatuses`) | — | `Ticket`, **`TicketCompanyDefault`, `TicketStatusTransition`** | `UX_TicketStatuses_Company_Initial|Paused|Final` (solo si el restaurado tiene la marca) |
| `TicketComplexity` | Empresa | `GetTicketComplexities` / `GetTicketComplexityById` (+ `GetSystemWideTicketComplexities`) | `TimeUnit` | `Ticket`, **`TicketCompanyDefault`** | `UX_TicketComplexities_Company_Name` |
| `TimeUnit` | Empresa | `GetTimeUnits` / `GetTimeUnitById` (+ `GetSystemWideTimeUnits`) | — | `Ticket`, `TicketComplexity`, **`TicketCompanyDefault`** | `UX_TimeUnits_Company_Name` |
| `Region` | Empresa | `GetRegions` / `GetRegionById` | `Country` | `Province`, **`PersonAddress`** | `UX_Regions_Company_Country_Name`, `…_Code` (ignora `Code` nulo, SPEC 40) |
| `Country` | Global | `GetCountriesPaged` / `GetCountryById` | — | **`Province`, `Region`, `PersonAddress`** | Índice `IsoCode` sin filtro → no aplica |
| `Province` | Global | `GetProvinces` / `GetProvinceById` | `Country`; `Region` si `RegionId` no es nulo | `Municipality`, `PersonAddress` | Índice `(CountryId, Code)` sin filtro → no aplica |
| `Municipality` | Global | `GetMunicipalities` / `GetMunicipalityById` | `Province` | `PersonAddress` | Índice `(ProvinceId, Name)` sin filtro → no aplica |
| `StreetType` | Global | `GetStreetTypesPaged` / `GetStreetTypeById` | — | **`PersonAddress`** | Índices `Name`, `Abbreviation` sin filtro → no aplica |
| `CommunicationChannel` | Global | `GetCommunicationChannelsPaged` / `GetCommunicationChannelById` | — | **`Ticket`, `TicketCompanyDefault`, `TicketNotification`, `UserCommunicationChannel`** | Índice `Name` sin filtro → no aplica |
| `IdentificationType` | Global | `GetIdentificationTypes` / `GetIdentificationTypeById` | — | **`Person`** | Sin índice único → no aplica |
| `EntityStatus` | Global | `GetEntityStatus` / `GetEntityStatusById` | — | `Area`, `Project` | Índice `Code` sin filtro → no aplica |
| `SystemModule` | Global | `GetSystemModules` / `GetSystemModuleById` | — | `SystemOption`, **`CompanyModule`** | Índice `Name` sin filtro → no aplica |
| `SystemOption` | Global | `GetSystemOptionsPaged` / `GetSystemOptionById` | `SystemModule`; `SystemOption` padre si `ParentId` no es nulo | `SystemOption` (hijas), `RoleSystemOption` | Índice `(ModuleId, Route)` sin filtro → no aplica |

"Padre" es la inversa de "Hijos" (decisión 2026-10-07): si un registro bloquea el borrado de otro, no se restaura mientras ese otro siga borrado.

**Endpoints `system-wide`** (`GetSystemWideTimeUnits`, `GetSystemWideTicketStatuses`, `GetSystemWideTicketComplexities`, SPEC 37): solo `SuperAdmin`, cruzan empresas e incluyen borrados para que pueda decidir si los restaura. Se dejan sin cambios (decisión 2026-10-07) y quedan fuera del test de guarda de visibilidad; el resto de los roles no accede (`[Authorize(Roles = "SuperAdmin")]`).

"No aplica" en la última columna: el índice único de los catálogos globales no está filtrado por `GcRecord`, así que el registro borrado sigue ocupando la clave y no puede existir un activo duplicado; el chequeo se omite. `TicketLog` referencia `TicketStatus` y `TimeUnit` pero no bloquea el borrado (auditoría, sección F).


#### Etapa 1 — Decisiones de implementación (Ajuste por SPEC 41, 2026-10-07)

- **Error de rol:** un no-SuperAdmin que llega al handler de restore recibe `SUPERADMIN_REQUIRED` (HTTP 403); el endpoint ya tiene `[Authorize(Roles = "SuperAdmin")]`. `SaveChanges` sin filas afectadas → `RESTORE_FAILED` (mismo criterio que `DELETE_FAILED`). Mapeo HTTP común en `RestoreResponseExtensions`: `NOT_FOUND` → 404; `NOT_DELETED` / `PARENT_DELETED` / `ACTIVE_DUPLICATE_EXISTS` → 409.
- **SQL portable:** en lugar de `WHERE (@IncludeDeleted = 1 OR x.GcRecord = 0)` el handler agrega `AND x.GcRecord = 0` solo cuando `SoftDeleteVisibility` devuelve `false`. Mismo resultado, sin comparar un booleano con `1` (no compila en PostgreSQL, SPEC 50).
- **DTO:** los DTO heredan `SoftDeletableDto` (`JOIN.Application.DTO.Common`): `GcRecord` se selecciona pero no se serializa (`[JsonIgnore]`) y el DTO expone `IsDeleted` y `DeletedOn` calculados.
- **Empresa explícita:** catálogos de empresa → `?companyId=`; `Area` y `Project` → el header `X-Company-Id`, que hasta ahora se usaba sin validar contra el token (un Manager de A podía leer B). Sus 4 queries pasan por `TenantResolver`; sus commands (crear/editar/borrar) quedan pendientes (README).
- **Borrado:** los chequeos de hijos activos cuentan con `GetAllIncludingDeletedAsync(predicado)` (SQL, todas las empresas). Antes `GetAllAsync()` aplicaba el filtro de tenant: borrar un catálogo global ignoraba las referencias de otras empresas. Detalle en `errors`: `"Active <hijos>: N"`.
- **`system-wide`:** sin cambios de filtro; ahora seleccionan `GcRecord`, así `IsDeleted` es correcto en esos listados.
- **Catálogos globales:** sus índices únicos no están filtrados por `GcRecord`, así que un registro borrado bloquea crear otro con la misma clave (comportamiento previo, fuera de alcance; README).

### Etapa 2 — Personas

`Person`, `PersonAddress`, `PersonContact`, `PersonEmployment`, `PersonBusinessProfile`, `PersonFinancialProfile`, `Customer`.

- Hijos (`PersonAddress`, `PersonContact`, perfiles) → `PARENT_DELETED` si la `Person` está borrada.
- Restaurar una `Person` restaura solo la persona (sección E); direcciones y contactos se restauran individualmente.
- Invariantes existentes: al restaurar una dirección/contacto/empleo/perfil marcado como default, principal o actual, el coordinador correspondiente (`PersonAddressDefaultCoordinator`, `PersonEmploymentCurrentCoordinator`, `PersonBusinessProfileActiveCoordinator`, `PersonFinancialProfileCurrentCoordinator`) se aplica igual que en Create/Update: si ya hay otro default activo, el restaurado vuelve **sin** la marca.

#### Etapa 2 — Plan e inventario (Ajuste por SPEC 41, 2026-10-08)

Rama `spec-41-etapa-2-personas` (PR propio, sobre la Etapa 1).

- **F1** `DeletePerson`: referencias (`Customer`, `Ticket`, `UserPerson`) → `PERSON_IN_USE`; cascada de los 5 hijos de composición (decisión 2026-10-08). Tests existentes actualizados.
- **F2** `SoftDeleteRestorer`: hook previo a restaurar (quitar marca default/principal/actual si ya hay otra activa) y chequeo de padre para `ApplicationUser`.
- **F3** `Restore<Entity>` (command, validator, handler, `POST /{id}/restore`, unit tests) para las 7 entidades.
- **F4** `includeDeleted` en las 14 queries (listado + por id), `IsDeleted`/`DeletedOn` en DTO, tests de visibilidad.
- **F5** Integración: listados de personas en el test de guarda; restore Manager → 403, SuperAdmin → 200.
- **F6** Build Release, gate 90 %, suite de integración.

| Entidad | Padres (`PARENT_DELETED`) | Hijos activos que bloquean su borrado | Duplicado activo | Marca al restaurar |
|---|---|---|---|---|
| `Person` | `IdentificationType`; `Gender` si tiene | `PersonAddress`, `PersonContact`, `PersonEmployment`, `PersonBusinessProfile`, `PersonFinancialProfile`, `Customer`, `Ticket`, `UserPerson` | `UX_Persons_Company_IdType_IdNumber` | — |
| `PersonAddress` | `Person`, `Country`, `Province`, `Municipality`, `StreetType`; `Region` si tiene | — | — | `IsDefault` |
| `PersonContact` | `Person` | — | `UX_PersonContacts_Person_Type_Value` | `IsPrimary` |
| `PersonEmployment` | `Person` | — | — | `IsCurrent` |
| `PersonBusinessProfile` | `Person`, `Industry`, `TaxRegime` | — | — | `IsActive` (perfil activo) |
| `PersonFinancialProfile` | `Person`, `IncomeRange` | — | — | `IsCurrent` |
| `Customer` | `Person`, `ApplicationUser` | — | `UX_Customers_Company_CustomerCode`, `UX_Customers_Company_Person_User` | — |

**Decisión (2026-10-08):** `Ticket` y `UserPerson` también bloquean el borrado de una persona (cualquier referencia activa bloquea). `PersonContactPrimaryCoordinator` se aplica igual que los otros cuatro coordinadores. `GetPersonById` devuelve los hijos anidados solo activos (los JOINs mantienen `GcRecord = 0`).

#### Etapa 2 — Decisiones de implementación (Ajuste por SPEC 41, 2026-10-08)

- **`DeletePerson`** (actualizado 2026-10-08, cascada de composición): `Customer`, `Ticket` y `UserPerson` activos → `PERSON_IN_USE` (HTTP 409) y no se borra nada; si no hay referencias, la persona y sus 5 hijos de composición se marcan con el mismo sello (`PersonCascadeCoordinator`). `RestorePerson` restaura los hijos de ese sello: valida catálogos (`PARENT_DELETED`) y contactos duplicados (`ACTIVE_DUPLICATE_EXISTS`) antes de tocar nada y conserva un solo titular por marca.
- **Marcas al restaurar:** `SoftDeleteRestorer` tiene un hook `beforeRestore`. Si otra fila activa de la misma persona ya tiene la marca (`IsDefault` en direcciones, `IsPrimary` por tipo de contacto, `IsCurrent` en empleos y perfiles financieros, `IsActive` en perfiles de negocio), el restaurado vuelve sin ella.
- **Padres Identity:** `IsParentDeletedAsync` acepta cualquier `IAuditableEntity`, así `Customer` valida su `ApplicationUser`.
- **Listados por persona** (`GET /{recurso}/person/{personId}`): aceptan `includeDeleted` y `companyId` como los demás. `GetPersonById` aplica `includeDeleted` a la persona; sus hijos anidados siguen siendo solo activos.
- **`Customer`:** su SQL compartido hace `INNER JOIN` con personas activas; un cliente borrado de una persona borrada aparece al restaurar la persona (el orden que exige la regla de padres). No se pasó a `LEFT JOIN` para no exponer clientes antiguos de personas ya borradas.

- **Menú (`SystemModule` / `SystemOption`):** `SystemOptionCascadeCoordinator` borra el subárbol activo en todos los niveles con el sello del padre; `CompanyModule` y `RoleSystemOption` activos en el subárbol → `_IN_USE` (`DELETE /SystemModules` ahora responde 409). Restaurar un módulo u opción trae de vuelta las opciones de su misma cascada cuyo padre quede activo. `DELETE /SystemOptions` sigue respondiendo 200 con `isSuccess=false` ante errores (comportamiento previo del controller, no se tocó).

### Etapa 3 — Seguridad y empresas

`ApplicationUser`, `ApplicationRole`, `Company`, `CompanyModule`, `UserCompany`, `RoleCompany`, `UserRoleCompany`, `RoleSystemOption`, `UserCommunicationChannel`, `UserPerson`.

- Restaurar un usuario **no** restaura sus membresías (`UserCompany`, `UserRoleCompany`) ni sus sesiones: cada una se restaura explícitamente.
- Restaurar un usuario queda registrado en el log de eventos de seguridad (`SecurityEventLog`).
- Invalidar la caché de permisos (`permissions:v2:{companyId}:{userId}`) al restaurar `UserRoleCompany`, `RoleCompany` o `RoleSystemOption`.

#### Etapa 3 — Plan e inventario (Ajuste por SPEC 41, 2026-10-08)

Rama `spec-41-etapa-3-seguridad` (desde `main` con las etapas 1 y 2). Dos partes: **(1)** que el SuperAdmin vea y restaure; **(2)** cambios de lógica del borrado de roles y empresas.

**Alcance verificado (F0).** Solo se restauran entidades con caso de uso de borrado:

| Entidad | Borrado hoy | Queries (`includeDeleted`) | Padres (`PARENT_DELETED`) | Duplicado activo | Restore |
|---|---|---|---|---|---|
| `ApplicationRole` | `DELETE /Roles/{id}` | `GetRolesDetailed`, `GetRoleById` (EF) | — | — (índice `NormalizedName` sin filtro) | `POST /Roles/{id}/restore` + cascada |
| `Company` | `DELETE /Companies/{id}` (SuperAdmin) | `GetCompaniesPaged`, `GetCompanyById` (Dapper) | — | — (índice `TaxId` sin filtro) | `POST /Companies/{id}/restore` |
| `CompanyModule` | `DELETE /CompanyModules/{id}` (SuperAdmin) | `GetCompanyModules`, `GetCompanyModulesById` (Dapper) | `Company`, `SystemModule` | — | `POST /CompanyModules/{id}/restore` |
| `RoleCompany` | `DELETE /RoleCompanies/{id}` | `GetRoleCompaniesPaged`, `GetRoleCompanyById` (EF) | `ApplicationRole`, `Company` | `(RoleId, CompanyId)` filtrado | `POST /RoleCompanies/{id}/restore` |
| `RoleSystemOption` | `DELETE /RoleSystemOptions/{id}` | `GetRoleSystemOptionsPaged`, `GetRoleSystemOptionById`, `GetSuperAdminAllRoleSystemOptionsPaged` (Dapper) | `ApplicationRole`, `SystemOption` | — (índice sin filtro) | `POST /RoleSystemOptions/{id}/restore` |
| `UserCompany` | `DELETE /Users/{userId}/companies/{companyId}` (borra también sus `UserRoleCompany`) | `GetUserCompanies` (Dapper) | `Company` | — (índice sin filtro); marca `IsDefault` | `POST /Users/{userId}/companies/{companyId}/restore` + cascada |

**Fuera de la Etapa 3:** `ApplicationUser`, `UserCommunicationChannel` y `UserPerson` no tienen caso de uso de borrado (los usuarios solo se activan/desactivan con `ChangeUserStatus`), por lo que no se restauran ni aplica el registro en `SecurityEventLog`. `UserRoleCompany` no tiene endpoint propio: se borra desde `ReplaceUserRoles`, `BulkUpdateUserRoles` y `RemoveUserCompany`, se reactiva reasignando el rol (camino existente) y se restaura en cascada con su `UserCompany`. `GetRoleSystemOptionMatrix` y `GetUsersByRoleId` no listan la entidad borrable y no cambian.

**Cambios de lógica (decisión 2026-10-08):**
- **`DeleteRole`:** composición → borra en cascada los `RoleSystemOption` y `RoleCompany` activos del rol (todas las empresas), con el mismo sello. Referencia → `UserRoleCompany` activos en **cualquier** empresa bloquean (`ROLE_HAS_USERS`); hoy solo cuenta la empresa del token. `RestoreRole` trae de vuelta los permisos y vínculos de su misma cascada.
- **`DeleteCompany`:** se bloquea (`COMPANY_IN_USE`) si la empresa tiene **cualquier** dato activo: un servicio de Persistence recorre el modelo de EF y cuenta las filas activas de toda entidad `BaseTenantEntity` de esa `CompanyId` (cubre entidades futuras sin mantener una lista). Sin cascada.
- **`RestoreUserCompany`:** restaura la membresía y sus `UserRoleCompany` de la misma cascada; si el usuario ya tiene otra empresa default activa, vuelve sin `IsDefault`.
- **Caché de permisos:** restaurar `UserCompany`, `RoleCompany` o `RoleSystemOption` invalida `permissions:v2:{companyId}:{userId}` de los usuarios afectados (`IPermissionService.InvalidateUserCacheAsync`).

**Implementación:** `ApplicationRole` no hereda de `BaseAuditableEntity`, así que su restore no usa `SoftDeleteRestorer.RestoreAsync` y va por el repositorio de roles (mismos códigos de error). Las queries EF de roles y `RoleCompany` agregan `IgnoreQueryFilters()` solo cuando `SoftDeleteVisibility` lo permite.

**Pasos:**
- **F1** Lógica de borrado: `DeleteRole` (cascada + bloqueo entre empresas), `DeleteCompany` (bloqueo por datos activos) + unit tests.
- **F2** Restore de las 6 entidades (commands, handlers, validators, cascadas, invalidación de caché) + unit tests.
- **F3** `includeDeleted` en las 13 queries + DTO + tests de visibilidad.
- **F4** Endpoints `POST …/restore` y parámetros `includeDeleted`/`companyId` en los GET.
- **F5** Integración: listados de la Etapa 3 en el test de guarda; restore Manager → 403 / SuperAdmin → 200; `DeleteCompany` con datos → 409; `DeleteRole` en cascada.
- **F6** Build Release, gate 90 %, suite de integración completa.

#### Etapa 3 — Decisiones de implementación (Ajuste por SPEC 41, 2026-10-08)

- **`DeleteRole`:** `UserRoleCompany` activos de cualquier empresa → `ROLE_HAS_USERS` (409); si no hay, el rol, sus `RoleSystemOption` y sus `RoleCompany` se marcan con el mismo sello. `RestoreRole` (handler propio, el rol es de Identity) trae de vuelta los de ese sello cuyo `SystemOption`/`Company` siga activo y sin vínculo activo duplicado; el resto queda borrado.
- **`DeleteCompany`:** `ITenantDataInspector` (Persistence) recorre el modelo de EF y cuenta filas activas de toda entidad con `CompanyId` + `GcRecord`; si hay alguna → `COMPANY_IN_USE` (409) con `"Active <Entidad>: N"`.
- **`RestoreUserCompany`** (`POST /Users/{userId}/companies/{companyId}/restore`): restaura la membresía y sus `UserRoleCompany` del mismo sello (rol activo), quita `IsDefault` si ya hay otra default activa e invalida la caché del usuario. `RestoreRoleCompany` y `RestoreRoleSystemOption` invalidan la caché de los usuarios con ese rol en esa empresa (`RolePermissionCacheInvalidator`).
- **Acceso a listados (cambio de contrato):** `GET /Roles/detailed`, `GET /Roles/{id}`, `GET /Companies` y `GET /Companies/{id}` eran solo `SuperAdminCompany`; se agrega `SuperAdmin` para que pueda ver los borrados. `GET /Users/{id}/companies` ya era solo `SuperAdmin`.
- **Hueco cerrado en roles:** `GET /Roles/detailed?isActive=false` (o sin `isActive`) devolvía roles borrados a cualquier usuario con lectura. Ahora sin `includeDeleted` (SuperAdmin) el listado es solo de activos y `isActive=false` no devuelve filas; con `includeDeleted` `isActive` conserva su sentido. Mismo criterio en `RoleCompanies`.
- **`RoleDto`** es un record posicional (Dapper lo construye por constructor), así que agrega `GcRecord` como último parámetro con `IsDeleted`/`DeletedOn` calculados en vez de heredar `SoftDeletableDto`.
- **Sin auditoría de restauración:** `AuditAction` solo tiene Created/Updated/Deleted; restaurar un rol no escribe en la bitácora (la spec solo pedía `SecurityEventLog` para usuarios, que no se borran).

### Etapa 4 — Tickets

`Ticket`, `TicketNotification`, `TicketCompanyDefault`, y las entidades de SPEC 34-37 y 99 que ya estén implementadas.

- `TicketLog` no se restaura (sección E).
- Restaurar un `Ticket` genera una entrada en `TicketLog` (auditoría del ticket).

#### Etapa 4 — Plan e inventario (2026-10-08)

Rama `spec-41-etapa-4-tickets` (desde `main` con las etapas 1 a 3).

**Alcance verificado (F0).** Solo se restauran entidades con caso de uso de borrado. `TicketNotification` no tiene borrado (queda fuera, igual que `ApplicationUser` en la Etapa 3); `TicketLog` nunca se restaura (sección F). BE-99 no está implementada.

| Entidad | Borrado hoy | Queries (`includeDeleted`) | Padres (`PARENT_DELETED`) | Duplicado activo | Restore |
|---|---|---|---|---|---|
| `Ticket` | `DELETE /Tickets/{id}` (solo marca el ticket) | `GetTickets`, `GetTicketById` (Dapper) | `TicketStatus`, `TicketComplexity`, `TimeUnit`, `CommunicationChannel`; `Person`, `Project`, `Area`, ticket precedente si los tiene | — (índice `(CompanyId, Code)` sin filtro: el código borrado sigue ocupado) | `POST /Tickets/{id}/restore` + cascada de adjuntos + `TicketLog` |
| `TicketDocument` | `DELETE /Tickets/{ticketId}/documents/{id}` | `GetTicketDocuments` (Dapper). `DownloadTicketDocument` sigue solo activos | `Ticket` | — ; tope `MaxFilesPerTicket` | `POST /Tickets/{ticketId}/documents/{id}/restore` |
| `TicketCompanyDefault` | `DELETE /TicketCompanyDefaults/{id}` | `GetTicketCompanyDefaults`, `GetTicketCompanyDefaultById` (Dapper) | `TicketStatus`, `TicketComplexity`, `TimeUnit`, `Area`, `Project`, `CommunicationChannel` (los que tenga) | `CompanyId` (índice pasa a filtrado, ver decisiones) | `POST /TicketCompanyDefaults/{id}/restore` |
| `TicketAttachmentSettings` | `DELETE /TicketAttachmentSettings/{id}` | `GetTicketAttachmentSettings`, `GetTicketAttachmentSettingById` (Dapper) | — | `UX_TicketAttachmentSettings_Company_Active` | `POST /TicketAttachmentSettings/{id}/restore` |
| `TicketStatusTransition` | `DELETE /TicketStatusTransitions/{id}` | `GetTicketStatusTransitions` (Dapper, sin "por id") | `TicketStatus` origen y destino | `(CompanyId, FromStatusId, ToStatusId)` filtrado | `POST /TicketStatusTransitions/{id}/restore` |
| `TicketUserCompany` | `DELETE /TicketUserCompanies/{id}` | `GetTicketUserCompanies`, `GetTicketUserCompanyById` (Dapper) | `ApplicationUser`; membresía `UserCompany` activa en la empresa | `(UserId, CompanyId)` filtrado | `POST /TicketUserCompanies/{id}/restore` |

Los endpoints `system-wide` (`GetSystemWideTickets`, `GetSystemWideTicketCompanyDefaults`, `GetSystemWideTicketAttachmentSettings`, `GetSystemWideTicketUserCompanies`) quedan sin cambios (decisión 2026-10-07). Las queries de empresa pasan a `TenantResolver` (`?companyId=` para SuperAdmin), como en las etapas anteriores.

**Decisiones (2026-10-08):**
- **`DeleteTicket`:** composición → los `TicketDocument` activos del ticket se borran en cascada con el mismo sello; referencia → tickets de seguimiento activos (`PrecedentTicketId` = el ticket) bloquean: `TICKET_IN_USE` (409) con `"Active follow-up tickets: N"`. `TicketNotification` y `TicketLog` no se tocan. `RestoreTicket` restaura los adjuntos de su misma cascada.
- **Bitácora:** nuevo `LogType.Restoration = 6` (la columna es entera, sin migración). `RestoreTicket` agrega una entrada pública con el usuario que restaura.
- **Índice de `TicketCompanyDefault`:** el índice único por `CompanyId` no estaba filtrado (SPEC 40 no lo incluyó): borrar la configuración y crear otra fallaba con un 500. Se filtra por `GcRecord = 0` (migración, patrón SPEC 40) y el restore chequea `ACTIVE_DUPLICATE_EXISTS`.
- **`TicketDocument`:** restaurar respeta `MaxFilesPerTicket` (`MAX_FILES_PER_TICKET_REACHED`, 409); la cuota diaria no aplica. El archivo físico no se borra con el soft delete, así que restaurar no toca el storage.
- **`TicketUserCompany`:** restaurar exige que el usuario siga siendo miembro activo de la empresa (`UserCompany`) → si no, `PARENT_DELETED`. Mismo criterio que el alta (`USER_NOT_IN_TENANT`).

**Pasos:**
- **F1** Lógica de borrado: `DeleteTicket` (cascada de adjuntos + bloqueo por seguimiento) + `LogType.Restoration` + migración del índice filtrado de `TicketCompanyDefault` + unit tests.
- **F2** Restore de las 6 entidades (commands, validators, handlers, cascada, `TicketLog`) + unit tests.
- **F3** `includeDeleted` + `TenantResolver` en las 10 queries, `IsDeleted`/`DeletedOn` en los DTO + tests de visibilidad.
- **F4** Endpoints `POST …/restore` y parámetros `includeDeleted`/`companyId` en los GET; `DELETE /Tickets` mapea `TICKET_IN_USE` a 409.
- **F5** Integración: listados de la Etapa 4 en el test de guarda; restore Manager → 403 / SuperAdmin → 200; `DeleteTicket` en cascada y bloqueado.
- **F6** Build Release, gate 90 %, suite de integración completa. Actualizar FE-15, FE-16 y FE-19 (`join_frontb`).

#### Etapa 4 — Decisiones de implementación (Ajuste por SPEC 41, 2026-10-08)

- **`DeleteTicket`:** `TicketCascadeCoordinator` cuenta los tickets de seguimiento activos (`TICKET_IN_USE`, 409, `"Active follow-up tickets: N"`) y, si no hay, marca los adjuntos activos con el sello del ticket. `RestoreTicket` restaura los adjuntos de ese sello y agrega la entrada `Restoration` con `UpdateAsync` del ticket (el repositorio marca como `Added` el `TicketLog` nuevo; sin eso EF lo trataba como fila existente y respondía 500). `GetTicketById` traduce `LogType = 6` a `"Restoration"`.
- **`RestoreTicketDocument`** (`POST /tickets/{ticketId}/documents/{id}/restore`): el adjunto debe ser del ticket de la ruta (si no, `NOT_FOUND`); `MAX_FILES_PER_TICKET_REACHED` (409) solo si la empresa tiene `TicketAttachmentSettings` activa; sin configuración no hay tope que aplicar.
- **`RestoreTicketUserCompany`:** el usuario debe existir y tener una membresía `UserCompany` activa en la empresa; si no, `PARENT_DELETED`.
- **Índice:** migración `Spec41FilterTicketCompanyDefaultsIndex` reemplaza `IX_TicketCompanyDefaults_CompanyId` por `UX_TicketCompanyDefaults_Company_Active` (`[GcRecord] = 0`).
- **DTO (cambio de contrato):** los 7 DTO de tickets heredan `SoftDeletableDto`. `TicketCompanyDefaultDto` y `TicketAttachmentSettingsDto` ya exponían `gcRecord` e `isDeleted` (los usan los listados `system-wide`): `gcRecord` deja de serializarse y se suma `deletedOn`. `GetSystemWideTickets` ahora selecciona `GcRecord`, así `isDeleted` es correcto en ese listado (su filtro no cambia).
- **Sin cambios:** `DownloadTicketDocument` sigue descargando solo adjuntos activos; `TicketNotification` no tiene borrado ni restore.

---

## Tests

### Test de guarda de visibilidad (Etapa 1)

Test de integración parametrizado sobre todos los endpoints `GET` de listado:

- Sembrar un registro activo y uno borrado en la empresa A, y uno activo en la empresa B.
- Autenticado como **Manager de A** con `?includeDeleted=true` → solo ve el activo de A (confirma que el parámetro se ignora y que no hay fuga hacia B).
- Autenticado como **SuperAdminCompany de A** con `?includeDeleted=true` → igual que el Manager.
- Autenticado como **SuperAdmin** (empresa activa A) con `?includeDeleted=true` → ve el activo y el borrado de A, no el de B.
- Autenticado como **SuperAdmin** con `?includeDeleted=true&companyId=B` → ve el de B.

Este test también cubre el hueco que SPEC 38 dejó abierto: una query Dapper nueva sin `CompanyId = @TenantId` falla aquí. Cada etapa agrega sus endpoints a la parametrización.

### Tests por entidad (cada etapa)

- Unit tests (gate de 90%) de cada `Restore<Entity>CommandHandler`: no encontrado, otra empresa → `NOT_FOUND`, no borrado → `NOT_DELETED`, padre borrado → `PARENT_DELETED`, duplicado activo → `ACTIVE_DUPLICATE_EXISTS`, no SuperAdmin → error, éxito.
- Unit tests de `SoftDeleteVisibility` y `SoftDeleteRestorer`.
- Integración: `POST /{id}/restore` como Manager → 403; como SuperAdmin → 200 y el registro vuelve a aparecer en el listado normal.

---

## Acceptance criteria

- [ ] Ningún rol distinto de `SuperAdmin` puede ver registros borrados ni datos de otra empresa, aunque envíe `includeDeleted=true` o `companyId`.
- [ ] `SuperAdmin` ve activos y borrados con `includeDeleted=true`, dentro de la empresa resuelta por `TenantResolver`.
- [ ] `SuperAdmin` puede restaurar cualquier entidad de las etapas implementadas; las de la sección E no son restaurables.
- [ ] Restaurar nunca produce un 500 por índice único: el duplicado activo se detecta antes y devuelve `ACTIVE_DUPLICATE_EXISTS`.
- [ ] No se puede restaurar un hijo cuyo padre está borrado.
- [ ] Un padre con referencias activas no se puede borrar (`<ENTIDAD>_IN_USE` lista las referencias). Un padre de composición (`Person`, `SystemModule`, `SystemOption`) borra en cascada (soft delete) a sus hijos de composición, y restaurarlo restaura los de esa misma cascada.
- [ ] No existe ningún borrado físico en el código (`DeleteAsync` eliminado de `IGenericRepository`).
- [ ] Los invariantes de default/principal/actual se mantienen al restaurar.
- [ ] El test de guarda de visibilidad cubre todos los endpoints de listado de las etapas implementadas.
- [ ] Gate de cobertura ≥ 90% y suite de integración en verde en cada etapa.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| `GetIncludingDeletedAsync` ignora el filtro de tenant. | Solo se usa dentro de `SoftDeleteRestorer`, que valida `CompanyId` antes de tocar el registro. |
| Un handler nuevo toma `IncludeDeleted` directo del request. | Convención: siempre pasa por `SoftDeleteVisibility`. El test de guarda de visibilidad lo detecta. |
| Restaurar un registro cuyas referencias (por ejemplo, el `Gender` de una `Person`) siguen borradas. | No se permite: `PARENT_DELETED` hasta restaurar el padre (decisión 2026-10-07). |
| Volumen: ~40 entidades × (command + handler + validator + tests + endpoint). | Etapas independientes; `SoftDeleteRestorer` concentra la lógica común. |

---

## Preguntas abiertas

Todas resueltas el 2026-09-28:

- Una empresa por request, salvo el catálogo de empresas.
- Restauración individual, sin cascada.
- Basta con el rol `SuperAdmin` para restaurar.
- ~~Se permite restaurar con referencias a catálogos borrados.~~ Reemplazada el 2026-10-07: un hijo no se restaura mientras su padre (incluido un catálogo referenciado) siga borrado.
- No hay vista "solo borrados"; solo `includeDeleted=true`.
- Un padre con hijos activos no se puede borrar: error `<ENTIDAD>_IN_USE` con el detalle de los hijos. Direcciones y contactos también bloquean el borrado de una persona.
