# SPEC 77 — WhatsApp: etapas del canal, pipeline por conversación y homologación con Leads y Clientes

> **Status:** Borrador
> **Depends on:** **SPEC 74** (conversaciones), **SPEC 75** (visibilidad y tiempo real), SPEC 55 y SPEC 66 (etapas de cliente y su cambio con historial), SPEC 61 (etapas de lead y su cambio). Rama de trabajo: `spec-77-whatsapp-stages-pipeline-mapping`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que cada conversación avance por etapas propias del canal (pipeline tipo kanban) y que cada etapa pueda homologarse con una etapa de Leads o de Clientes. JOIN **sugiere** al agente mover también el lead o el cliente; nunca los cambia solo.
> **Origen:** diseño del canal WhatsApp (2026-10-18). Propuesta visual: `join_frontb/SpecPropuestaWhatsApp/PropuestaWhatsApp_JOIN.html` (Pipeline, panel de la bandeja, Configuración › Etapas y homologación). Front: FE-52.

---

## Reglas de negocio (decisiones del usuario, 2026-10-18)

1. **Etapas propias del canal, por empresa** (`Channel_Meta.Stages`), con código, nombre, color, orden y tipo (`Initial`, `Open`, `Final`). Debe haber exactamente una etapa `Initial` activa por empresa.
2. **Pipeline por conversación:** la etapa vive en la conversación (`Conversation.StageId`), no en la persona. Toda conversación nueva arranca en la etapa `Initial`.
3. **Homologación:** cada etapa puede apuntar a una `LeadStage` (SPEC 61) y/o a una `CustomerStage` (SPEC 55) de la empresa.
4. **Solo se sugiere:**
   - Al mover la conversación a una etapa homologada, la respuesta trae la sugerencia (lead o cliente vinculado, etapa actual y etapa sugerida).
   - El agente la aplica con los endpoints existentes de Leads (SPEC 61) o de Clientes (SPEC 66).
   - Si el lead o el cliente ya está en esa etapa, o no hay vínculo, no hay sugerencia.
5. **Seed por empresa** (al crear el canal y en el seed):

   | Etapa | Homologa con |
   |---|---|
   | Nuevo (`Initial`) | Leads · Nuevo |
   | En atención | Leads · Contactado |
   | Cotizando | Leads · Propuesta |
   | Negociando | Leads · Negociación |
   | Venta cerrada (`Final`) | Leads · Ganado; Clientes · Cliente |
   | Soporte | Clientes · Recurrente |
   | Finalizada (`Final`) | — |

   La homologación del seed se busca por **código** de etapa de lead y de cliente. Si la empresa no tiene esa etapa, la homologación queda vacía.

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Channel_Meta`)
1. `Stage` (`BaseTenantEntity`, único `(CompanyId, Code)` filtrado; único filtrado `(CompanyId) WHERE kind = Initial AND isactive AND gcrecord = 0`): `Code` (máx. 30), `Name` (máx. 60), `Color` (máx. 20), `SortOrder`, `Kind`, `IsActive`.
2. `StageMapping` (1:1 con la etapa): `StageId`, `LeadStageId?`, `CustomerStageId?`, con FK a los catálogos de SPEC 61 y 55 de la misma empresa.
3. `Conversation.StageId` (BE-74) pasa a tener FK. Las conversaciones existentes quedan en la etapa `Initial`.
4. `ConversationStageHistory`: `ConversationId`, `FromStageId?`, `ToStageId`, `ChangedByUserId?`, `ChangedAt`, `SuggestionApplied?` (lo informa el frontend al aplicar).
5. Migración `Spec77WhatsAppStages`, con seed, menú y permisos.

**B. Endpoints**
6. Recurso `WhatsAppSettings`:
   - `GET /api/v1/WhatsAppStages`, `POST`, `PUT /{id}`, `PUT /order` y `DELETE /{id}` (soft delete; no se borra una etapa con conversaciones abiertas ni la `Initial`);
   - `PUT /api/v1/WhatsAppStages/{id}/mapping` `{ leadStageId?, customerStageId? }`.
7. Recurso `WhatsAppInbox`: `PUT /api/v1/WhatsAppConversations/{id}/stage` `{ stageId }`. Registra el historial, publica `ConversationChanged` y devuelve `{ suggestion?: { target: Lead|Customer, id, code, currentStage, suggestedStageId, suggestedStage } }`.
8. Recurso `WhatsAppPipeline`: `GET /api/v1/WhatsAppPipeline`, con las columnas por etapa y sus tarjetas (código, persona, estado, agente, vista previa, lead). Respeta la visibilidad de BE-75. Filtros: agente y "solo abiertas" (por defecto).
9. `POST /api/v1/WhatsAppConversations/{id}/stage-history/{historyId}/suggestion-applied`: el frontend marca que el agente aplicó la sugerencia (para medir su uso).
10. `SystemOption` `WhatsAppPipeline` (`/whatsapp/pipeline`, módulo `WhatsApp`) con los mismos roles que la bandeja.

**Out of scope:**
- Mover el lead o el cliente automáticamente.
- Un pipeline distinto por agente o por grupo.
- Reglas automáticas de cambio de etapa (podrían llegar con automatizaciones avanzadas).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `WHATSAPP_STAGE_NOT_FOUND` | 404 | De otra empresa o inexistente |
| `WHATSAPP_STAGE_CODE_EXISTS` | 409 | Código repetido |
| `WHATSAPP_STAGE_INITIAL_REQUIRED` | 409 | Se intenta dejar la empresa sin etapa `Initial` activa |
| `WHATSAPP_STAGE_IN_USE` | 409 | Borrar una etapa con conversaciones abiertas |
| `INVALID_STAGE_MAPPING` | 400 | La etapa de lead o de cliente es de otra empresa o está inactiva |

## Implementation plan

1. Entidades, seed y migración.
2. CRUD, orden y homologación.
3. Cambio de etapa con historial y sugerencia.
4. Pipeline con visibilidad.
5. Integración y `EXPLAIN`.

## Criterios de aceptación

- [ ] Una conversación nueva queda en la etapa `Initial`.
- [ ] Mover a "Cotizando" una conversación con lead en "Contactado" devuelve la sugerencia de pasar el lead a "Propuesta" y **no** cambia el lead.
- [ ] Sin lead ni cliente vinculado, mover de etapa no devuelve sugerencia.
- [ ] El pipeline de un agente sin "Ve todas" muestra solo sus conversaciones.
- [ ] No se puede borrar la etapa `Initial` ni una con conversaciones abiertas.
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: etapas propias del canal por conversación, homologadas con Leads y Clientes** (usuario, 2026-10-18).
- **Sí: la homologación solo se sugiere al agente** (usuario, 2026-10-18).
- **Sí: el seed homologa por código,** así no depende de los nombres que cada empresa haya puesto a sus etapas.

## Impacto en el frontend

- **FE-52** (Borrador): pipeline kanban, etapa en el panel de la bandeja con la sugerencia, y configuración de etapas y homologación.
