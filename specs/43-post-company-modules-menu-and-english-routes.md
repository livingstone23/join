# SPEC 43-post — Módulos por empresa: módulos base, opciones conectadas a su módulo, menú filtrado y rutas en inglés

> **Status:** Borrador
> **Origen:** copia de SPEC 43 de `main` (estado allí al copiarla: Borrador), adaptada a PostgreSQL para el fork `main_postgresql`. Se implementa **desde cero** en el fork, sin traer commits de `main` (decisión del usuario, 2026-10-09).
> **Rama de trabajo:** `spec-43-post-company-modules-menu-and-english-routes`, creada desde `main_postgresql`; PR hacia `main_postgresql`, nunca hacia `main`.
> **Lectura en el fork:** dentro de esta spec, toda referencia a una spec de la serie copiada (40–49, 51, 99) se lee como su versión `-post` ("SPEC 41" = SPEC 41-post), y aplican las convenciones de `specs/README.md` → "Serie `-post`". Donde el texto copiado de `main` y la sección "Adaptación a PostgreSQL" difieren, prevalece esta última.
> **Depends on:** SPEC 39 (fork). Ninguna otra para implementarse.
> **Related:** SPEC 44 (módulo `Calendar`, opcional), SPEC 48 (bloqueo de APIs por módulo e interruptor `Modules:EnforceCompanyModules`, que también condiciona el filtro del menú de esta spec), SPEC 45 (opciones de menú del agente y de personas pendientes), SPEC 48.
> **Date:** 2026-10-09 (copia `-post`; original: 2026-10-01)
> **Objective:** Dejar `CompanyModule` como la fuente de verdad de qué módulos tiene cada empresa y de qué se pinta en su menú: marcar qué módulos son **base**, conectar cada opción de menú (`SystemOption`) con su **módulo real** (hoy todas cuelgan de "Administracion"), filtrar el menú lateral por los módulos activos de la empresa, restringir al SuperAdmin la habilitación de módulos, y traducir al inglés los nombres de módulos, opciones y rutas, con guion como separador.

---

## Adaptación a PostgreSQL (fork `main_postgresql`)

| Tema | En `main` (SQL Server) | En esta versión |
|---|---|---|
| Columna `IsBase` | `bit NOT NULL DEFAULT 0` | `isbase boolean NOT NULL DEFAULT FALSE`; migración `AddSystemModuleIsBase` generada contra Npgsql. |
| Filtro del menú (sección D) | SQL con `= 1` | SQL de PostgreSQL con `= TRUE` y nombres en minúsculas (abajo). |
| Orden de módulos | `sm.[Order]` | `sm."order"` (palabra reservada; el fork ya la usa así en `GetSystemModules*`). |
| Renombres, `ModuleId`, checksum, `IsBase` en la siembra | EF/LINQ | Igual (independiente del motor). La siembra de opciones corre en una transacción de EF. |
| Tests | Integración sobre `CustomWebApplicationFactory` | `SystemMenuSeedPostgreSqlTests` (base con los nombres anteriores → nombres nuevos, mismos `Id`, sin duplicados) y `SidebarMenuPostgreSqlTests` sobre `PostgreSqlWebApplicationFactory`. Los unitarios de `GetSidebarMenuQueryHandler` afirman el SQL de PostgreSQL. |

Filtro del menú en el fork:

```sql
INNER JOIN admin.systemmodules sm
    ON sm.id = so.moduleid
   AND sm.isactive = TRUE
   AND sm.gcrecord = 0
INNER JOIN admin.companymodules cm
    ON cm.moduleid = sm.id
   AND cm.companyid = @CompanyId
   AND cm.isactive = TRUE
   AND cm.gcrecord = 0
```

Verificación con Docker (convención 12 del README): la base nueva del contenedor nace con los nombres anteriores (es la seed del fork antes de esta spec); al arrancar con esta spec quedan los nombres y rutas nuevos con los mismos `Id`, y el menú de un usuario no SuperAdmin solo muestra los módulos activos de su empresa (con `Modules:EnforceCompanyModules = true` si SPEC 48-post ya está).

---

## Por qué existe esta spec

Al diseñar el módulo Calendario (SPEC 44) se decidió que un módulo se habilita por empresa y que, sin el módulo activo, la empresa no puede usarlo. La revisión del código (2026-10-01) mostró que hoy `CompanyModule` no gobierna nada:

1. **`SeedCompanyModulesAsync`** (`DatabaseSeeder.cs`) asigna **todos** los módulos activos a **todas** las empresas activas, y solo en la seed inicial. No existe el concepto de módulo base u opcional.
2. **Todas las `SystemOptions` se guardan con `ModuleId` = "Administracion"** (`SeedSystemOptionsAsync` busca ese módulo y lo asigna a cada opción), sin importar si la opción es de Tickets, Clientes o Seguridad. La relación opción → módulo no significa nada.
3. **`GetSidebarMenuQueryHandler`** arma el menú cruzando `SystemOptions`, `RoleSystemOptions` y `UserRoleCompanies`, sin consultar `CompanyModule` ni `SystemModule`. Una empresa ve opciones de módulos que no tiene.
4. **`CompanyModulesController`** exige `[Authorize(Roles = "SuperAdminCompany")]` en todas sus acciones: el administrador de una empresa puede habilitarse módulos a sí mismo, y un SuperAdmin sin la marca `SuperAdminCompany` no puede.
5. Los nombres y rutas mezclan idiomas y formatos (`/administracion/...`, `/Clientes/Cliente`, `/ManejoTickets/...`, `/administracion/Street-types`), mientras que las páginas ya hechas en el frontend usan inglés con guion (`/security/system-options`).

## Decisiones acordadas (2026-10-01)

| # | Decisión |
|---|---|
| A1 | Los módulos son **base** u **opcionales**. Se agrega `SystemModule.IsBase`. Por ahora se marcan como base los cuatro módulos existentes; `Calendar` (SPEC 44) es opcional. La seed asigna a las empresas **solo los módulos base**. |
| A2 | Cada fila de la seed de opciones declara **su módulo**. La seed corrige el `ModuleId` de las opciones existentes. |
| A3 | Las empresas nuevas **nacen sin permisos por rol** (`RoleSystemOptions`). El método que los llene es una etapa futura. |
| A4 | Los roles privilegiados (`Admin`, `SuperAdminCompany`) **mantienen acceso total** a todas las opciones, incluidos los catálogos globales del calendario. |
| A5 | El **menú lateral** oculta las opciones de los módulos que la empresa no tiene activos. El bloqueo de las APIs queda para la SPEC 48, y ambos dependen del interruptor `Modules:EnforceCompanyModules`. |
| A6 | La asignación automática de módulos base a empresas **nuevas** o a empresas existentes **queda para una etapa futura**. Por ahora se prueba con las empresas existentes. |
| A7 | **Solo el SuperAdmin** habilita o desactiva módulos de una empresa. `SuperAdminCompany` solo consulta los de su empresa. |
| A8 | El menú del **SuperAdmin no se filtra**: sigue viendo todas las opciones. |
| A9 | Desactivar un módulo **conserva** los datos y los permisos por rol; solo se ocultan. Al reactivarlo, todo vuelve como estaba. |
| A10 | Módulos, opciones y rutas se traducen **al inglés en esta spec**, con **guion** como separador en todo el sistema. |

---

## Scope

**In:**

### A. Dominio

- `src/1.Domain/Admin/SystemModule.cs`: nueva propiedad `bool IsBase` (default `false`), con este comentario XML:

```csharp
/// <summary>
/// Indicates whether this module is a BASE module: part of the minimum set every company must have.
/// Base modules are the ones the seed assigns to companies in <c>Admin.CompanyModules</c>; optional
/// modules (IsBase = false, e.g. "Calendar") are enabled per company by a SuperAdmin.
/// <para>
/// This flag does NOT control access by itself: what a company can see and use is always decided by
/// its <see cref="CompanyModule"/> rows (active or not). IsBase only decides which modules are
/// assigned automatically.
/// </para>
/// </summary>
public bool IsBase { get; set; }
```

### B. Persistencia

- `SystemModuleConfiguration`: `IsBase` requerido, default `false`.
- Migración `AddSystemModuleIsBase`: solo la columna. Los datos (marcar base, renombrar, corregir `ModuleId`) los hace la seed, que es idempotente y se ejecuta después de cada migración pendiente.

### C. Seed (`src/3.Persistence/Seed/DatabaseSeeder.cs`)

**C1. `SeedSystemModulesAsync`**

| Nombre actual | Nombre nuevo | Description | IsBase | Order |
|---|---|---|---|---|
| `Administracion` | `Administration` | Administrative management module | Sí | 1 |
| `Clientes` | `Customers` | Customer management module | Sí | 2 |
| `Tickets` | `Tickets` | Ticket management module | Sí | 3 |
| — (SPEC 44) | `Calendar` | Calendar and activities module | **No** | 4 |
| `Seguridad` | `Security` | Security module | Sí | 99 |

- **Renombre sin duplicar:** se agrega un mapa `LegacySystemModuleNames` (`Administracion` → `Administration`, `Clientes` → `Customers`, `Seguridad` → `Security`). Antes de buscar por nombre nuevo, la seed busca por el nombre anterior y, si lo encuentra, **actualiza la fila existente** (mismo `Id`, así `CompanyModules` y `SystemOptions` siguen apuntando bien).
- Las filas existentes se actualizan en `IsBase`, `Description` y `Order` según la tabla.

**C2. `SeedCompanyModulesAsync`**

- Asigna solo los módulos con `IsActive = true` **y** `IsBase = true` (hoy asigna todos los activos).
- **No** desactiva ni borra asignaciones existentes (A9). Una empresa que hoy tiene un módulo lo conserva.
- Sigue ejecutándose **solo en la seed inicial**, como hoy (A6: sin cambios de cuándo se asigna).

**C3. `SystemOptionSeed` y `SeedSystemOptionsAsync`**

- `SystemOptionSeed` agrega el parámetro `string ModuleName` (nombre del `SystemModule`).
- `SeedSystemOptionsAsync`:
  - ya **no** busca el módulo "Administracion" ni filtra las opciones existentes por ese módulo; busca entre **todas** las opciones;
  - resuelve el `ModuleId` de cada seed por `ModuleName`; si el módulo no existe, registra un warning y omite la opción;
  - en opciones existentes, **corrige el `ModuleId`** si no coincide (A2);
  - las opciones hijas usan el módulo de su seed; se valida que coincida con el de su padre (warning si no).
- **Renombre sin duplicar:** mapa `LegacySystemOptionNames` (nombre anterior → nombre nuevo). Una opción existente se reconoce por nombre nuevo, ruta nueva, **nombre anterior o ruta anterior**, y se actualiza en el lugar (mismo `Id`; sus `RoleSystemOptions` no se pierden).

**C4. Tabla de traducción de opciones** (fila completa de seed en F3)

| Módulo | Nombre anterior → nuevo | Ruta anterior → nueva | ControllerName (sin cambios) |
|---|---|---|---|
| Administration | `Administracion` → `Administration` | `/administracion` → `/administration` | — |
| Administration | `Paises` → `Countries` | `/administracion/countries` → `/administration/countries` | `Countries` |
| Administration | `Regions` | `/administracion/regions` → `/administration/regions` | `Regions` |
| Administration | `Provinces` | → `/administration/provinces` | `Provinces` |
| Administration | `Municipalities` | → `/administration/municipalities` | `Municipalities` |
| Administration | `IdentificationTypes` | → `/administration/identification-types` | `IdentificationTypes` |
| Administration | `StreetTypes` | `/administracion/Street-types` → `/administration/street-types` | `StreetTypes` |
| Administration | `Areas` | → `/administration/areas` | `Areas` |
| Administration | `Projects` | → `/administration/projects` | `Projects` |
| Administration | `SystemModules` | → `/administration/system-modules` | `SystemModules` |
| Administration | `EntityStatuses` | → `/administration/entity-statuses` | `EntityStatuses` |
| Administration | `CompanyModules` | → `/administration/company-modules` | `CompanyModules` |
| Administration | `CommunicationChannels` | → `/administration/communication-channels` | `CommunicationChannels` |
| Administration | `Genero` → `Genders` | `/administracion/gender` → `/administration/genders` | `Genders` |
| Administration | `Industria` → `Industries` | `/administracion/industry` → `/administration/industries` | `Industries` |
| Administration | `Rango Ingreso` → `IncomeRanges` | `/administracion/incomerange` → `/administration/income-ranges` | `IncomeRanges` |
| Administration | `Regimen Tributario` → `TaxRegimes` | `/administracion/taxregime` → `/administration/tax-regimes` | `TaxRegimes` |
| Customers | `Persons` (padre) | `/Clientes` → `/customers` | — |
| Customers | `Clientes` → `Persons` | `/Clientes/Cliente` → `/customers/persons` | `Persons` |
| Customers | `Customers` | `/Clientes/customers` → `/customers/customers` | `Customers` |
| Tickets | `ManejoTickets` → `TicketManagement` (padre) | `/ManejoTickets` → `/tickets` | — |
| Tickets | `Tickets` | `/ManejoTickets/tickets` → `/tickets/tickets` | `Tickets` |
| Tickets | `TimeUnits`, `TicketComplexities`, `TicketStatuses`, `TicketCompanyDefaults`, `TicketUserCompanies`, `TicketAttachmentSettings`, `TicketDocuments` | `/ManejoTickets/<x>` → `/tickets/<x>` (mismo segmento en guion) | sin cambios |
| Security | `Seguridad` → `Security` (padre) | `/security` | — |
| Security | `Usuarios` → `Users` | `/security/users` | `Users` |
| Security | `Roles` | `/security/roles` | `Roles` |
| Security | `Compañias` → `Companies` | `/security/companies` | `Companies` |
| Security | `RoleCompanies` | `/security/role-companies` | `RoleCompanies` |
| Security | `SystemOption` → `SystemOptions` | `/security/system-options` | `SystemOptions` |
| Security | `RoleSystemOption` → `RoleSystemOptions` | `/security/role-system-options` | `RoleSystemOption` |
| Security | `Bitacora` → `AuditLog` | `/security/audit-log` | `Audit` |

Conflicto de nombres a resolver en el orden de la seed: hoy existen las opciones `Persons` (padre, `/Clientes`) y `Clientes` (hija, `/Clientes/Cliente`), y la hija pasa a llamarse `Persons`. Se resuelve así:
- el padre pasa a llamarse **`CustomersMenu`** (ruta `/customers`);
- la hija toma el nombre `Persons`;
- el mapa legacy se aplica **primero al padre** (`Persons` con ruta `/Clientes` → `CustomersMenu`) y después a la hija (`Clientes` → `Persons`), siempre reconociendo cada fila por su **ruta anterior**, que es única.

Las opciones de seguridad que hoy tienen `ParentName = null` pasan a colgar del padre `Security` (hoy quedan sueltas en la raíz del menú).

`ControllerName` **no cambia** en ninguna opción: es el recurso de permiso que usan `[PermissionResource]`, `PermissionService` y la cache `permissions:v2`. Cambiarlo rompería los permisos vigentes.

**C5. `GetRoleSystemOptionSeeds()`**

Todas las filas que referencian un nombre de opción traducido se actualizan (`"Paises"` → `"Countries"`, `"Compañias"` → `"Companies"`, `"Administracion"` → `"Administration"`, `"ManejoTickets"` → `"TicketManagement"`, `"Persons"` (padre) → `"CustomersMenu"`, `"Clientes"` → `"Persons"`, etc.). Los permisos de cada rol no cambian.

**C6. Calendario (SPEC 44 y 45)**

Las opciones del calendario declaran `ModuleName = "Calendar"` y usan rutas con guion: `/calendar/my-calendar`, `/calendar/panel`, `/calendar/activity-confirmation`, `/calendar/settings`, `/calendar/catalogs`, `/calendar/channel-intake`. `PendingPersons` usa `ModuleName = "Customers"`, padre `CustomersMenu` y ruta `/customers/pending-persons`. Esta spec deja actualizadas la SPEC 44 y la 45.

**C7. Checksum y re-seed de desarrollo**

- `ComputeMenuPermissionsSeedChecksum` incluye la lista de módulos (nombre, `IsBase`, orden) y el `ModuleName` de cada opción, para que el cambio dispare el re-seed.
- `SeedMenuAndPermissionsAsync` (re-seed idempotente de Development) ejecuta también `SeedSystemModulesAsync` antes de `SeedSystemOptionsAsync`, para que una base existente reciba los renombres y `IsBase` sin recrearse.

### D. Menú lateral (`GetSidebarMenuQueryHandler`)

> **Ajuste por SPEC 48 (2026-10-01):** este filtro se aplica solo con `Modules:EnforceCompanyModules = true`. Con el interruptor apagado, el menú se arma como hoy.

- La consulta de usuarios no SuperAdmin agrega:

```sql
INNER JOIN admin.systemmodules sm
    ON sm.id = so.moduleid
   AND sm.isactive = TRUE
   AND sm.gcrecord = 0
INNER JOIN admin.companymodules cm
    ON cm.moduleid = sm.id
   AND cm.companyid = @CompanyId
   AND cm.isactive = TRUE
   AND cm.gcrecord = 0
```

- Los padres se incluyen solo si tienen al menos un hijo visible después del filtro (no aparece un menú padre vacío).
- **SuperAdmin** (`AllSystemOptionsSql`): sin filtro (A8).
- Cache del menú (`sidebar:{companyId}:{userId}`): al habilitar o desactivar un módulo de una empresa se invalida la cache de menú y permisos de **todos los usuarios de esa empresa**. Se reutiliza el mecanismo de `InvalidateSidebarCache`/`IPermissionService` existente, extendido con un método por empresa si hoy solo existe por usuario.

### E. `CompanyModulesController` y handlers

| Acción | Hoy | Después |
|---|---|---|
| GET (listado, por id) | `SuperAdminCompany` | `SuperAdmin` (todas las empresas) **o** `SuperAdminCompany` (solo su empresa; el handler fuerza `CompanyId` del token) |
| POST, PUT, DELETE | `SuperAdminCompany` | **Solo `SuperAdmin`** |

- `[Authorize(Roles = "SuperAdmin")]` en las escrituras. Para las lecturas, `[Authorize(Roles = "SuperAdmin,SuperAdminCompany")]` y filtro de empresa en el handler cuando el usuario no es SuperAdmin.
- `UpdateCompanyModules` (activar o desactivar) **no** toca `RoleSystemOptions` ni datos del módulo (A9). Al terminar, invalida la cache de menú y permisos de la empresa (sección D).
- `DeleteCompanyModules`: borrado lógico de la asignación, también sin tocar permisos ni datos.
- La respuesta del listado incluye `isBase` del módulo, para que el frontend lo distinga.

### F. DTOs

- `SystemModuleDto` y los DTOs de `SystemModules` (CRUD existente) exponen y aceptan `IsBase`. El SuperAdmin puede marcar o desmarcar un módulo como base desde ese CRUD. Desmarcarlo **no** quita el módulo a las empresas que ya lo tienen.
- `CompanyModuleListItemDto` y `CompanyModuleDto`: agregan `IsBase`.

### G. Frontend (`join_frontb`)

- Las únicas páginas implementadas que usan rutas de `SystemOption` son las de `/security/...`, y **sus rutas no cambian**. No hay páginas en `/administracion`, `/Clientes` ni `/ManejoTickets`.
- Revisar en F6 `Sidenav.razor`, `MyPermissionsState.cs` y `PermissionView.razor.cs` por referencias a **nombres** de opción (`"Compañias"`, `"Bitacora"`, `"Usuarios"`…). Si hay, se actualizan en el mismo cambio coordinado. Las specs del front que nombren rutas antiguas se actualizan en `join_frontb/specs`.

### H. Tests

- Unitarios (cobertura ≥ 90% en lo nuevo): handlers de `CompanyModules` (permisos por rol, filtro de empresa para `SuperAdminCompany`, invalidación de cache), `SystemModules` con `IsBase`, y `GetSidebarMenuQueryHandler` (filtro por módulo, padres sin hijos ocultos, SuperAdmin sin filtro).
- Integración: una base con los nombres **anteriores** pasa por la seed nueva y queda con los nombres nuevos, **sin filas duplicadas**, con los mismos `Id`, los `RoleSystemOptions` intactos, y el `ModuleId` correcto en cada opción.

**Out of scope:**

- **Asignación automática de módulos base** a empresas nuevas (`CreateCompany`) o a empresas existentes cuando un módulo pasa a ser base (A6, etapa futura).
- **Poblar `RoleSystemOptions`** para empresas nuevas (A3, etapa futura). Hasta entonces, los usuarios de una empresa nueva ven el menú vacío, salvo el SuperAdmin.
- **Bloqueo de APIs** por `CompanyModule` en `DynamicAuthorizationFilter` → **SPEC 48**, que además condiciona el filtro del menú de esta spec al interruptor `Modules:EnforceCompanyModules` (apagado: menú como hoy).
- Traducir `ControllerName`/recursos de permiso, nombres de tablas o esquemas de base de datos (`Admin`, `Messaging`…).
- Traducir textos visibles del frontend (etiquetas, títulos): eso es de las specs del front.

---

## Implementation plan

### F1 — Dominio y migración
`SystemModule.IsBase` con su comentario, configuración EF, migración `AddSystemModuleIsBase`.

### F2 — Seed de módulos
Tabla C1, `LegacySystemModuleNames`, actualización en el lugar, `IsBase`. `SeedCompanyModulesAsync` solo con módulos base.

### F3 — Seed de opciones
`ModuleName` en `SystemOptionSeed`, tabla C4 completa, `LegacySystemOptionNames`, reconocimiento por nombre o ruta nuevos o anteriores, corrección de `ModuleId`, resolución del conflicto `Persons`/`Clientes`, opciones de seguridad bajo `Security`. Actualización de `GetRoleSystemOptionSeeds()` (C5) y del checksum (C7). Ajuste de `SeedMenuAndPermissionsAsync`.

### F4 — Menú lateral
Filtro de la sección D, padres sin hijos ocultos, invalidación de cache por empresa.

### F5 — `CompanyModules` y `SystemModules`
Autorización de la sección E, filtro de empresa para `SuperAdminCompany`, `IsBase` en DTOs, invalidación de cache al activar o desactivar.

### F6 — Specs y frontend
Actualizar SPEC 44 y 45 (rutas con guion, `ModuleName`, matriz de roles según A4). Revisar las referencias a nombres de opción en `join_frontb` (sección G).

### F7 — Tests y verificación
Unitarios e integración de la sección H. `dotnet build` sin warnings nuevos; arrancar en Development contra una base existente y verificar en el menú de un usuario no SuperAdmin que solo aparecen los módulos activos de su empresa. Al terminar, detener la API.

---

## Acceptance criteria

- [ ] `SystemModule` tiene `IsBase`; `Administration`, `Customers`, `Tickets` y `Security` son base; `Calendar` no.
- [ ] La seed inicial asigna a las empresas solo módulos base, y no desactiva ni borra asignaciones existentes.
- [ ] Cada `SystemOption` tiene el `ModuleId` de su módulo real; ninguna opción de Tickets, Clientes, Seguridad o Calendario queda bajo `Administration`.
- [ ] Después de la seed, una base existente tiene los nombres y rutas en inglés con guion, sin filas duplicadas, con los mismos `Id` y con sus `RoleSystemOptions` intactos.
- [ ] Ningún `ControllerName` cambió y los permisos vigentes siguen funcionando.
- [ ] El menú de un usuario no SuperAdmin no muestra opciones de módulos inactivos o no asignados a su empresa, ni padres sin hijos visibles.
- [ ] El menú del SuperAdmin muestra todas las opciones.
- [ ] Solo el SuperAdmin puede crear, activar, desactivar o borrar asignaciones de módulos; `SuperAdminCompany` solo consulta las de su empresa.
- [ ] Desactivar y reactivar un módulo no cambia datos ni permisos por rol, y el menú se actualiza sin esperar a que venza la cache.

---

## Decisions taken and discarded

- **`IsBase` en `SystemModule`** (A1) vs una lista fija en código. El flag es editable por el SuperAdmin y visible en el CRUD de módulos y en las asignaciones.
- **Renombrar en el lugar con mapas legacy** (elegido) vs borrar e insertar filas nuevas. Conservar los `Id` mantiene `CompanyModules` y `RoleSystemOptions` sin migrar datos ni perder permisos ajustados a mano. La seed ya reconocía opciones por nombre **o** ruta; se extiende a los nombres y rutas anteriores.
- **Datos en la seed, no en la migración** (elegido): la seed ya es idempotente y ya se ejecuta después de cada migración. Una migración con SQL de datos duplicaría la lógica de nombres.
- **`ControllerName` intacto** (elegido): es el contrato de permisos. Traducir solo `Name` y `Route` no afecta la autorización.
- **Padre `CustomersMenu`** (elegido) para liberar el nombre `Persons` a la pantalla de personas, que es el recurso real (`ControllerName = "Persons"`).
- **Acceso total de `Admin` y `SuperAdminCompany`** (A4): se mantiene la regla `PrivilegedRoleNames`. Como consecuencia, `SuperAdminCompany` tiene escritura sobre `CalendarCatalogs` (catálogo global del calendario); la SPEC 44 se corrige para reflejarlo.
- **Habilitar módulos solo SuperAdmin** (A7) vs mantener a `SuperAdminCompany`: habilitar un módulo es una decisión del dueño del sistema, no de cada empresa.
- **Menú del SuperAdmin sin filtro** (A8): administra el sistema completo, incluido habilitar módulos.

---

## Identified risks

| Riesgo | Mitigación |
|---|---|
| El renombre en la seed falla a mitad de camino y deja nombres mezclados. | La seed de opciones corre en una transacción; la prueba de integración parte de una base con los nombres anteriores. |
| Con el filtro del menú, usuarios de empresas con asignaciones incompletas en `CompanyModule` dejan de ver opciones que hoy ven. | Las empresas existentes recibieron todos los módulos en la seed inicial y C2 no quita asignaciones. Antes de desplegar, consultar las empresas activas sin los cuatro módulos base y completarlas a mano desde `CompanyModules` (SuperAdmin). |
| Una empresa nueva queda sin módulos ni permisos hasta que existan los procesos de A3 y A6. | Documentado: por ahora se prueba con las empresas existentes. El SuperAdmin puede asignar módulos a mano desde `CompanyModules`. |
| Referencias del frontend a nombres de opción en español. | Revisión explícita en F6 (sección G). |
| `SuperAdminCompany` puede editar catálogos globales del calendario que afectan a todas las empresas (consecuencia de A4). | Decisión aceptada por el usuario; queda documentada en SPEC 44 y en esta spec. |

---

## Puntos a validar en la revisión

Ninguno: las decisiones A1–A10 se tomaron con el usuario el 2026-10-01.
