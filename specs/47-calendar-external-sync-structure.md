# SPEC 46 — Calendario: estructura de datos para sincronizar con calendarios externos (Google Calendar)

> **Status:** Borrador
> **Depends on:** SPEC 44 (`CalendarEvent`, `UserCalendar`, estados `CONFIRMED`/`CANCELLED`).
> **Date:** 2026-09-29
> **Objective:** Dejar creadas, en el esquema `Calendar`, las entidades, enums, configuraciones EF y la migración que necesita la sincronización de actividades de JOIN con el calendario de Google de cada usuario: la conexión de la cuenta externa, la relación entre una actividad de JOIN y su copia externa, y la cola de cambios pendientes de enviar. Esta spec **no** implementa acciones (OAuth, llamadas a la API de Google, webhook ni worker); documenta las reglas que esas acciones deberán cumplir para que la estructura quede correcta desde ahora.

---

## Por qué existe esta spec

El usuario quiere que una actividad registrada en JOIN pueda reflejarse en su Google Calendar "si la persona lo facilita". Se acordó (2026-09-29) dejar el proyecto de Google Cloud y las acciones para la última etapa, pero considerar **ya** los objetos y la base de datos. Así, las acciones se agregan después sin rediseñar el modelo.

Decisiones acordadas:

| # | Decisión |
|---|---|
| 1 | Cada usuario **vincula su propia cuenta** de Google (OAuth del usuario, no una cuenta de servicio de dominio). |
| 2 | Solo se sincronizan las actividades **creadas en JOIN**. Las actividades que el usuario tenga en Google no se importan. |
| 3 | Solo se sincroniza **desde la vinculación en adelante**: nada anterior. |
| 4 | Ante un conflicto (la misma actividad se cambió en ambos lados), **gana el último cambio**. |
| 5 | Solo se envían actividades **confirmadas** (`CONFIRMED`); las pendientes no ensucian el Google del usuario. |
| 6 | El proyecto de Google Cloud, OAuth, webhook y worker (Hangfire) son **etapa posterior**. |
| 7 | El modelo debe permitir agregar otros proveedores (Outlook) sin rediseño. |

---

## Scope

**In:**

- `src/1.Domain/Calendars/CalendarExternalConnection.cs`, `CalendarExternalEventLink.cs`, `CalendarSyncOutboxItem.cs` (nuevos, `BaseTenantEntity`).
- Enums en `src/1.Domain/Calendars/Enums/`: `CalendarExternalProvider`, `CalendarExternalConnectionStatus`, `CalendarExternalSyncState`, `CalendarSyncOperation`, `CalendarSyncOutboxStatus`.
- Configuraciones EF en `Configuration/Calendars/`, `DbSet`s, líneas `HasQueryFilter` explícitas tenant + soft delete (convención SPEC 39), FKs `Restrict`, índices filtrados (SPEC 41).
- Migración `AddCalendarExternalSyncStructure`.
- Tests de configuración: el modelo EF compila y la migración crea las tres tablas con sus índices (prueba de integración con Testcontainers). No hay handlers nuevos, así que no cambia la cobertura del proyecto de unitarios.

**Out of scope (spec posterior, "Sincronización con Google Calendar"):**

- Proyecto de Google Cloud, pantalla de consentimiento OAuth, credenciales y configuración.
- Endpoints para vincular y desvincular la cuenta (`/CalendarExternalConnections/google/authorize`, callback, `DELETE`).
- Encolar cambios desde los handlers de SPEC 44 y 45.
- Worker (Hangfire) que procesa la cola, llamadas a Google Calendar API v3, webhook de notificaciones push (`events.watch`) y sincronización incremental.
- DTOs, controllers, permisos y menús de la integración.

---

## Data model

Esquema `Calendar`. Las tres entidades heredan de `BaseTenantEntity`: una conexión pertenece al usuario **dentro de una empresa**, porque las actividades son por empresa.

> **Guía para el diseño del frontend:** estas tablas son de **operación técnica**. El usuario solo verá la pantalla "Mi calendario → Conexiones" (vincular/desvincular Google, estado y última sincronización) y un indicador de sincronización en el detalle de la actividad. No hay parametrización de empresa en esta etapa.

### 1. `CalendarExternalConnection` — cuenta externa vinculada

Tabla `Calendar.ExternalConnections`.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `UserId` | Guid FK → Users | Sí | Usuario de JOIN dueño de la conexión. |
| `Provider` | int (`CalendarExternalProvider`) | Sí | `Google = 1`. Reservado: `Microsoft = 2`. |
| `ExternalAccountEmail` | nvarchar(256) | Sí | Cuenta de Google vinculada (para mostrarla al usuario). |
| `ExternalCalendarId` | nvarchar(256) | Sí | Calendario de Google destino. Default `"primary"`. |
| `SourceCalendarId` | Guid FK → UserCalendar | No | Calendario de JOIN que se sincroniza. Nulo = **todos** los calendarios del usuario en la empresa. |
| `EncryptedRefreshToken` | nvarchar(2000) | Sí | Refresh token cifrado con ASP.NET Core Data Protection (propósito `Calendar.ExternalConnection.RefreshToken`). **Nunca** se devuelve por API ni se escribe en logs. |
| `GrantedScopes` | nvarchar(500) | Sí | Scopes otorgados (`https://www.googleapis.com/auth/calendar.events`). |
| `Status` | int (`CalendarExternalConnectionStatus`) | Sí | `Active = 1`, `Revoked = 2` (el usuario desvinculó o revocó en Google), `Error = 3` (token inválido; requiere volver a vincular), `Paused = 4`. |
| `ConnectedUtc` | datetime2 | Sí | Momento de la vinculación. **Solo** se sincronizan actividades con `Created >= ConnectedUtc` (decisión 3). |
| `LastSyncUtc` | datetime2 | No | Última sincronización exitosa. |
| `LastError` | nvarchar(1000) | No | Último error (sin datos sensibles). |
| `ConsecutiveFailures` | int | Sí | Fallos seguidos; al llegar al límite configurado pasa a `Error`. |
| `InboundSyncToken` | nvarchar(500) | No | `nextSyncToken` de Google para traer solo los cambios de las actividades vinculadas. |
| `WatchChannelId` | nvarchar(200) | No | Id del canal de notificaciones push (`events.watch`). |
| `WatchResourceId` | nvarchar(200) | No | `resourceId` que devuelve Google (necesario para detener el canal). |
| `WatchChannelToken` | nvarchar(200) | No | Token propio que Google reenvía en cada notificación, para validar el origen. |
| `WatchExpiresUtc` | datetime2 | No | Vencimiento del canal (Google los expira; el worker los renueva). |

Índices:
- Único filtrado `(CompanyId, UserId, Provider)` where `GcRecord = 0 AND Status <> 2`: una conexión viva por proveedor, usuario y empresa. Tras revocar, se puede volver a vincular.
- Único filtrado `(WatchChannelId)` where `WatchChannelId IS NOT NULL AND GcRecord = 0`, para resolver la conexión desde el webhook.

### 2. `CalendarExternalEventLink` — actividad de JOIN ↔ actividad externa

Tabla `Calendar.ExternalEventLinks`.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `CalendarEventId` | Guid FK → Calendar.Events | Sí | Actividad de JOIN: maestra, puntual o excepción. |
| `ConnectionId` | Guid FK → ExternalConnections | Sí | |
| `ExternalEventId` | nvarchar(1024) | Sí | Id del evento en Google. |
| `ExternalICalUid` | nvarchar(1024) | No | `iCalUID` de Google (identificador estable entre calendarios). |
| `ExternalEtag` | nvarchar(200) | No | `etag` de la última versión conocida, para detectar cambios y enviar actualizaciones condicionales. |
| `ExternalUpdatedUtc` | datetime2 | No | `updated` de Google en la última lectura. Se compara con `CalendarEvent.LastModified` para resolver conflictos. |
| `LastSyncedUtc` | datetime2 | No | |
| `SyncState` | int (`CalendarExternalSyncState`) | Sí | `PendingPush = 1`, `Synced = 2`, `Conflict = 3` (registrado para auditoría; se resolvió por "último cambio gana"), `Error = 4`, `RemovedExternally = 5` (se borró en Google), `Unlinked = 6` (la actividad dejó de ser sincronizable y se borró en Google). |
| `LastError` | nvarchar(1000) | No | |

Índices:
- Único filtrado `(ConnectionId, CalendarEventId)` where `GcRecord = 0`.
- Único filtrado `(ConnectionId, ExternalEventId)` where `GcRecord = 0`.

En Google, cada evento creado por JOIN lleva `extendedProperties.private.joinEventId = <CalendarEvent.Id>` y `joinCompanyId`. Así, un cambio que llega por webhook se reconoce como propio aunque se pierda el vínculo, y se ignoran los eventos que no nacieron en JOIN (decisión 2).

### 3. `CalendarSyncOutboxItem` — cambios pendientes de enviar

Tabla `Calendar.SyncOutbox`. Patrón *outbox*: los handlers de actividades escriben aquí **en la misma transacción** que el cambio, y el worker lo envía después con reintentos. Si Google no responde, la actividad en JOIN se guarda igual.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `CalendarEventId` | Guid FK → Calendar.Events | Sí | |
| `ConnectionId` | Guid FK → ExternalConnections | Sí | |
| `Operation` | int (`CalendarSyncOperation`) | Sí | `Upsert = 1` (crear o actualizar en Google), `Delete = 2`. |
| `Status` | int (`CalendarSyncOutboxStatus`) | Sí | `Pending = 1`, `Processing = 2`, `Done = 3`, `Failed = 4` (agotó reintentos), `Superseded = 5` (un cambio posterior de la misma actividad lo reemplazó). |
| `Attempts` | int | Sí | |
| `NextAttemptUtc` | datetime2 | Sí | Reintento con espera exponencial. |
| `EnqueuedUtc` | datetime2 | Sí | |
| `ProcessedUtc` | datetime2 | No | |
| `LockedUntilUtc` | datetime2 | No | Para que dos instancias del worker no tomen el mismo ítem. |
| `LastError` | nvarchar(1000) | No | |

Índices:
- `(Status, NextAttemptUtc)` where `GcRecord = 0`, para la cola del worker.
- Único filtrado `(ConnectionId, CalendarEventId)` where `Status = 1 AND GcRecord = 0`: un solo pendiente por actividad y conexión. Un cambio nuevo actualiza el pendiente en vez de encolar otro.

Los ítems `Done` y `Superseded` se purgan pasados 30 días (tarea de mantenimiento de la spec posterior).

---

## Reglas para la spec de acciones (documentadas ahora para fijar el modelo)

1. **Qué se envía.** Una actividad se sincroniza hacia una conexión `Active` si:
   - su dueño es el usuario de la conexión;
   - su calendario coincide con `SourceCalendarId` (o este es nulo);
   - `Created >= ConnectedUtc`;
   - y su estado es `CONFIRMED`.
2. **Cuándo se borra en Google.** Si una actividad sincronizada deja de cumplir la regla 1 (anulada, vuelve a pendiente o reprogramada por otro, cambia de calendario, se borra), se encola `Delete` y el vínculo queda `Unlinked`. Si vuelve a `CONFIRMED`, se encola `Upsert` y se crea de nuevo.
3. **Series.** La maestra se envía con su `RRULE` tal cual (SPEC 44 guarda el formato de Google). Las excepciones se envían como instancias modificadas o canceladas de la serie en Google (`recurringEventId` + `originalStartTime`), con su propio `CalendarExternalEventLink`.
4. **Cambios que vienen de Google.** Solo se procesan eventos con `extendedProperties.private.joinEventId`. Se aplican título, descripción, lugar, horario y cancelación. Si el horario cambia, pasa por `CalendarEventScheduleGuard` **como dueño** (sin R5, con R4). Si genera un choque, no se aplica, se marca `Conflict` y se reenvía la versión de JOIN.
5. **Conflicto.** Se compara `ExternalUpdatedUtc` (Google) con `CalendarEvent.LastModified` (JOIN): gana el más reciente. El perdedor se sobrescribe y queda registrado en `CalendarEventLog` (canal = `GOOGLE_CALENDAR`, ver punto siguiente).
6. **Canal.** La spec de acciones siembra el `CommunicationChannel` `Google Calendar` (`Code = "GOOGLE_CALENDAR"`, `Provider = "Google"`) para registrar como origen los cambios que llegan desde Google.
7. **Desvincular.** Revoca el token en Google, detiene el canal push, pasa la conexión a `Revoked` y borra el token cifrado. **No** borra los eventos ya creados en Google (el usuario los puede conservar), salvo que lo pida explícitamente.
8. **Seguridad.** El refresh token se cifra con Data Protection. El webhook valida `WatchChannelToken` y nunca confía en el cuerpo de la notificación: con ella solo dispara una lectura incremental con `InboundSyncToken`.

---

## Implementation plan

### F1 — Dominio
Tres entidades y cinco enums.

### F2 — Persistencia
Configuraciones EF, `DbSet`s, query filters, relaciones `Restrict` (hacia `Users`, `UserCalendars` y `Events`, para evitar rutas de cascada múltiples), índices filtrados, migración `AddCalendarExternalSyncStructure`.

### F3 — Tests
Prueba de integración que aplica las migraciones en Testcontainers y verifica que existen las tres tablas y sus índices únicos, y que el modelo EF se construye sin errores.

### F4 — Verificación final
Build sin warnings nuevos, migración reversible (`Down` elimina las tres tablas).

---

## Acceptance criteria

- [ ] Existen `Calendar.ExternalConnections`, `Calendar.ExternalEventLinks` y `Calendar.SyncOutbox` con los campos e índices de esta spec.
- [ ] Todas las entidades heredan de `BaseTenantEntity` y respetan tenant y soft delete.
- [ ] No hay columnas específicas de Google en `Calendar.Events`: toda la información externa vive en las tablas de esta spec.
- [ ] El modelo admite un segundo proveedor agregando solo un valor al enum `CalendarExternalProvider`.
- [ ] Ningún campo que almacene tokens está pensado para exponerse en DTOs.

---

## Decisions taken and discarded

- **Solo estructura ahora** (decisión del usuario). La tabla vacía no cambia el comportamiento del sistema y fija el modelo antes de que existan datos.
- **Tablas separadas en vez de columnas en `CalendarEvent`** (elegido). Una actividad puede estar vinculada a más de una conexión (dos proveedores, o un cambio de cuenta), y agregar Outlook no toca `Calendar.Events`.
- **Outbox transaccional** (elegido) vs llamar a Google dentro del handler. Una caída de Google no debe impedir guardar una actividad en JOIN, y los reintentos y el orden quedan controlados.
- **Conexión por usuario y empresa** (elegido). Las actividades y calendarios son por empresa. Un usuario de dos empresas puede vincular la misma cuenta de Google en ambas: son dos conexiones con el mismo `ExternalCalendarId`.
- **Solo `CONFIRMED`** (decisión 5). Las pendientes y reprogramadas por otro esperan confirmación del dueño; enviarlas mostraría en Google citas que quizá se anulen.

---

## Identified risks

| Riesgo | Mitigación |
|---|---|
| Las reglas de la sección "Reglas para la spec de acciones" pueden cambiar al diseñar la integración real y requerir ajustes al modelo. | Las tablas nacen vacías y sin uso; ajustar columnas antes de tener datos es barato. La spec de acciones revisa este modelo como primer paso. |
| Guardar refresh tokens de Google convierte la base de datos en un objetivo sensible. | Cifrado con Data Protection (llaves fuera de la base), scope mínimo `calendar.events`, borrado del token al desvincular. |
| Google limita las llamadas por usuario y por proyecto. | Outbox con un solo pendiente por actividad (los cambios rápidos se agrupan), espera exponencial y `ConsecutiveFailures`. |
