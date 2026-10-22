# SPEC 79 — WhatsApp: automatizaciones básicas (bienvenida, fuera de horario, asignación automática, cierre por inactividad y aviso de ventana)

> **Status:** Aprobado
> **Depends on:** **SPEC 74** (conversaciones), **SPEC 75** (agentes, Cola, envío, notificaciones), **SPEC 76** (plantillas), SPEC 44-post (horario y días no laborables de la empresa). Rama de trabajo: `spec-79-whatsapp-basic-automations`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Cinco reglas fijas que se encienden, se apagan y se configuran, para que el canal funcione desde el primer día. Conviven con el asistente IA (BE-80): con la IA encendida, la IA da la bienvenida y el aviso fuera de horario.
> **Origen:** diseño del canal WhatsApp (2026-10-18: "lo más práctico y rápido para poner a funcionar el canal y luego ajustar con IA"; 2026-10-19: convivencia con la IA). Propuesta visual: `join_frontb/SpecPropuestaWhatsApp/PropuestaWhatsApp_JOIN.html` (Automatizaciones). Front: FE-54.

---

## Reglas de negocio

1. **Cinco automatizaciones por empresa**, cada una con `IsEnabled` y su configuración:

   | Tipo | Qué hace | Configuración |
   |---|---|---|
   | `Welcome` (Bienvenida) | Al abrirse una conversación desde el cliente, envía un saludo. | Plantilla aprobada o texto libre; "en cada conversación nueva" o "solo contactos nuevos". |
   | `OutOfHours` (Fuera de horario) | Fuera del horario laboral o en un día no laborable, responde con un aviso, una vez por conversación. | Texto. El horario sale de `CalendarCompany` (`IsOpen24Hours`, `BusinessHoursStart`/`End`, `TimeZone`) y de `CalendarCompanyNonWorkingDays` (SPEC 44-post). |
   | `AutoAssign` (Asignación automática) | Asigna la conversación nueva a un agente disponible, con cupo y con `ReceivesAutoAssignment`. | Rotativa (round robin) o menos conversaciones abiertas; preferir al último agente del contacto (sí/no); las sin identificar se asignan o quedan en la Cola; con la IA encendida: solo escaladas sin asignar tras N min, o nunca. |
   | `InactivityClose` (Cierre por inactividad) | Cierra la conversación por expiración de Meta (24 h) o tras N horas sin mensajes. | Criterio y mensaje de cierre opcional (texto, solo con la ventana abierta). |
   | `WindowReminder` (Aviso de ventana por vencer) | Avisa al agente asignado cuando faltan X minutos para que cierre la ventana y el último mensaje es del cliente. | 60 o 30 minutos. |

2. **Convivencia con el asistente IA** (BE-80, decisión del usuario 2026-10-19):
   - Con la IA encendida, `Welcome` y `OutOfHours` no se ejecutan: la IA se presenta y atiende a cualquier hora. Lo que escala fuera de horario queda `ON_HOLD` hasta que un coordinador lo tome.
   - `AutoAssign` solo actúa sobre las escaladas (`ON_HOLD`) que ningún coordinador asignó en el tiempo configurado.
   - Con la IA apagada funcionan como en la tabla.
3. **Los mensajes automáticos** salen por el mismo envío de BE-75, con `SenderType = Automation`, y respetan la ventana de 24 h (texto libre solo con la ventana abierta).
4. **Cada ejecución queda en el log** (`AutomationLogs`): automatización, conversación, fecha, resultado (`Sent`, `Assigned`, `Closed`, `Notified`, `Skipped`, `Failed`) y detalle.
5. **Orden** al llegar una conversación nueva sin IA: `OutOfHours` → `Welcome` → `AutoAssign`. Si `AutoAssign` no encuentra agente, la conversación queda en la Cola (BE-75).

## Scope

**In:**
1. `Automation` (`BaseTenantEntity`, único `(CompanyId, Type)` filtrado): `Type`, `IsEnabled`, `Config` (`jsonb`, validado por tipo), `TemplateId?`. Se siembran las cinco por empresa con valores iniciales: todas encendidas menos `WindowReminder`.
2. `AutomationLog` (`BaseTenantEntity`): `AutomationId`, `ConversationId?`, `ExecutedAt`, `Result`, `Detail?` (máx. 500). Índice `(CompanyId, AutomationId, ExecutedAt)`.
3. `Agent.LastAssignedAt` (BE-75) para el round robin.
4. `AutomationEngine` (Application):
   - se llama desde `ConversationRouter` (BE-75) para `Welcome`, `OutOfHours` y `AutoAssign`;
   - `WhatsAppAutomationMonitor` (`BackgroundService`, cada minuto) se encarga de `InactivityClose`, `WindowReminder` y de `AutoAssign` sobre escaladas.

   La asignación usa el mismo bloqueo que `assign` (BE-75).
5. Migración `Spec79WhatsAppAutomations`, con seed, menú y permisos.
6. Recurso `WhatsAppAutomations`:
   - `GET /api/v1/WhatsAppAutomations`, cada una con la fecha de su última ejecución y las ejecuciones del mes;
   - `PUT /api/v1/WhatsAppAutomations/{type}` `{ isEnabled, config, templateId? }`;
   - `GET /api/v1/WhatsAppAutomations/{type}/logs` (paginado).
7. `SystemOption` `WhatsAppAutomations` (`/whatsapp/automations`, módulo `WhatsApp`). Permisos: Admin, SuperAdminCompany y Coordinador con todo; Manager con lectura.

**Out of scope:**
- Constructor visual de flujos, palabras clave y respuestas por intención.
- Crear leads o tickets automáticamente.
- Respuestas con IA (BE-80).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `AUTOMATION_CONFIG_INVALID` | 400 | La configuración no corresponde al tipo |
| `TEMPLATE_NOT_APPROVED` | 409 | `Welcome` con una plantilla que no está aprobada |
| `CALENDAR_COMPANY_NOT_CONFIGURED` | 400 | Se enciende `OutOfHours` sin parametrización del calendario de la empresa |

## Implementation plan

1. Entidades, seed y migración.
2. `AutomationEngine` con las cinco reglas y su convivencia con la IA (con un `IAiAssistantState` que BE-80 implementa; aquí siempre "apagada").
3. Monitor de inactividad, ventana y escaladas.
4. Endpoints y logs.
5. Integración con reloj simulado (`TimeProvider`).

## Criterios de aceptación

- [ ] Con `Welcome` encendida y la IA apagada, una conversación nueva recibe la plantilla de bienvenida con `SenderType = Automation`.
- [ ] Un mensaje un domingo no laborable recibe una sola vez el aviso de fuera de horario.
- [ ] `AutoAssign` rotativo reparte tres conversaciones entre dos agentes disponibles alternando, y no asigna a un agente sin cupo ni a uno no disponible.
- [ ] Con la IA encendida no se envían ni la bienvenida ni el aviso fuera de horario, y una escalada sin asignar a los 15 min se asigna automáticamente si la regla lo indica.
- [ ] Una conversación sin mensajes por 12 h se cierra con el criterio "12 h sin mensajes".
- [ ] Cada ejecución queda en `AutomationLogs`.
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: reglas fijas para arrancar** (usuario, 2026-10-18: opción recomendada).
- **Sí: con la IA encendida, la bienvenida y el aviso fuera de horario los da la IA** (usuario, 2026-10-19).
- **Sí: el horario sale del calendario de la empresa**, para no mantener dos horarios.

## Impacto en el frontend

- **FE-54** (Borrador): pantalla de automatizaciones (y la del asistente IA).
