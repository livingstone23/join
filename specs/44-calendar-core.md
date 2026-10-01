# SPEC 44 — Módulo Calendario: parametrización, calendarios de usuario y actividades

> **Status:** Borrador
> **Depends on:** Ninguna para implementarse. Es prerequisito de SPEC 45 (ingreso de actividades por canales / agente) y SPEC 46 (estructura de integración con Google Calendar).
> **Related:** SPEC 41 (índices únicos filtrados por `GcRecord = 0` — mismo patrón en todos los índices de esta spec), SPEC 40 (fork PostgreSQL — la sintaxis de `HasFilter` de esta spec se porta igual que el resto), SPEC 43 (patrón "valores iniciales desde la configuración de la empresa").
> **Date:** 2026-09-29
> **Objective:** Crear el módulo Calendario en su propio esquema `Calendar`: un catálogo global de tipos de actividad y de estados que cada empresa habilita y ajusta, la configuración de calendario por empresa (usuario por defecto, horario hábil, zona horaria, feriados), la configuración por usuario (calendario por defecto, horario de contrato, días hábiles, periodos no hábiles), los calendarios de cada usuario y sus actividades (puntuales, de día completo y periódicas), con validación de superposición y de horario hábil, un panel para consultar calendarios de otros usuarios de la misma empresa y todas las APIs que el frontend necesita para las vistas de día, semana y mes.

---

## Por qué existe esta spec

El usuario pidió un calendario por usuario del sistema que funcione como Google Calendar (actividades con inicio y fin, periódicas, vistas de día/semana/mes), que a futuro reciba actividades desde canales de comunicación (un agente de WhatsApp que agenda una cita) y que se pueda sincronizar con Google. El diseño se acordó en una sesión de preguntas y respuestas el 2026-09-29; esta spec cubre la **base**: todo lo que funciona dentro de JOIN sin canales externos ni Google.

Las dos piezas siguientes se separaron a propósito:

- **SPEC 45** agrega el ingreso por canales (agente de n8n), el registro de personas nuevas (`PersonPendingConfirmation`) y el flujo de revisión.
- **SPEC 46** deja creadas las entidades y tablas de la integración con Google, sin acciones.

Hoy el sistema no tiene ninguna entidad de calendario, agenda ni cita. Lo más parecido es el módulo de tickets, del que esta spec reutiliza patrones: catálogo por empresa (`TicketStatus`), configuración por empresa con valores iniciales (`TicketCompanyDefault`, SPEC 43) y bitácora de cambios (`TicketLog`).

---

## Decisiones de negocio acordadas (resumen)

| # | Decisión |
|---|---|
| 1 | El calendario es **solo para usuarios del sistema** (`ApplicationUser`). |
| 2 | Un usuario puede tener **varios calendarios por empresa**, y exactamente **un calendario por defecto por empresa**. Si pertenece a varias empresas, tiene al menos un calendario en cada una. |
| 3 | Ver y escribir en calendarios de **otros** usuarios requiere permiso por rol (`SystemOption` → `RoleSystemOption`), y siempre **solo dentro de la misma empresa**. Hay un **panel** para filtrar y consultar calendarios de distintos usuarios. |
| 4 | Tres recursos de permiso operativos: `Calendar` (propio), `CalendarOthers` (panel y calendarios ajenos), `CalendarActivityConfirmation` (cambio de estado). |
| 5 | Los **tipos de actividad** son un catálogo global (`CalendarActivity`) que cada empresa habilita en `CalendarActivityCompany`. Solo se pueden crear actividades de un tipo habilitado y vigente en ambas tablas. La empresa usa **sus** valores (duración, color, superposición, horario hábil, estado inicial). |
| 6 | Los **estados** siguen la misma dinámica: catálogo global `CalendarStatus` + habilitación por empresa `CalendarStatusCompany`. |
| 7 | Cada tipo indica si **permite superposición**. Una actividad que no la permite no puede chocar con otra que tampoco la permite. Un recordatorio puede coincidir con cualquier cosa. |
| 8 | La validación de choques es **por usuario** (todos sus calendarios de la empresa). Las actividades pendientes de confirmar **bloquean** el horario; las anuladas no. |
| 9 | `CalendarCompany` (una fila por empresa) define el **usuario que recibe las actividades por defecto**, el **horario hábil** de la empresa (o 24 horas) y la **zona horaria**. |
| 10 | `CalendarUserConfiguration` define el calendario por defecto del usuario, su **horario de contrato** (dentro del horario de la empresa) y sus **días hábiles**. |
| 11 | Cada tipo indica si **requiere horario hábil** (una reunión sí, una notificación no). Nadie puede crearle a otro una actividad de ese tipo fuera de su horario ni en un día no hábil. **El propio dueño sí puede.** |
| 12 | Días no hábiles del usuario (vacaciones, permisos, faltas) en `CalendarUserNonWorkingPeriod`, como rangos de fechas. Feriados de la empresa en `CalendarCompanyNonWorkingDay`. Ambos bloquean **solo** los tipos que requieren horario hábil. |
| 13 | El estado inicial de una actividad sale de la configuración de la empresa. Después, el usuario puede confirmar, anular, borrar o cambiar libremente. **Sin reglas de transición** (una tarea Hangfire futura hará barridos). "Completada" se marca a mano. |
| 14 | **Anular es un cambio de estado**, conserva el histórico. El borrado lógico queda para registros creados por error. Toda modificación queda en la bitácora `CalendarEventLog`. |
| 15 | Actividades periódicas: diaria, semanal, mensual y anual, "cada N", con fin por fecha, por número de repeticiones **o sin fin**. Choques y horario hábil se validan en los próximos **12 meses**. |
| 16 | Actividades de **día completo**: permitidas, sin validación de hora, pero si las crea alguien distinto al dueño deben caer en día hábil. |
| 17 | Una actividad tiene **un solo dueño** (sin invitados en esta etapa), con enlace de videollamada opcional y enlaces opcionales a `Person` (solicitante), `Customer`, `Ticket` y `Project`. |
| 18 | La **zona horaria es por empresa**. Los usuarios se adecúan a la de su empresa. |
| 19 | El módulo se habilita **por empresa** (`CompanyModule`). Sin el módulo activo no se pueden registrar calendarios ni actividades. |
| 20 | Al asignar un usuario a una empresa se le crea "Mi calendario" como calendario por defecto **si la empresa ya tiene `CalendarCompany`**. Si no, queda pendiente y se crea cuando se configure. |
| 21 | Recordatorios y notificaciones automáticas quedan para el futuro (Hangfire). En esta etapa, el usuario ve sus actividades programadas al entrar a la app. |
| 22 | Esta spec define **solo la API**. Las specs del frontend se diseñan después, tomando como base la documentación de entidades de esta spec. |

---

## Scope

**In:**

### A. Dominio (`src/1.Domain/Calendars/`, namespace `JOIN.Domain.Calendars`)

Entidades nuevas (detalle campo por campo en **Data model**):

- **Parametrización:** `CalendarActivity`, `CalendarActivityCompany`, `CalendarStatus`, `CalendarStatusCompany`, `CalendarCompany`, `CalendarCompanyNonWorkingDay`, `CalendarUserConfiguration`, `CalendarUserNonWorkingPeriod`.
- **Operación:** `UserCalendar`, `CalendarEvent`, `CalendarEventLog`.
- **Enums** (`src/1.Domain/Calendars/Enums/`): `CalendarWorkDays` (`[Flags]`), `CalendarViewType`, `CalendarNonWorkingReason`, `CalendarEventOriginType`, `CalendarEventLogAction`, `CalendarRecurrenceEditScope`.
- **Códigos de sistema** (`src/1.Domain/Calendars/CalendarCodes.cs`): constantes de los `Code` de `CalendarStatus` y `CalendarActivity` que la lógica usa (ver Data model). La lógica **nunca** compara por `Name`.

### B. Persistencia (`src/3.Persistence/`)

- Configuraciones EF en `Configuration/Calendars/`, todas con `ToTable("<Tabla>", "Calendar")`.
- `DbSet`s nuevos en `ApplicationDbContext` bajo una sección `// --- 7. CALENDAR MODULE ---`.
- Query filters: las entidades tenant (`BaseTenantEntity`) quedan cubiertas por el mecanismo genérico de SPEC 39. Si SPEC 39 no está implementada al momento de implementar esta, se agregan las líneas explícitas en `ConfigureGlobalQueryFilters` (tenant + soft delete para las tenant, soft delete para los dos catálogos globales).
- Relaciones con `DeleteBehavior.Restrict` en todas las FK hacia `ApplicationUser`, `CalendarStatusCompany` y `CalendarActivityCompany` (varias rutas de cascada hacia `Users` y `Companies`, mismo motivo que `ConfigureTicketRelationships`).
- Índices únicos **filtrados** por `[GcRecord] = 0` (patrón SPEC 41), nunca con `GcRecord` en la clave.
- Migración `AddCalendarModule`.

### C. Application — parametrización

Casos de uso en `src/2.Application/UseCases/Calendars/<Feature>/{Commands|Queries}/<Name>/`:

| Feature | Commands | Queries |
|---|---|---|
| `CalendarActivities` (catálogo global) | Create, Update, Delete | GetPaged, GetById |
| `CalendarStatuses` (catálogo global) | Create, Update, Delete | GetPaged, GetById |
| `CalendarActivityCompanies` | Enable (copia desde global), Update, Delete | GetPaged, GetById, GetUsable (lookup para formularios) |
| `CalendarStatusCompanies` | Enable, Update, Delete | GetPaged, GetById, GetUsable |
| `CalendarCompanies` | Create, Update, SyncCatalogs, ProvisionUsers | GetCurrent |
| `CalendarCompanyNonWorkingDays` | Create, Update, Delete | GetPaged (por año) |
| `CalendarUserConfigurations` | UpdateMine, UpdateForUser | GetMine, GetByUser |
| `CalendarUserNonWorkingPeriods` | CreateMine, DeleteMine, Create, Update, Delete | GetMine, GetPaged (por usuario) |

### D. Application — operación

| Feature | Commands | Queries |
|---|---|---|
| `UserCalendars` | Create, Update, Delete, SetDefault | GetMine |
| `CalendarEvents` | Create, Update (con alcance para periódicas), ChangeStatus, Delete | GetRange (ocurrencias expandidas), GetById, GetUpcoming, GetLogs |
| `CalendarPanel` | CreateForUser, UpdateForUser, ChangeStatusForUser, DeleteForUser | GetUsers, GetCalendars, GetEvents, GetAvailability |

Los handlers del panel reutilizan los mismos coordinadores que los de `CalendarEvents`. La diferencia está en quién puede ser el dueño: en `CalendarEvents` debe ser el usuario actual, en `CalendarPanel` cualquier usuario de la empresa.

### E. Application — coordinadores y servicios del módulo

Clases DI con constructor primario, registradas `Scoped` en `2.Application/Common/ConfigureServices.cs`, en `src/2.Application/UseCases/Calendars/Common/`:

- **`CalendarModuleGuard`**: `Task<Response<CalendarCompanyContext>> EnsureEnabledAsync(Guid companyId, bool requireCalendarCompany, CancellationToken ct)`. Verifica que exista un `CompanyModule` activo para el `SystemModule` "Calendar". Si `requireCalendarCompany` es true, carga también `CalendarCompany` y devuelve un contexto con la zona horaria, el horario hábil y el usuario por defecto. Todos los handlers del módulo lo llaman después del chequeo de `CompanyId`. Errores: `CALENDAR_MODULE_NOT_ENABLED`, `CALENDAR_COMPANY_NOT_CONFIGURED`.
- **`CalendarUserProvisioner`**: `Task<bool> EnsureUserCalendarAsync(Guid companyId, Guid userId, CancellationToken ct)`. Si el módulo está activo, `CalendarCompany` existe y el usuario no tiene `CalendarUserConfiguration` en esa empresa, crea un `UserCalendar` con el nombre `CalendarCompany.DefaultCalendarName` y su `CalendarUserConfiguration` con el horario de la empresa y días hábiles L–V. Es idempotente. **No llama a `SaveAsync`**: agrega al `IUnitOfWork` del handler que lo invoca, para que todo se confirme en una sola transacción.
- **`CalendarUserDefaultCoordinator`**: mantiene la invariante "un calendario por defecto por usuario y empresa" (`SetDefault`, y el bloqueo de borrar o desactivar el calendario por defecto). Sigue el patrón de `PersonAddressDefaultCoordinator`.
- **`CalendarEventScheduleGuard`**: valida superposición, horario hábil, días hábiles, periodos no hábiles y feriados para una actividad nueva o modificada (reglas completas en **Reglas de negocio**). Devuelve `Response` con el detalle de conflictos.
- **`CalendarEventInitialStatusResolver`**: resuelve el estado inicial desde `CalendarActivityCompany.InitialCalendarStatusCompanyId`. Mismo patrón que `TicketInitialStatusResolver` de SPEC 43.
- **`CalendarEventLogWriter`**: arma y agrega la fila de `CalendarEventLog` para cada operación.
- **`ICalendarRecurrenceExpander`** (interfaz en `2.Application/Interface/`): `IReadOnlyList<CalendarOccurrence> Expand(CalendarRecurrenceSeries series, DateTime fromUtc, DateTime toUtc, string timeZoneId)` y `Response ValidateRule(string rrule)`. La implementación está en `3.Infrastructure/Calendars/IcalRecurrenceExpander.cs` con el paquete **Ical.Net**, para que Application no dependa de la librería. Expande en la zona horaria de la empresa (el horario de verano y los cambios de hora se respetan) y devuelve UTC.

### F. Integraciones con código existente

- `ICompanyCatalogSeeder.SeedDefaultCatalogsForCompanyAsync` (`DatabaseSeeder.SeedDefaultCatalogsForCompanyAsync`, hoy solo llama a `SeedMessagingCatalogsForCompanyAsync`): agregar `SeedCalendarCatalogsForCompanyAsync(companyId)`. Copia a `CalendarActivityCompany` y `CalendarStatusCompany` cada entrada activa de los catálogos globales que la empresa todavía no tenga, y fija como `InitialCalendarStatusCompanyId` el estado `PENDING` de la empresa. Es idempotente. **No** crea `CalendarCompany`, porque requiere elegir un usuario por defecto (decisión humana). Así, toda empresa nueva nace con los catálogos listos. Lo usa también el seeder básico que se hará cuando estén los módulos base, y el endpoint `SyncCatalogs`.
- `InviteUserCommandHandler` (`UseCases/Security/Users/Commands/InviteUser`, donde se crea `UserCompany` en la línea ~160) y `AddUserCompanyCommandHandler` (línea ~148): después de crear el `UserCompany`, llaman a `CalendarUserProvisioner.EnsureUserCalendarAsync(companyId, userId)`. Si el módulo o `CalendarCompany` no están, no hace nada: el usuario queda pendiente.
- `CreateCalendarCompanyCommandHandler`: al crear la configuración de la empresa, provisiona a todos los usuarios activos de la empresa que no tengan calendario.

### G. WebApi (`src/4.Services.WebApi/Controllers/Calendars/`)

Controllers nuevos con `[Route("api/v{version:apiVersion}/[controller]")]`. Endpoints completos en **API**.

### H. Seed

- `SeedSystemModulesAsync`: nuevo módulo `Calendar` (Description "Calendar and activities module", `fa-solid fa-calendar-days`, `Order = 4`).
- Catálogos globales `CalendarActivity` y `CalendarStatus` (valores en **Data model**), idempotentes por `Code`.
- `GetAdministrativeSystemOptionSeeds()` y `GetRoleSystemOptionSeeds()`: menú y permisos (ver **Permisos**).
- Empresa maestra `JOIN-001` (solo desarrollo): catálogos de empresa, `CalendarCompany` (usuario por defecto = el SuperAdmin de la empresa, 08:00–17:00, `America/Managua`, estado de reprogramación = `RESCHEDULED`), `CompanyModule` "Calendar" activo y provisión de calendario para los usuarios semilla. Esto permite probar el módulo de punta a punta en Development y en las pruebas de integración.

### I. Tests

- Unitarios en `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Calendars/...` con la misma estructura de carpetas, cobertura ≥ 90% en clases nuevas (gate de CI). Detalle en F10.
- Integración: un flujo completo (configurar empresa → crear actividad → choque → cambio de estado → consulta por rango) en `tests/IntegrationTests`.

**Out of scope (specs futuras):**

- Ingreso por canales, agente de n8n, `PersonPendingConfirmation` y flujo de revisión → **SPEC 45**.
- Entidades de integración con Google Calendar → **SPEC 46**. Acciones de sincronización (OAuth, envío, webhook, worker) → spec posterior a la 46.
- Recordatorios y notificaciones automáticas (Hangfire), cierre automático de actividades vencidas ("Completada") y barridos de estados.
- Invitados o participantes adicionales en una actividad.
- Calendarios privados u ocultos dentro de la empresa: todo calendario es visible para quien tiene `CalendarOthers`.
- Turnos nocturnos (horario que cruza la medianoche) y horarios distintos por día de la semana.
- Periodos no hábiles de medio día (permiso de 2 horas): en esta etapa los periodos son por días completos.
- Control de `CompanyModule` en el filtro global de autorización para todos los módulos (en esta spec el control es propio del Calendario; ver Decisiones).
- Frontend (specs en `join_frontb`, a diseñar a partir de esta).

---

## Data model

Esquema de base de datos: **`Calendar`**. Todas las tablas tenant heredan de `BaseTenantEntity` y obtienen `Id` (Guid), `CompanyId`, `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy` y `GcRecord`. Los dos catálogos globales heredan de `BaseAuditableEntity` (sin `CompanyId`), igual que `CommunicationChannel` y `SystemModule`.

> **Guía para el diseño del frontend.** Las entidades se dividen en **Parametrización** (pantallas de configuración que usan administradores y supervisores, pocas veces) y **Operación** (el calendario del día a día). Cada entidad indica **quién la administra** y **en qué pantalla** se espera verla.

### Diagrama de relaciones

```
PARAMETRIZACIÓN                                         OPERACIÓN

CalendarActivity (global) ──1:N──► CalendarActivityCompany ◄──N:1── CalendarEvent
CalendarStatus   (global) ──1:N──► CalendarStatusCompany   ◄──N:1── CalendarEvent
                                        ▲                             │  │
       CalendarActivityCompany.InitialCalendarStatusCompanyId ────────┘  │
                                                                         │
CalendarCompany (1 por empresa) ── DefaultActivityUserId ─► Users        │
CalendarCompanyNonWorkingDay (N por empresa)                             │
                                                                         │
CalendarUserConfiguration (1 por usuario+empresa) ── DefaultCalendarId ─► UserCalendar ◄─N:1─┘
CalendarUserNonWorkingPeriod (N por usuario+empresa)                      │
                                                                  OwnerUserId ─► Users
CalendarEvent ──1:N──► CalendarEventLog
CalendarEvent ──(SeriesMasterId)──► CalendarEvent   (excepciones de una serie periódica)
CalendarEvent ──► Person / Customer / Ticket / Project / CommunicationChannel (opcionales)
```

---

### PARAMETRIZACIÓN

#### 1. `CalendarActivity` — catálogo global de tipos de actividad

Tabla `Calendar.Activities`. Hereda `BaseAuditableEntity` (sin empresa). **Administra:** SuperAdmin. **Pantalla:** Configuración del sistema → Tipos de actividad.

Es la lista maestra de todos los tipos posibles. Ninguna actividad apunta directo a esta tabla: apunta a `CalendarActivityCompany`. Estos valores son los **valores base** que se copian cuando una empresa habilita el tipo.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `Code` | nvarchar(30) | Sí | Código técnico inmutable (`MEETING`). Lo usan la lógica y el agente (SPEC 45). Único entre activos. |
| `Name` | nvarchar(100) | Sí | Nombre visible base ("Reunión"). |
| `Description` | nvarchar(500) | No | Para qué sirve el tipo. |
| `Color` | nvarchar(7) | Sí | Color hex base (`#1E88E5`). |
| `Icon` | nvarchar(100) | No | Ícono para el frontend. |
| `DefaultDurationMinutes` | int | Sí | Duración propuesta al crear (5–1440). |
| `AllowsOverlap` | bit | Sí | `true` = puede coincidir en horario con cualquier actividad (recordatorio). `false` = no puede chocar con otra que tampoco lo permita (reunión). |
| `RequiresBusinessHours` | bit | Sí | `true` = solo dentro del horario hábil y en días hábiles del dueño, salvo que la cree el propio dueño. |
| `IsActive` | bit | Sí | Un tipo inactivo no se puede usar en ninguna empresa aunque esté habilitado allí. |
| `SortOrder` | int | Sí | Orden en listas. |

Índice único filtrado: `(Code)` where `GcRecord = 0`.

**Seed** (idempotente por `Code`):

| Code | Name | Duración | AllowsOverlap | RequiresBusinessHours | Color |
|---|---|---|---|---|---|
| `MEETING` | Reunión | 60 | No | Sí | `#1E88E5` |
| `APPOINTMENT` | Cita | 60 | No | Sí | `#43A047` |
| `TASK` | Tarea | 30 | Sí | Sí | `#FB8C00` |
| `REMINDER` | Recordatorio | 15 | Sí | No | `#8E24AA` |
| `PERSON_REVIEW` | Revisión de persona | 15 | Sí | No | `#E53935` |

`PERSON_REVIEW` la usa SPEC 45 para avisar al supervisor que revise una persona creada por el agente.

#### 2. `CalendarActivityCompany` — tipos de actividad habilitados por empresa

Tabla `Calendar.ActivityCompanies`. Hereda `BaseTenantEntity`. **Administra:** administrador de la empresa (recurso `CalendarSettings`). **Pantalla:** Configuración del calendario → Tipos de actividad.

Contiene los **valores efectivos** de la empresa. Al habilitar un tipo se copian los valores del global; después la empresa los edita. Ejemplo: el global dice "Reunión 60 minutos" y la empresa la deja en 30; el formulario de nueva actividad propone 30.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `CalendarActivityId` | Guid FK | Sí | Tipo global de origen. No se puede cambiar después de creado. |
| `Name` | nvarchar(100) | Sí | Nombre visible en la empresa (copiado; editable). |
| `Color` | nvarchar(7) | Sí | Copiado; editable. |
| `DefaultDurationMinutes` | int | Sí | Copiado; editable (5–1440). |
| `AllowsOverlap` | bit | Sí | Copiado; editable. |
| `RequiresBusinessHours` | bit | Sí | Copiado; editable. |
| `InitialCalendarStatusCompanyId` | Guid FK | Sí | Estado con el que nace toda actividad de este tipo (lo cree un usuario o el agente). Debe ser un `CalendarStatusCompany` usable de la misma empresa. |
| `IsActive` | bit | Sí | Habilitado en la empresa. |
| `SortOrder` | int | Sí | Orden en el selector de tipos. |

Índice único filtrado: `(CompanyId, CalendarActivityId)` where `GcRecord = 0`.

**Regla "usable":** un tipo se puede usar para crear o modificar una actividad solo si `CalendarActivityCompany.IsActive = 1`, `CalendarActivity.IsActive = 1` y ambos tienen `GcRecord = 0`. Si falla → `CALENDAR_ACTIVITY_NOT_ENABLED`.

#### 3. `CalendarStatus` — catálogo global de estados

Tabla `Calendar.Statuses`. Hereda `BaseAuditableEntity`. **Administra:** SuperAdmin. **Pantalla:** Configuración del sistema → Estados de actividad.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `Code` | nvarchar(30) | Sí | Código técnico inmutable. Único entre activos. |
| `Name` | nvarchar(100) | Sí | Nombre base. |
| `Description` | nvarchar(500) | No | |
| `Color` | nvarchar(7) | Sí | |
| `BlocksTime` | bit | Sí | `true` = una actividad en este estado ocupa el horario para la validación de choques. |
| `IsFinal` | bit | Sí | Informativo en esta etapa: indica que el ciclo de la actividad terminó (para filtros y para Hangfire a futuro). **No** impide cambios de estado. |
| `IsActive` | bit | Sí | |
| `SortOrder` | int | Sí | |

Índice único filtrado: `(Code)` where `GcRecord = 0`.

**Seed** (idempotente por `Code`):

| Code | Name | BlocksTime | IsFinal | Color | Uso por la lógica |
|---|---|---|---|---|---|
| `PENDING` | Pendiente de confirmación | Sí | No | `#FDD835` | Estado inicial por defecto al sincronizar catálogos. |
| `CONFIRMED` | Confirmada | Sí | No | `#43A047` | SPEC 46: solo las confirmadas se sincronizan con Google. |
| `RESCHEDULED` | Reprogramada | Sí | No | `#FB8C00` | Valor por defecto de `CalendarCompany.RescheduledByOthersStatusCompanyId` (estado tras una reprogramación hecha por alguien distinto al dueño). |
| `CANCELLED` | Anulada | **No** | Sí | `#9E9E9E` | Anular. Libera el horario. |
| `COMPLETED` | Completada | Sí | Sí | `#546E7A` | Se marca a mano (Hangfire a futuro). |

`BlocksTime` e `IsFinal` **no** se copian a la empresa: definen el comportamiento del sistema y deben ser iguales en todas las empresas (ver Decisiones).

#### 4. `CalendarStatusCompany` — estados habilitados por empresa

Tabla `Calendar.StatusCompanies`. Hereda `BaseTenantEntity`. **Administra:** administrador de la empresa (`CalendarSettings`). **Pantalla:** Configuración del calendario → Estados.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `CalendarStatusId` | Guid FK | Sí | Estado global de origen. Inmutable. |
| `Name` | nvarchar(100) | Sí | Nombre visible en la empresa (copiado; editable). |
| `Color` | nvarchar(7) | Sí | Copiado; editable. |
| `IsActive` | bit | Sí | |
| `SortOrder` | int | Sí | |

Índice único filtrado: `(CompanyId, CalendarStatusId)` where `GcRecord = 0`.

**Regla "usable":** igual que en tipos (activo y no borrado en ambas tablas) → si no, `CALENDAR_STATUS_NOT_ENABLED`. No se puede desactivar ni borrar un `CalendarStatusCompany` que sea `InitialCalendarStatusCompanyId` de algún tipo de la empresa (`CALENDAR_STATUS_IN_USE_AS_INITIAL`), que sea el `RescheduledByOthersStatusCompanyId` de `CalendarCompany` (`CALENDAR_STATUS_IN_USE_AS_RESCHEDULE`), ni uno que tenga actividades (`CALENDAR_STATUS_IN_USE`). Tampoco se pueden desactivar los estados con código `PENDING` o `CANCELLED`, porque la lógica los necesita (`CALENDAR_STATUS_REQUIRED_BY_SYSTEM`).

#### 5. `CalendarCompany` — configuración del calendario de la empresa

Tabla `Calendar.CalendarCompanies`. Hereda `BaseTenantEntity`. **Una sola fila por empresa.** **Administra:** administrador de la empresa (`CalendarSettings`). **Pantalla:** Configuración del calendario → General.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `DefaultActivityUserId` | Guid FK → Users | Sí | Usuario (típicamente un supervisor) que recibe toda actividad que llega sin destinatario identificado: solicitantes nuevos (servicio nuevo, ticket), correos de quien atiende que no coinciden con ningún usuario, y la revisión de personas creadas por el agente (SPEC 45). Debe pertenecer a la empresa (`UserCompany` activo). |
| `TimeZone` | nvarchar(64) | Sí | Zona IANA (`America/Managua`). Todas las horas del módulo se interpretan en esta zona. Se valida con `TimeZoneInfo.FindSystemTimeZoneById`. |
| `IsOpen24Hours` | bit | Sí | `true` = la empresa opera las 24 horas. Se ignoran `BusinessHoursStart`/`End`. |
| `BusinessHoursStart` | time | Condicional | Hora de apertura. Obligatoria si `IsOpen24Hours = 0`. |
| `BusinessHoursEnd` | time | Condicional | Hora de cierre. Obligatoria si `IsOpen24Hours = 0`; debe ser mayor que la apertura (sin turnos nocturnos). |
| `DefaultCalendarName` | nvarchar(100) | Sí | Nombre del calendario que se crea automáticamente a cada usuario. Default `"Mi calendario"`. |
| `RescheduledByOthersStatusCompanyId` | Guid FK → CalendarStatusCompany | Sí | Estado que se asigna **automáticamente** a una actividad cuando alguien que **no es su dueño** cambia su fecha u hora (ver R6). Default al crear la configuración: el estado `RESCHEDULED` de la empresa. Debe ser un `CalendarStatusCompany` usable de la misma empresa. |

Índice único filtrado: `(CompanyId)` where `GcRecord = 0`.

Comentario XML obligatorio de `DefaultActivityUserId` en la entidad:

```csharp
/// <summary>
/// User (normally a supervisor) who RECEIVES every activity that arrives without an identified
/// recipient, so they can attend it or redistribute it to another user. Used when:
/// <list type="bullet">
///   <item>The channel agent (SPEC 45) registers a request from a NEW requester (a new service,
///   a ticket, a first contact) who does not ask for a specific user.</item>
///   <item>An existing requester gives the e-mail of the user who attends them, but that e-mail does
///   not match (exact, case-insensitive) an active user of this company with a calendar. The activity
///   is routed here directly (no error) with a note containing the e-mail the requester gave.</item>
///   <item>The agent registers a new <c>Person</c>: the "Review created person" activity
///   (<c>PERSON_REVIEW</c>) is created in this user's calendar.</item>
/// </list>
/// The activity always goes to this user's DEFAULT calendar (<see cref="CalendarUserConfiguration.DefaultCalendarId"/>).
/// Must be a user with an active <c>UserCompany</c> in this company and a calendar configuration;
/// if not, channel intake fails with <c>CALENDAR_INTAKE_TARGET_USER_HAS_NO_CALENDAR</c>.
/// There is no lookup of users by name: a recipient is identified only by exact e-mail or falls back here.
/// </summary>
public Guid DefaultActivityUserId { get; set; }
```

Comentario XML obligatorio de `RescheduledByOthersStatusCompanyId` en la entidad (lo leen los desarrolladores y los agentes que generen código o diseño a partir del dominio):

```csharp
/// <summary>
/// Company-level status (<see cref="CalendarStatusCompany"/>) that the system assigns automatically
/// to a <see cref="CalendarEvent"/> when its start or end time is changed by someone who is NOT the
/// owner of the calendar (another user through the calendar panel, or the channel agent of SPEC 45).
/// Its purpose is to make the owner review and confirm the new date/time.
/// <para>
/// NOT applied when: the owner reschedules their own activity (the status is kept), the activity is
/// created (the initial status comes from <see cref="CalendarActivityCompany.InitialCalendarStatusCompanyId"/>),
/// or only non-time fields change (title, description, location...).
/// </para>
/// <para>
/// Default value: the company's status whose global code is <c>RESCHEDULED</c>. Must reference an
/// active, non-deleted status of the same company. Read it from here; never hard-code the
/// <c>RESCHEDULED</c> code in handlers.
/// </para>
/// </summary>
public Guid RescheduledByOthersStatusCompanyId { get; set; }
```

Si se cambia el horario de la empresa y algún usuario queda con un horario fuera del nuevo rango, la operación falla con `CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE` y la lista de usuarios afectados. Primero hay que ajustar a esos usuarios.

#### 6. `CalendarCompanyNonWorkingDay` — feriados de la empresa

Tabla `Calendar.CompanyNonWorkingDays`. Hereda `BaseTenantEntity`. **Administra:** administrador de la empresa (`CalendarSettings`). **Pantalla:** Configuración del calendario → Feriados.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `Date` | date | Sí | Fecha del feriado. |
| `Name` | nvarchar(150) | Sí | "Día de la Independencia". |
| `IsRecurringYearly` | bit | Sí | `true` = se repite cada año en el mismo día y mes (15 de septiembre). Para feriados móviles (Semana Santa) se registra una fila por año con `false`. |

Índice único filtrado: `(CompanyId, Date)` where `GcRecord = 0`.

Aplica a **todos** los usuarios de la empresa y bloquea solo las actividades cuyo tipo requiere horario hábil, salvo que las cree el propio dueño.

#### 7. `CalendarUserConfiguration` — configuración del usuario en la empresa

Tabla `Calendar.UserConfigurations`. Hereda `BaseTenantEntity`. **Una fila por usuario y empresa.** **Administra:** el propio usuario (sus preferencias) y el administrador (horario de contrato, `CalendarSettings`). **Pantalla:** Mi calendario → Preferencias; Configuración del calendario → Usuarios.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `UserId` | Guid FK → Users | Sí | |
| `DefaultCalendarId` | Guid FK → UserCalendar | Sí | Calendario donde se registran las actividades cuando no se indica otro (incluidas las del agente). Debe ser del mismo usuario y empresa, activo y no borrado. |
| `BusinessHoursStart` | time | No | Hora de entrada del contrato. Nulo = usa la de la empresa. |
| `BusinessHoursEnd` | time | No | Hora de salida. Nulo = usa la de la empresa. Si se indica una, se deben indicar las dos. Debe ser mayor que la entrada y estar **dentro** del horario de la empresa (en una empresa 24 horas, cualquier rango del día es válido). |
| `WorkDays` | int (`CalendarWorkDays` flags) | Sí | Días en que trabaja. Default L–V (`Monday \| Tuesday \| Wednesday \| Thursday \| Friday`). Al menos un día. |
| `DefaultView` | int (`CalendarViewType`) | Sí | `Day = 1`, `Week = 2`, `Month = 3`, `Agenda = 4`. Default `Week`. Preferencia de la vista al abrir el calendario. |
| `WeekStartsOn` | int (`DayOfWeek`) | Sí | `Monday` (1) o `Sunday` (0). Default `Monday`. |

Índice único filtrado: `(CompanyId, UserId)` where `GcRecord = 0`. **Este índice es el que garantiza un solo calendario por defecto por usuario y empresa**: hay una sola fila y apunta a un solo calendario.

Horario efectivo del usuario = el suyo si lo tiene; si no, el de la empresa. Si la empresa opera 24 horas y el usuario no tiene horario propio, el horario efectivo es todo el día.

#### 8. `CalendarUserNonWorkingPeriod` — periodos no hábiles del usuario

Tabla `Calendar.UserNonWorkingPeriods`. Hereda `BaseTenantEntity`. **Administra:** el propio usuario (sus vacaciones y permisos) o el administrador por él (`CalendarSettings`). **Pantalla:** Mi calendario → Ausencias; Configuración del calendario → Usuarios → Ausencias.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `UserId` | Guid FK → Users | Sí | |
| `StartDate` | date | Sí | Primer día no hábil (inclusive). |
| `EndDate` | date | Sí | Último día no hábil (inclusive). `>= StartDate`. Rango máximo: 366 días. |
| `Reason` | int (`CalendarNonWorkingReason`) | Sí | `Vacation = 1` (Vacaciones), `Leave = 2` (Permiso), `Absence = 3` (Falta), `Other = 99`. |
| `Notes` | nvarchar(500) | No | |

Índice: `(CompanyId, UserId, StartDate, EndDate)` where `GcRecord = 0` (no único). Dos periodos del mismo usuario **no** pueden solaparse (`CALENDAR_NON_WORKING_PERIOD_OVERLAP`).

Al registrar un periodo **no** se tocan las actividades ya existentes en esas fechas. La respuesta incluye la lista de actividades que caen en el periodo (`affectedEvents`) para que el usuario decida si las reprograma o anula.

---

### OPERACIÓN

#### 9. `UserCalendar` — calendario de un usuario

Tabla `Calendar.UserCalendars`. Hereda `BaseTenantEntity`. **Administra:** el propio usuario. **Pantalla:** Mi calendario → lista lateral de calendarios.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `OwnerUserId` | Guid FK → Users | Sí | Dueño. Inmutable. |
| `Name` | nvarchar(100) | Sí | "Trabajo", "Ventas". Único por dueño y empresa entre activos. |
| `Description` | nvarchar(500) | No | |
| `Color` | nvarchar(7) | Sí | Color con que se pintan sus actividades cuando se ven varios calendarios superpuestos. |
| `IsActive` | bit | Sí | Un calendario inactivo no recibe actividades nuevas y se oculta de la vista por defecto. |
| `SortOrder` | int | Sí | Orden en la lista lateral. |

Índice único filtrado: `(CompanyId, OwnerUserId, Name)` where `GcRecord = 0`.

Reglas:
- No se puede **desactivar ni borrar** el calendario por defecto (`CALENDAR_IS_DEFAULT`); primero hay que marcar otro como predeterminado.
- No se puede **desactivar ni borrar** un calendario con actividades futuras que no estén anuladas (`CALENDAR_HAS_ACTIVE_EVENTS`), y la respuesta dice cuántas hay.

#### 10. `CalendarEvent` — la actividad

Tabla `Calendar.Events`. Hereda `BaseTenantEntity`. **Administra:** el dueño del calendario; otros usuarios con `CalendarOthers`; el agente (SPEC 45). **Pantalla:** vistas día/semana/mes, detalle de actividad, formulario de actividad.

**Qué es y cuándo**

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `CalendarId` | Guid FK → UserCalendar | Sí | Calendario donde vive. Se puede mover a otro calendario del **mismo** dueño. |
| `OwnerUserId` | Guid FK → Users | Sí | Dueño (copia de `UserCalendar.OwnerUserId`). Se guarda en la actividad para que la validación de choques "por usuario" sea una sola consulta indexada. |
| `CalendarActivityCompanyId` | Guid FK | Sí | Tipo de actividad (debe ser usable). |
| `CalendarStatusCompanyId` | Guid FK | Sí | Estado actual. |
| `Title` | nvarchar(200) | Sí | Tema ("Revisión de contrato"). |
| `Summary` | nvarchar(500) | No | Resumen breve (lo que el agente entendió del pedido). |
| `Description` | nvarchar(4000) | No | Detalle libre. |
| `Location` | nvarchar(300) | No | Lugar físico. |
| `VideoCallUrl` | nvarchar(500) | No | Enlace de videollamada (Meet, Teams, Zoom). Debe ser una URL `https` válida. |
| `IsAllDay` | bit | Sí | Actividad de día completo. |
| `StartUtc` | datetime2 | Sí | Inicio en UTC. En actividades de día completo es la medianoche local (zona de la empresa) del primer día, convertida a UTC. |
| `EndUtc` | datetime2 | Sí | Fin en UTC, exclusivo, `> StartUtc`. En día completo es la medianoche local del día siguiente al último. Duración máxima de una ocurrencia: 14 días. |

**Repetición**

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `RecurrenceRule` | nvarchar(500) | No | Regla RFC 5545 **sin** el prefijo `RRULE:` (`FREQ=WEEKLY;INTERVAL=1;BYDAY=MO,WE;COUNT=10`). Nulo = actividad puntual. Solo se aceptan `FREQ` `DAILY`, `WEEKLY`, `MONTHLY` o `YEARLY`, con `INTERVAL`, `BYDAY`, `BYMONTHDAY`, `COUNT` o `UNTIL` (ver Decisiones). Es el mismo formato que usa Google, así SPEC 46 la envía sin traducir. |
| `RecurrenceEndUtc` | datetime2 | No | Calculado por el sistema: fin de la última ocurrencia (a partir de `UNTIL` o `COUNT`). Nulo = sin fin. Sirve para filtrar series en consultas por rango sin expandirlas. |
| `SeriesMasterId` | Guid FK → CalendarEvent | No | En una **excepción** (una ocurrencia modificada o anulada de una serie), apunta a la actividad maestra. Nulo en actividades puntuales y en maestras. |
| `OriginalStartUtc` | datetime2 | Condicional | En una excepción: el inicio que la ocurrencia tenía en la serie original. Identifica qué ocurrencia reemplaza. Obligatorio si `SeriesMasterId` tiene valor. |

Índice único filtrado: `(SeriesMasterId, OriginalStartUtc)` where `SeriesMasterId IS NOT NULL AND GcRecord = 0`.

**Origen: quién y por dónde se creó**

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `OriginChannelId` | Guid FK → CommunicationChannel | Sí | Canal por el que se creó. Desde la app, `WEB` (lo siembra SPEC 37; si todavía no existe, esta spec lo agrega a `SeedCommunicationChannelsAsync` con la misma fila que define SPEC 37). |
| `OriginType` | int (`CalendarEventOriginType`) | Sí | `User = 1` (usuario de JOIN), `Phone = 2` (número de teléfono o WhatsApp), `Email = 3`, `System = 4` (proceso interno). |
| `OriginUserId` | Guid FK → Users | No | Usuario de JOIN que la registró (en `OriginType = User`, quien la creó; si vino por el agente, el usuario de servicio del agente). |
| `OriginIdentifier` | nvarchar(200) | No | Teléfono en formato E.164 o correo del solicitante cuando no es un usuario de JOIN. |
| `OriginReference` | nvarchar(200) | No | Id externo del mensaje o conversación. Evita duplicados cuando el agente reintenta (SPEC 45). |

Índice único filtrado: `(CompanyId, OriginChannelId, OriginReference)` where `OriginReference IS NOT NULL AND GcRecord = 0`.

**Enlaces con el CRM** (todos opcionales y de la misma empresa; se validan al guardar)

| Campo | Tipo | Descripción |
|---|---|---|
| `RequesterPersonId` | Guid FK → Person | Persona que pidió la actividad. |
| `CustomerId` | Guid FK → Customer | Cliente relacionado. |
| `TicketId` | Guid FK → Ticket | Ticket relacionado. |
| `ProjectId` | Guid FK → Project | Proyecto relacionado. |

**Estado**

| Campo | Tipo | Descripción |
|---|---|---|
| `StatusChangedUtc` | datetime2 | Último cambio de estado. |
| `StatusChangedByUserId` | Guid FK → Users | Quién hizo el último cambio de estado. |

Índices adicionales (no únicos, filtrados `GcRecord = 0`):
- `IX_CalendarEvents_Owner_Range`: `(CompanyId, OwnerUserId, StartUtc, EndUtc)` para choques y vistas por usuario.
- `IX_CalendarEvents_Calendar_Range`: `(CalendarId, StartUtc)` para las vistas.
- `IX_CalendarEvents_Owner_Recurring`: `(CompanyId, OwnerUserId, RecurrenceEndUtc)` where `RecurrenceRule IS NOT NULL` para cargar las series que alcanzan un rango.
- `(RequesterPersonId)` y `(TicketId)` para las consultas desde la ficha.

#### 11. `CalendarEventLog` — bitácora de la actividad

Tabla `Calendar.EventLogs`. Hereda `BaseTenantEntity`. Solo lectura desde la API. **Pantalla:** detalle de actividad → Historial.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `CalendarEventId` | Guid FK | Sí | |
| `Action` | int (`CalendarEventLogAction`) | Sí | `Created = 1`, `Updated = 2`, `Rescheduled = 3`, `StatusChanged = 4`, `MovedToCalendar = 5`, `OccurrenceModified = 6`, `SeriesSplit = 7`, `Deleted = 8`. |
| `PreviousStartUtc` / `PreviousEndUtc` | datetime2 | No | En reprogramaciones. |
| `NewStartUtc` / `NewEndUtc` | datetime2 | No | |
| `PreviousStatusCompanyId` / `NewStatusCompanyId` | Guid | No | En cambios de estado (y en reprogramaciones que cambian el estado). |
| `ChannelId` | Guid FK → CommunicationChannel | Sí | Canal por el que se hizo el cambio. |
| `ActorUserId` | Guid FK → Users | No | Usuario de JOIN que hizo el cambio. |
| `ActorIdentifier` | nvarchar(200) | No | Teléfono o correo cuando el cambio lo pidió alguien externo vía agente. |
| `Notes` | nvarchar(1000) | No | Motivo (obligatorio al anular). |
| `OccurredUtc` | datetime2 | Sí | |

Índice: `(CompanyId, CalendarEventId, OccurredUtc)` where `GcRecord = 0`.

---

## Reglas de negocio

### R1. Contexto de empresa y módulo

Todo handler del módulo sigue estos pasos, en orden:
1. `CompanyId == Guid.Empty` → `COMPANY_REQUIRED`.
2. `CalendarModuleGuard.EnsureEnabledAsync` → `CALENDAR_MODULE_NOT_ENABLED` si la empresa no tiene el módulo activo.
3. Los handlers de operación y de configuración de usuario, además → `CALENDAR_COMPANY_NOT_CONFIGURED` si no existe `CalendarCompany`.

Solo los catálogos globales (SuperAdmin) y la creación de `CalendarCompany` se saltan el paso 3.

### R2. Dueño y permisos

- Endpoints de `Calendar` (propio): el calendario o la actividad debe ser del usuario actual. Si no → `404 CALENDAR_EVENT_NOT_FOUND` / `CALENDAR_NOT_FOUND`. No se usa `403`, para no confirmar la existencia de un recurso ajeno.
- Endpoints de `CalendarPanel`: el dueño puede ser cualquier usuario con `UserCompany` activo en la empresa actual. Un usuario de otra empresa → `CALENDAR_USER_NOT_IN_COMPANY`.
- El dueño de una actividad nunca cambia. Para "pasarla" a otro usuario se crea una nueva en el calendario del otro y se anula la original. Mover entre calendarios **del mismo dueño** sí está permitido.

### R3. Tiempos y zona horaria

- La API recibe fechas como `DateTimeOffset` ISO 8601 (`2026-10-05T10:00:00-06:00`) y responde en UTC (`...Z`) junto con `timeZone` (la de la empresa), para que el frontend convierta.
- Actividad de día completo: el request envía `startDate` y `endDate` (fechas, inclusive) y el sistema calcula `StartUtc` y `EndUtc` con la zona de la empresa.
- Si falta el fin, `EndUtc = StartUtc + CalendarActivityCompany.DefaultDurationMinutes`.

### R4. Superposición (`CalendarEventScheduleGuard`)

Se aplica si el tipo de la actividad tiene `AllowsOverlap = false`, a **cualquier** creador, incluido el dueño.

1. Ocurrencias candidatas: si es puntual, una; si es periódica, todas las del rango `[StartUtc, min(RecurrenceEndUtc, StartUtc + 12 meses))`.
2. Actividades existentes que compiten: mismo `CompanyId` y `OwnerUserId` (en **todos** sus calendarios), cuyo tipo tiene `AllowsOverlap = false`, cuyo estado tiene `BlocksTime = true`, sin la propia actividad o serie que se edita. Las series existentes se expanden en el mismo rango, respetando sus excepciones.
3. Hay choque si `a.Start < b.End && b.Start < a.End` (los extremos que se tocan no chocan: 10:00–11:00 y 11:00–12:00 son válidos).
4. Con choques → `409 CALENDAR_EVENT_OVERLAP` con `errors` que lista hasta 10 conflictos (`eventId`, `title`, `startUtc`, `endUtc`). Con pendientes de confirmar también, porque bloquean.

### R5. Horario hábil y días hábiles

Se aplica **solo si** el tipo tiene `RequiresBusinessHours = true` **y** quien crea o modifica **no es el dueño**: un usuario vía panel, o el agente.

Para cada ocurrencia candidata (en la zona de la empresa):
1. Día de la semana incluido en `CalendarUserConfiguration.WorkDays` → si no, `CALENDAR_OUTSIDE_WORK_DAYS`.
2. Fecha fuera de todo `CalendarUserNonWorkingPeriod` del dueño → si no, `CALENDAR_USER_NOT_AVAILABLE` (sin revelar el motivo: vacaciones, falta… es información del empleado).
3. Fecha que no sea feriado de la empresa (`CalendarCompanyNonWorkingDay`, considerando `IsRecurringYearly`) → si no, `CALENDAR_COMPANY_HOLIDAY`.
4. Si **no** es de día completo: inicio y fin dentro del horario efectivo del dueño. Una ocurrencia no puede cruzar la medianoche. Si no → `CALENDAR_OUTSIDE_BUSINESS_HOURS` (con el horario válido en `errors`).

El dueño que se agenda a sí mismo **no** pasa por R5.

### R6. Estado inicial y cambios de estado

- Al crear: `CalendarStatusCompanyId = CalendarActivityCompany.InitialCalendarStatusCompanyId`, sea quien sea el creador. El request **no** acepta estado.
- `ChangeStatus`: acepta cualquier `CalendarStatusCompany` usable de la empresa, sin reglas de transición. Al pasar a `CANCELLED`, `notes` es obligatorio (`CALENDAR_CANCELLATION_REASON_REQUIRED`). Si la actividad pasa de un estado que no bloquea (`CANCELLED`) a uno que sí bloquea, se ejecuta R4 de nuevo, porque recuperar una actividad anulada puede generar un choque.
- **Reprogramar** (cambiar `StartUtc`/`EndUtc`) lo hace alguien distinto al dueño → el estado pasa al configurado en `CalendarCompany.RescheduledByOthersStatusCompanyId` (por defecto "Reprogramada"), para que el dueño lo confirme. Si reprograma el propio dueño, el estado se conserva. Si solo cambian campos que no son de horario, el estado no cambia.
- Toda operación escribe una fila en `CalendarEventLog`.

### R7. Actividades periódicas

- La regla se valida con `ICalendarRecurrenceExpander.ValidateRule` → `CALENDAR_INVALID_RECURRENCE_RULE`. `COUNT` máximo: 730. Sin `COUNT` ni `UNTIL` = sin fin (permitido).
- Una serie **no** puede ser de día completo con `FREQ=DAILY` sin fin (evita series infinitas que ocupan todos los días; `CALENDAR_INVALID_RECURRENCE_RULE`).
- Alcance al modificar, anular o borrar una ocurrencia (`scope` + `occurrenceStartUtc`, que identifica la ocurrencia):
  - `ThisOccurrence` (1): crea o actualiza una **excepción** (`SeriesMasterId` + `OriginalStartUtc`). Anular una sola ocurrencia = excepción en estado `CANCELLED`.
  - `ThisAndFollowing` (2): corta la serie original (`UNTIL` = el instante anterior a la ocurrencia) y crea una **nueva maestra** desde esa ocurrencia con los cambios. Las excepciones posteriores al corte pasan a la nueva serie. Queda `SeriesSplit` en la bitácora de ambas.
  - `Series` (3): modifica la maestra. Si cambia el horario de la serie, las excepciones **se conservan** y se revalidan.
- Las excepciones heredan de la maestra todo campo que no cambien.

### R8. Borrado

`Delete` es borrado lógico (`MarkAsDeleted`), pensado para registros creados por error. Deja una fila `Deleted` en la bitácora. Borrar una maestra borra también sus excepciones. Para cancelar una actividad real se usa `ChangeStatus` → `CANCELLED`.

### R9. Enlaces con el CRM

`RequesterPersonId`, `CustomerId`, `TicketId` y `ProjectId`, si vienen, deben existir, no estar borrados y ser de la misma empresa → si no, `CALENDAR_INVALID_LINK` indicando el campo.

---

## Permisos

Nuevo módulo de menú **Calendario** (`SystemModule`). Recursos (`SystemOption.ControllerName`):

| Recurso | Qué controla | Endpoints |
|---|---|---|
| `Calendar` | El calendario propio: calendarios, actividades, preferencias y ausencias propias. | `UserCalendars`, `CalendarEvents` (salvo el cambio de estado), `CalendarUserConfigurations/me`, `CalendarUserNonWorkingPeriods/me`, lookups de tipos y estados usables. |
| `CalendarOthers` | El panel: consultar calendarios de otros usuarios de la empresa (`CanRead`) y crear, modificar o borrar actividades en ellos (`CanCreate` / `CanUpdate` / `CanDelete`). | `CalendarPanel` |
| `CalendarActivityConfirmation` | Cambiar el estado de una actividad: confirmar, anular, completar (`CanUpdate`). En calendario ajeno se necesita además `CalendarOthers.CanUpdate`: el endpoint del panel exige ambos. | `CalendarEvents/{id}/status`, `CalendarPanel/events/{id}/status` |
| `CalendarSettings` | Parametrización de la empresa: tipos y estados habilitados, `CalendarCompany`, feriados, y configuración y ausencias de **otros** usuarios. | `CalendarActivityCompanies`, `CalendarStatusCompanies`, `CalendarCompanies`, `CalendarCompanyNonWorkingDays`, `CalendarUserConfigurations/{userId}`, `CalendarUserNonWorkingPeriods` (sin `/me`) |
| `CalendarCatalogs` | Catálogos globales. Por defecto solo SuperAdmin. | `CalendarActivities`, `CalendarStatuses` |

**Doble permiso en el cambio de estado desde el panel:** el filtro global resuelve un solo recurso por acción. `CalendarPanelController.ChangeStatus` declara `[PermissionResource("CalendarActivityConfirmation")]`, y el handler verifica además `CalendarOthers.CanUpdate` con el mismo servicio de permisos que usa el filtro (`IPermissionService`, ya declarado en `2.Application/Interface/IPermissionService.cs`, cache `permissions:v2:{companyId}:{userId}`).

**Seed de menú** (`GetAdministrativeSystemOptionSeeds`):

```csharp
new("Calendar", "/calendar", "@Icons.Material.Filled.CalendarMonth", null, null, false, false, false, false),
new("MyCalendar", "/calendar/my_calendar", "@Icons.Material.Filled.Today", "Calendar", "Calendar", true, true, true, true),
new("CalendarPanel", "/calendar/panel", "@Icons.Material.Filled.ViewTimeline", "Calendar", "CalendarOthers", true, true, true, true),
new("ActivityConfirmation", "/calendar/activity_confirmation", "@Icons.Material.Filled.EventAvailable", "Calendar", "CalendarActivityConfirmation", false, false, true, false, IsVisibleMenu: false),
new("CalendarSettings", "/calendar/settings", "@Icons.Material.Filled.EditCalendar", "Calendar", "CalendarSettings", true, true, true, true, CanExecute: true),
new("CalendarCatalogs", "/calendar/catalogs", "@Icons.Material.Filled.Category", "Calendar", "CalendarCatalogs", true, true, true, true),
```

Nombres de opción y rutas **en inglés**, en minúsculas y con `_` para separar palabras (decisión del usuario, 2026-10-01). Son definitivos para el frontend.

**Seed de permisos por rol** (`GetRoleSystemOptionSeeds`, flags `Read, Create, Update, Delete`). Las filas de seed referencian el **nombre de la opción**, no el recurso: `Calendar` → opción `MyCalendar`; `CalendarOthers` → `CalendarPanel`; `CalendarActivityConfirmation` → `ActivityConfirmation`; `CalendarSettings` → `CalendarSettings`; `CalendarCatalogs` → `CalendarCatalogs`.

| Rol | Calendar | CalendarOthers | CalendarActivityConfirmation | CalendarSettings | CalendarCatalogs |
|---|---|---|---|---|---|
| SuperAdmin | ✔✔✔✔ | ✔✔✔✔ | –, –, ✔, – | ✔✔✔✔ + Execute | ✔✔✔✔ |
| SuperAdminCompany | ✔✔✔✔ | ✔✔✔✔ | –, –, ✔, – | ✔✔✔✔ + Execute | ✔ – – – |
| Manager | ✔✔✔✔ | ✔✔✔✔ | –, –, ✔, – | ✔✔✔ – | – |
| Supervisor | ✔✔✔✔ | ✔✔✔ – | –, –, ✔, – | ✔ – – – | – |
| Coordinador | ✔✔✔✔ | ✔ – – – | –, –, ✔, – | – | – |
| UsuarioSimple | ✔✔✔✔ | – | –, –, ✔, – | – | – |

`Admin`, `Agent` y `Person` no reciben permisos de calendario en esta spec (el `Agent` los recibe en SPEC 45). El administrador de la empresa es **`SuperAdminCompany`** (rol que ya existe y que el sistema usa para la configuración por empresa, p.ej. `CompanyModulesController` y `RolesController`), no `Admin`.

**Verificación en F3:** hoy la seed no tiene filas de `RoleSystemOption` para `SuperAdminCompany` (sus endpoints actuales usan `[Authorize(Roles = "SuperAdminCompany")]`). Confirmar que `DynamicAuthorizationFilter` resuelve los permisos de este rol desde `RoleSystemOption` como los demás roles; si no, documentar cómo obtiene acceso y ajustar la seed de esta spec.

### Aislamiento del módulo: qué puede modificar cada permiso

Los cinco recursos de esta spec **solo** habilitan endpoints del módulo Calendario, y esos endpoints **solo escriben en tablas del esquema `Calendar`**. Ningún permiso de calendario permite modificar otras entidades:

| Entidad fuera del módulo | Qué hace el Calendario con ella |
|---|---|
| `Person`, `Customer`, `Ticket`, `Project` | **Solo lectura**: se valida que existan, sean de la empresa y no estén borradas al enlazarlas en una actividad (R9). Nunca se crean, modifican ni borran desde esta spec. |
| `ApplicationUser`, `UserCompany` | **Solo lectura**: validar dueños, usuario por defecto y listar usuarios en el panel. |
| `CommunicationChannel`, `Company`, `CompanyModule` | **Solo lectura.** |
| `InviteUser` / `AddUserCompany` (Security) | Llaman a `CalendarUserProvisioner`, que **solo** agrega filas en `Calendar.UserCalendars` y `Calendar.UserConfigurations`. No cambia nada del usuario ni de su empresa. |

La única excepción es SPEC 45: el agente crea `Person`, `PersonContact` y `PersonPendingConfirmation` con su propio recurso (`CalendarChannelIntake`), solo para personas **nuevas** y sin poder modificar ni borrar personas existentes.

Se agrega una prueba de arquitectura (F10) que verifica que ningún command handler de `UseCases/Calendars` llama a `GetRepository<T>()` ni a repositorios con nombre de entidades fuera de `JOIN.Domain.Calendars`, salvo para lectura.

---

## API

Todas las rutas bajo `api/v1/`. Respuestas en `Response<T>`. Listados paginados con `PagedResult<T>` y la configuración centralizada de SPEC 33. Todas requieren `X-Company-Id` o el claim de empresa.

### Parametrización

**`CalendarActivitiesController`** — `[PermissionResource("CalendarCatalogs")]`

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/CalendarActivities?search&isActive&page&pageSize` | Catálogo global paginado. |
| GET | `/CalendarActivities/{id}` | Detalle. |
| POST | `/CalendarActivities` | Crea. `Code` único → `CALENDAR_ACTIVITY_CODE_DUPLICATE`. |
| PUT | `/CalendarActivities/{id}` | Actualiza (`Code` no editable). No propaga a las empresas: cada empresa conserva sus valores. |
| DELETE | `/CalendarActivities/{id}` | Borrado lógico. Bloqueado si alguna empresa lo tiene habilitado (`CALENDAR_ACTIVITY_IN_USE`) o si es un código de sistema (`PERSON_REVIEW`). |

**`CalendarStatusesController`** — `[PermissionResource("CalendarCatalogs")]`: mismo contrato. No se pueden borrar ni desactivar `PENDING`, `RESCHEDULED`, `CANCELLED`, `CONFIRMED` ni `COMPLETED` (`CALENDAR_STATUS_REQUIRED_BY_SYSTEM`).

**`CalendarActivityCompaniesController`** — `[PermissionResource("CalendarSettings")]`

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/CalendarActivityCompanies?search&isActive&page&pageSize` | Tipos de la empresa, con valores efectivos y los del global (`globalName`, `globalIsActive`) para comparar. |
| GET | `/CalendarActivityCompanies/{id}` | |
| GET | `/CalendarActivityCompanies/usable` | Lookup para el formulario de actividad: solo usables, ordenados. `[PermissionResource("Calendar")]` a nivel de acción, porque lo necesita cualquier usuario del calendario. |
| POST | `/CalendarActivityCompanies` | Habilita un tipo global `{ calendarActivityId, initialCalendarStatusCompanyId? }`. Copia los valores; sin estado inicial usa `PENDING`. Si ya está habilitado → `CALENDAR_ACTIVITY_ALREADY_ENABLED`. |
| PUT | `/CalendarActivityCompanies/{id}` | Edita `name, color, defaultDurationMinutes, allowsOverlap, requiresBusinessHours, initialCalendarStatusCompanyId, isActive, sortOrder`. **No** revalida actividades existentes; los cambios aplican a lo que se cree o modifique después. |
| DELETE | `/CalendarActivityCompanies/{id}` | Bloqueado si hay actividades de ese tipo (`CALENDAR_ACTIVITY_IN_USE`); en ese caso se desactiva. |

**`CalendarStatusCompaniesController`** — `[PermissionResource("CalendarSettings")]`: mismo contrato (GET paginado, GET id, GET `usable` con `[PermissionResource("Calendar")]`, POST habilitar, PUT `name, color, isActive, sortOrder`, DELETE con los bloqueos de la sección 4 del Data model).

**`CalendarCompaniesController`** — `[PermissionResource("CalendarSettings")]`

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/CalendarCompanies/current` | Configuración de la empresa actual, más `isConfigured`, `moduleEnabled` y `usersWithoutCalendar` (cantidad). Nunca 404: si no existe, devuelve `isConfigured = false`. |
| POST | `/CalendarCompanies` | Crea la configuración. `rescheduledByOthersStatusCompanyId` es opcional: si no viene, se usa el estado `RESCHEDULED` de la empresa. Si ya existe → `CALENDAR_COMPANY_ALREADY_CONFIGURED`. Provisiona los calendarios de los usuarios de la empresa que no tengan uno. |
| PUT | `/CalendarCompanies/current` | Actualiza. Valida el horario contra los usuarios (`CALENDAR_USER_HOURS_OUT_OF_COMPANY_RANGE`) y que `rescheduledByOthersStatusCompanyId` sea un estado usable de la empresa (`CALENDAR_STATUS_NOT_ENABLED`). |
| POST | `/CalendarCompanies/current/sync-catalogs` | `[RequirePermission(CanExecute)]`. Ejecuta `SeedCalendarCatalogsForCompanyAsync`: agrega tipos y estados globales que falten. Devuelve cuántos agregó. |
| POST | `/CalendarCompanies/current/provision-users` | `[RequirePermission(CanExecute)]`. Crea el calendario por defecto a los usuarios pendientes. Devuelve la lista de usuarios provisionados. |

**`CalendarCompanyNonWorkingDaysController`** — `[PermissionResource("CalendarSettings")]`: `GET ?year&page&pageSize` (resuelve también los recurrentes para ese año), `GET {id}`, `POST`, `PUT {id}`, `DELETE {id}`. Además `GET /CalendarCompanyNonWorkingDays/range?from&to` con `[PermissionResource("Calendar")]`, para que las vistas marquen los feriados.

**`CalendarUserConfigurationsController`**

| Método | Ruta | Recurso | Descripción |
|---|---|---|---|
| GET | `/CalendarUserConfigurations/me` | `Calendar` | Mi configuración, más el horario efectivo calculado y los datos de `CalendarCompany` que el frontend necesita (`timeZone`, horario de empresa). |
| PUT | `/CalendarUserConfigurations/me` | `Calendar` | Solo preferencias: `defaultView`, `weekStartsOn`. El horario y los días hábiles **no** los edita el propio usuario (son de contrato). |
| GET | `/CalendarUserConfigurations?search&page&pageSize` | `CalendarSettings` | Usuarios de la empresa con su configuración; incluye a los que no tienen calendario (`hasCalendar = false`). |
| GET | `/CalendarUserConfigurations/{userId}` | `CalendarSettings` | |
| PUT | `/CalendarUserConfigurations/{userId}` | `CalendarSettings` | `businessHoursStart`, `businessHoursEnd`, `workDays`, `defaultView`, `weekStartsOn`. |

El calendario por defecto se cambia con `UserCalendars/{id}/set-default`, no aquí.

**`CalendarUserNonWorkingPeriodsController`**

| Método | Ruta | Recurso | Descripción |
|---|---|---|---|
| GET | `/CalendarUserNonWorkingPeriods/me?year` | `Calendar` | |
| POST | `/CalendarUserNonWorkingPeriods/me` | `Calendar` | Devuelve `affectedEvents`. |
| DELETE | `/CalendarUserNonWorkingPeriods/me/{id}` | `Calendar` | |
| GET | `/CalendarUserNonWorkingPeriods?userId&year&page&pageSize` | `CalendarSettings` | |
| POST / PUT {id} / DELETE {id} | `/CalendarUserNonWorkingPeriods` | `CalendarSettings` | Para otro usuario (`userId` en el body). |

### Operación

**`UserCalendarsController`** — `[PermissionResource("Calendar")]`

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/UserCalendars/mine` | Mis calendarios de la empresa actual, con `isDefault`. Si no tengo calendario → lista vacía y `pendingProvisioning = true`. |
| POST | `/UserCalendars` | `{ name, description, color, sortOrder }`. |
| PUT | `/UserCalendars/{id}` | `{ name, description, color, isActive, sortOrder }`. |
| PUT | `/UserCalendars/{id}/set-default` | Lo marca como predeterminado. |
| DELETE | `/UserCalendars/{id}` | Con los bloqueos de la sección 9. |

**`CalendarEventsController`** — `[PermissionResource("Calendar")]`

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/CalendarEvents?from&to&calendarIds&activityCompanyIds&statusCompanyIds&includeCancelled` | **Consulta principal de las vistas.** Devuelve las **ocurrencias expandidas** del rango (puntuales, ocurrencias de series y excepciones aplicadas) de mis calendarios. Rango máximo: 62 días (una vista mes con semanas de borde). `includeCancelled` por defecto `false`. |
| GET | `/CalendarEvents/upcoming?days=7` | Próximas actividades no anuladas ni completadas, para la bienvenida de la app ("tus actividades programadas"). `days` entre 1 y 31. |
| GET | `/CalendarEvents/{id}` | Detalle completo (en una serie, datos de la maestra más `recurrenceRule` y su descripción legible). |
| GET | `/CalendarEvents/{id}/logs` | Bitácora. |
| POST | `/CalendarEvents` | Crea en mi calendario (`calendarId` opcional → el predeterminado). |
| PUT | `/CalendarEvents/{id}?scope&occurrenceStartUtc` | Modifica. `scope` y `occurrenceStartUtc` son obligatorios si la actividad es periódica. |
| PATCH | `/CalendarEvents/{id}/status?scope&occurrenceStartUtc` | `[PermissionResource("CalendarActivityConfirmation")]`. `{ calendarStatusCompanyId, notes }`. |
| DELETE | `/CalendarEvents/{id}?scope&occurrenceStartUtc` | Borrado lógico (R8). |

**`CalendarPanelController`** — `[PermissionResource("CalendarOthers")]`

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/CalendarPanel/users?search&onlyWithCalendar&page&pageSize` | Usuarios de la empresa para el filtro del panel (nombre, correo, `hasCalendar`). |
| GET | `/CalendarPanel/calendars?userIds` | Calendarios de los usuarios seleccionados (máximo 20 usuarios). |
| GET | `/CalendarPanel/events?userIds&calendarIds&from&to&activityCompanyIds&statusCompanyIds&includeCancelled` | Igual que `GET /CalendarEvents`, pero para varios usuarios; cada ocurrencia incluye `ownerUserId` y `ownerName`. Máximo 20 usuarios y 62 días. |
| GET | `/CalendarPanel/events/{id}` y `/CalendarPanel/events/{id}/logs` | Detalle y bitácora de una actividad ajena. |
| GET | `/CalendarPanel/availability?userId&from&to&activityCompanyId` | Huecos libres del usuario en el rango según R4 y R5, en bloques de la duración del tipo (sirve al panel y al agente de SPEC 45). Rango máximo: 14 días. |
| POST | `/CalendarPanel/events` | Crea en el calendario de otro usuario (`ownerUserId` obligatorio; `calendarId` opcional → el predeterminado del dueño). Aplica R5. |
| PUT | `/CalendarPanel/events/{id}?scope&occurrenceStartUtc` | Aplica R5 y, si cambia el horario, el estado de `CalendarCompany.RescheduledByOthersStatusCompanyId` (R6). |
| PATCH | `/CalendarPanel/events/{id}/status?scope&occurrenceStartUtc` | `[PermissionResource("CalendarActivityConfirmation")]` + chequeo de `CalendarOthers.CanUpdate` en el handler. |
| DELETE | `/CalendarPanel/events/{id}?scope&occurrenceStartUtc` | |

### Contratos principales (DTOs en `src/2.Application.DTO/Calendars/`)

```jsonc
// POST /CalendarEvents  (el panel agrega "ownerUserId")
{
  "calendarId": "guid | null",
  "calendarActivityCompanyId": "guid",
  "title": "Revisión de contrato",
  "summary": "El cliente quiere revisar la cláusula 4",
  "description": null,
  "location": null,
  "videoCallUrl": "https://meet.google.com/abc-defg-hij",
  "isAllDay": false,
  "start": "2026-10-05T10:00:00-06:00",   // si isAllDay: "startDate": "2026-10-05"
  "end": "2026-10-05T11:00:00-06:00",     // opcional; si isAllDay: "endDate"
  "recurrenceRule": "FREQ=WEEKLY;BYDAY=MO;COUNT=10",  // opcional
  "requesterPersonId": null, "customerId": null, "ticketId": null, "projectId": null
}

// Ocurrencia en GET /CalendarEvents y /CalendarPanel/events
{
  "eventId": "guid",               // id de la maestra o de la actividad puntual
  "exceptionId": "guid | null",    // si la ocurrencia es una excepción
  "occurrenceStartUtc": "2026-10-05T16:00:00Z",   // clave de la ocurrencia para PUT/PATCH/DELETE con scope
  "startUtc": "...", "endUtc": "...", "isAllDay": false,
  "isRecurring": true,
  "title": "...", "summary": "...", "location": null, "videoCallUrl": "...",
  "calendarId": "guid", "calendarName": "Trabajo", "calendarColor": "#1E88E5",
  "activity": { "id": "guid", "code": "MEETING", "name": "Reunión", "color": "#1E88E5", "allowsOverlap": false },
  "status": { "id": "guid", "code": "PENDING", "name": "Pendiente de confirmación", "color": "#FDD835", "blocksTime": true, "isFinal": false },
  "ownerUserId": "guid", "ownerName": "Livingstone Cano",
  "origin": { "channelCode": "WEB", "type": "User", "identifier": null },
  "timeZone": "America/Managua"
}
```

Respuesta de `GET /CalendarEvents`: `{ timeZone, from, to, occurrences: [...], nonWorkingDays: [...] }`. Se incluyen los feriados de la empresa y los periodos no hábiles **del propio usuario** del rango, para que la vista los marque sin otra llamada. En el panel, los periodos de otros usuarios aparecen solo como `"unavailable"`, sin motivo.

### Implementación de las consultas (Dapper)

`GetCalendarEventsRangeQueryHandler` y el del panel, en tres pasos:
1. Actividades puntuales que intersectan el rango: `WHERE e.CompanyId = @TenantId AND e.GcRecord = 0 AND e.RecurrenceRule IS NULL AND e.SeriesMasterId IS NULL AND e.StartUtc < @To AND e.EndUtc > @From AND e.OwnerUserId IN @Owners`.
2. Maestras que pueden aportar ocurrencias: `RecurrenceRule IS NOT NULL AND StartUtc < @To AND (RecurrenceEndUtc IS NULL OR RecurrenceEndUtc > @From)`, y sus excepciones con `SeriesMasterId IN @Masters`.
3. En memoria: `ICalendarRecurrenceExpander.Expand` por maestra, reemplazo de las ocurrencias que tienen excepción y descarte de las excepciones anuladas (salvo `includeCancelled`), filtros de tipo y estado, orden por inicio.

Se usa SQL portable (sin funciones de fecha del motor), igual que el resto de las consultas.

---

## Implementation plan

### F1 — Dominio
Entidades, enums y `CalendarCodes` en `src/1.Domain/Calendars/`.

### F2 — Persistencia
Configuraciones EF (`Configuration/Calendars/`), `DbSet`s, query filters (o SPEC 39), relaciones `Restrict`, índices filtrados y migración `AddCalendarModule`. Verificar que la migración crea el esquema `Calendar` y las 11 tablas.

### F3 — Seed
`SystemModule` "Calendar", catálogos globales, `SystemOptions`, `RoleSystemOptions`, `SeedCalendarCatalogsForCompanyAsync` dentro de `SeedDefaultCatalogsForCompanyAsync`, y el seed de desarrollo de `JOIN-001` (`CompanyModule`, `CalendarCompany`, provisión). Canal `WEB` si SPEC 37 todavía no lo sembró.

### F4 — Servicios comunes
`CalendarModuleGuard`, `CalendarUserProvisioner`, `CalendarUserDefaultCoordinator`, `CalendarEventInitialStatusResolver`, `CalendarEventLogWriter`, `ICalendarRecurrenceExpander` + `IcalRecurrenceExpander` (Infrastructure, paquete Ical.Net) y `CalendarEventScheduleGuard`. Registro DI.

### F5 — Catálogos globales y de empresa
`CalendarActivities`, `CalendarStatuses`, `CalendarActivityCompanies`, `CalendarStatusCompanies` (commands, queries, validators, mappers Mapperly, controllers).

### F6 — Configuración de empresa y usuario
`CalendarCompanies` (incluidos `sync-catalogs` y `provision-users`), `CalendarCompanyNonWorkingDays`, `CalendarUserConfigurations`, `CalendarUserNonWorkingPeriods`. Llamada al provisioner desde `InviteUserCommandHandler` y `AddUserCompanyCommandHandler`.

### F7 — Calendarios de usuario
`UserCalendars`.

### F8 — Actividades
`CalendarEvents`: Create, Update (tres alcances), ChangeStatus, Delete, GetRange, GetById, GetUpcoming, GetLogs.

### F9 — Panel
`CalendarPanel`: users, calendars, events, availability y las escrituras, con el doble chequeo de permisos en el cambio de estado.

### F10 — Tests (~110 casos)
- **Guard / módulo (6):** sin empresa → error; módulo inactivo → `CALENDAR_MODULE_NOT_ENABLED`; sin `CalendarCompany` → `CALENDAR_COMPANY_NOT_CONFIGURED`; catálogos globales no exigen `CalendarCompany`.
- **Provisioner (6):** crea calendario + configuración; idempotente; no hace nada sin módulo o sin `CalendarCompany`; toma el horario de la empresa; lo llaman `InviteUser`, `AddUserCompany` y `CreateCalendarCompany`.
- **Catálogos (16):** códigos duplicados; borrado bloqueado en uso; códigos de sistema protegidos; habilitar copia valores; habilitar dos veces → error; "usable" exige ambas tablas activas; estado inicial inválido.
- **CalendarCompany / usuario (16):** `RescheduledByOthersStatusCompanyId` por defecto = `RESCHEDULED` de la empresa; estado de reprogramación no usable → error; horario de cierre ≤ apertura; 24 horas ignora horas; zona inválida; horario de usuario fuera del de la empresa; cambio de horario de empresa con usuarios afectados; `WorkDays` vacío; periodos solapados; `affectedEvents`.
- **UserCalendars (8):** default no borrable ni desactivable; con actividades futuras → bloqueado; `set-default` cambia la configuración; nombre duplicado.
- **ScheduleGuard (24):** choque simple; extremos que se tocan no chocan; `AllowsOverlap` en cualquiera de los lados → sin choque; anulada no bloquea; pendiente sí bloquea; choque entre calendarios distintos del mismo dueño; choque con una ocurrencia de una serie; serie nueva contra actividad puntual (horizonte de 12 meses); excepción anulada libera; R5 solo si el creador no es el dueño; fuera de horario; día no hábil; periodo no hábil (sin revelar motivo); feriado anual y puntual; día completo en día hábil; 24 horas; cruce de medianoche.
- **CalendarEvents (24):** estado inicial desde configuración; fin por defecto con la duración de la empresa; día completo calcula UTC en la zona de la empresa (incluye una zona con horario de verano); regla inválida; `COUNT` > 730; los tres alcances (excepción, corte de serie, serie completa); reprogramar por no dueño → estado configurado en `RescheduledByOthersStatusCompanyId` (probar con un estado distinto de `RESCHEDULED`); por dueño conserva estado; cambio solo de título por no dueño conserva estado; anular exige nota; reactivar anulada revalida choques; enlaces CRM de otra empresa → error; bitácora en cada operación; borrar maestra borra excepciones.
- **Consultas (12):** expansión con excepciones; rango máximo; filtros; `upcoming`; feriados y periodos en la respuesta; panel con más de 20 usuarios → error; `availability`.
- **Arquitectura (1):** los command handlers de `UseCases/Calendars` no agregan, modifican ni borran entidades fuera de `JOIN.Domain.Calendars`.
- **Integración (1 flujo largo):** configurar empresa → crear actividad → choque 409 → confirmar → consulta semana.

### F11 — Verificación final
`dotnet build` sin warnings nuevos (Mapperly `RMG020` incluido), `dotnet test` del proyecto de unitarios con cobertura ≥ 90%, pruebas de integración en verde, y `CURL_REQUESTS.md` con ejemplos de los endpoints principales.

---

## Acceptance criteria

### Datos
- [ ] Existe el esquema `Calendar` con las 11 tablas de esta spec; ninguna tabla nueva queda en otro esquema.
- [ ] Todas las entidades tenant heredan de `BaseTenantEntity`; los dos catálogos globales, de `BaseAuditableEntity`.
- [ ] Todos los índices únicos son filtrados por `GcRecord = 0` y ninguno incluye `GcRecord` en la clave.
- [ ] Un usuario no puede tener dos filas de `CalendarUserConfiguration` activas en la misma empresa (índice único) y por lo tanto nunca tiene dos calendarios por defecto.

### Parametrización
- [ ] Una empresa nueva nace con todos los tipos y estados globales activos copiados a sus tablas, con `PENDING` como estado inicial.
- [ ] Solo se pueden crear actividades de tipos usables (activos en global y en empresa).
- [ ] La duración, el color, la superposición y el horario hábil que aplican son los de `CalendarActivityCompany`, no los del global.
- [ ] `CalendarCompany` exige usuario por defecto de la empresa, zona IANA válida y horario válido o 24 horas.
- [ ] El horario de un usuario no puede salir del horario de la empresa.

### Operación
- [ ] Al asignar un usuario a una empresa con `CalendarCompany`, se le crea "Mi calendario" como predeterminado; sin `CalendarCompany` no se crea nada, y al crearla se provisionan todos los pendientes.
- [ ] Dos actividades que no permiten superposición no pueden coincidir para el mismo usuario, aunque estén en calendarios distintos; una pendiente bloquea y una anulada no.
- [ ] Nadie, salvo el dueño, puede crearle una actividad que requiere horario hábil fuera de su horario, en un día que no trabaja, en sus periodos no hábiles o en un feriado de la empresa. Los recordatorios no tienen esa restricción.
- [ ] Las actividades periódicas (diaria, semanal, mensual, anual, cada N, fin por fecha, por cantidad o sin fin) se expanden correctamente en las vistas, y se pueden modificar o anular por ocurrencia, desde una ocurrencia en adelante o la serie completa.
- [ ] Anular exige motivo, conserva la actividad y deja registro en la bitácora; cualquier cambio queda en `CalendarEventLog` con canal y actor.
- [ ] `GET /CalendarEvents` devuelve lo necesario para las vistas de día, semana y mes en una sola llamada.
- [ ] El panel solo muestra usuarios de la empresa actual y exige `CalendarOthers`.
- [ ] Sin el módulo activo en `CompanyModule`, ningún endpoint del módulo permite registrar calendarios ni actividades.
- [ ] Ningún endpoint de esta spec crea, modifica ni borra registros fuera del esquema `Calendar`; las entidades del CRM y de seguridad solo se leen.
- [ ] Los permisos de administración de la empresa se siembran para `SuperAdminCompany`, no para `Admin`.

### General
- [ ] Todas las consultas son Dapper con `WHERE CompanyId = @TenantId AND GcRecord = 0`; los comandos usan `IUnitOfWork`.
- [ ] Cobertura ≥ 90%, cero warnings nuevos.

---

## Decisions taken and discarded

- **Entidad `UserCalendar` (tabla `Calendar.UserCalendars`) en vez de `Calendar`** (elegido). Una clase `Calendar` choca con `System.Globalization.Calendar`, que ya se importa en `BaseAuditableEntity`, y con el nombre del esquema y del namespace. `UserCalendar` además dice explícitamente que pertenece a un usuario.
- **Namespace `JOIN.Domain.Calendars`** (plural), por el mismo motivo.
- **Catálogos globales heredan de `BaseAuditableEntity`** (elegido) vs `BaseTenantEntity`. Son la "lista de todas las posibles" y no tienen empresa dueña; seguir la regla "todo hereda de `BaseTenantEntity`" obligaría a inventarles una empresa. Es el mismo criterio de `CommunicationChannel` y `SystemModule`. Todas las demás entidades sí son tenant.
- **Una fila de `CalendarUserConfiguration` apunta al calendario por defecto** (elegido) vs flag `IsDefault` en `UserCalendar` con índice único filtrado `WHERE IsDefault = 1`. Es la "estructura separada" pedida. El índice único `(CompanyId, UserId)` garantiza la regla sin depender de un filtro por valor (que en PostgreSQL requiere otra sintaxis, SPEC 40), y la misma fila guarda horario y preferencias.
- **`BlocksTime` e `IsFinal` solo en el catálogo global** (elegido). Definen el comportamiento del sistema (choques, sincronización con Google, barridos futuros). Si cada empresa pudiera cambiarlos, "Anulada" podría bloquear en una empresa y no en otra, y SPEC 46 no podría decidir qué sincronizar. La empresa sí personaliza nombre, color, orden y habilitación.
- **Estado inicial en `CalendarActivityCompany`** (elegido) vs en `CalendarCompany`. El usuario definió `CalendarCompany` como una sola fila por empresa, y el estado inicial es **por tipo** de actividad (un recordatorio puede nacer confirmado y una cita pendiente), así que va en la fila del tipo.
- **Se descarta `RequiresConfirmationWhenCreatedByAgent`** (propuesta inicial): el estado inicial configurable por tipo cubre ese caso sin un flag extra.
- **Cinco recursos de permiso: los tres operativos acordados más `CalendarSettings` y `CalendarCatalogs`** (confirmado por el usuario, 2026-10-01). La parametrización tiene permisos propios para que un usuario común no edite la configuración de su empresa, y el catálogo global queda solo para SuperAdmin. Se descartó un único recurso de configuración (mezclaba empresa y catálogo global) y un recurso por pantalla (demasiadas filas en la matriz de roles).
- **Administrador de empresa = `SuperAdminCompany`** (decisión del usuario, 2026-10-01): es el rol que el sistema ya usa para la configuración por empresa. El resto de la matriz de roles queda como se propuso.
- **Módulo, opciones de menú y rutas en inglés** (`Calendar`, `/calendar/my_calendar`…), decisión del usuario, 2026-10-01.
- **Sin reglas de transición de estados** (decisión del usuario, SPEC futura con Hangfire). La excepción es técnica: reactivar una anulada revalida choques, para no dejar una superposición inválida.
- **Estado tras reprogramar por alguien distinto al dueño, parametrizable en `CalendarCompany.RescheduledByOthersStatusCompanyId`** (decisión del usuario, 2026-10-01) vs fijarlo en el código como `RESCHEDULED` o `PENDING`. Cada empresa elige qué estado usar; el seed y la creación de la configuración proponen "Reprogramada". Los handlers leen siempre la configuración y nunca comparan contra el código `RESCHEDULED`.
- **Repetición como `RRULE` (RFC 5545) con expansión en memoria** (elegido) vs materializar cada ocurrencia como una fila. Guardar la regla es lo que hace Google, permite series sin fin y hace la sincronización de SPEC 46 directa. Materializar simplificaría las consultas, pero multiplica filas, complica "esta y las siguientes" e impide series sin fin. Se acota el costo con el horizonte de 12 meses y el rango máximo de 62 días por consulta.
- **Subconjunto de RRULE** (`FREQ` diaria/semanal/mensual/anual, `INTERVAL`, `BYDAY`, `BYMONTHDAY`, `COUNT`, `UNTIL`). Cubre lo pedido y casos como "lunes y miércoles cada 2 semanas" o "el último viernes del mes" (`BYDAY=-1FR`). Se rechazan `BYHOUR`, `BYSETPOS` y `FREQ` menores a diaria.
- **Ical.Net detrás de una interfaz, en Infrastructure** (elegido). Application solo depende de Domain y DTO; la librería queda reemplazable.
- **`OwnerUserId` copiado en `CalendarEvent`** (elegido) vs obtenerlo siempre por join con `UserCalendar`. La validación de choques por usuario es la consulta más frecuente del módulo; el dueño de un calendario es inmutable, así que la copia no puede quedar desactualizada.
- **Control de módulo propio del Calendario (`CalendarModuleGuard`)** (elegido) vs control global en `DynamicAuthorizationFilter`. `CompanyModule` hoy no se consulta en ningún lado y todavía no está poblado (lo poblará el seeder de módulos base). Activarlo globalmente cortaría el acceso a módulos existentes; queda como spec futura.
- **Zona horaria en `CalendarCompany`** (elegido) vs columna nueva en `Common.Companies`. La decisión fue "zona por empresa"; guardarla en la configuración del módulo evita tocar `Company`, que no tiene hoy ningún dato de este tipo. Si otro módulo la necesita, se mueve a `Company` en su propia spec.
- **Empresa 24 horas con `IsOpen24Hours`** (elegido) vs `00:00–00:00` o `00:00–23:59`. Un flag explícito no tiene ambigüedad para el frontend ni pierde el último minuto del día.
- **R5 no se aplica al dueño.** Decisión del usuario: el dueño siempre puede agendarse (incluso fuera de horario o en vacaciones). R4 (superposición) sí se le aplica, porque protege la consistencia del calendario, no la disponibilidad.
- **Motivo de la ausencia oculto en choques y en el panel** (elegido): "Vacaciones" o "Falta" es información laboral del empleado; quien agenda solo necesita saber que no está disponible.

---

## Identified risks

| Riesgo | Mitigación |
|---|---|
| `CompanyModule` no está poblado: hasta que el seeder de módulos base lo llene, **ninguna empresa** puede usar el calendario. | El seed de desarrollo activa el módulo en `JOIN-001`. En producción, activar el módulo "Calendar" por empresa es parte del despliegue (documentado en `CURL_REQUESTS.md`), y el seeder de módulos base lo incluirá. |
| Expandir series en memoria puede ser costoso con muchas series activas en el panel (20 usuarios × varias series). | Rango máximo de 62 días, máximo 20 usuarios, carga de solo las maestras que alcanzan el rango (índice `IX_CalendarEvents_Owner_Recurring`). Si hace falta, cache por serie en una spec futura. |
| Carrera: dos creaciones simultáneas para el mismo usuario y horario pasan la validación de choques. | El comando es `ITransactionalCommand`; la verificación y la inserción van en la misma transacción. Para cerrar la carrera por completo se toma un bloqueo de aplicación por `(CompanyId, OwnerUserId)` (`sp_getapplock` en SQL Server / `pg_advisory_xact_lock` en el fork PostgreSQL) dentro de `CalendarEventScheduleGuard`. |
| Cambiar la zona horaria de la empresa desplaza la hora local de las actividades existentes (se guardan en UTC). | `PUT /CalendarCompanies/current` devuelve una advertencia si cambia la zona y hay actividades futuras. Recalcular es una operación explícita fuera de alcance. |
| Cambios en `CalendarActivityCompany` (p.ej. pasar a "no permite superposición") no se aplican a actividades ya existentes, que podrían quedar superpuestas. | Documentado: la regla aplica a lo que se cree o modifique después. |
| La expansión de RRULE depende de Ical.Net; una versión con errores afectaría todas las vistas. | Interfaz propia con pruebas de contrato (casos DST, último viernes del mes, `COUNT`, `UNTIL`) que corren contra la implementación real. |
| Actividades de día completo en series largas bloquean muchos días si su tipo no permite superposición. | Se usan normalmente con tipos que permiten superposición; la validación de 12 meses acota el costo. |

---

## Puntos a validar en la revisión

Ninguno. Los cuatro puntos abiertos se resolvieron con el usuario el 2026-10-01: estado de reprogramación parametrizable en `CalendarCompany`, recursos `CalendarSettings`/`CalendarCatalogs`, matriz de roles con `SuperAdminCompany`, y menú y rutas en inglés.
