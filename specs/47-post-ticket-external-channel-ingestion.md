# SPEC 47-post — Ingesta de tickets por canales externos: WhatsApp y correo (etapa posterior)

> **Status:** Pospuesto
> **Origen:** copia de SPEC 47 de `main` (estado allí al copiarla: Pospuesto), adaptada a PostgreSQL para el fork `main_postgresql`. Se implementa **desde cero** en el fork, sin traer commits de `main` (decisión del usuario, 2026-10-09).
> **Rama de trabajo:** `spec-47-post-ticket-external-channel-ingestion`, creada desde `main_postgresql`; PR hacia `main_postgresql`, nunca hacia `main`.
> **Lectura en el fork:** dentro de esta spec, toda referencia a una spec de la serie copiada (40–49, 51, 99) se lee como su versión `-post` ("SPEC 41" = SPEC 41-post), y aplican las convenciones de `specs/README.md` → "Serie `-post`". Donde el texto copiado de `main` y la sección "Adaptación a PostgreSQL" difieren, prevalece esta última.
> **Depends on:** SPEC 34 (`TicketUserCompany`), SPEC 35 (`TicketDtoAssembler`), SPEC 36 (`IFileStorageService`, `TicketAttachmentSettings`, `InboundAttachment`) — las tres ya en el fork —, SPEC 99-post (canales internos `WEB`/`APP`), SPEC 42-post (`TicketInitialStatusResolver`), SPEC 43-post (menú `TicketManagement`, rutas `/tickets/...`).
> **Related:** SPEC 45 (agente de canales del calendario; la forma en que conviven el agente y la ingesta directa se decide al retomar esta spec).
> **Date:** 2026-10-09 (copia `-post`; original: 2026-10-01 (contenido original de SPEC 99, 2026-09-25))
> **Objective:** Permitir que un ticket se registre automáticamente cuando llega un mensaje de WhatsApp o un correo a una dirección/número configurado por la empresa, sin sesión de usuario ni token JWT, resolviendo el tenant de forma segura vía un token de webhook único por empresa y canal.

> **Origen y etapa (decisión del usuario, 2026-10-01):** este diseño estaba en la SPEC 99 (aprobada el 2026-09-25). Se movió aquí sin cambios de fondo porque la creación automática de tickets desde canales externos —**WhatsApp y correo**— queda para una **etapa posterior**, después de concluir los módulos de Tickets y Calendar y junto con la definición de los agentes de canal (SPEC 45). Hasta entonces, los tickets los crean usuarios con acceso al módulo y permisos (`POST /Tickets`, que ya acepta `PersonId` para crear a nombre de otra persona). Al retomarla, se revisa completa contra el estado final de ambos módulos y vuelve a aprobarse.
>
> **Ya no forman parte de esta spec:**
> - Los canales internos `WEB` y `APP` → SPEC 99.
> - El índice único de `Ticket.Code`: ya es `(CompanyId, Code)` en el código. **No** se filtra por `GcRecord`, porque `TicketCodeGenerator` cuenta también los tickets borrados y nunca reutiliza un código; mantener los borrados en el índice garantiza que restaurar un ticket (SPEC 41) no choque.
> - `InboundAttachment`: ya existe (SPEC 36).
>
> **Al retomarla:** la búsqueda de personas por teléfono debe reutilizar el `IntakeRequesterResolver` de SPEC 45 (normalización por dígitos) en vez de la coincidencia exacta descrita abajo, y el estado inicial del ticket sale de `TicketInitialStatusResolver` (SPEC 42).

---

## Adaptación a PostgreSQL (fork `main_postgresql`)

Sigue `Pospuesto`, igual que en `main`: al retomarla se revisa completa contra el estado final del fork. Además:

| Tema | En `main` (SQL Server) | En esta versión |
|---|---|---|
| Tabla nueva | `Messaging.TicketInboundChannels` | Física `messaging.ticketinboundchannels`; migración `AddTicketInboundChannelsAndExternalMessageId` generada contra Npgsql. |
| Índices de `TicketInboundChannel` | `WHERE GcRecord = 0` | `HasFilter("\"gcrecord\" = 0")` en `WebhookToken` (global) y en `(CompanyId, ChannelId)`. |
| Deduplicación de `ExternalMessageId` | `WHERE ExternalMessageId IS NOT NULL AND GcRecord = 0` | `HasFilter("\"externalmessageid\" IS NOT NULL AND \"gcrecord\" = 0")`. Ante una carrera, la violación es `PostgresException` `23505` y el handler devuelve el ticket existente (lo reconoce un servicio de Persistence). |
| Tipos | `nvarchar(200)`, `nvarchar(100)` | `varchar(200)`, `varchar(100)`. |
| Búsqueda de personas por teléfono/correo | Coincidencia exacta (y el resolver de SPEC 45 al retomarla) | El `IntakeRequesterResolver` de SPEC 45-post (`regexp_replace` para dígitos, `LOWER(TRIM(...))` para correos). |
| Tests | MsSql | Integración con sufijo `PostgreSqlTests` sobre `PostgreSqlWebApplicationFactory`. |

---

## Por qué existe esta spec

Es la cuarta y última pieza *core* del módulo de tickets extendido antes de SPEC 37 (workflow/SLA, la de alcance más abierto, evaluada aparte). El brief la pide explícitamente:

> "Habrán diferentes canales para registrarlos: WhatsApp, web operativa, correo, app; una vez registrado se gestionará por la web."

Al auditar el estado actual (ver el análisis que originó SPEC 34) se confirmó que esto era, de las cuatro brechas originales, la más engañosa: el catálogo `CommunicationChannel` ya existe sembrado (SendGrid, Telegram, Twilio, WhatsApp) y `Ticket.ChannelId` ya registra el origen — pero **no existe ningún endpoint que reciba un mensaje real desde afuera**. Hoy "canal" es solo un dropdown que completa quien ya está autenticado y llamando `POST /Tickets` desde adentro. Nada en el sistema recibe un webhook de Twilio o de un proveedor de correo.

Releyendo el brief con cuidado, "web operativa" y "app" no son canales de *ingesta* de terceros: son clientes propios del sistema (la consola de gestión interna, y —si existiera— una app first-party con su propio login) que ya pueden crear tickets llamando `POST /Tickets` autenticados, sin ningún gap real que cerrar. Los dos canales que sí necesitan una integración nueva —porque el remitente no tiene ni tendrá nunca un usuario ni un JWT de este sistema— son **WhatsApp** y **correo**. Esta spec se acota a esos dos.

---

## Scope

**In:**

### A. Dominio

- `src/1.Domain/Messaging/TicketInboundChannel.cs` (nuevo): `BaseTenantEntity` con `ChannelId` (Guid, FK a `CommunicationChannel`) y `WebhookToken` (string, opaco, generado por el servidor). Cada fila es "un endpoint de ingesta configurado": qué empresa, qué canal, y el token que la URL del webhook usa para resolver ambos sin necesitar `X-Company-Id` ni JWT.
- `src/1.Domain/Messaging/ticket.cs`: agregar `string? ExternalMessageId` (varchar(200)) — el id de mensaje que el proveedor externo asigna (`MessageSid` de Twilio, el `Message-Id` del correo), usado exclusivamente para deduplicar reintentos de webhook (ver Data model).
- `src/2.Application/Mappings/Messaging/Ticket/TicketMapper.cs`: agregar `[MapperIgnoreTarget(nameof(Ticket.ExternalMessageId))]` **a los dos métodos** (`ToEntity` y `ApplyUpdate`). Toda propiedad escalar nueva en `Ticket` que no exista en `CreateTicketCommand`/`UpdateTicketCommand` se convierte en un miembro destino sin mapear y Mapperly emite `RMG020` al compilar — dos diagnósticos nuevos que violarían el criterio "0 warnings nuevos" de esta spec. `ExternalMessageId` nunca viaja por esos comandos: solo lo setea `RegisterInboundTicketCommandHandler`, y deliberadamente ningún cliente autenticado puede escribirlo (si pudiera, podría falsificar la clave de deduplicación de un canal entrante).

### B. Application — `TicketInboundChannel` (alta/baja de endpoints de ingesta)

- DTOs en `src/2.Application.DTO/Messaging/TicketInboundChannels/`:
  - `TicketInboundChannelDto` (listado): `{ Id, ChannelId, ChannelName, WebhookTokenMasked, WebhookUrl, CreatedAt }`. `WebhookTokenMasked` muestra solo los últimos 4 caracteres (`"••••••••1a2b"`) — el token completo **no se puede volver a consultar** después de la creación.
  - `CreateTicketInboundChannelResultDto`: `{ Id, ChannelId, WebhookToken, WebhookUrl }` — el **único** momento en que el token completo viaja en una respuesta. Mismo patrón que una API key de cualquier proveedor: se muestra una vez, después queda enmascarada.
- `src/2.Application/UseCases/Messaging/TicketInboundChannels/Commands/CreateTicketInboundChannel/CreateTicketInboundChannelCommand.cs` + `CommandHandler` + `CommandValidator`: genera `WebhookToken` con `RandomNumberGenerator.GetBytes(32)` codificado Base64Url (43 caracteres, sin padding — no confundible con un Guid, no adivinable). Rechaza con `409 INBOUND_CHANNEL_ALREADY_ACTIVE` si ya hay una fila activa para `(CompanyId, ChannelId)` — rotar un token es soft-delete de la fila vieja + create de una nueva, no un update in-place (un token comprometido no debe poder "recuperarse" reactivando la fila).
- `src/2.Application/UseCases/Messaging/TicketInboundChannels/Commands/DeleteTicketInboundChannel/DeleteTicketInboundChannelCommand.cs` + `CommandHandler`: soft delete estándar (`entity.MarkAsDeleted()`). Un token soft-deleted dejado de resolver de inmediato invalida el webhook — el proveedor externo empieza a recibir 404.
- `src/2.Application/UseCases/Messaging/TicketInboundChannels/Queries/GetTicketInboundChannels/GetTicketInboundChannelsQuery.cs` + `QueryHandler`: lista tenant-scoped (Dapper), token siempre enmascarado.
- `src/4.Services.WebApi/Controllers/Messaging/TicketInboundChannelsController.cs` (nuevo): `[PermissionResource("TicketInboundChannels")]`, `GET` (lista), `POST` (crea, `201` con `CreateTicketInboundChannelResultDto`), `DELETE {id}`.

### C. Application — ingesta genérica

- `src/2.Application/UseCases/Messaging/Tickets/Commands/RegisterInboundTicket/RegisterInboundTicketCommand.cs`: **no** implementa `ITransactionalCommand` leyendo `ICurrentUserService` — recibe `CompanyId` y `ChannelId` como parámetros explícitos porque no hay JWT en esta ruta. Firma completa y flujo del handler en Data model.
- `InboundAttachment` (`src/2.Application/UseCases/Messaging/Tickets/InboundAttachment.cs`) — **lo declara SPEC 36**, y esta spec lo consume tal cual. Antes esta spec decía declararlo acá "por orden cronológico" y dejaba a SPEC 36 la opción de adoptarlo; se resolvió al revés para evitar que, si ambas se implementan en el mismo PR (lo esperable), queden dos records con la misma forma. El tipo vive junto a la entidad que persiste los archivos (`TicketDocument`), no junto al primer consumidor que apareció en la redacción.
- El handler reutiliza `TicketDtoAssembler` (SPEC 35) para la respuesta y aplica la **misma** validación de `TicketAttachmentSettings`/`IFileStorageService` que `UploadTicketDocumentCommandHandler` (SPEC 36) para cada adjunto entrante — sin refactorizar SPEC 36 para extraer un servicio compartido (ver Decisiones: es un camino *best-effort*, no vale la pena la abstracción todavía).

### D. WebApi — dos controllers anónimos de ingesta

- `src/4.Services.WebApi/Controllers/Messaging/InboundWhatsAppController.cs` (nuevo): `[AllowAnonymous]`, `[Route("api/v1/inbound/whatsapp")]`, `POST {webhookToken}`. Recibe el `application/x-www-form-urlencoded` que Twilio envía a un webhook de WhatsApp: `From`, `To`, `Body`, `MessageSid`, `NumMedia`, `MediaUrl0..N`, `MediaContentType0..N`. Cada `MediaUrlN` se descarga vía `HttpClient` con Basic Auth (`Twilio:AccountSid`/`Twilio:AuthToken` de configuración) antes de pasarla como `InboundAttachment`.
- `src/4.Services.WebApi/Controllers/Messaging/InboundEmailController.cs` (nuevo): `[AllowAnonymous]`, `[Route("api/v1/inbound/email")]`, `POST {webhookToken}`. Recibe el `multipart/form-data` de SendGrid **Inbound Parse** (mismo proveedor que ya usa el sistema para saliente — cero superficie de integración nueva del lado del proveedor): `from`, `to`, `subject`, `text`, y N archivos adjuntos ya materializados como partes del multipart (sin fetch adicional, a diferencia de WhatsApp).
- Ambos: resuelven `TicketInboundChannel` por `webhookToken` (`GcRecord = 0`); sin match → `404` desnudo (sin cuerpo ni detalle — no confirmar ni negar la existencia de un token a quien no lo tiene). Con match, **verifican además que `CommunicationChannel.Code` del canal resuelto sea el esperado por ese controller** (`WHATSAPP` para `InboundWhatsAppController`, `SENDGRID` para `InboundEmailController`); si no coincide → el mismo `404` desnudo. Así un token de correo pegado en la URL de WhatsApp no procesa un payload con el parser equivocado.
- La discriminación es por `CommunicationChannel.Code`, **nunca por `Name`**. `Code` (`WHATSAPP`, `SENDGRID`, `TWILIO`, `TELEGRAM`, y los nuevos `WEB`/`APP`) existe en la entidad exactamente para esto — su comentario XML lo dice: *"Internal code to facilitate logic in the Application layer"*. `Name` es texto editable de catálogo y romper el ruteo de un webhook productivo con un rename accidental sería un fallo silencioso caro.
- Respuesta: `200` con un cuerpo mínimo (`{ "ticketCode": "TICK-202609-0042" }`) — el proveedor del webhook no necesita más que un 2xx para no reintentar; no se expone `Response<TicketDto>` completo hacia afuera de la red.

### E. Seed

- `SystemOptionSeed` para `TicketInboundChannels` (CRUD completo, grupo `TicketManagement`, `ModuleName = "Tickets"`, ruta `/tickets/ticket-inbound-channels`) en `GetAdministrativeSystemOptionSeeds()`, **más** las filas de `RoleSystemOptionSeed` en `GetRoleSystemOptionSeeds()` (mismo motivo detallado en SPEC 34 sección H). Este recurso entrega tokens de webhook, así que el reparto es más restrictivo que el de los catálogos:
  ```csharp
  new("Manager", "TicketInboundChannels", true, true, false, true, CanDownload: false, CanExport: false, CanExecute: true),
  new("Supervisor", "TicketInboundChannels", true, false, false, false),
  new("UsuarioSimple", "TicketInboundChannels", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
  ```
  (`CanUpdate = false` en todos: este recurso no tiene endpoint de update por diseño — rotar es delete + create.)
- **Sin** seed de datos de `TicketInboundChannel`: cada empresa configura sus propios endpoints de ingesta con datos reales de su cuenta de Twilio/SendGrid; no hay un token de ejemplo razonable para sembrar en desarrollo.
- `appsettings.json`: nueva sección `"Twilio": { "AccountSid": "...", "AuthToken": "..." }` para el fetch de media de WhatsApp.

### F. Tests

- Cobertura ≥ 90% en clases nuevas (gate de CI). Detalle en Implementation plan / F9.

**Out of scope (para specs futuras):**

- **"App" como canal de ingesta.** Requiere una identidad/portal de cliente (login, sesión) que no existe en el repo hoy — `IsVisibleToExternals` en `Ticket` ya es el mecanismo de visibilidad que un futuro portal usaría, pero construir el portal en sí es una spec de alcance completamente distinto (auth de clientes, no de empleados).
- **"Web operativa" como canal de ingesta.** Ya está resuelto por el `POST /Tickets` autenticado existente; no hay gap.
- **Validación criptográfica de firma del proveedor** (`X-Twilio-Signature` HMAC, DKIM/SPF del correo entrante). El `webhookToken` en la URL es la única barrera de esta spec — suficiente para no ser adivinable, pero no protege contra un atacante que interceptó la URL exacta configurada. Endurecer esto (firma Twilio, verificación de remitente SendGrid) es trabajo de seguridad incremental fuera de este alcance inicial.
- **Auto-creación o matching difuso de `Person`.** El remitente se vincula a un `Person` existente solo si hay un `PersonContact` activo con coincidencia exacta; si no hay match, el ticket se crea con `PersonId = null` y el remitente queda en el texto del ticket para que un agente lo vincule manualmente. Construir de-duplicación/fuzzy-matching de clientes es responsabilidad del módulo de Personas, no de este.
- **Auto-respuesta al remitente** (confirmar por WhatsApp/correo "tu ticket #X fue creado"). `TicketNotification` sigue sin consumidor después de esta spec — activarlo es una extensión natural de una spec de notificaciones, no de esta.
- **Rate limiting / anti-spam** sobre los endpoints de ingesta anónimos más allá de la validez del `webhookToken`. Si negocio lo pide, es una spec de hardening aparte (o una regla a nivel de gateway/WAF, fuera del código de la aplicación).
- **Soporte de otros proveedores de WhatsApp** (Meta Cloud API directo, sin Twilio de por medio) o de correo entrante (Mailgun, Postmark). El payload que cada controller parsea es específico de Twilio/SendGrid; agregar otro proveedor es un controller nuevo, no un cambio a `RegisterInboundTicketCommand`.
- **Reintentar la descarga de un adjunto de WhatsApp que falla** (timeout de Twilio, credencial inválida). Se omite ese adjunto puntual y se deja constancia en el log del ticket; no hay cola de reintentos.

---

## Data model

### `TicketInboundChannel` (nuevo, schema `Messaging`)

```csharp
// src/1.Domain/Messaging/TicketInboundChannel.cs
public class TicketInboundChannel : BaseTenantEntity
{
    public Guid ChannelId { get; set; }
    public string WebhookToken { get; set; } = string.Empty; // varchar(100), opaco
    public virtual CommunicationChannel Channel { get; set; } = null!;
}
```

EF config: tabla `Messaging.TicketInboundChannels`, `WebhookToken` `HasMaxLength(100)`, índice único **global** sobre `WebhookToken` filtrado `WHERE GcRecord = 0` (es la clave de lookup de un request anónimo — tiene que ser única en todo el sistema, no por empresa), índice único filtrado sobre `(CompanyId, ChannelId) WHERE GcRecord = 0` (a lo sumo un endpoint activo por empresa y canal; rotar = soft-delete + create), FK `ChannelId → Common.CommunicationChannels` Restrict, query filter `GcRecord == 0`.

No hay una columna `IsActive` separada de `GcRecord`: "activo" y "no soft-deleted" son la misma cosa acá — no hay un estado intermedio de "pausado pero no rotado" que el brief haya pedido.

### `Ticket.ExternalMessageId` (columna nueva)

```csharp
// src/1.Domain/Messaging/ticket.cs — agregado, sin tocar el resto de la entidad
public string? ExternalMessageId { get; set; } // varchar(200)
```

Índice único filtrado `(CompanyId, ChannelId, ExternalMessageId) WHERE ExternalMessageId IS NOT NULL AND GcRecord = 0` en `TicketConfiguration`. Un reintento de webhook con el mismo `MessageSid`/`Message-Id` no crea un segundo ticket — el handler lo detecta antes del `INSERT` y devuelve el ticket ya existente.

### `RegisterInboundTicketCommand` — flujo completo

```csharp
public sealed record RegisterInboundTicketCommand(
    Guid CompanyId,
    Guid ChannelId,
    string SenderIdentifier,          // E.164 phone o email, según el canal
    string? SenderDisplayName,
    string MessageSubject,
    string MessageBody,
    string ExternalMessageId,
    IReadOnlyList<InboundAttachment> Attachments)
    : IRequest<Response<TicketDto>>;
```

1. Resolver el `IsSuperAdminTicket` activo de la empresa (`_unitOfWork.GetRepository<TicketUserCompany>().GetAllAsync()` filtrado por `CompanyId`/`GcRecord == 0`/`IsSuperAdminTicket`, primer resultado). Sin ninguno → `NO_TICKET_ADMIN_CONFIGURED` (400): sin un roster de SPEC 34 poblado, no hay a quién atribuirle la autoría del ticket automático, y el mensaje entrante **no se pierde silenciosamente** — el proveedor recibe un error explícito y puede reintentar una vez que la empresa complete su configuración.
2. Buscar `Ticket` existente con `CompanyId`, `ChannelId`, `ExternalMessageId` iguales y `GcRecord = 0`. Si existe, devolver **ese** ticket ya proyectado (200, no error) — idempotencia de reintentos de webhook.
3. Cargar `TicketCompanyDefault` activo de la empresa; si falta, o si `TicketStatusDefaultId`/`TicketComplexityDefaultId`/`TimeUnitDefaultId` son `null` → `TICKET_DEFAULTS_NOT_CONFIGURED` (400). Un ticket automático no tiene a nadie completando esos campos a mano — la empresa tiene que haberlos configurado de antemano (la misma tabla que ya usa `CreateTicketCommandHandler` para el formato de código).
4. Buscar `PersonContact` activo cuyo `ContactValue` coincida exactamente con `SenderIdentifier` (comparación case-insensitive para email; exacta para teléfono E.164) y cuyo `ContactType` sea el relevante al canal (`WhatsApp`/`MobilePhone` para WhatsApp; `PrimaryEmail`/`AlternativeEmail` para correo), dentro de la empresa. Si hay match, `PersonId` = el de ese contacto. Si no, `PersonId = null` — el remitente queda identificado en el cuerpo del ticket, no vinculado.
5. Armar `Name` (truncado a 150 caracteres: `MessageSubject`, o si viene vacío, los primeros 150 de `MessageBody`) y `Description` (`MessageBody`, truncado a 2000; si no hay match de `Person`, se antepone `"Remitente: {SenderDisplayName ?? SenderIdentifier} ({SenderIdentifier})\n\n"`).
6. Generar el código igual que `CreateTicketCommandHandler` (`UsePersonalizedCode` del `TicketCompanyDefault` o el formato estándar `TICK-YYYYMM-XXXX`), con el mismo chequeo de colisión.
7. Construir la entidad: `CreatedByUserId` = el `IsSuperAdminTicket` resuelto en el paso 1; `AssignedToUserId = null` (llega sin asignar — la distribución es responsabilidad de `ReassignTicket`, SPEC 35); `TicketStatusId`/`TicketComplexityId`/`TimeUnitId`/`AreaId`/`ProjectId` = los del `TicketCompanyDefault`; `ChannelId` = el del comando; `ExternalMessageId` = el del comando; `IsVisibleToExternals = true` (un ticket que nació de un canal externo es, por definición, algo que su remitente espera poder seguir).
8. `entity.AddLog(ticketAdminUserId, LogType.Creation, $"Ticket creado automáticamente desde {channelName} ({SenderDisplayName ?? SenderIdentifier})")`.
9. `InsertAsync` + `SaveChangesAsync`.
10. Para cada `InboundAttachment`: repetir la validación de `TicketAttachmentSettings` de SPEC 36 (tipo permitido, tamaño, cupo por ticket, cuota diaria) contra el ticket recién creado y su log `Creation`. Si pasa, `IFileStorageService.SaveAsync` + `INSERT` de `TicketDocument`, igual que `UploadTicketDocumentCommandHandler`. Si **no** pasa (cualquier motivo — tipo no permitido, muy grande, cupo alcanzado, o falla la descarga desde Twilio), se **omite** ese adjunto puntual y se agrega una línea a `Description` o un log adicional (`LogType.InternalNote`, `IsOnlyForCreatedAndAssigned = true`) indicando qué se omitió y por qué — la creación del ticket **nunca** falla por un adjunto problemático.
11. `await ticketDtoAssembler.BuildAsync(entity, company, cancellationToken)` → `Response<TicketDto>.Ok`.

### Controllers de ingesta — parseo de payload

**`InboundWhatsAppController`** (Twilio, `application/x-www-form-urlencoded`):

```
SenderIdentifier   = From (quitando el prefijo "whatsapp:" que Twilio antepone)
SenderDisplayName  = null (Twilio no lo envía)
MessageSubject     = "" (WhatsApp no tiene asunto — se arma desde el cuerpo)
MessageBody        = Body
ExternalMessageId  = MessageSid
Attachments        = por cada i en [0, NumMedia): descargar MediaUrl{i} con HttpClient
                      + Basic Auth (Twilio:AccountSid/AuthToken), ContentType = MediaContentType{i},
                      FileName = un nombre sintético (Twilio no manda uno)
```

**`InboundEmailController`** (SendGrid Inbound Parse, `multipart/form-data`):

```
SenderIdentifier   = from (parseado: solo la dirección, sin el display name si viene como "Nombre <a@b.com>")
SenderDisplayName  = el display name del From, si vino
MessageSubject     = subject
MessageBody        = text
ExternalMessageId  = el header Message-Id si SendGrid lo expone en el campo "headers"; si no está disponible, un hash SHA-256 de (from + subject + text) como fallback determinístico para la deduplicación
Attachments        = cada parte de archivo del multipart
```

---

## Implementation plan

### F1 — Dominio y persistencia

1. Crear `TicketInboundChannel.cs`. Agregar `ExternalMessageId` a `Ticket.cs`.
2. Crear `TicketInboundChannelConfiguration.cs`. En `TicketConfiguration.cs`: agregar el índice de deduplicación de `ExternalMessageId`.
3. Agregar `[MapperIgnoreTarget(nameof(Ticket.ExternalMessageId))]` a `ToEntity` **y** a `ApplyUpdate` en `TicketMapper.cs`.
4. `DbSet<TicketInboundChannel>` en `ApplicationDbContext`.
5. `dotnet ef migrations add AddTicketInboundChannelsAndExternalMessageId --project ../3.Persistence --startup-project .`. Verificar en el `Up()` (Npgsql): los tres índices únicos filtrados, con filtros `"gcrecord" = 0` y `"externalmessageid" IS NOT NULL AND "gcrecord" = 0` (`WebhookToken`, `(CompanyId, ChannelId)`, `(CompanyId, ChannelId, ExternalMessageId)`) y que `ExternalMessageId` es nullable.
6. `dotnet build -c Release` → 0 errores y **0 diagnósticos `RMG0xx` nuevos** (la verificación real del paso 3).
7. `dotnet ef database update`.

### F2 — `TicketInboundChannel` CRUD

1. DTOs, comandos (`Create`/`Delete`), query (`GetTicketInboundChannels`), todos siguiendo el patrón nativo del módulo (`GetRepository<T>` en commands, Dapper inline en la query, sin Mapperly — tres campos no lo justifican).
2. `CreateTicketInboundChannelCommandHandler` genera el token con `RandomNumberGenerator.GetBytes(32)` + `Base64UrlTextEncoder.Encode(...)` (ya usado en el repo para tokens de seguridad — reutilizar el mismo helper que `JwtTokenGenerator`/refresh tokens si existe uno común, o `Convert.ToBase64String(...).Replace(...)` si no).
3. `dotnet build` → 0 errores.

### F3 — `TicketInboundChannelsController`

1. Crear el controller con los 3 endpoints.
2. `WebhookUrl` en la respuesta se arma con `{HttpContext.Request.Scheme}://{Request.Host}/api/v1/inbound/{whatsapp|email}/{token}` según el **`CommunicationChannel.Code`** resuelto: `WHATSAPP` → segmento `whatsapp`; `SENDGRID` → segmento `email`; cualquier otro (`TELEGRAM`, `TWILIO`, `WEB`, `APP`) → `400 UNSUPPORTED_INBOUND_CHANNEL`. Se rutea por `Code`, no por `Name`, por el motivo de la sección D.
3. `dotnet build` → 0 errores.

### F4 — `RegisterInboundTicketCommand`

1. Reutilizar `InboundAttachment` de SPEC 36 (`src/2.Application/UseCases/Messaging/Tickets/InboundAttachment.cs`). Si SPEC 36 todavía no está implementada, crearlo acá con exactamente esa ruta y firma, y que SPEC 36 lo consuma cuando llegue — pero en ningún caso declarar un segundo record equivalente.
2. Crear `RegisterInboundTicketCommand` + `RegisterInboundTicketCommandHandler` con el flujo completo de la sección Data model.
3. `dotnet build` → 0 errores.

### F5 — `InboundWhatsAppController`

1. Crear el controller `[AllowAnonymous]`, parseo de los campos de Twilio de la sección Data model.
2. `TwilioMediaDownloader` (clase pequeña, `IHttpClientFactory` + `Twilio:AccountSid`/`Twilio:AuthToken`): descarga cada `MediaUrlN` con Basic Auth; una falla de red o un `4xx`/`5xx` se traduce en "adjunto omitido", **no** en una excepción que tumbe el request completo.
3. Agregar `"Twilio": { "AccountSid": "", "AuthToken": "" }` a `appsettings.json`.
4. `dotnet build` → 0 errores.

### F6 — `InboundEmailController`

1. Crear el controller `[AllowAnonymous]`, parseo del multipart de SendGrid Inbound Parse de la sección Data model.
2. `dotnet build` → 0 errores.

### F7 — Seed

1. Agregar el `SystemOptionSeed` de `TicketInboundChannels` a `GetAdministrativeSystemOptionSeeds()` y las tres filas de `RoleSystemOptionSeed` a `GetRoleSystemOptionSeeds()` (sección Scope E).
2. `dotnet build` → 0 errores. Confirmar que el seeder corre sin error contra base limpia **y** contra una base ya sembrada.

### F8 — Documentación operativa

1. `CURL_REQUESTS.md`: agregar los 3 endpoints de `TicketInboundChannels` y una nota de configuración explicando cómo apuntar el webhook de Twilio/SendGrid Inbound Parse a la URL devuelta por `POST /TicketInboundChannels`.

### F9 — Tests (~30 casos)

- `TicketInboundChannel` CRUD (7): `CompanyId` vacío → 401 en cada uno; `Create` duplicado activo para el mismo canal → 409; token generado tiene 43+ caracteres y pasa una regex Base64Url; `Delete` no encontrado → 404; happy paths de los tres.
- `RegisterInboundTicketCommandHandlerTests` (14): sin `IsSuperAdminTicket` en la empresa → 400 `NO_TICKET_ADMIN_CONFIGURED`; `ExternalMessageId` repetido devuelve el ticket existente sin crear uno nuevo (verificar que `InsertAsync` no se llama una segunda vez); sin `TicketCompanyDefault` o con defaults incompletos → 400 `TICKET_DEFAULTS_NOT_CONFIGURED`; remitente con `PersonContact` activo coincidente → `PersonId` seteado; remitente sin match → `PersonId = null` y `Description` con el prefijo de remitente; `Name` se trunca a 150 caracteres; `IsVisibleToExternals = true` siempre; `CreatedByUserId` es el `IsSuperAdminTicket` resuelto, nunca null; `AssignedToUserId` siempre `null` en el alta; adjunto que pasa todas las validaciones se persiste; adjunto que excede `MaxFileSizeBytes` se omite sin fallar la creación del ticket; adjunto con tipo no permitido se omite; happy path completo (sin adjuntos) crea el ticket y el log `Creation`.
- `TwilioMediaDownloaderTests` (3): descarga exitosa devuelve el stream; `404` del proveedor devuelve "omitido" sin lanzar; timeout devuelve "omitido" sin lanzar.
- `InboundWhatsAppControllerTests` / `InboundEmailControllerTests` (6, nivel controller o integración liviana): token inexistente → 404 desnudo; token de un canal que no coincide con el controller (ej. un token de canal `SendGrid` pegado en la URL de WhatsApp) → 404 (el lookup filtra también por `ChannelId` esperado del segmento de ruta); payload válido → 200 con `ticketCode`.
- `dotnet test --filter "FullyQualifiedName~TicketInboundChannel|FullyQualifiedName~RegisterInboundTicket|FullyQualifiedName~Inbound"` → 0 fallidos.

### F10 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → gate de 90% en `JOIN.Application` sigue en verde.
3. Smoke con Twilio Sandbox (o un `curl` simulando el POST de Twilio): mensaje de WhatsApp nuevo → ticket creado, visible en `GET /Tickets`. Reenviar el mismo `MessageSid` → mismo ticket, no uno nuevo.
4. Smoke con SendGrid Inbound Parse en un dominio de prueba: correo con un adjunto `.pdf` de 500 KB → ticket creado con el documento adjunto en `GET /tickets/{id}/documents`.
5. Smoke: empresa sin ningún `TicketUserCompany.IsSuperAdminTicket` → el webhook responde `400` y el log de la aplicación lo deja explícito (no un 500 genérico).
6. `CURL_REQUESTS.md` actualizado.

---

## Acceptance criteria

### F1 — Dominio y persistencia

- [ ] `TicketInboundChannel` hereda `BaseTenantEntity`, tiene `ChannelId` y `WebhookToken`, sin columna `IsActive` separada de `GcRecord`.
- [ ] Índice único global filtrado sobre `WebhookToken`.
- [ ] Índice único filtrado sobre `(CompanyId, ChannelId)`.
- [ ] `Ticket.ExternalMessageId` es nullable, `varchar(200)`.
- [ ] Índice único filtrado `(CompanyId, ChannelId, ExternalMessageId) WHERE ExternalMessageId IS NOT NULL AND GcRecord = 0`.
- [ ] `TicketMapper.ToEntity` y `TicketMapper.ApplyUpdate` ignoran `Ticket.ExternalMessageId`; el build no emite diagnósticos `RMG0xx` nuevos.
- [ ] Ningún endpoint autenticado permite escribir `ExternalMessageId` (no está en `CreateTicketCommand` ni en `UpdateTicketCommand`).
- [ ] Una sola migración nueva, aplica limpio sobre base vacía y sobre una base ya migrada con tickets existentes.

### F2/F3 — `TicketInboundChannel` CRUD

- [ ] El token generado tiene al menos 32 bytes de entropía codificados (43+ caracteres Base64Url).
- [ ] El token completo solo aparece en la respuesta de `POST`; toda lectura posterior lo enmascara.
- [ ] `Create` duplicado (misma empresa, mismo canal, ya con una fila activa) → 409 `INBOUND_CHANNEL_ALREADY_ACTIVE`.
- [ ] `Delete` invalida el token de inmediato (el siguiente request al webhook con ese token → 404).

### F4 — `RegisterInboundTicketCommand`

- [ ] Sin `IsSuperAdminTicket` activo en la empresa → 400 `NO_TICKET_ADMIN_CONFIGURED`, ningún ticket se crea.
- [ ] `ExternalMessageId` repetido para la misma `(CompanyId, ChannelId)` devuelve el ticket ya existente, no crea un duplicado.
- [ ] Sin `TicketCompanyDefault` completo (los tres defaults obligatorios) → 400 `TICKET_DEFAULTS_NOT_CONFIGURED`.
- [ ] `PersonId` se resuelve por coincidencia exacta de `PersonContact.ContactValue` activo del tipo correcto para el canal; sin match, `PersonId = null` y el remitente queda en `Description`.
- [ ] `CreatedByUserId` siempre es el `IsSuperAdminTicket` resuelto; `AssignedToUserId` siempre `null` en el alta.
- [ ] `IsVisibleToExternals = true` en todo ticket creado por esta vía.
- [ ] Un adjunto que no pasa la validación de `TicketAttachmentSettings` se omite sin abortar la creación del ticket.
- [ ] El log `Creation` menciona el canal y el remitente.

### F5/F6 — Controllers de ingesta

- [ ] Ambos controllers están `[AllowAnonymous]` y **no** dependen de `ICurrentUserService.CompanyId`.
- [ ] Token inexistente o soft-deleted → 404 desnudo, sin filtrar información sobre la validez del formato del token.
- [ ] Un token válido cuyo canal tiene un `Code` distinto del esperado por ese controller → el mismo 404 desnudo (nunca se procesa un payload con el parser del otro canal).
- [ ] Toda la discriminación de canal (ruteo de webhook y construcción de `WebhookUrl`) usa `CommunicationChannel.Code`; `Name` no aparece en ninguna comparación de control de flujo.
- [ ] Una descarga de media de Twilio que falla no impide que el resto del payload se procese.
- [ ] `InboundEmailController` acepta el multipart de SendGrid Inbound Parse y extrae `from`/`subject`/`text`/adjuntos correctamente.

### F7 — Seed

- [ ] `SystemOption` con `ControllerName = "TicketInboundChannels"` existe, y `GetRoleSystemOptionSeeds()` tiene sus tres filas de rol.
- [ ] `manager@join.com` (rol `Manager`, no privilegiado) obtiene 200 en `GET /api/v1/TicketInboundChannels` tras el seed.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test` → 0 fallidos, ≥ 30 tests nuevos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate de CI).
- [ ] Ningún endpoint de ingesta expone `Response<TicketDto>` completo hacia el proveedor externo (solo `{ ticketCode }`).
- [ ] `CURL_REQUESTS.md` documenta los 3 endpoints de `TicketInboundChannels` y el contrato esperado por los dos webhooks.

---

## Decisions taken and discarded

- **Token único por webhook en la URL, en vez de firma criptográfica del proveedor** (elegido) vs. implementar validación de `X-Twilio-Signature`/DKIM desde el día uno. Un token de 32 bytes en la URL es la misma clase de mecanismo que usa la enorme mayoría de integraciones webhook de terceros (Stripe, GitHub, etc. lo combinan con firma, pero muchas integraciones más simples se apoyan solo en la URL secreta). Implementar la validación de firma específica de cada proveedor es trabajo real que no bloquea el valor central de esta spec (recibir el mensaje y crear el ticket) y queda documentado como hardening futuro.
- **`CreatedByUserId` del ticket automático es el `IsSuperAdminTicket` de la empresa, no un usuario "System" nuevo** (elegido) vs. crear un `ApplicationUser` de servicio global o volver `Ticket.CreatedByUserId` nullable. Evita tocar el schema de `Security.Users`/Identity y evita una columna nullable en una entidad que SPEC 34/35 ya dejaron estable. Como efecto colateral deseado, ata la ingesta multicanal a que la empresa haya completado SPEC 34 — un roster de tickets sin un admin no puede recibir tickets automáticos, lo cual es coherente con el resto del diseño (todo lo demás también falla cerrado sin ese roster).
- **Sin auto-creación de `Person`** (elegido) vs. crear un `Person` mínimo con datos placeholder cuando no hay match. `Person.IdentificationTypeId`/`IdentificationNumber` son obligatorios y no tienen un valor sensato derivable de un número de WhatsApp o una dirección de correo — forzar un placeholder ahí sería inventar datos de identificación falsos en un módulo (Personas) cuyas reglas esta spec no audita ni controla. Dejar `PersonId = null` con el remitente visible en el texto es más honesto y no bloquea nada: un agente lo vincula manualmente con `UpdateTicket` en el primer contacto.
- **Reutilizar SendGrid para correo entrante (Inbound Parse) en vez de sumar un segundo proveedor de correo** (elegido). El sistema ya tiene `SendGridOptions`/`SendGridEmailAdapter` para saliente; Inbound Parse es una feature del mismo proveedor, cero SDKs ni cuentas nuevas.
- **Sin servicio compartido para la validación de adjuntos entre `UploadTicketDocumentCommandHandler` (SPEC 36) y `RegisterInboundTicketCommandHandler`** (elegido) vs. extraer un `TicketDocumentIngestionService` común. A diferencia de `TicketDtoAssembler` (que sí se justificó por tener 4 call sites idénticos en el camino *feliz*), acá el camino de ingesta automática es deliberadamente *best-effort* (omite en silencio, nunca falla duro) mientras que el camino interactivo de SPEC 36 sí falla duro — son dos políticas de error distintas sobre la misma validación. Forzarlas a compartir código las acoplaría de una forma que no vale la pena todavía; se revisita si aparece un tercer consumidor.
- **`ExternalMessageId` es un hash SHA-256 determinístico cuando SendGrid no expone `Message-Id`** (elegido) vs. no deduplicar correo entrante. Sin un id estable, cada reintento del proveedor crearía un ticket duplicado — un hash de `(from + subject + text)` es determinístico para el mismo mensaje sin depender de que el proveedor exponga un header que no siempre está disponible en el payload de Inbound Parse.
- **`InboundAttachment` declarado en SPEC 36, no acá** (revertido respecto de la primera redacción de esta spec). El criterio original era cronológico —SPEC 36 ya estaba escrita, así que el record se declaraba acá y SPEC 36 "podía adoptarlo"—, pero eso dejaba abierta la posibilidad concreta de terminar con dos records idénticos si ambas specs se implementan en el mismo PR, que es el escenario esperado. Se decidió por ubicación lógica en vez de por orden de redacción: el tipo vive junto a la entidad que persiste los archivos (`TicketDocument`), y `UploadTicketDocumentCommand` pasa a recibirlo como parámetro `File` en vez de los cuatro campos sueltos. SPEC 36 quedó actualizada en consecuencia.
- **La ingesta de WhatsApp se registra contra la fila `WhatsApp` (`Code = WHATSAPP`), no contra la fila `Twilio`** (elegido). El catálogo sembrado tiene ambas, y la implementación de esta spec habla el protocolo de Twilio, así que la elección no era obvia. Criterio: `Ticket.ChannelId` responde "¿por dónde llegó esto?" desde el punto de vista del negocio —el cliente escribió por WhatsApp—, no "¿qué SaaS lo transportó?". Twilio es el `Provider` de esa integración, que es exactamente el rol que la columna `CommunicationChannel.Provider` cumple. Como efecto práctico, migrar mañana de Twilio a Meta Cloud API directo no invalida el histórico de tickets ni obliga a reasignar `ChannelId` en filas viejas: cambia el controller y el `Provider`, no el canal. **Consecuencia menor a aceptar**: la fila sembrada tiene `Provider = "Meta"` aunque el transporte real de esta spec sea Twilio. No se toca. `SeedCommunicationChannelsAsync` es idempotente por `Name` (`AnyAsync(c => c.Name == seed.Name)`), así que cambiar el literal solo afectaría bases nuevas y dejaría las existentes con el valor viejo — una corrección a medias es peor que ninguna. `Provider` es informativo; la verdad operativa de qué transporte está activo vive en la fila de `TicketInboundChannel` y en la configuración `Twilio:*`. Corregirlo de verdad exige que el seeder actualice campos de filas existentes, que es un cambio de comportamiento del seeder fuera del alcance de esta spec.
- **Respuesta mínima (`{ ticketCode }`) a los webhooks, no el `Response<TicketDto>` completo** (elegido). El proveedor externo (Twilio, SendGrid) no necesita —ni debería recibir— el detalle interno del ticket (nombres de usuarios, ids internos); solo necesita un 2xx para no reintentar, y el código es útil para logs de diagnóstico del lado del proveedor si hace falta correlacionar.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **Un `webhookToken` filtrado** (log accidental, URL compartida por error) permite crear tickets arbitrarios en esa empresa hasta que alguien lo rote. | Es el trade-off aceptado de la Decisión de "token en URL, sin firma criptográfica". Mitigación operativa: `DELETE /TicketInboundChannels/{id}` invalida de inmediato; se documenta en `CURL_REQUESTS.md` como el procedimiento de rotación de emergencia. |
| **Twilio deja de poder alcanzar `MediaUrlN`** (credenciales `Twilio:AccountSid`/`AuthToken` vencidas o mal configuradas) — todos los adjuntos de WhatsApp se omiten silenciosamente sin que nadie lo note. | El log del ticket registra explícitamente "adjunto omitido: <motivo>" por cada fallo (paso 10 del flujo) — visible en `GET /Tickets/{id}` para cualquier agente, no un fallo mudo. Una alerta operativa sobre esa tasa de omisión queda fuera de esta spec. |
| **Un correo de spam o un número de WhatsApp desconocido genera tickets basura** sin ningún filtro de contenido. | Aceptado explícitamente (ver "Rate limiting / anti-spam" en Out of scope). El `webhookToken` ya exige conocer la URL exacta configurada por la empresa — no es un endpoint público indexable — pero no filtra contenido de quien sí la conoce. |
| **Colisión de `ExternalMessageId` entre dos canales distintos de la misma empresa** (improbable pero no imposible si dos proveedores generan el mismo id por coincidencia). | El índice único incluye `ChannelId`, no solo `CompanyId` + `ExternalMessageId` — dos canales distintos con el mismo id externo no colisionan entre sí. |
| **`TwilioMediaDownloader` como dependencia HTTP saliente adicional** en el pipeline de creación de tickets introduce latencia y un punto de falla externo dentro de un `ITransactionalCommand`. | Aceptado: la descarga ocurre **después** de que el ticket ya se insertó (paso 10 es posterior al paso 9), así que una descarga lenta o fallida nunca bloquea ni revierte la creación del ticket en sí — solo retrasa/omite el adjunto. |
| **`TicketCompanyDefault` incompleto es un fallo silencioso hasta que llega el primer mensaje real.** Una empresa puede configurar su `TicketInboundChannel` (URL de webhook) sin haber completado `TicketCompanyDefault`, y no se entera hasta que un cliente le escribe y el mensaje rebota. | Se documenta explícitamente en el runbook de `CURL_REQUESTS.md`: el orden de configuración correcto es `TicketCompanyDefault` → `TicketUserCompany` (al menos un `IsSuperAdminTicket`) → `TicketAttachmentSettings` → `TicketInboundChannel`. Una validación proactiva ("¿está todo configurado?") en un endpoint de diagnóstico queda fuera de esta spec. |

---

## What is **not** in this spec

- Canal "app" (requiere portal/identidad de cliente, no existe).
- Canal "web operativa" como ingesta (ya resuelto por `POST /Tickets` autenticado).
- Validación de firma criptográfica de Twilio/SendGrid.
- Auto-creación o matching difuso de `Person`.
- Auto-respuesta al remitente por el mismo canal.
- Rate limiting / anti-spam.
- Otros proveedores de WhatsApp o correo entrante más allá de Twilio/SendGrid.
- Reintentos automáticos de descarga de adjuntos fallidos.
- Workflow/SLA parametrizable — SPEC 37.

Cada uno, si llega, va en su propia spec.
