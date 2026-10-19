# SPEC 78 — WhatsApp: índice de conversaciones por persona o cliente, con filtros y exportación

> **Status:** Borrador
> **Depends on:** **SPEC 74** (conversaciones y mensajes), **SPEC 75** (visibilidad), SPEC 77 (etapas, para el filtro), SPEC 14 (exportación a Excel y PDF del frontend). Rama de trabajo: `spec-78-whatsapp-conversation-index`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Un índice general de conversaciones agrupado por persona o cliente. Cada fila despliega los códigos de sus conversaciones (`20261015-7`, `20260810-4`…), y cada código abre la conversación completa. Se filtra por rango de fechas, número, nombre o identificación, tipo, estado, agente, etapa y quién atendió.
> **Origen:** pedido del usuario (2026-10-18). Ejemplo: "Livingstone Cano (cli) Ced 0012306 · 06" despliega sus códigos y cada código muestra el detalle. Propuesta visual: `join_frontb/SpecPropuestaWhatsApp/PropuestaWhatsApp_JOIN.html` (Conversaciones). Front: FE-53.

---

## Reglas de negocio

1. **Agrupado por persona:**
   - Una fila por persona (con su cliente, si tiene) que tenga al menos una conversación dentro de los filtros.
   - Muestra el nombre, `(cli)` o `(per)`, la identificación, el número principal, la fecha y el código de la última conversación, y la cantidad.
   - John Doe agrupa los números sin identificar, y cada conversación muestra su número.
2. **Detalle por código:**
   - Al desplegar una fila se listan sus conversaciones, de la más reciente a la más antigua.
   - Cada una muestra el código, la fecha, el `MetaConversationId`, el estado, quién atendió (IA, IA → agente o agente), el agente, la etapa, la cantidad de mensajes y la de archivos.
3. **Filtros** (todos opcionales, combinables):
   - rango de fechas (por `OpenedAt`, en la zona de la empresa);
   - número de contacto (solo dígitos, contiene);
   - nombre, nombre comercial o identificación (`ILIKE`);
   - tipo: clientes, personas, sin identificar (John Doe) o persona pendiente de confirmar;
   - estado, agente, etapa y atendida por.
4. **Visibilidad:**
   - Un agente sin "Ve todas" solo ve las personas y conversaciones que tuvo asignadas alguna vez (BE-75).
   - Un coordinador ve todas.
5. **Transcripción:** abrir un código devuelve la conversación completa en solo lectura, aunque esté cerrada, con sus archivos descargables y las transcripciones de audio.
6. **Exportar** a Excel y PDF lo filtrado, con el mismo permiso `CanExport`.

## Scope

**In:**
1. Recurso `WhatsAppConversations`:
   - `GET /api/v1/WhatsAppConversations/index`, paginado por persona. Cada fila trae `personId`, `customerId?`, `displayName`, `isCustomer`, `identification`, `primaryPhone`, `lastConversationCode`, `lastConversationAt`, `conversationCount` y la marca `isGeneric` (John Doe) o `isPendingConfirmation`.
   - `GET /api/v1/WhatsAppConversations/index/{personId}/conversations`, con los mismos filtros: la lista de la regla 2.
2. El detalle completo usa `GET /WhatsAppConversations/{id}` y `/messages` (BE-74). Una conversación cerrada que el agente tuvo asignada sigue siendo visible para él.
3. `GET /api/v1/WhatsAppConversations/index/export?format=xlsx|pdf` (`[RequirePermission(PermissionFlags.CanExport)]`): una fila por conversación con la persona, el código, la fecha, el número, el estado, quién atendió, el agente, la etapa, los mensajes y los archivos. Máximo 10 000 filas (`EXPORT_TOO_LARGE`).
4. Una sola consulta Dapper con CTE: conversaciones filtradas → agregadas por persona → página. Índices de BE-74 `(CompanyId, PersonId, OpenedAt)` y `(CompanyId, StatusId, LastMessageAt)`; se agrega `(CompanyId, OpenedAt)` si el `EXPLAIN` lo pide.
5. Tabla `ConversationAgentAssignments` (`ConversationId`, `AgentId`, `AssignedAt`, `UnassignedAt?`), que llenan `take`, `assign` y la reasignación (BE-75). Resuelve "tuvo asignada alguna vez" sin recorrer los mensajes. Migración `Spec78WhatsAppConversationIndex`, que también llena la tabla desde los datos existentes.
6. `SystemOption` `WhatsAppConversations` (`/whatsapp/conversations`, módulo `WhatsApp`, `CanExport` y `CanDownload`) con los mismos roles que la bandeja.

**Out of scope:**
- Búsqueda de texto dentro de los mensajes (requiere índice de texto completo; otra spec si se pide).
- Reportes de tiempos de respuesta o de volumen por agente.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_DATE_RANGE` | 400 | `from` mayor que `to`, o un rango de más de 3 años |
| `EXPORT_TOO_LARGE` | 400 | Más de 10 000 conversaciones en la exportación |

## Implementation plan

1. `ConversationAgentAssignments`: tabla, migración con carga inicial y escritura desde BE-75.
2. Consulta del índice y del detalle por persona, con visibilidad; `EXPLAIN`.
3. Exportación.
4. Integración con datos de varias personas, John Doe y una persona pendiente.

## Criterios de aceptación

- [ ] Filtrar por "Livingstone" muestra una fila con su cantidad de conversaciones; desplegarla lista sus códigos del más reciente al más antiguo.
- [ ] Filtrar por número con espacios o guiones encuentra la conversación por dígitos.
- [ ] El tipo "Sin identificar" muestra solo John Doe, con el número de cada conversación.
- [ ] Un agente sin "Ve todas" no ve personas cuyas conversaciones nunca tuvo asignadas, pero sí sus conversaciones cerradas.
- [ ] El filtro "Atendida por: Solo IA" excluye las escaladas.
- [ ] La exportación respeta los filtros y la visibilidad.
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: índice agrupado por persona o cliente, con códigos desplegables** (usuario, 2026-10-18).
- **Sí: filtros de fecha, número y nombre** (usuario, 2026-10-18), más los que sugiere la propuesta: tipo, estado, agente, etapa y atendida por (usuario, 2026-10-18: "agrega los filtros sugeridos").
- **Sí: tabla de asignaciones históricas** en lugar de deducirlas de los mensajes, para que la visibilidad sea una consulta indexada.

## Impacto en el frontend

- **FE-53** (Borrador): pantalla Conversaciones con filtros, filas desplegables, transcripción y exportación.
