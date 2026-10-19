# SPEC 74 — WhatsApp: conversaciones con código propio, mensajes, estados, marca de identificación, personas pendientes y archivos

> **Status:** Borrador
> **Depends on:** **SPEC 73** (canal, webhook y `WebhookEvents`), SPEC 45-post (`IntakeRequesterResolver`, `PersonIntakeRegistrar`, personas pendientes), SPEC 49-post (`IFileStorageResolver`), SPEC 54 (`GET /Persons/lookup`), SPEC 55 (clientes), SPEC 58 (persona sin identificación), SPEC 61 (leads), SPEC 65 (personas pendientes → lead). Rama de trabajo: `spec-74-whatsapp-conversations-messages-media`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Convertir cada entrega de Meta en mensajes agrupados por conversación con un código propio (`AAAAMMDD-N`), vinculados a una persona o cliente (o al cliente genérico John Doe), con sus estados, sus archivos guardados en el storage como ruta y la identificación del contacto como una marca aparte.
> **Origen:** diseño del canal WhatsApp (2026-10-18 y 2026-10-19). Propuesta visual: `join_frontb/SpecPropuestaWhatsApp/PropuestaWhatsApp_JOIN.html` (Bandeja, Configuración › Archivos y Estados). Front: FE-49.

---

## Reglas de negocio (decisiones del usuario, 2026-10-18 y 2026-10-19)

1. **Código propio por empresa y día:**
   - Formato `AAAAMMDD-N`, sin ceros a la izquierda ni tope de dígitos (`20261015-1`, `20261201-100`).
   - El día es el de la zona horaria de la empresa (`CalendarCompany.TimeZone`, SPEC 44-post); sin configuración, UTC.
   - La conversación tiene `Id` (GUID), `Code` (único por empresa) y `MetaConversationId`.
2. **Meta decide cuándo empieza una conversación** (regla igual para todas las empresas). El código propio agrupa; el id de Meta identifica.
   - Un mensaje entrante se suma a la conversación vigente del contacto. Si no hay vigente (cerrada o expirada), se crea una nueva.
   - `MetaConversationId` se llena con el primer webhook de estado que lo traiga (Meta solo lo manda en los estados de mensajes enviados por la empresa).
   - Si un estado trae un id de Meta distinto del que ya tiene la conversación, el mensaje se mueve a una conversación nueva con el siguiente código y se registra en el log.
   - **Respaldo:** si Meta no manda el objeto `conversation` (tarificación por mensaje), una conversación queda vigente mientras no hayan pasado 24 h desde el último mensaje del cliente.
3. **Estados propios** en un catálogo global (`Channel_Meta.ConversationStatuses`), paralelo a lo que expone Meta. La tabla de transiciones está más abajo.

   | Código | Nombre | Equivalencia en Meta |
   |---|---|---|
   | `AI_ATTENDING` | Atendida por IA | ventana abierta (BE-80) |
   | `QUEUED` | En cola | ventana abierta, sin agente |
   | `ON_HOLD` | En espera de asesor | ventana abierta, escalada (BE-80) |
   | `OPEN` | Abierta | ventana abierta, último mensaje del cliente |
   | `WAITING_CUSTOMER` | Esperando al cliente | ventana abierta, último mensaje de la empresa |
   | `CLOSED` | Cerrada | conversación expirada o cerrada a mano |
4. **La identificación no es un estado, es una marca** (`IdentificationState`): `Unidentified` (vinculada a John Doe), `PendingConfirmation` (persona registrada desde el canal, en la cola de personas pendientes) e `Identified`. Una conversación puede estar "Atendida por IA" y "Sin identificar" a la vez.
5. **Número desconocido → John Doe:**
   - Si el teléfono no coincide con ningún contacto de una persona de la empresa, la conversación se vincula al **cliente genérico John Doe** de la empresa, con la marca `Unidentified`.
   - El número real y el nombre de perfil de WhatsApp quedan en la conversación (`ContactWaId`, `ContactProfileName`).
   - Se pasa a `Identified` al vincular una persona o cliente existente.
6. **Persona nueva desde el canal:**
   - Si la persona no existe, se registra como **persona pendiente de confirmar** (cola `/customers/pending-persons`, SPEC 45-post). La registra la IA (BE-80) o un agente desde la bandeja.
   - La conversación queda vinculada a esa persona con la marca `PendingConfirmation`.
   - Los coordinadores reciben un aviso (BE-75) y **designan** a un miembro del equipo para completar sus datos: la actividad de revisión pasa al calendario de ese miembro.
   - Cuando la pendiente se resuelve (confirmar, vincular a cliente, convertir en lead), la marca pasa a `Identified`. Si se descarta, vuelve a `Unidentified` (John Doe).
7. **Archivos:**
   - Audio, video, imagen, documento y sticker se descargan de Meta al procesar el mensaje: la URL de Meta dura unos 5 minutos y Meta borra el archivo a los 30 días.
   - Se guardan con `IFileStorageResolver` (Local, Azure Blob, S3 o R2, configurable). En la base queda **solo la ruta y el proveedor**.
   - Los tipos y tamaños son una configuración propia del canal (`MediaSettings`), separada de la de tickets.
8. **Contacto conocido:** el teléfono se compara solo por dígitos con los contactos `WhatsApp`, `Mobile` y `Phone` de las personas vivas de la empresa (`IntakeRequesterResolver`). Si hay más de una persona con ese número, se toma la que tenga cliente activo y, si sigue el empate, la conversación queda `Unidentified` para que el agente elija.

## Transiciones de estado

| De | A | Quién |
|---|---|---|
| (nueva) | `AI_ATTENDING` | entrante con el asistente IA activo (BE-80) |
| (nueva) | `QUEUED` | entrante con la IA apagada y sin agente asignable |
| (nueva) | `OPEN` | entrante con agente asignado por automatización (BE-79) |
| (nueva) | `WAITING_CUSTOMER` | plantilla enviada por un agente con la ventana cerrada (BE-75) |
| `AI_ATTENDING` | `ON_HOLD` | escalado de la IA o falla del flujo (BE-80) |
| `QUEUED`, `ON_HOLD`, `AI_ATTENDING` | `OPEN` | un agente la toma o el coordinador la asigna (BE-75) |
| `OPEN` | `WAITING_CUSTOMER` | envío de un agente |
| `WAITING_CUSTOMER` | `OPEN` | entrante del cliente |
| `OPEN`, `WAITING_CUSTOMER` | `AI_ATTENDING` | el agente la devuelve a la IA (BE-80) |
| cualquiera menos `CLOSED` | `CLOSED` | expiración de Meta o de la ventana de respaldo, cierre por inactividad (BE-79) o un agente |

Las reglas de transición viven en `ConversationStatusTransitions` (clase de Application), que usan todas las specs de la serie. Un cambio no permitido responde `CONVERSATION_STATUS_TRANSITION_INVALID` (409).

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Channel_Meta`)
1. `ConversationStatus` (catálogo global, sembrado): `Code`, `Name`, `MetaEquivalent`, `IsSystem`, `SortOrder`.
2. `Conversation` (`BaseTenantEntity`):
   - **Identidad:** `Code` (máx. 20, único `(CompanyId, Code)`), `MetaConversationId?` (máx. 100), `MetaCategory?`, `MetaExpiresAt?`.
   - **Contacto e identificación:** `ContactWaId` (máx. 20, solo dígitos), `ContactProfileName?`, `PersonId`, `CustomerId?`, `IdentificationState`, `PersonPendingConfirmationId?`.
   - **Atención:** `StatusId`, `AgentId?` (BE-75), `StageId?` (BE-77).
   - **Ciclo de vida:** `OpenedAt`, `ClosedAt?`, `ClosedReason?`.
   - **Último mensaje y ventana:** `LastMessageAt`, `LastMessagePreview` (máx. 200), `LastInboundAt?` (ventana de 24 h), `UnreadCount`.
   - **Vínculos:** `LeadId?`, `TicketId?`.
   - **Atención por IA** (columnas que llena BE-80; se crean aquí para no migrar dos veces): `HandledBy` (`Agent = 1`, `Ai = 2`, `Handoff = 3`), `AiPaused`, `AiTurns`, `HandoffReason?`, `HandoffSummary?`, `HandoffAt?`.
   - **Cola** (las usa BE-75): `QueuedAt?`, `TakenAt?`, `LastNotifiedAt?`.

   Índices: `(CompanyId, ContactWaId, StatusId)`, `(CompanyId, PersonId, OpenedAt)`, `(CompanyId, StatusId, LastMessageAt)`, `(CompanyId, MetaConversationId)` filtrado no nulo.
3. `ConversationDailySequence` (`CompanyId`, `Day` `date`, `LastNumber`), único `(CompanyId, Day)`. El sufijo se calcula bajo `pg_advisory_xact_lock`, como `TicketCodeGenerator`.
4. `Message` (`BaseTenantEntity`):
   - **Origen:** `ConversationId`, `Direction` (`In = 1`, `Out = 2`), `SenderType` (`Customer = 1`, `Agent = 2`, `Automation = 3`, `Ai = 4`), `AgentId?`.
   - **Id de Meta:** `WaMessageId?` (máx. 128, único filtrado no nulo).
   - **Contenido:** `Type` (text, image, audio, video, document, sticker, location, contacts, interactive, button, template, reaction, unsupported), `Body?` (`text`), `ReplyToMessageId?`, `TemplateId?` (BE-76).
   - **Entrega:** `Status` (`Received = 0`, `Sending = 1`, `Sent = 2`, `Delivered = 3`, `Read = 4`, `Failed = 5`), `StatusAt?`, `ErrorCode?`, `ErrorTitle?`.
   - **Extras:** `Payload` (`jsonb`, el objeto original de Meta), `Transcript?` (`text`, BE-80), `AiInteractionId?` (BE-80), `SentAt`.

   Índice `(ConversationId, SentAt)`.
5. `MessageMedia` (`BaseTenantEntity`): `MessageId`, `MetaMediaId`, `MimeType`, `FileName?`, `SizeBytes?`, `Sha256?`, `StoragePath?`, `StorageProvider?` (`StorageProviderKind`), `DownloadStatus` (`Pending = 1`, `Stored = 2`, `Rejected = 3`, `Failed = 4`), `RejectedReason?`.
6. `MediaSettings` (`BaseTenantEntity`, único por empresa): por tipo (imagen, audio, video, documento, sticker) `Allowed` y `MaxBytes`, sin pasar el límite de Meta. Seed:

   | Tipo | Máximo | Permitido |
   |---|---|---|
   | Imagen | 5 MB | sí |
   | Audio | 16 MB | sí |
   | Video | 16 MB | sí |
   | Documento | 20 MB | sí |
   | Sticker | 500 KB | no |
7. **John Doe por empresa:** `Admin.Persons` sin identificación (SPEC 58) con nombre "John Doe" y `Customer.Customers` activo. Se marca con `IsSystem = true`, una columna nueva en `Customer` (no se puede inactivar, borrar ni ganar como lead), y se crea con `SeedWhatsAppGenericCustomerAsync`, que es idempotente, al crear el canal (BE-73) y en el seed.
8. `Admin.PersonPendingConfirmations` (ajuste a SPEC 45-post): suma `AssignedUserId?` y `ConversationId?`.
9. Migración `Spec74WhatsAppConversations`: tablas, catálogo, columnas nuevas, índices y seed.

**B. Procesador de webhooks**
10. `WhatsAppWebhookProcessor` (implementa `IWhatsAppWebhookProcessor` de BE-73) en un `BackgroundService` que lee el canal en memoria y, al arrancar, los `WebhookEvents` pendientes. Por cada evento, en una transacción:
    - `messages[]`:
      1. deduplica por `wamid`;
      2. resuelve el contacto (regla 8) o usa John Doe;
      3. busca o crea la conversación (reglas 1 y 2);
      4. guarda el `Message`;
      5. actualiza `LastInboundAt`, `LastMessageAt`, la vista previa y `UnreadCount`;
      6. decide el estado inicial (tabla de transiciones);
      7. deja los archivos `Pending`.
    - `statuses[]`: actualiza `Status`, `StatusAt` y el error del mensaje por `wamid` (sin retroceder: un `read` no vuelve a `delivered`), y llena o compara `MetaConversationId`, `MetaCategory` y `MetaExpiresAt`.
    - Marca `ProcessedAt`. Si falla, `Attempts + 1` y `Error`. Se reintenta con espera creciente hasta 5 veces.
11. `WhatsAppMediaDownloader`, fuera de la transacción del mensaje:
    1. `GET /{media-id}` con el token del canal y descarga la URL;
    2. valida el tipo y el tamaño contra `MediaSettings`;
    3. calcula el SHA-256 y guarda en `channel-meta/{companyId}/{yyyy}/{MM}/{code}/{wamid}.{ext}`.

    Si se rechaza, el mensaje queda igual con `DownloadStatus = Rejected` y el aviso "Archivo no guardado" para que el agente lo pida de nuevo.
12. Después de guardar, publica `ConversationChanged` y `MessageReceived` en `IWhatsAppEventPublisher`. En esta spec, una implementación nula; BE-75 la conecta a SignalR.
13. **Cierre por expiración:** `WhatsAppConversationExpiryMonitor` (`BackgroundService`, cada 5 min) cierra las conversaciones con `MetaExpiresAt` vencido o, sin ese dato, con 24 h desde `LastInboundAt` y sin envíos posteriores. Usa `ClosedReason = MetaExpired` o `WindowExpired`.

**C. Endpoints** (recurso `WhatsAppConversations`)
14. `GET /api/v1/WhatsAppConversations` (paginado): filtros de estado, agente, marca de identificación, `handledBy`, texto (nombre, número o código, `ILIKE`) y "solo no leídas". Ordena por `LastMessageAt` desc. Aplica la visibilidad de BE-75 cuando exista; aquí, todo para quien tenga `CanRead`.
15. `GET /api/v1/WhatsAppConversations/{id}`: cabecera con persona, cliente, estado, marca, ventana (`windowOpen`, `windowExpiresAt`), vínculos y datos de atención.
16. `GET /api/v1/WhatsAppConversations/{id}/messages?before=&take=50`: mensajes hacia atrás, con archivos (ruta, estado y nombre) y transcripción.
17. `POST /api/v1/WhatsAppConversations/{id}/read`: pone `UnreadCount = 0`.
18. `GET /api/v1/WhatsAppConversations/{id}/media/{mediaId}` (`CanDownload`): entrega el archivo desde su proveedor (`IFileStorageResolver`), igual que los adjuntos de ticket.
19. `POST /api/v1/WhatsAppConversations/{id}/link-person` `{ personId, addPhoneToContacts }` (`CanUpdate`):
    - vincula la persona y su cliente activo, si tiene;
    - pasa la marca a `Identified`;
    - si se pide, agrega el número como contacto `WhatsApp` de la persona. Si el número es de otra persona, responde `CONTACT_BELONGS_TO_OTHER_PERSON`, como SPEC 45-post.
20. `POST /api/v1/WhatsAppConversations/{id}/register-pending-person` `{ firstName, lastName, identificationTypeId, identificationNumber, reason }` (`CanUpdate`):
    - reutiliza `PersonIntakeRegistrar` con el canal `WHATSAPP` y el número de la conversación;
    - deja la pendiente con `ConversationId` y la conversación en `PendingConfirmation`;
    - avisa a los coordinadores (BE-75).

    Si la identificación ya existe en una persona activa, no crea nada y responde la persona para que el agente la vincule (`PERSON_ALREADY_EXISTS`, 409).
21. `POST /api/v1/WhatsAppConversations/{id}/link-lead` `{ leadId }` y `/link-ticket` `{ ticketId }` (`CanUpdate`): ambos de la persona de la conversación.
22. `POST /api/v1/WhatsAppConversations/{id}/close` (`CanUpdate`).
23. `GET/PUT /api/v1/WhatsAppMediaSettings` (recurso `WhatsAppSettings`).
24. `GET /api/v1/WhatsAppConversationStatuses` (catálogo).

**D. Personas pendientes** (ajuste a SPEC 45-post y SPEC 65)
25. `POST /api/v1/PersonPendingConfirmations/{id}/assign` `{ userId }` (recurso `PersonPendingConfirmations`, `CanUpdate`):
    - guarda `AssignedUserId`;
    - mueve la actividad de revisión al calendario del usuario designado (si no tiene calendario, `CALENDAR_USER_WITHOUT_CALENDAR`);
    - le avisa (BE-75).
26. Resolver, vincular, convertir en lead o descartar una pendiente con `ConversationId` actualiza la marca de la conversación (regla 6).
27. La lista de la cola (SPEC 45-post) suma `AssignedUserId`, el nombre del asignado y `ConversationCode`, y el filtro "Asignadas a mí".

**Out of scope:**
- Mensajes salientes (BE-75).
- Asignación, visibilidad, Cola y notificaciones (BE-75).
- Índice por persona (BE-78).
- Crear tickets o leads automáticamente desde la conversación: el agente usa las pantallas existentes y luego vincula.
- Mensajes de grupos de WhatsApp (la API de Cloud no los entrega).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `CONVERSATION_NOT_FOUND` | 404 | De otra empresa o inexistente |
| `CONVERSATION_STATUS_TRANSITION_INVALID` | 409 | Cambio de estado no permitido |
| `CONVERSATION_CLOSED` | 409 | Acción sobre una conversación cerrada que no lo permite |
| `CONTACT_BELONGS_TO_OTHER_PERSON` | 409 | El número ya es contacto de otra persona |
| `PERSON_ALREADY_EXISTS` | 409 | La identificación ya existe; se devuelve la persona |
| `INVALID_LEAD_PERSON` / `INVALID_TICKET_PERSON` | 400 | El lead o el ticket es de otra persona |
| `MEDIA_NOT_STORED` | 404 | El archivo fue rechazado o no se pudo descargar |
| `GENERIC_CUSTOMER_PROTECTED` | 409 | Intento de inactivar, borrar o ganar a John Doe |
| `CALENDAR_USER_WITHOUT_CALENDAR` | 400 | El usuario designado no tiene calendario |

## Implementation plan

1. Entidades, catálogo, columnas en `Customer` y `PersonPendingConfirmations`, migración y seed de John Doe. Build verde.
2. `ConversationCodeGenerator` (lock por empresa y día) y `ConversationStatusTransitions`, con tests de concurrencia y de cada transición.
3. Procesador de mensajes y estados con fixtures reales de Meta (texto, imagen, audio, documento, interactivo, reacción, estados con y sin `conversation`).
4. Descarga de archivos y `MediaSettings`.
5. Monitor de expiración.
6. Endpoints de lectura, vincular, registrar pendiente, cerrar y archivos.
7. Ajustes a personas pendientes (`assign`, marca de la conversación, filtros).
8. Integración y `EXPLAIN`. Smoke con la App de prueba de Meta: mensaje de texto, audio y documento desde un teléfono real.

## Criterios de aceptación

- [ ] El primer mensaje del día de una empresa crea `AAAAMMDD-1`; dos mensajes simultáneos de contactos distintos nunca reciben el mismo código.
- [ ] El mismo `wamid` entregado dos veces crea un solo mensaje.
- [ ] Un número desconocido crea la conversación vinculada a John Doe con la marca `Unidentified`; vincular una persona la pasa a `Identified` y agrega el número a sus contactos si se pidió.
- [ ] Registrar una persona pendiente desde la conversación crea la pendiente con `ConversationId`. Asignarla mueve la actividad de revisión al calendario del designado, y confirmarla deja la conversación `Identified`.
- [ ] Un audio entrante queda con ruta y proveedor en `MessageMedia`, y la descarga desde JOIN devuelve el archivo; en la base no hay bytes del archivo.
- [ ] Un video mayor al máximo configurado queda `Rejected` y el mensaje se guarda igual.
- [ ] Un estado `read` actualiza el mensaje; un `delivered` posterior no lo retrocede.
- [ ] El primer estado con `conversation.id` llena `MetaConversationId`; uno distinto mueve el mensaje a una conversación nueva.
- [ ] Sin datos de Meta, la conversación se cierra a las 24 h del último mensaje del cliente.
- [ ] John Doe no se puede inactivar, borrar ni ganar.
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: código `AAAAMMDD-N` sin tope de dígitos** (usuario, 2026-10-18).
- **Sí: Meta decide cuándo empieza una conversación; el código propio agrupa** (usuario, 2026-10-18).
- **Sí: estados propios en una tabla paralela a Meta** (usuario, 2026-10-18).
- **Sí: John Doe para números desconocidos** (usuario, 2026-10-18).
- **Sí: la identificación es una marca, no un estado** (usuario, 2026-10-19). Con IA, "quién atiende" e "identificada o no" avanzan por separado.
- **Sí: la persona nueva entra a la cola de personas pendientes y el coordinador designa quién la completa** (usuario, 2026-10-19).
- **Sí: archivos en el storage configurable, solo la ruta en la base** (usuario, 2026-10-18 y 2026-10-19), con configuración propia del canal.
- **Sí: las columnas de atención por IA se crean aquí** aunque las llene BE-80, para no migrar dos veces la tabla más grande del canal.

## Preguntas abiertas

1. **Respaldo de 24 h:** se confirma en el primer smoke si la versión de Graph API que usemos manda `conversation.id` en los estados. Si no lo manda, la regla de respaldo (24 h desde el último mensaje del cliente) pasa a ser la regla principal. ¿Conforme con ese respaldo?
2. **John Doe:** uno por empresa, como persona sin identificación más cliente, marcado de sistema. ¿Conforme?
3. **Personas pendientes registradas por un agente desde la bandeja:** hoy FE-40 (regla 4) dice que las personas creadas por un usuario no entran a la cola. Esta spec propone que las registradas desde la bandeja de WhatsApp sí entren, igual que las de la IA. ¿Conforme?

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Ráfagas de webhooks saturan el procesador. | Canal en memoria con un consumidor por empresa y eventos pendientes leídos al arrancar; el webhook nunca espera al procesador. |
| La descarga de un archivo falla y Meta lo borra a los 30 días. | Reintentos con espera creciente y un aviso al agente si queda `Failed`. |
| Dos personas con el mismo número. | Regla 8: gana la que tiene cliente activo; si no, el agente elige. |

## Impacto en el frontend

- **FE-49** (Borrador): bandeja tipo WhatsApp (lista, chat y panel), vincular o registrar la persona, archivos.
- **FE-40** (personas pendientes): columna y filtro "Asignada a", acción "Asignar a", código de conversación con enlace. Ajuste por BE-74.
