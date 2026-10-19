# SPEC 75 — WhatsApp: agentes y coordinadores, disponibilidad, Cola y En espera, notificaciones, envío de mensajes y tiempo real

> **Status:** Borrador
> **Depends on:** **SPEC 73** (canal y cliente de Meta), **SPEC 74** (conversaciones, mensajes, estados y archivos), SPEC 49-post (storage, para los archivos que se envían). Rama de trabajo: `spec-75-whatsapp-agents-queue-sending-realtime`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que los usuarios de la empresa atiendan WhatsApp como agentes o coordinadores. Cada agente ve lo que le corresponde, toma conversaciones de la Cola, el coordinador asigna las que esperan asesor, y todos responden con lo que Meta permite. La bandeja se actualiza en vivo con aviso sonoro.
> **Origen:** diseño del canal WhatsApp (2026-10-18 y 2026-10-19). Propuesta visual: `join_frontb/SpecPropuestaWhatsApp/PropuestaWhatsApp_JOIN.html` (Bandeja y Agentes). Front: FE-49 (bandeja) y FE-50 (agentes).

---

## Reglas de negocio (decisiones del usuario, 2026-10-18 y 2026-10-19)

1. **Agente = usuario del sistema.** La tabla `Channel_Meta.Agents` vincula un `ApplicationUser` de la empresa con su tipo:
   - **Agente:** atiende conversaciones.
   - **Coordinador:** recibe las escaladas, asigna y reasigna. Normalmente hay uno, pero puede haber dos o más por temporada.
2. **Visibilidad:**
   - Un agente ve **solo sus conversaciones asignadas**.
   - Si el administrador le marca **"Ve todas"**, ve todas.
   - Los coordinadores ven todas siempre (el flag queda marcado y bloqueado).
   - Además, todo agente **disponible** ve la **Cola**.
3. **Disponibilidad:** cada agente marca "Disponible" o "No disponible" desde la bandeja. Solo los disponibles reciben avisos de la Cola y de las escaladas. Desconectarse de SignalR no cambia la disponibilidad, pero la presencia (`LastSeenAt`) se muestra aparte.
4. **Con el asistente IA apagado** (o sin BE-80): una conversación nueva sin agente entra a la **Cola** (`QUEUED`) y se avisa a los agentes disponibles con sonido. El primero que la **toma** se la queda.
   - Si nadie la toma en `RenotifyMinutes`, se re-avisa a todos los disponibles.
   - La asignación automática (BE-79) puede asignarla antes.
5. **Escaladas (`ON_HOLD`, BE-80):**
   - se avisa a los **coordinadores disponibles**, que la asignan a un miembro o la toman;
   - se re-avisa cada `RenotifyMinutes`;
   - si pasan `EscalateToAllAfterMinutes`, se avisa también a todos los agentes disponibles;
   - si no hay coordinador disponible, se avisa de inmediato a todos los disponibles.
6. **Concurrencia:** tomar o asignar usa un bloqueo de fila (`SELECT … FOR UPDATE`). Si dos lo intentan a la vez, gana el primero y el otro recibe `CONVERSATION_ALREADY_TAKEN`.
7. **Envío:** el agente envía todo lo que Meta permite:
   - texto con vista previa de enlaces;
   - imagen, video, audio o nota de voz (OGG/Opus) y documento;
   - mensajes interactivos (lista y botones de respuesta);
   - respuesta citada (`context.message_id`) y reacción;
   - plantilla (BE-76).
8. **Ventana de 24 h:**
   - **Cerrada:** solo se acepta una plantilla aprobada. Si no, `WINDOW_CLOSED_TEMPLATE_REQUIRED`, como `Response` de error y no como excepción.
   - **Plantilla con la ventana cerrada:** abre una conversación nueva en JOIN, con el siguiente código del día, en estado `WAITING_CUSTOMER` y asignada a quien envía.
9. **Quién puede escribir:** el agente asignado o un coordinador. Escribir en una conversación de otro, en la Cola o atendida por la IA exige **tomarla** primero.
10. **Tiempo real:** un hub SignalR por empresa avisa de mensajes nuevos, cambios de estado de entrega, asignaciones y notificaciones. El aviso sonoro es preferencia de cada usuario (frontend).
11. **Respuesta en nombre de la empresa:** el cliente siempre ve el nombre de la empresa. En JOIN cada mensaje enviado muestra el agente que lo escribió (nombre y `AvatarUrl` de "Mi cuenta").

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Channel_Meta`)
1. `Agent` (`BaseTenantEntity`, único `(CompanyId, UserId)` filtrado):
   - `UserId`, `AgentType` (`Agent = 1`, `Coordinator = 2`), `CanSeeAllConversations`;
   - `IsAvailable`, `MaxOpenConversations` (0 = sin tope), `ReceivesAutoAssignment` (BE-79);
   - `IsActive`, `LastSeenAt?`, `Note?` (máx. 100, por ejemplo "temporada alta").
2. `Notification` (`BaseTenantEntity`):
   - destino y tipo: `UserId`, `Type` (`QueueNew = 1`, `HandoffPending = 2`, `Renotify = 3`, `ConversationAssigned = 4`, `PersonPendingAssigned = 5`, `TokenExpiring = 6`);
   - referencias: `ConversationId?`, `PersonPendingConfirmationId?`;
   - contenido: `Title` (máx. 150), `Body` (máx. 500);
   - fechas: `CreatedAt`, `ReadAt?`.

   Índice `(UserId, ReadAt, CreatedAt)`.
3. `QueueSettings` (`BaseTenantEntity`, único por empresa): `RenotifyMinutes` (5), `EscalateToAllAfterMinutes` (15). Con BE-80, el asistente usa estos mismos valores.
4. Migración `Spec75WhatsAppAgentsQueue`, con opciones de menú y permisos.

**B. Tiempo real**
5. SignalR (primer hub del proyecto): `WhatsAppHub` en `/hubs/whatsapp`, con JWT por `access_token` en la query.
   - Grupos `company:{companyId}`, `user:{userId}` y `coordinators:{companyId}`.
   - Implementa `IWhatsAppEventPublisher` (BE-74) con estos eventos: `ConversationChanged`, `MessageReceived`, `MessageStatusChanged`, `NotificationCreated` y `AgentPresenceChanged`.
   - Cada evento solo llega a quien puede ver esa conversación (regla 2): los no visibles se filtran en el servidor.
6. En AWS con más de una instancia de la API, el hub necesita un backplane (Redis). Ver Preguntas abiertas.

**C. Cola, escaladas y avisos**
7. `ConversationRouter`, llamado por el procesador de BE-74 al crear una conversación y por BE-80 al escalar. Decide entre Cola, automatización (BE-79) o IA (BE-80), y crea las `Notification` con su evento SignalR.
8. `WhatsAppRenotifyMonitor` (`BackgroundService`, cada minuto): re-avisa las conversaciones `QUEUED` y `ON_HOLD` según `LastNotifiedAt` (reglas 4 y 5).
9. Las notificaciones de personas pendientes asignadas (BE-74, punto 25) y de vencimiento del token (BE-73) también se crean aquí.

**D. Envío** (Application + Infrastructure)
10. `IMetaGraphClient` suma `SendMessageAsync` (`POST /{phone-number-id}/messages`), `UploadMediaAsync` (`POST /{phone-number-id}/media`) y `MarkAsReadAsync`.
11. `SendWhatsAppMessageCommand` `{ conversationId, type, text?, mediaFile?, interactive?, replyToMessageId?, reaction? }`:
    1. valida empresa, agente activo y visibilidad (reglas 2 y 9), y la ventana (regla 8);
    2. si lleva archivo, lo guarda **primero** en el storage (`MessageMedia` con ruta) y luego lo sube a Meta;
    3. crea el `Message` en `Sending`;
    4. envía y guarda el `wamid` que devuelve Meta (o `Failed` con el error de Meta);
    5. pasa la conversación a `WAITING_CUSTOMER`.

    El envío a Meta ocurre **fuera** de la transacción.
12. Leer una conversación (`POST /{id}/read`, BE-74) también envía `MarkAsReadAsync` del último entrante, para que el cliente vea los checks azules.

**E. Endpoints**
13. Recurso `WhatsAppInbox`:
    - `POST /api/v1/WhatsAppConversations/{id}/messages` (multipart si lleva archivo; `CanCreate`).
    - `POST /api/v1/WhatsAppConversations/{id}/take` (`CanUpdate`): pasa a `OPEN`, asigna al usuario y registra `TakenAt`. Si venía de la IA, también `HandledBy = Handoff` y `AiPaused = true`. Agrega un mensaje de sistema.
    - `POST /api/v1/WhatsAppConversations/{id}/assign` `{ agentId }`: solo coordinadores (`WHATSAPP_COORDINATOR_REQUIRED`). Mismos efectos que `take`, más el aviso al asignado.
    - `GET /api/v1/WhatsAppInbox/summary`: contadores por pestaña (Mías, Cola, En espera, IA, Sin identificar, Todas) y no leídas, según la visibilidad del usuario.
    - `GET /api/v1/WhatsAppInbox/me` y `PUT /api/v1/WhatsAppInbox/me/availability` `{ isAvailable }`: el agente actual, su tipo y su disponibilidad. Si el usuario no es agente, `me` responde 404 `WHATSAPP_AGENT_NOT_FOUND`.
14. `GET /api/v1/WhatsAppConversations` (BE-74) aplica la visibilidad de la regla 2 y suma el filtro `tab`.
15. Recurso `WhatsAppAgents`: `GET` (con abiertas por agente), `POST`, `PUT /{id}`, `DELETE /{id}` (soft delete; sus conversaciones abiertas vuelven a la Cola, o a `ON_HOLD` si venían de la IA). `GET /api/v1/WhatsAppAgents/candidates` lista los usuarios de la empresa que aún no son agentes.
16. `GET/PUT /api/v1/WhatsAppQueueSettings` (recurso `WhatsAppSettings`).
17. Notificaciones (`[SkipDynamicAuthorization]`, siempre del usuario actual): `GET /api/v1/Notifications?unreadOnly=`, `POST /api/v1/Notifications/{id}/read` y `POST /api/v1/Notifications/read-all`.

**F. Menú y permisos**
18. `SystemOption` `WhatsAppInbox` (`/whatsapp/inbox`, `ControllerName` `WhatsAppInbox`) y `WhatsAppAgents` (`/whatsapp/agents`, `ControllerName` `WhatsAppAgents`), en el módulo `WhatsApp`. `WhatsAppConversations` (recurso de BE-74) va sin menú hasta BE-78.
19. Permisos sembrados:
    - Admin y SuperAdminCompany: todo.
    - Coordinador y Supervisor: todo en la bandeja y lectura en Agentes.
    - Agent y UsuarioSimple: la bandeja sin `CanDelete`.

    El tipo Agente o Coordinador de `Channel_Meta.Agents` decide qué hace dentro de la bandeja; el permiso decide si entra.

**Out of scope:**
- Plantillas (BE-76), etapas (BE-77), índice (BE-78), automatizaciones (BE-79) e IA (BE-80).
- Transferencias entre empresas.
- Métricas de tiempos de respuesta por agente (otra spec de reportes).
- Mensajes de ubicación y de contactos salientes.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `WHATSAPP_AGENT_NOT_FOUND` | 404 | El usuario no es agente activo de la empresa |
| `WHATSAPP_AGENT_ALREADY_EXISTS` | 409 | El usuario ya es agente |
| `WHATSAPP_COORDINATOR_REQUIRED` | 403 | Asignar sin ser coordinador |
| `CONVERSATION_NOT_VISIBLE` | 404 | Fuera de la visibilidad del agente (se responde como no encontrada) |
| `CONVERSATION_ALREADY_TAKEN` | 409 | Otro agente la tomó o la asignó primero |
| `CONVERSATION_NOT_ASSIGNED_TO_YOU` | 409 | Escribir sin tomarla (regla 9) |
| `WINDOW_CLOSED_TEMPLATE_REQUIRED` | 409 | Ventana cerrada y el envío no es una plantilla |
| `WHATSAPP_CHANNEL_NOT_CONNECTED` | 409 | El canal no está `Connected` |
| `MEDIA_TYPE_NOT_ALLOWED` / `MEDIA_TOO_LARGE` | 400 | Contra `MediaSettings` y los límites de Meta |
| `META_API_ERROR` | 502 | Meta rechazó el envío; el mensaje queda `Failed` |

## Implementation plan

1. Entidades, migración, menú y permisos.
2. CRUD de agentes, `me` y disponibilidad.
3. Visibilidad en las consultas de BE-74 y `summary`.
4. `take` y `assign` con bloqueo y mensajes de sistema; tests de concurrencia.
5. `ConversationRouter`, `Notification` y `WhatsAppRenotifyMonitor`.
6. Hub SignalR, publicador y filtrado por visibilidad. Integración con `Microsoft.AspNetCore.SignalR.Client` en los tests.
7. Envío de texto, archivos, interactivos, citas y reacciones; la ventana; `MarkAsRead`.
8. Smoke con la App de prueba: envío y checks hasta azul en un teléfono real.

## Criterios de aceptación

- [ ] Un agente sin "Ve todas" no recibe en la lista, en el detalle ni por SignalR una conversación de otro agente.
- [ ] Una conversación nueva con la IA apagada queda `QUEUED`, aparece en la Cola de los agentes disponibles y les llega una `Notification`; a los no disponibles no les llega.
- [ ] Dos `take` simultáneos: uno responde 200 y el otro 409 `CONVERSATION_ALREADY_TAKEN`.
- [ ] Una `QUEUED` sin tomar se re-avisa a los 5 min (valor de `QueueSettings`).
- [ ] Una `ON_HOLD` avisa a los coordinadores disponibles; sin coordinador disponible, a todos los disponibles; a los 15 min, también a todos.
- [ ] `assign` sin ser coordinador responde 403.
- [ ] Un texto con la ventana cerrada responde 409 `WINDOW_CLOSED_TEMPLATE_REQUIRED`.
- [ ] Un documento enviado queda primero en el storage con su ruta y luego en Meta; el `wamid` queda guardado y los estados lo llevan a `Read`.
- [ ] Borrar un agente devuelve sus conversaciones abiertas a la Cola.
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: tabla de agentes en `Channel_Meta` vinculada al usuario, con flag "Ve todas"** (usuario, 2026-10-18).
- **Sí: "Coordinador" en lugar de "Supervisor"**; normalmente uno, a veces dos (usuario, 2026-10-19).
- **Sí: Cola por "tomar" con la IA apagada y aviso a los disponibles** (usuario, 2026-10-19).
- **Sí: SignalR y aviso sonoro** (usuario, 2026-10-18).
- **Sí: tiempos parametrizables** (usuario, 2026-10-19): `QueueSettings`.
- **Sí: enviar todo lo que Meta permite al agente** (usuario, 2026-10-18).
- **Sí: el archivo saliente se guarda en el storage antes de subirlo a Meta**, para que la conversación siempre tenga su copia.
- **Sí: el tipo de agente no reemplaza los permisos del rol.** El rol decide si se entra a la opción; el tipo, qué se hace dentro.

## Preguntas abiertas

1. **Backplane de SignalR en AWS:** si la API corre en más de una instancia, hace falta Redis (ElastiCache) o Azure SignalR Service. Con una sola instancia no hace falta. ¿Cuántas instancias se planean?

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Un evento SignalR filtra datos de una conversación no visible. | El filtrado se hace en el servidor por usuario, con tests que lo afirman. |
| Nadie toma la Cola (todos no disponibles). | Re-aviso periódico y el contador del menú siempre visible; la asignación automática (BE-79) puede cubrirlo. |
| Meta rechaza envíos por la calidad del número. | El error de Meta queda en el mensaje (`ErrorCode`, `ErrorTitle`) y la calidad se ve en la configuración (BE-73). |

## Impacto en el frontend

- **FE-49** (Borrador): bandeja con pestañas, tomar y asignar, envío, SignalR y sonido.
- **FE-50** (Borrador): pantalla de agentes.
