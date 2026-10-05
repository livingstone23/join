# SPEC 45 — Calendario: ingreso de actividades por canales (agente) y personas pendientes de confirmación

> **Status:** Pospuesto
> **Depends on:** SPEC 43 (menú y rutas), SPEC 48 (bloqueo por módulo), SPEC 44 (módulo Calendario: `CalendarEvent`, `CalendarCompany`, `CalendarEventScheduleGuard`, `CalendarEventInitialStatusResolver`, `CalendarEventLogWriter`, tipo `PERSON_REVIEW`, estado `CANCELLED`, `CalendarCompany.RescheduledByOthersStatusCompanyId`).
> **Related:** SPEC 47 (ingesta de tickets por canal, etapa posterior; antes SPEC 99 — mismo catálogo `CommunicationChannel` y mismo criterio de deduplicación por id de mensaje externo).
> **Date:** 2026-09-29
> **Objective:** Permitir que un agente automatizado (n8n) que atiende WhatsApp, correo u otro canal, autenticado vía API como usuario de servicio, identifique a la empresa y a la persona que escribe, registre a la persona si no existe (`Person` + `PersonContact` + `PersonPendingConfirmation`), consulte disponibilidad, y cree, reprograme o anule actividades en el calendario del usuario indicado o del usuario por defecto de la empresa, dejando trazado el canal y el solicitante, y generando una actividad de revisión para que un usuario interno complete los datos de la persona nueva.

> **Etapa (decisión del usuario, 2026-10-01):** WhatsApp y los demás canales con agente se configuran **después** de concluir los módulos de Tickets y Calendar. Esta spec no se implementa hasta entonces; antes de hacerlo se revisa contra el estado final de ambos módulos.
---

## Por qué existe esta spec

El módulo Calendario (SPEC 44) se diseñó para recibir actividades desde canales de comunicación. El caso de uso original:

> "Si un agente por canal de WhatsApp identifica que el usuario necesita crearse una cita para el lunes a las 10 de la mañana por una hora, podrá registrarla con un tema, un breve resumen, el canal que lo creó y quién lo creó (número de teléfono, correo si es por email o un usuario de JOIN)."

Decisiones acordadas el 2026-09-29:

| # | Decisión |
|---|---|
| 1 | El agente se autentica **vía API** como un usuario del sistema (usuario de servicio) y debe identificar al solicitante. |
| 2 | Existe un **número general** que atiende varias empresas: el agente **siempre** obliga a la persona a indicar a qué empresa se dirige. Los números exclusivos de una empresa ya operan bajo esa empresa. El mapeo número ↔ empresa queda **fuera** de esta spec. |
| 3 | El agente pide a la persona que se identifique: a qué empresa va y con quién quiere hablar. **No hay búsqueda por nombre**: un solicitante que ya está siendo atendido debe dar **el correo electrónico del usuario que lo atiende** (coincidencia exacta). Un solicitante nuevo (servicio nuevo, ticket) no indica usuario. Si no da correo, o el correo no corresponde a un usuario de la empresa con calendario, la actividad va **directamente** al **usuario por defecto** de `CalendarCompany` (`DefaultActivityUserId`), que luego procede o la distribuye. |
| 4 | Si el número **no está registrado**, el agente pide los datos y se crea `Person` + `PersonContact` en la empresa indicada, más un registro en **`PersonPendingConfirmation`** (persona, motivo, canal, fecha) para vincularla después como `Customer` si hace falta. Siempre se registra en una empresa. |
| 5 | Los datos mínimos que pide el agente incluyen **identificación** (tipo y número): así el `Person` es válido y no se duplica si la persona vuelve a escribir. El género es opcional para personas creadas por canal. |
| 6 | Si el número **sí está registrado**, la persona se identifica por su `PersonContact` y la actividad va al usuario que indique o al usuario por defecto. |
| 7 | Al crear una persona nueva se generan **dos actividades** en el calendario del usuario por defecto: la cita solicitada y una actividad **"Revisar persona creada"** con los datos de la persona. |
| 8 | La actividad que registra el agente nace con el estado inicial configurado para su tipo (normalmente "Pendiente de confirmación"). Después, el dueño del calendario la confirma o la anula de forma manual. |
| 9 | El agente puede **reprogramar y anular** actividades si su rol tiene el permiso. |
| 10 | El agente sabe por contexto si debe crear un recordatorio o una reunión: envía el **código del tipo** de actividad. |
| 11 | Si el agente reintenta la misma solicitud, **no se crea dos veces** (deduplicación por id del mensaje). |

---

## Scope

**In:**

### A. Dominio

- `src/1.Domain/Admin/PersonPendingConfirmation.cs` (nuevo, `BaseTenantEntity`, esquema **`Admin`**, junto a `Person`). Detalle en Data model.
- `src/1.Domain/Enums/PersonPendingConfirmationStatus.cs` (nuevo).

### B. Persistencia

- `Configuration/Admin/PersonPendingConfirmationConfiguration.cs`: `ToTable("PersonPendingConfirmations", "Admin")`, FKs `Restrict`, índices (Data model).
- `DbSet<PersonPendingConfirmation>` en la sección administrativa del `ApplicationDbContext`; línea `HasQueryFilter` explícita tenant + soft delete en `ConfigureGlobalQueryFilters` (convención SPEC 38).
- Migración `AddPersonPendingConfirmation`.

### C. Application — intake del agente (`src/2.Application/UseCases/Calendars/ChannelIntake/`)

| Tipo | Nombre | Propósito |
|---|---|---|
| Query | `GetIntakeCompanies` | Empresas en las que opera el agente y que tienen el calendario operativo. |
| Query | `GetIntakeIdentificationTypes` | Tipos de identificación válidos (para pedir los datos a la persona). |
| Query | `LookupIntakePerson` | Buscar a la persona por teléfono/correo del canal o por identificación. |
| Command | `RegisterIntakePerson` | Crear `Person` + `PersonContact` + `PersonPendingConfirmation` + actividad de revisión. |
| Query | `LookupIntakeUserByEmail` | Confirmar, por correo exacto, el usuario que atiende al solicitante. |
| Query | `GetIntakeAvailability` | Huecos libres de un usuario (reutiliza la lógica de `CalendarPanel/availability` de SPEC 44). |
| Command | `CreateIntakeEvent` | Crear la actividad solicitada. |
| Query | `GetIntakePersonEvents` | Actividades de la persona (para reprogramar o anular). |
| Command | `RescheduleIntakeEvent` | Reprogramar una actividad de la persona. |
| Command | `CancelIntakeEvent` | Anular una actividad de la persona. |

Clase de apoyo **`IntakeRequesterResolver`** (feature-local): normaliza el identificador del canal y resuelve la persona. La usan `LookupIntakePerson`, `RegisterIntakePerson` y la validación de pertenencia en reprogramar y anular.

### D. Application — personas pendientes (`src/2.Application/UseCases/Admin/PersonPendingConfirmations/`)

| Tipo | Nombre | Propósito |
|---|---|---|
| Query | `GetPersonPendingConfirmationsPaged` | Bandeja de personas pendientes (filtro por estado, canal, fechas). |
| Query | `GetPersonPendingConfirmationById` | Detalle con datos de la persona y actividades relacionadas. |
| Command | `ResolvePersonPendingConfirmation` | Cerrar como `Confirmed` (datos revisados) o `Discarded`. |
| Command | `LinkPersonPendingConfirmationToCustomer` | Vincular a un `Customer` existente de esa persona y cerrar como `LinkedToCustomer`. |

### E. WebApi

- `src/4.Services.WebApi/Controllers/Calendars/CalendarChannelIntakeController.cs` — `[PermissionResource("CalendarChannelIntake")]`.
- `src/4.Services.WebApi/Controllers/Admin/PersonPendingConfirmationsController.cs` — `[PermissionResource("PersonPendingConfirmations")]`.

### F. Seed

- `SystemOptions`: `CalendarChannelIntake` (no visible en menú) y `PersonPendingConfirmations` (menú en el módulo de Clientes).
- `RoleSystemOptions`: el rol **`Agent`**, que ya existe en la seed de roles, recibe `CalendarChannelIntake` (ver Permisos).
- Desarrollo: un usuario de servicio `agent@join.com` con rol `Agent` en `JOIN-001`, para probar el flujo.

### G. Tests

Cobertura ≥ 90% en clases nuevas. Detalle en F7.

**Out of scope:**

- **Crear tickets desde el agente o desde WhatsApp** (decisión del usuario, 2026-10-01). Una solicitud de servicio nuevo o de ticket que llega al agente se registra como **actividad** para el usuario por defecto (`DefaultActivityUserId`), que decide si crea el ticket desde JOIN. La creación de tickets por WhatsApp y correo quedó en SPEC 47 (etapa posterior).

- Tabla de números o identificadores por empresa (`CompanyCommunicationChannel`) y autenticación por número exclusivo. El agente opera con su usuario de servicio y `X-Company-Id`.
- Webhooks directos de proveedores (Twilio, Meta, SendGrid): el agente (n8n) es quien habla con el canal y llama a esta API.
- Interpretación de lenguaje natural ("el lunes a las 10"): la hace el agente. La API recibe fechas ISO 8601 con zona.
- Respuestas automáticas al solicitante y notificaciones al dueño del calendario (Hangfire futuro).
- Crear el `Customer` desde la bandeja de pendientes: se crea con el flujo existente de Customers y aquí solo se vincula.
- Vincular un número nuevo a una persona existente identificada por cédula (ver Decisiones).
- **Proceso de limpieza de personas descartadas** (borrado lógico u otro tratamiento de los `Person` cuyo pendiente quedó `Discarded`). Se define en una spec futura; esta spec deja los datos necesarios para hacerlo: `Status = Discarded`, `ResolvedUtc` y `ResolutionNotes`.

---

## Data model

### `PersonPendingConfirmation` (nuevo, esquema `Admin`)

**Administra:** supervisores o usuarios con permiso en la bandeja de pendientes. **Pantalla:** Clientes → Personas pendientes de confirmación.

| Campo | Tipo | Req. | Descripción |
|---|---|---|---|
| `PersonId` | Guid FK → Person | Sí | Persona creada por el agente. |
| `Reason` | nvarchar(500) | Sí | Motivo del contacto, en palabras del agente ("Quiere una cita para revisar su contrato"). |
| `CommunicationChannelId` | Guid FK → CommunicationChannel | Sí | Canal por el que llegó. |
| `ChannelIdentifier` | nvarchar(200) | Sí | Teléfono E.164 o correo con el que escribió. |
| `RequestedUtc` | datetime2 | Sí | Fecha y hora del registro. |
| `Status` | int (`PersonPendingConfirmationStatus`) | Sí | `Pending = 1`, `Confirmed = 2` (datos revisados y completados, queda como persona), `LinkedToCustomer = 3`, `Discarded = 4` (spam, error, duplicado). |
| `ReviewEventId` | Guid FK → Calendar.Events | No | Actividad "Revisar persona creada" generada para el supervisor. |
| `CustomerId` | Guid FK → Customer | No | Cliente vinculado cuando `Status = LinkedToCustomer`. |
| `ResolvedByUserId` | Guid FK → Users | No | Quién la cerró. |
| `ResolvedUtc` | datetime2 | No | |
| `ResolutionNotes` | nvarchar(1000) | No | Obligatorio al descartar. |

Índices:
- Único filtrado `(CompanyId, PersonId)` where `Status = 1 AND GcRecord = 0`: una persona no puede tener dos pendientes abiertos.
- `(CompanyId, Status, RequestedUtc)` where `GcRecord = 0`, para la bandeja.

### Cambios en entidades existentes

Ninguno. `Person.GenderId` ya es nullable en la entidad. La obligatoriedad del género para personas físicas vive en `CreatePersonCommandValidator` y **no** se toca: el registro por canal tiene su propio validador.

---

## Autenticación del agente y contexto de empresa

1. El agente es un `ApplicationUser` de servicio con rol **`Agent`**, con `UserCompany` en **cada** empresa que atiende. Obtiene su JWT con el login normal y lo renueva con refresh token.
2. `GET /CalendarChannelIntake/companies` no requiere empresa: lista las empresas del usuario de servicio que tienen el módulo Calendario activo y `CalendarCompany` configurada. Se marca `[SkipDynamicAuthorization]` y el handler exige rol `Agent` o `SuperAdmin` (`currentUserService.IsInRole`) → si no, `403`.
3. Una vez identificada la empresa, todas las demás llamadas envían `X-Company-Id`. **Verificación en F1:** confirmar que `ICurrentUserService`/`DynamicAuthorizationFilter` rechazan un `X-Company-Id` de una empresa donde el usuario no tiene `UserCompany` activo. Si hoy no lo hacen, `CalendarCompanyGuard` agrega ese chequeo para los endpoints de intake (`CALENDAR_INTAKE_COMPANY_NOT_ALLOWED`). **Dato a revisar (2026-10-01):** `DynamicAuthorizationFilter` evalúa permisos y módulo (SPEC 48) con el claim `CompanyId` del JWT, no con el header `X-Company-Id`; para un agente que opera en varias empresas hay que confirmar que el claim refleja la empresa elegida (p.ej. cambiando de empresa con el flujo de workspaces y renovando el token) o ajustar el filtro.
4. `OriginUserId` de toda actividad o log creado por el agente = el usuario de servicio. `OriginIdentifier` / `ActorIdentifier` = teléfono o correo del solicitante.

---

## Flujo del agente

```
Persona escribe por WhatsApp
        │
        ▼
1. ¿A qué empresa?  ── GET /companies ── (número general: pregunta; número exclusivo: ya la sabe)
        │  X-Company-Id
        ▼
2. ¿Quién eres?     ── GET /persons/lookup?channelCode=WHATSAPP&identifier=+50588887777
        │
        ├─ encontrada ─────────────────────────────────────────────┐
        │                                                          │
        └─ no encontrada → pide nombre, tipo y número de identificación
                 GET /identification-types                         │
                 POST /persons  → Person + PersonContact           │
                                 + PersonPendingConfirmation       │
                                 + actividad PERSON_REVIEW          │
                                   (calendario del usuario por     │
                                    defecto)                       │
                 (si la identificación ya existe → devuelve        │
                  la persona existente, sin crear nada)            │
        ◄──────────────────────────────────────────────────────────┘
        ▼
3. ¿Con quién?      ── solicitante ya atendido: da el correo de quien lo atiende → GET /users/lookup?email=
                       solicitante nuevo (servicio / ticket) o correo sin coincidencia → usuario por defecto
        ▼
4. ¿Cuándo?         ── GET /availability?userId&from&to&activityCode
        ▼
5. Registrar        ── POST /events  { activityCode, title, summary, start, end, requesterPersonId,
                                       targetUserEmail?, originChannelCode, originIdentifier, originReference }
        ▼
   Actividad en estado inicial (Pendiente) → el dueño la confirma o anula en JOIN

Después: "mueve mi cita" / "cancela mi cita"
        GET /events?requesterPersonId  →  PUT /events/{id}/reschedule  |  POST /events/{id}/cancel
```

---

## API — `CalendarChannelIntakeController`

Ruta base `api/v1/CalendarChannelIntake`. Todas las operaciones pasan por el filtro global de autorización (recurso `CalendarChannelIntake`, módulo `Calendar`, SPEC 48) y por `CalendarCompanyGuard` (SPEC 44 R1).

| Método | Ruta | Flag | Descripción |
|---|---|---|---|
| GET | `/companies?search` | — (`[SkipDynamicAuthorization]` + rol) | `[{ companyId, name, taxId }]`. `search` compara contra nombre y `TaxId` (contiene, sin distinguir mayúsculas). |
| GET | `/identification-types` | Read | `[{ id, name }]` activos. |
| GET | `/persons/lookup?channelCode&identifier` o `?identificationTypeId&identificationNumber` | Read | `{ found, personId?, fullName?, hasPendingConfirmation }`. Devuelve **solo** id y nombre: la persona que escribe no debe poder obtener datos de otra. |
| POST | `/persons` | Create | Registra persona nueva (ver abajo). |
| GET | `/users/lookup?email` | Read | Coincidencia **exacta** (sin distinguir mayúsculas) con el correo de un usuario con `UserCompany` activo y calendario en la empresa: `{ found, userId?, fullName? }`. No hay búsqueda por nombre ni listados: el agente solo puede confirmar un correo que la persona ya conoce. |
| GET | `/availability?userId&from&to&activityCode` | Read | `userId` opcional → usuario por defecto. Rango máximo 14 días. Aplica R4 y R5 de SPEC 44 (el agente nunca es el dueño). Devuelve `[{ startUtc, endUtc }]` en bloques de la duración del tipo, más `timeZone`. |
| POST | `/events` | Create | Crea la actividad (ver abajo). |
| GET | `/events?requesterPersonId&from&to` | Read | Actividades **no anuladas** de esa persona desde hoy (rango máximo 90 días): `[{ eventId, occurrenceStartUtc, title, startUtc, endUtc, status, ownerFullName }]`. |
| PUT | `/events/{id}/reschedule` | Update | `{ requesterPersonId, start, end?, scope?, occurrenceStartUtc?, originChannelCode, originIdentifier, originReference, notes? }`. |
| POST | `/events/{id}/cancel` | Update (`[RequirePermission(CanUpdate)]`) | `{ requesterPersonId, reason, scope?, occurrenceStartUtc?, originChannelCode, originIdentifier }`. |

### `POST /persons` — registrar persona nueva

```jsonc
{
  "personType": 1,                       // Physical | Legal
  "firstName": "Ana", "lastName": "López",
  "commercialName": null,                // obligatorio si Legal
  "identificationTypeId": "guid",
  "identificationNumber": "001-010190-0001A",
  "genderId": null,                      // opcional en este flujo
  "phone": "+50588887777",               // E.164; obligatorio si el canal es de teléfono
  "email": "ana@correo.com",             // obligatorio si el canal es correo
  "reason": "Quiere una cita para revisar su contrato",
  "originChannelCode": "WHATSAPP",
  "originIdentifier": "+50588887777",
  "originReference": "wamid.HBgM..."     // id del mensaje
}
```

Handler (`ITransactionalCommand`, una sola transacción):
1. Guard de módulo y `CalendarCompany`. Canal `originChannelCode` activo en `CommunicationChannel` → si no, `CALENDAR_INTAKE_INVALID_CHANNEL`.
2. Si ya existe una persona **activa** con `(IdentificationTypeId, IdentificationNumber)` en la empresa → responde `200 { personId, created: false, hasPendingConfirmation }` **sin crear ni modificar nada**. Tampoco agrega el teléfono nuevo a la persona existente (ver Decisiones).
3. Si el `phone`/`email` ya pertenece a un `PersonContact` activo de **otra** persona de la empresa → `409 CALENDAR_INTAKE_CONTACT_BELONGS_TO_OTHER_PERSON` (sin revelar quién es).
4. Crea `Person`. Crea `PersonContact`: `WhatsApp` si el canal es WhatsApp, `MobilePhone` si es otro canal de teléfono, `PrimaryEmail` si hay correo; el del canal de origen queda como `IsPrimary`.
5. Crea la actividad de revisión en el **calendario por defecto** del `CalendarCompany.DefaultActivityUserId`:
   - Tipo `PERSON_REVIEW` (debe estar usable en la empresa → si no, `CALENDAR_ACTIVITY_NOT_ENABLED`).
   - Inicio = ahora redondeado al siguiente múltiplo de 15 minutos; duración la del tipo en la empresa.
   - Título `"Revisar persona creada: Ana López"`.
   - Descripción con nombre, identificación, contactos, motivo, canal, identificador y fecha.
   - `RequesterPersonId` = la persona; origen = el del request.
   - Estado inicial del tipo. Pasa por `CalendarEventScheduleGuard` (el tipo permite superposición y no requiere horario hábil, así que en la práctica no choca).
6. Crea `PersonPendingConfirmation` (`Pending`, `ReviewEventId` = la actividad del paso 5).
7. Responde `201 { personId, created: true, pendingConfirmationId, reviewEventId }`.

Validador propio (`RegisterIntakePersonCommandValidator`): las mismas reglas de longitud y tipo que `CreatePersonCommandValidator`, **salvo** el género, que es opcional. `phone` en formato E.164 (`^\+[1-9]\d{7,14}$`). `reason` obligatorio, máximo 500. Al menos `phone` o `email`.

### `POST /events` — registrar actividad solicitada

```jsonc
{
  "activityCode": "APPOINTMENT",          // CalendarActivity.Code; debe estar usable en la empresa
  "title": "Cita: revisión de contrato",
  "summary": "Ana quiere revisar la cláusula 4 de su contrato",
  "description": null,
  "location": null,
  "videoCallUrl": null,
  "isAllDay": false,
  "start": "2026-10-05T10:00:00-06:00",   // o startDate/endDate si isAllDay
  "end": "2026-10-05T11:00:00-06:00",     // opcional → duración del tipo en la empresa
  "recurrenceRule": null,                 // opcional, mismas reglas que SPEC 44
  "targetUserEmail": "maria@empresa.com", // opcional: correo de quien atiende al solicitante; sin valor o sin coincidencia → CalendarCompany.DefaultActivityUserId
  "requesterPersonId": "guid",            // obligatorio
  "originChannelCode": "WHATSAPP",
  "originType": "Phone",                  // Phone | Email
  "originIdentifier": "+50588887777",
  "originReference": "wamid.HBgM..."      // obligatorio
}
```

Handler (`ITransactionalCommand`):
1. Guard de módulo y `CalendarCompany`.
2. **Deduplicación:** si existe una actividad con el mismo `(CompanyId, OriginChannelId, OriginReference)` → `200 { eventId, isDuplicate: true, status }` sin crear nada. El índice único de SPEC 44 garantiza esto también ante una carrera: una violación de ese índice se captura y se responde igual.
3. `requesterPersonId`: persona activa de la empresa → si no, `CALENDAR_INVALID_LINK`.
4. Destinatario:
   - Con `targetUserEmail` que coincide **exactamente** con un usuario con `UserCompany` activo y `CalendarUserConfiguration` en la empresa → ese usuario.
   - Sin `targetUserEmail`, o si no coincide → **directamente** `CalendarCompany.DefaultActivityUserId`, sin error. Si vino un correo sin coincidencia, se agrega a `Description` la nota `"El solicitante indicó ser atendido por: <correo> (no corresponde a un usuario de la empresa)."` para que el usuario por defecto lo revise.
   - Si el usuario por defecto no tiene calendario → `CALENDAR_INTAKE_TARGET_USER_HAS_NO_CALENDAR` (error de configuración de la empresa).
   - La actividad va al **calendario por defecto** del destinatario. La respuesta indica `routedToDefaultUser: true|false`, para que el agente se lo comunique a la persona.
5. Tipo por `activityCode` → `CalendarActivityCompany` usable → si no, `CALENDAR_ACTIVITY_NOT_ENABLED`.
6. Estado inicial = `CalendarEventInitialStatusResolver`. El request **no** acepta estado.
7. `CalendarEventScheduleGuard` con creador ≠ dueño: aplica **R4 (superposición)** y **R5 (horario hábil, días hábiles, ausencias, feriados)**. Con conflicto → `409`/`422` con el código de SPEC 44, para que el agente proponga otro horario (usando `/availability`).
8. Guarda la actividad con origen completo y la fila `Created` en `CalendarEventLog` (`ChannelId` = canal de origen, `ActorUserId` = usuario de servicio, `ActorIdentifier` = `originIdentifier`).
9. `201 { eventId, isDuplicate: false, status: { code, name }, ownerFullName, startUtc, endUtc, timeZone }`.

### `PUT /events/{id}/reschedule` y `POST /events/{id}/cancel`

- La actividad debe existir en la empresa **y** tener `RequesterPersonId == requesterPersonId` del request → si no, `404 CALENDAR_EVENT_NOT_FOUND`. El agente solo puede tocar actividades de la persona que se identificó en la conversación, nunca actividades internas ni de otras personas.
- La actividad no debe estar en un estado `IsFinal` → si no, `CALENDAR_INTAKE_EVENT_ALREADY_CLOSED`. El agente no reabre actividades anuladas ni completadas; el usuario interno sí puede (SPEC 44 no tiene reglas de transición).
- **Reprogramar:** valida R4 y R5 con creador ≠ dueño; el estado pasa al configurado en `CalendarCompany.RescheduledByOthersStatusCompanyId` (SPEC 44 R6; el agente nunca es el dueño); bitácora `Rescheduled`. Con `originReference` repetido en el log para esa actividad → respuesta idempotente sin volver a aplicar.
- **Anular:** estado `CANCELLED`, `reason` obligatorio → `notes` del log; bitácora `StatusChanged`.
- Actividades periódicas: `scope` y `occurrenceStartUtc` con las mismas reglas de SPEC 44 R7. Si no se indica `scope`, se asume `ThisOccurrence`.

### Normalización de identificadores (`IntakeRequesterResolver`)

> **Referencia única (2026-10-01):** esta es la regla de búsqueda de personas por teléfono del sistema. Cuando se retome la creación de tickets por WhatsApp (SPEC 47, etapa posterior), se reutiliza este resolver (moviéndolo a un lugar compartido de `UseCases/Admin/Persons`) en lugar de la coincidencia exacta que describe la SPEC 47.

- **Teléfono:** el agente envía E.164 (`+50588887777`). `PersonContact.ContactValue` puede tener otros formatos (`8888-7777`, `+505 8888 7777`). La búsqueda compara solo dígitos: `REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ContactValue,' ',''),'-',''),'(',''),')',''),'+','')` contra el E.164 sin `+`, y también contra los últimos 8 dígitos para números guardados sin código de país. Si hay **más de una** persona que coincide → `found = false` con `ambiguous = true`: el agente debe pedir la identificación.
- **Correo:** comparación exacta sin distinguir mayúsculas y sin espacios.
- Solo `PersonContact` activos (`IsActive = 1`, `GcRecord = 0`) de tipo `WhatsApp`, `MobilePhone`, `Landline` (teléfono) o `PrimaryEmail`, `AlternativeEmail` (correo), de personas activas de la empresa.

---

## API — `PersonPendingConfirmationsController`

Ruta base `api/v1/PersonPendingConfirmations`, `[PermissionResource("PersonPendingConfirmations")]`.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/?status&channelId&from&to&search&page&pageSize` | Bandeja paginada: `{ id, personId, personFullName, identification, channelName, channelIdentifier, reason, requestedUtc, status, reviewEventId, reviewEventStatus }`. Por defecto `status = Pending`, más recientes primero. |
| GET | `/{id}` | Detalle, con contactos de la persona y actividades con `RequesterPersonId` = la persona. |
| POST | `/{id}/resolve` | `{ status: "Confirmed" \| "Discarded", notes }`. `notes` obligatorio si `Discarded`. Solo desde `Pending` → si no, `PERSON_PENDING_ALREADY_RESOLVED`. |
| POST | `/{id}/link-customer` | `{ customerId }`. El `Customer` debe existir en la empresa y ser de la misma persona (`Customer.PersonId`) → si no, `PERSON_PENDING_INVALID_CUSTOMER`. Queda `LinkedToCustomer`. |

Al resolver o vincular:
- Se guardan `ResolvedByUserId` y `ResolvedUtc`.
- Si la actividad de revisión (`ReviewEventId`) sigue en un estado no final, pasa a `COMPLETED` con una fila en su bitácora (`Notes` = "Resuelta desde personas pendientes").
- `Discarded` **no** desactiva ni borra la persona: queda activa (decisión del usuario, 2026-10-01). La limpieza de personas descartadas es un proceso futuro (ver Out of scope).

---

## Permisos

**`SystemOptions`** (`GetAdministrativeSystemOptionSeeds`):

```csharp
// SPEC 45 — endpoints del agente de canales. No visible en menú.
new("CalendarChannelIntake", "/calendar/channel-intake", "@Icons.Material.Filled.SmartToy", "Calendar", "CalendarChannelIntake", true, true, true, false, IsVisibleMenu: false),
// SPEC 45 — bandeja de personas creadas por canal.
new("PendingPersons", "/customers/pending-persons", "@Icons.Material.Filled.PersonSearch", "CustomersMenu", "PersonPendingConfirmations", true, false, true, false),
```

Nombres y rutas en inglés con guion (decisión del usuario, 2026-10-01; SPEC 43). `CalendarChannelIntake` declara `ModuleName = "Calendar"`; `PendingPersons` declara `ModuleName = "Customers"` y cuelga del padre `CustomersMenu` (ruta `/customers`), que SPEC 43 renombra desde `Persons`/`/Clientes`.

**`RoleSystemOptions`** (Read, Create, Update, Delete). Las filas referencian el nombre de la opción: `CalendarChannelIntake` y `PendingPersons`:

| Rol | CalendarChannelIntake | PersonPendingConfirmations |
|---|---|---|
| SuperAdmin | ✔ ✔ ✔ – | ✔ – ✔ – |
| Agent | ✔ ✔ ✔ – | – |
| SuperAdminCompany | ✔ ✔ ✔ ✔ (acceso total, SPEC 43 A4) | ✔ ✔ ✔ ✔ (acceso total, SPEC 43 A4) |
| Manager | – | ✔ – ✔ – |
| Supervisor | – | ✔ – ✔ – |

**Permisos fuera de `JOIN-001` (decisión del usuario, 2026-10-01; SPEC 43 A3):** la seed de `RoleSystemOptions` solo carga la empresa `JOIN-001`. En cualquier otra empresa, los permisos de este módulo se asignan **a mano** desde la pantalla de roles (SuperAdmin o el `SuperAdminCompany` de la empresa) antes de usarlo; sin ellos, los endpoints responden `403`. Es un paso de despliegue por empresa, documentado en `CURL_REQUESTS.md`.

El agente **no** recibe `CalendarOthers` ni `Persons`: todo lo que necesita pasa por `CalendarChannelIntake`, con reglas más estrictas que las del panel (solo actividades de la persona identificada, sin reabrir estados finales, sin acceso a datos de otras personas).

---

## Implementation plan

### F1 — Verificación previa
Confirmar el comportamiento de `X-Company-Id` para un usuario sin `UserCompany` en esa empresa (sección de autenticación, paso 3), y confirmar que el rol `Agent` existe en la seed (`DatabaseSeeder.cs:49`).

### F2 — Dominio y persistencia
`PersonPendingConfirmation`, enum, configuración EF, `DbSet`, query filter, migración `AddPersonPendingConfirmation`.

### F3 — `IntakeRequesterResolver` y consultas de intake
`GetIntakeCompanies`, `GetIntakeIdentificationTypes`, `LookupIntakePerson`, `LookupIntakeUserByEmail`, `GetIntakeAvailability` (reutiliza el cálculo de SPEC 44), `GetIntakePersonEvents`.

### F4 — Comandos de intake
`RegisterIntakePerson`, `CreateIntakeEvent`, `RescheduleIntakeEvent`, `CancelIntakeEvent`, reutilizando `CalendarEventScheduleGuard`, `CalendarEventInitialStatusResolver` y `CalendarEventLogWriter`.

### F5 — Personas pendientes
Queries, `Resolve`, `LinkToCustomer`, cierre automático de la actividad de revisión.

### F6 — Controllers y seed
Los dos controllers, `SystemOptions`, `RoleSystemOptions` y el usuario `agent@join.com` de desarrollo.

### F7 — Tests (~50 casos)
- **Resolver (8):** teléfono con formatos distintos; últimos 8 dígitos; coincidencia ambigua; correo sin mayúsculas; contactos inactivos ignorados; personas de otra empresa ignoradas.
- **RegisterIntakePerson (12):** crea las cuatro entidades en una transacción; identificación existente → devuelve la persona sin crear; teléfono de otra persona → 409; género opcional; E.164 inválido; `PERSON_REVIEW` deshabilitado → error; actividad de revisión en el calendario del usuario por defecto con descripción completa; canal inválido; sin `CalendarCompany` → error.
- **CreateIntakeEvent (14):** deduplicación por `originReference` (secuencial y por violación de índice); sin `targetUserEmail` → usuario por defecto; correo sin coincidencia → usuario por defecto con nota en la descripción y `routedToDefaultUser = true`; correo de usuario de otra empresa → usuario por defecto; correo con mayúsculas distintas coincide; usuario por defecto sin calendario → error; tipo por código; estado inicial desde configuración; R4 y R5 aplicadas; persona de otra empresa → error; bitácora con actor y canal.
- **Reschedule / Cancel (10):** persona distinta → 404; estado final → error; estado tras reprogramar = el configurado en `CalendarCompany`; anular exige motivo; ocurrencia de serie; idempotencia de reprogramación.
- **Personas pendientes (8):** bandeja filtrada; resolver solo desde pendiente; descartar exige notas; vincular valida `Customer.PersonId`; actividad de revisión pasa a `COMPLETED`.
- **Companies (2):** solo empresas del agente con módulo y configuración; rol distinto de `Agent`/`SuperAdmin` → 403.

### F8 — Verificación final
Build sin warnings nuevos, cobertura ≥ 90%, y en `CURL_REQUESTS.md` el flujo completo del agente con ejemplos (lookup → registrar persona → disponibilidad → crear → reprogramar → anular).

---

## Acceptance criteria

- [ ] El agente, autenticado como usuario `Agent`, solo ve y opera en empresas donde tiene `UserCompany` activo y con el calendario operativo.
- [ ] Una persona desconocida queda registrada como `Person` + `PersonContact` + `PersonPendingConfirmation` en la empresa indicada, con motivo, canal, identificador y fecha, y el usuario por defecto recibe una actividad "Revisar persona creada" con sus datos.
- [ ] Una persona que ya existe (por contacto o identificación) no se duplica.
- [ ] La actividad solicitada queda en el calendario por defecto del usuario indicado o, si no se identificó a nadie, en el del usuario por defecto de la empresa, con tema, resumen, canal, identificador del solicitante, persona solicitante y estado inicial configurado.
- [ ] El agente no puede agendar fuera del horario hábil ni en días no hábiles, ausencias o feriados del destinatario cuando el tipo lo requiere, ni generar superposiciones.
- [ ] Reenviar la misma solicitud (mismo `originReference`) no crea una segunda actividad.
- [ ] El agente puede reprogramar (queda en el estado configurado para reprogramaciones, por defecto "Reprogramada") y anular (con motivo) solo actividades de la persona identificada, y cada cambio queda en la bitácora con canal y solicitante.
- [ ] La bandeja de personas pendientes permite confirmar, descartar o vincular a un cliente, y cierra la actividad de revisión.
- [ ] Ningún endpoint del agente devuelve datos de contacto de usuarios internos ni datos de otras personas, ni permite buscar usuarios por nombre.
- [ ] Una solicitud con el correo de quien atiende al solicitante llega a ese usuario; sin correo o con un correo sin coincidencia llega al usuario por defecto, con una nota cuando el correo no coincidió.

---

## Decisions taken and discarded

- **Controller dedicado `CalendarChannelIntake`** (elegido) vs reutilizar `CalendarPanel` con permisos al rol `Agent`. El panel permite ver y editar cualquier calendario de la empresa; el agente representa a una persona externa y debe quedar limitado a las actividades de esa persona, a datos mínimos de los usuarios y a no reabrir estados. Un recurso propio permite dar y quitar ese acceso sin tocar el panel.
- **Identificación obligatoria al registrar** (decisión del usuario) vs una identificación provisional o relajar `Person`. Mantiene `Person` válido, evita duplicados y no requiere cambios en el módulo de Personas. Costo: el agente hace una pregunta más.
- **Si la identificación ya existe, no se agrega el teléfono nuevo a esa persona** (elegido). Cualquiera que conozca la cédula de otra persona podría asociar su número a esa ficha y después mover o cancelar sus citas. Agregar el contacto queda como tarea del usuario interno que revisa.
- **Solo id y nombre en las respuestas de `lookup`** (elegido): la conversación con el agente es con alguien no autenticado; exponer correos o teléfonos de clientes o empleados sería una fuga de datos.
- **Destinatario solo por correo exacto, sin búsqueda por nombre** (decisión del usuario, 2026-10-01). La persona debe ser específica: si ya la atiende alguien, da su correo; si es una solicitud nueva o un ticket, va al usuario por defecto. Un correo que no coincide **no** es un error: la solicitud va directo al usuario por defecto con una nota. El agente no puede enumerar empleados.
- **Deduplicación por `OriginReference` con respuesta `200 isDuplicate`** (elegido) vs `409`. Un reintento del agente no es un error: devolver la actividad existente deja al agente continuar la conversación igual que si la primera llamada hubiera respondido.
- **`PersonPendingConfirmation` en el esquema `Admin`** (decisión del usuario), junto a `Person`, porque su ciclo de vida es de la persona y no del calendario.
- **Descartar no modifica la `Person`** (decisión del usuario, 2026-10-01). La persona queda activa; su limpieza (borrado lógico u otro) será un proceso aparte, para que el módulo de calendario no modifique entidades del CRM.
- **La actividad de revisión se agenda en el momento** (redondeado a los siguientes 15 minutos), confirmado por el usuario el 2026-10-01, vs el siguiente hueco hábil del supervisor. Es tipo recordatorio: no choca ni requiere horario hábil, y el supervisor la ve de inmediato.
- **La actividad de revisión usa el tipo de sistema `PERSON_REVIEW`** (elegido) vs un recordatorio genérico. Permite filtrarla en las vistas, darle su color, y cerrarla automáticamente al resolver el pendiente.

---

## Identified risks

| Riesgo | Mitigación |
|---|---|
| El usuario de servicio del agente queda vinculado a muchas empresas; si su token se filtra, se pueden crear personas y citas en todas. | Permisos mínimos (solo `CalendarChannelIntake`), sin `CanDelete`, refresh tokens revocables (flujo existente), y toda acción trazada con `OriginUserId` y canal. Rotación documentada en `CURL_REQUESTS.md`. |
| Una persona puede suplantar a otra si da su número de identificación y el teléfono ya estaba registrado a nombre de esa otra. | El agente identifica por el número desde el que escribe (lo aporta el canal, no la persona); la identificación solo se usa para no duplicar, y nunca asocia un número nuevo a una persona existente. |
| Normalizar teléfonos por dígitos puede dar falsos positivos entre países (últimos 8 dígitos iguales). | Si hay más de una coincidencia se trata como ambigua y el agente pide la identificación. |
| Muchos registros de personas por spam. | Bandeja con estado `Discarded` y filtros por canal; la limitación de tasa por identificador queda como mejora futura. |

---

## Puntos a validar en la revisión

Ninguno. Los tres puntos abiertos se resolvieron con el usuario el 2026-10-01: descartar no modifica la `Person` (su limpieza será un proceso futuro), la actividad de revisión se agenda en el momento, y el destinatario se identifica solo por correo exacto, con el usuario por defecto como destino si no hay coincidencia.
