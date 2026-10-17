# SPEC 65 — Leads con calendario y personas pendientes: próxima acción agendada y convertir una persona pendiente en lead

> **Status:** Aprobado
> **Depends on:** SPEC 44-post (calendario: eventos y actividades por empresa), SPEC 45-post (personas pendientes que llegan por canal), SPEC 48-post (bloqueo por módulo activo), **SPEC 59** (tipos de contacto, origen `PENDING`), **SPEC 61** (leads y contactos). Rama de trabajo: `spec-65-leads-calendar-and-pending-persons`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Que la próxima acción de un lead (una reunión, una visita, una demostración) sea un evento real en el calendario del dueño, enlazado al lead, y que una persona que llegó por un canal (WhatsApp, correo) se pueda convertir en lead desde la cola de personas pendientes.
> **Origen:** diseño del módulo de clientes (2026-10-16: "Agendar próxima acción" en el modal de contacto y "Convertir en lead" en personas pendientes).

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **Próxima acción = evento de calendario.** Al registrar un contacto se puede agendar la siguiente acción: se crea un `CalendarEvent` en el calendario del dueño del lead, con la actividad que corresponde al tipo de contacto, enlazado al lead.
2. **Requiere el módulo Calendario activo** para la empresa (SPEC 48-post). Sin él, la opción no se ofrece y la API responde 403 `MODULE_NOT_ENABLED` (mismo código y formato que el filtro de SPEC 48-post). Como el endpoint es del recurso `Leads` (módulo Clientes), el chequeo lo hace el handler con el mismo servicio de acceso por módulo (`ModuleAccessResult`), no el filtro.
3. **Convertir una persona pendiente en lead** crea un lead abierto para esa persona con origen `PENDING` y resuelve la persona pendiente.
4. **Vincular no activa** (SPEC 55 regla 9): crear el lead desde una persona pendiente no crea ni activa clientes.

## Scope

**In:**

**A. Calendario**
1. `Calendars.CalendarEvents` suma `LeadId?` (FK opcional, como `CustomerId` y `TicketId`). Índice `(CompanyId, LeadId)` filtrado por `"leadid" IS NOT NULL AND "gcrecord" = 0`.
2. `Customer.ContactTypeCompanies` (SPEC 59) suma `CalendarActivityCompanyId?`: la actividad del calendario que se usa al agendar con ese tipo. El seed la completa para los tipos con `SchedulesCalendarEvent` (`ONLINE_MEETING`, `VISIT`, `DEMO`) cuando la empresa tiene el calendario.
3. `LeadInteractions` suma `NextActionEventId?`. `POST /api/v1/Leads/{id}/interactions` acepta `nextAction { contactTypeCompanyId, startUtc, endUtc, title?, location?, videoCallUrl? }`: crea el evento con el servicio de calendario existente (mismas validaciones de SPEC 44-post: horario laboral, solapamiento según la actividad), en el calendario del dueño, con `LeadId` y, si el lead tiene cliente, `CustomerId`; `OriginType = User` y `OriginUserId` = quien registra el contacto.
4. La línea de tiempo del lead (SPEC 61) incluye los eventos con `LeadId` (pendientes y pasados) con su estado de calendario.
5. `GET /api/v1/Leads/{id}/events` → eventos del lead.

**B. Personas pendientes**
6. `POST /api/v1/PersonPendingConfirmations/{id}/convert-to-lead` `{ topic, relationshipTypeId, salesTeamId?, notes? }` (recurso `PersonPendingConfirmations`, `CanUpdate`): pendiente en estado `Pending`; crea el lead con `CreateLeadCommand` (SPEC 61: dueño = quien convierte, origen `PENDING`, reglas de duplicados), y resuelve la pendiente con estado nuevo `ConvertedToLead = 5`, `ResolvedByUserId`, `ResolvedUtc` y `LeadId` (columna nueva `LeadId?` en `PersonPendingConfirmations`).
7. `PersonPendingConfirmationStatus` suma `ConvertedToLead = 5`; el filtro de la cola lo incluye.

**Out of scope:**

- Que completar el evento registre solo un contacto en el lead (se registra a mano).
- Sincronizar estos eventos con Google Calendar (sigue lo de SPEC 46-post).
- Mover `PersonPendingConfirmations` al esquema `Customer` (pendiente de SPEC 55).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `MODULE_NOT_ENABLED` | 403 | Agendar sin el módulo Calendario activo (SPEC 48-post) |
| `CONTACT_TYPE_NOT_SCHEDULABLE` | 400 | El tipo de contacto no agenda eventos o no tiene actividad de calendario |
| (errores de calendario) | 400 | Las validaciones existentes de SPEC 44-post (horario, solapamiento, calendario del dueño) |
| `PERSON_PENDING_NOT_PENDING` | 409 | Convertir una pendiente ya resuelta |
| (errores de SPEC 61) | — | Los del alta de lead (duplicados, vinculación, grupo) |

## Criterios de aceptación

1. Registrar un contacto con próxima acción crea el evento en el calendario del dueño, con `LeadId`, y aparece en la línea de tiempo y en `GET /Leads/{id}/events`.
2. Con el módulo Calendario inactivo y `Modules:EnforceCompanyModules = true`, agendar responde 403 `MODULE_NOT_ENABLED`; el contacto sin próxima acción se registra igual.
3. Agendar con un tipo sin actividad responde 400.
4. Convertir una persona pendiente crea un lead abierto con origen `PENDING`, deja la pendiente en `ConvertedToLead` con `LeadId`, y no crea cliente.
5. Convertir dos veces la misma pendiente responde 409.
6. Unit tests de los comandos; integración para 1–5 (con `ModuleEnforcementPostgreSqlWebApplicationFactory` para 2).

## Decisiones

- **Reutilizar el calendario existente** en vez de un campo "próxima acción" en el lead: el evento ya tiene horario, estado, recordatorios futuros y sincronización.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Eventos huérfanos si el lead se cierra. | Cerrar el lead no borra eventos; la línea de tiempo los muestra con su estado y el dueño los cancela desde el calendario. |

## Impacto en el frontend

- Specs FE (todas en Borrador, `join_frontb/specs`): **FE-37 (próxima acción) y FE-40 (convertir en lead)**. Base común del módulo: FE-29.
- Opción "Agendar próxima acción" en el modal de contacto; eventos en la línea de tiempo; acción "Convertir en lead" en Clientes → Personas pendientes.
