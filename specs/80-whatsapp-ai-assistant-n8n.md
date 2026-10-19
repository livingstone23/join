# SPEC 80 — WhatsApp: asistente IA opcional vía un flujo de n8n (contexto, herramientas, escalado y transcripción)

> **Status:** Borrador
> **Depends on:** **SPEC 74** (conversaciones, mensajes, archivos, personas pendientes), **SPEC 75** (Cola, En espera, notificaciones, envío), SPEC 76 (plantillas, solo para excluirlas), SPEC 79 (convivencia con las automatizaciones), SPEC 45-post (API del agente de canales: personas y citas), SPEC 57 (catálogo de productos), SPEC 61 y SPEC 63 (leads y leads ganados), SPEC 34 a 37 (tickets). Rama de trabajo: `spec-80-whatsapp-ai-assistant-n8n`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Un asistente IA, que el administrador del módulo enciende o apaga por empresa, atiende primero las conversaciones entrantes con un único flujo publicado de n8n. Responde sobre la empresa, sus productos, las últimas compras, los tickets y las citas, con el contexto de las últimas 3 conversaciones del contacto. Cuando no puede resolver, pasa la conversación a "En espera de asesor" con un resumen y avisa al coordinador.
> **Origen:** pedido del usuario (2026-10-19) y sus respuestas del mismo día. Propuesta visual: `join_frontb/SpecPropuestaWhatsApp/PropuestaWhatsApp_JOIN.html` (Bandeja con burbujas "Asistente IA", Configuración › Asistente IA, Modelo y encaje › Atiende el asistente IA). Front: FE-54 (configuración) y FE-49 (bandeja).

---

## Reglas de negocio (decisiones del usuario, 2026-10-19)

1. **Opcional por empresa.** El administrador del módulo lo enciende o lo apaga.
   - **Apagado:** las conversaciones nuevas van a la Cola (BE-75).
   - **Encendido:** la IA las atiende primero (`AI_ATTENDING`).
2. **Un único flujo de n8n para todas las empresas.**
   - JOIN identifica la empresa por la URL de webhook de su App y el `phone_number_id` que recibió el contacto (BE-73), y se la pasa al flujo.
   - La URL del flujo se guarda por empresa: así una empresa puede apuntar a un flujo dedicado sin tocar código.
3. **n8n nunca habla con Meta.**
   - Responde a través de JOIN, que envía, registra y respeta la ventana.
   - Cada evento lleva un **token de 10 minutos limitado a esa conversación y empresa**. n8n no guarda credenciales de larga duración.
   - Las herramientas filtran por la empresa del token.
4. **La IA nunca inicia una conversación:** solo responde a mensajes entrantes. No envía plantillas ni escribe con la ventana cerrada.
5. **Reglas del asistente**, editables por el administrador y enviadas en cada evento. Las iniciales:
   1. Antes de responder, cargar la ficha y las últimas 3 conversaciones.
   2. Si el número no está identificado, invitar a la persona a identificarse y buscarla en JOIN.
   3. Si no existe, registrarla como persona pendiente de confirmar y seguir atendiendo (el coordinador designa quién completa sus datos, BE-74).
   4. Si la consulta es por un ticket, identificar su código y consultar su estado.
   5. Datos personales solo con verificación (regla 9).
   6. Responder solo sobre la empresa.
   7. Escalar cuando corresponda (regla 8).
   8. Al escalar, despedirse y dejar el motivo y un resumen.
6. **Contexto:**
   - la ficha de la persona o cliente;
   - sus leads abiertos y sus próximas citas;
   - las **últimas N conversaciones** anteriores (N = 3, configurable), con hasta M mensajes de texto cada una (M = 30). Los archivos van como descripción y transcripción.

   Si no hay historial, va vacío.
7. **Fuentes de respuesta:**
   - **Base de conocimiento:** documentos de la empresa indexados en n8n, una colección por empresa.
   - **Productos:** catálogo de SPEC 57.
   - **Últimas compras:** leads ganados de la persona y sus productos aceptados (SPEC 61 y 63), hasta que exista el módulo de ventas y facturación. Cuando llegue, esta herramienta cambia de fuente y los productos de leads pasan a ser historial.
   - **Tickets:** solo los de la persona identificada.
   - **Citas:** disponibilidad y alta en el calendario (SPEC 45-post).
8. **Escalado (handoff)** si:
   - el cliente pide un asesor;
   - hay un reclamo o un tema sensible;
   - la IA no encuentra la respuesta;
   - se llega a `MaxAiTurns` turnos sin resolver (6);
   - el flujo falla o no responde en `FlowTimeoutSeconds` (20 s).

   La conversación pasa a `ON_HOLD` con `HandoffReason`, `HandoffSummary` y `HandoffAt`, y se avisa a los coordinadores (BE-75). Cuando un agente la toma, la IA queda **en pausa** en esa conversación, y el agente puede **devolverla** a la IA.
9. **Verificación de identidad** (propuesta, ver Preguntas abiertas): la IA da datos personales (tickets, compras, citas existentes) solo si el número de WhatsApp está entre los contactos de la persona identificada. Si la identificación existe pero el número no coincide, no muestra datos y escala.
10. **Archivos:**
    - La IA puede responder audios, imágenes y documentos.
    - Todo archivo se guarda primero en el storage de JOIN (BE-74); n8n recibe un **enlace temporal firmado de JOIN**, nunca el de Meta.
    - Si la IA transcribe un audio, la transcripción se guarda en `Message.Transcript`.
11. **Agrupar mensajes seguidos:** JOIN espera `DebounceSeconds` (5) desde el último entrante antes de llamar al flujo, para que la IA no conteste varias veces.
12. **Límite de costo:** `MaxAiCallsPerConversationDay` (40). Si se supera, escala.
13. **Presentación:** la IA siempre se presenta como asistente virtual (`IntroMessage`) y responde solo sobre temas de la empresa, conforme a la política de Meta para la WhatsApp Business API, que desde enero de 2026 no admite chatbots de IA de propósito general.
14. **Todos los tiempos y límites son parametrizables.** Las sugerencias al agente humano (copiloto) quedan para una etapa posterior.

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Channel_Meta`)
1. `AiAssistantSettings` (`BaseTenantEntity`, único por empresa):
   - **Flujo y conocimiento:** `IsEnabled`, `FlowUrl` (máx. 500), `EncryptedHmacSecret` (`ISecretProtector`), `KnowledgeBaseId` (máx. 100), `IntroMessage` (máx. 500).
   - **Contexto:** `ContextConversations` (3), `ContextMessagesPerConversation` (30).
   - **Tiempos y límites:** `DebounceSeconds` (5), `MaxAiTurns` (6), `FlowTimeoutSeconds` (20), `MaxAiCallsPerConversationDay` (40).
   - **Herramientas:** `TranscribeAudio` (sí), `AllowedTools` (`jsonb`).

   Los re-avisos usan `QueueSettings` (BE-75).
2. `AiAssistantRule` (`BaseTenantEntity`): `SortOrder`, `Text` (máx. 1000), `IsActive`. Seed con las 8 reglas de la regla 5.
3. `AiKnowledgeDocument` (`BaseTenantEntity`): `FileName`, `StoragePath` + `StorageProvider`, `SizeBytes`, `IndexStatus` (`Pending`, `Indexing`, `Indexed`, `Failed`), `IndexedAt?`, `Error?`.
4. `AiInteraction` (`BaseTenantEntity`):
   - petición: `ConversationId`, `RequestedAt`, `MessageIds` (`jsonb`);
   - resultado: `LatencyMs?`, `ToolsUsed` (`jsonb`), `Outcome` (`Replied`, `HandedOff`, `Error`, `Timeout`, `Skipped`), `Error?`;
   - costo: `TokenUsage?` (`jsonb`, si n8n lo informa).
5. Las columnas de atención de la conversación y `Message.Transcript`/`AiInteractionId` ya existen (BE-74).
6. Migración `Spec80WhatsAppAiAssistant`. Usuario de servicio por empresa con rol `Agent` (SPEC 45-post), si no existe.

**B. Llamada al flujo** (Application + Infrastructure)
7. `ConversationRouter` (BE-75): con la IA encendida, una conversación nueva queda `AI_ATTENDING` con `HandledBy = Ai`. Cada entrante en una conversación `AI_ATTENDING` no pausada agenda una llamada.
8. `AiDispatcher` (`BackgroundService`): espera el debounce y arma el evento:
   - empresa: `companyId`, `channelId`, `phoneNumberId`;
   - conversación: `conversationId`, `code`, los mensajes nuevos (con el enlace temporal de cada archivo);
   - configuración: `rules`, `parameters`, `knowledgeBaseId`, `allowedTools`;
   - acceso: `toolsBaseUrl` y `accessToken` (JWT de 10 min con claims `CompanyId`, `ConversationId` y `scope = whatsapp-ai`).

   Lo firma con HMAC-SHA256 (`X-Join-Signature`) y hace `POST` a `FlowUrl`. El flujo responde 202 y trabaja con las herramientas. Si no llega `reply` ni `handoff` en `FlowTimeoutSeconds`, JOIN escala solo (regla 8).
9. Enlace temporal de archivos: `GET /api/v1/channels/meta/ai/media/{token}` (anónimo, token firmado de 10 min y un solo archivo) que entrega desde el storage.

**C. API de herramientas para n8n** (recurso `WhatsAppAiTools`; requiere el token de la regla 3 y valida que la conversación sea la del token)
10. `GET /api/v1/WhatsAppAiTools/context`: ficha, identificación (marca y si el número está verificado), leads abiertos, próximas citas y las últimas N conversaciones con sus mensajes.
11. `POST /api/v1/WhatsAppAiTools/identify-person` `{ identificationTypeId?, identificationNumber?, phone? }`: reutiliza `persons/lookup` de SPEC 45-post.
    - Si la encuentra y el número está en sus contactos, vincula la conversación (`Identified`).
    - Si la encuentra pero el número no coincide, responde `verified = false` y no vincula (regla 9).
12. `POST /api/v1/WhatsAppAiTools/register-person`: reutiliza `register-pending-person` de BE-74.
13. `GET /api/v1/WhatsAppAiTools/products?search=` y `/products/{id}`: catálogo activo de SPEC 57, con precio en la moneda de la empresa.
14. `GET /api/v1/WhatsAppAiTools/last-purchases?take=5`: leads ganados de la persona identificada y verificada, con sus productos aceptados, cantidades, valor y fecha de cierre.
15. `GET /api/v1/WhatsAppAiTools/tickets/{code}` y `/tickets?status=open`: estado, fechas y última nota pública de los tickets de la persona identificada y verificada. Un ticket de otra persona responde `TICKET_NOT_FOUND`. El ticket consultado se vincula a la conversación (`TicketId`).
16. `GET /api/v1/WhatsAppAiTools/availability` y `POST /appointments`: delegan en los handlers de SPEC 45-post (actividad para el usuario por defecto o el último agente del contacto).
17. `POST /api/v1/WhatsAppAiTools/reply` `{ text?, interactive?, transcripts?: [{ messageId, text }] }`:
    - envía por BE-75 con `SenderType = Ai` y `AiInteractionId`;
    - suma un turno;
    - guarda las transcripciones.

    Solo con la ventana abierta y la conversación `AI_ATTENDING` no pausada. Si un agente la tomó mientras tanto, responde `AI_PAUSED` y no envía.
18. `POST /api/v1/WhatsAppAiTools/handoff` `{ reason, summary, farewellText? }`: envía la despedida si viene, pasa a `ON_HOLD` y llama a `ConversationRouter` para los avisos.

**D. Endpoints de la bandeja y de configuración**
19. Recurso `WhatsAppInbox`: `POST /api/v1/WhatsAppConversations/{id}/return-to-ai` (asignado o coordinador). Pasa a `AI_ATTENDING`, quita el agente, `AiPaused = false` y agrega un mensaje de sistema. Solo con el asistente encendido y la ventana abierta.
20. Recurso `WhatsAppSettings`:
    - `GET/PUT /api/v1/WhatsAppAiAssistant`, y `POST /api/v1/WhatsAppAiAssistant/test` (`CanExecute`, manda un evento de prueba firmado y mide la respuesta);
    - `POST /rotate-secret`;
    - `GET/POST/PUT/DELETE /api/v1/WhatsAppAiAssistant/rules` y `PUT /rules/order`;
    - `GET/POST/DELETE /api/v1/WhatsAppAiAssistant/documents`: subir guarda en el storage y avisa al flujo (`POST {FlowUrl}/knowledge` firmado) para indexar. El flujo informa el resultado con `POST /api/v1/WhatsAppAiTools/knowledge-status`.
    - `GET /api/v1/WhatsAppAiAssistant/interactions` (paginado, filtros de resultado y fechas).
21. Apagar el asistente deja las conversaciones `AI_ATTENDING` en `ON_HOLD` (motivo "Asistente apagado") para que un humano las atienda.
22. `IAiAssistantState` (BE-79) informa si está encendido.

**Out of scope:**
- El flujo de n8n en sí (prompt, modelo, vector store): se entrega una guía `docs/whatsapp-ai-n8n-flow.md` con el contrato del evento y de las herramientas, y un flujo de ejemplo exportado.
- Copiloto (sugerencias de respuesta al agente humano).
- La IA en otros canales (correo, web).
- Módulo de ventas y facturación.

## Contrato del evento (resumen)

```json
{
  "eventId": "guid", "companyId": "guid", "channelId": "guid", "phoneNumberId": "118204466390127",
  "conversation": { "id": "guid", "code": "20261018-8", "identification": "Identified", "turn": 3 },
  "messages": [ { "id": "guid", "type": "audio", "text": null, "mediaUrl": "https://api…/ai/media/{token}", "sentAt": "…" } ],
  "rules": ["…"], "parameters": { "contextConversations": 3, "maxAiTurns": 6 },
  "knowledgeBaseId": "kb-distribuidora-andina", "allowedTools": ["get_context", "reply", "handoff"],
  "toolsBaseUrl": "https://api…/api/v1/WhatsAppAiTools", "accessToken": "jwt-10-min"
}
```

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `AI_ASSISTANT_NOT_CONFIGURED` | 400 | Encender sin URL, secreto o colección |
| `AI_TOKEN_INVALID` | 401 | Token de herramientas vencido, de otra conversación o con otro alcance |
| `AI_PAUSED` | 409 | `reply` o `handoff` sobre una conversación que tomó un agente |
| `AI_TOOL_NOT_ALLOWED` | 403 | Herramienta no habilitada para la empresa |
| `PERSON_NOT_VERIFIED` | 403 | Datos personales sin verificación del número (regla 9) |
| `WINDOW_CLOSED_TEMPLATE_REQUIRED` | 409 | La IA intenta escribir con la ventana cerrada |
| `AI_DAILY_LIMIT_REACHED` | 429 | Se superó `MaxAiCallsPerConversationDay` (y se escala) |
| `AI_FLOW_UNAVAILABLE` | 502 | El flujo no respondió o respondió error (y se escala) |

## Implementation plan

1. Entidades, seed de reglas, migración y usuario de servicio.
2. Configuración, reglas, documentos y prueba del flujo.
3. Token de herramientas y enlace temporal de archivos.
4. `AiDispatcher` con debounce, firma, timeout y escalado automático.
5. Herramientas: contexto, identificar y registrar, productos, últimas compras, tickets, disponibilidad y citas.
6. `reply`, `handoff`, pausa y `return-to-ai`; apagado del asistente.
7. Guía de n8n con un flujo de ejemplo.
8. Integración con un servidor de n8n simulado (WireMock o un `HttpMessageHandler` falso) y smoke con n8n real y un teléfono.

## Criterios de aceptación

- [ ] Con el asistente encendido, un número nuevo queda `AI_ATTENDING` y el flujo recibe un evento con la firma válida, el `companyId` correcto y un token de 10 min.
- [ ] Tres mensajes seguidos en 3 s generan una sola llamada al flujo.
- [ ] `context` de Rosa Jiménez devuelve sus 3 conversaciones anteriores, no la cuarta.
- [ ] Con el token de una conversación no se puede leer el contexto de otra (`AI_TOKEN_INVALID`).
- [ ] `tickets/{code}` de un ticket de otra persona responde 404; uno propio queda vinculado a la conversación.
- [ ] `identify-person` con una cédula existente desde un número que no es de esa persona responde `verified = false` y no vincula.
- [ ] Un número sin persona registrado con `register-person` aparece en `/customers/pending-persons` con su `ConversationId`, y la conversación queda `PendingConfirmation`.
- [ ] `handoff` deja `ON_HOLD` con motivo y resumen, y avisa a los coordinadores disponibles.
- [ ] Si el flujo no responde en 20 s, la conversación pasa sola a `ON_HOLD` y queda un `AiInteraction` con `Timeout`.
- [ ] Un agente que toma la conversación pausa la IA: un `reply` posterior responde `AI_PAUSED` y no se envía. `return-to-ai` la reactiva.
- [ ] Un audio transcrito queda en `Message.Transcript`, y el flujo nunca recibe una URL de Meta.
- [ ] Con el asistente apagado, una conversación nueva va a la Cola y no se llama al flujo.
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: asistente opcional por empresa, activable por el administrador** (usuario, 2026-10-19).
- **Sí: un único flujo de n8n; JOIN identifica la empresa por la App y el número que recibe** (usuario, 2026-10-19).
- **Sí: contexto de las últimas 3 conversaciones antes de responder; reglas editables** (usuario, 2026-10-19).
- **Sí: base de conocimiento de documentos indexados; respuestas limitadas a la empresa** (usuario, 2026-10-19).
- **Sí: la IA nunca inicia conversaciones** (usuario, 2026-10-19).
- **Sí: últimas compras desde los leads ganados** hasta el módulo de ventas y facturación (usuario, 2026-10-19).
- **Sí: tickets identificados por su código** (usuario, 2026-10-19).
- **Sí: la IA responde audios, imágenes y documentos, guardados en el storage de JOIN, solo la ruta** (usuario, 2026-10-19).
- **Sí: tiempos parametrizables; copiloto para después** (usuario, 2026-10-19).
- **Sí: n8n responde por JOIN y nunca habla con Meta.** Así todo queda en la conversación, con su código y la ventana respetada.
- **Sí: si el flujo falla, se escala.** Ningún cliente queda sin atender.

## Preguntas abiertas

1. **Verificación de identidad (regla 9):** ¿basta con que el número de WhatsApp esté entre los contactos de la persona, o la IA debe pedir un segundo dato (por ejemplo, fecha de nacimiento) cuando no coincide?
2. **Hospedaje de n8n:** el contexto que sale hacia n8n incluye datos personales. Se recomienda n8n en la infraestructura de la empresa (AWS) y no en un servicio compartido. ¿Dónde estará?

## Riesgos

| Riesgo | Mitigación |
|---|---|
| La IA da datos personales a quien no corresponde. | Regla 9, herramientas que filtran por la persona verificada y token limitado a la conversación. |
| Política de Meta contra chatbots de propósito general. | Reglas 4, 6 y 13: la IA solo responde sobre la empresa y se presenta como asistente. |
| Costo de la IA. | Tope diario por conversación, debounce y `AiInteractions` con el uso de tokens. |
| El flujo cae. | Timeout con escalado automático, y la prueba del flujo en la configuración. |

## Impacto en el frontend

- **FE-49** (Borrador): burbujas "Asistente IA", banner de la IA y de En espera con el resumen, tomar (pausa la IA) y "Devolver a la IA", transcripciones y ticket vinculado.
- **FE-54** (Borrador): Configuración › Asistente IA (activo, flujo, secreto, reglas, parámetros, herramientas, documentos, interacciones).
