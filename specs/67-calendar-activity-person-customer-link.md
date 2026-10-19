# SPEC 67 — Calendario: "Relacionado con" de la actividad unificado en Persona/Cliente

> **Status:** Aprobado
> **Depends on:** SPEC 44-post (actividades: `RequesterPersonId` y `CustomerId`), SPEC 45-post (ingreso por canales), SPEC 54 (`GET /Persons/lookup`), SPEC 55 (un cliente por persona y empresa), SPEC 65 (próxima acción del lead en el calendario). Rama de trabajo: `spec-67-calendar-activity-person-customer-link`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que la persona y el cliente de una actividad se elijan con un solo buscador "Persona/Cliente", como en tickets (FE-27), y que el backend mantenga siempre coherentes `RequesterPersonId` y `CustomerId`.
> **Revisión de implementación (2026-10-19):** la tabla es `calendar.events` (no `calendar.calendarevents`); se agregó el `Designer.cs` que faltaba a la migración y un test de integración que verifica que se aplica. Ajuste editorial, mantiene el estado.
> **Origen:** pedido del usuario (2026-10-19): en "Nueva actividad", "Relacionado con" tiene dos buscadores separados, "Persona (solicitante)" y "Cliente"; debe ser uno solo, como en el ticket. Se divide con SPEC 68 (índice de actividades) y SPEC 69 (comentarios y documentos). Front: FE-42.

---

## Reglas de negocio (decisión del usuario, 2026-10-19)

1. **Un solo vínculo lógico, dos columnas.** La actividad sigue guardando `RequesterPersonId` y `CustomerId` (BE-44-post); el usuario elige una **persona** con `GET /Persons/lookup` (BE-54) y el backend completa el cliente.
2. **Persona → cliente.** Si llega `RequesterPersonId` sin `CustomerId`, el backend pone en `CustomerId` el cliente de esa persona en la empresa (BE-55: hay uno como máximo), **activo o inactivo**. Si la persona no es cliente, `CustomerId` queda nulo.
3. **Cliente → persona.** Si llega solo `CustomerId` (agente de canales SPEC 45-post, próxima acción del lead SPEC 65, clientes viejos de la API), el backend pone en `RequesterPersonId` la persona de ese cliente.
4. **Incoherencia = error.** Si llegan ambos y el cliente no es de esa persona: `400 CALENDAR_PERSON_CUSTOMER_MISMATCH`.
5. **Mismo comportamiento en todos los caminos de escritura**: `CalendarEvents`, `CalendarPanel`, `CalendarChannelIntake` y `LeadNextActionScheduler`, todos pasan por `CalendarEventCommandProcessor`.
6. Quitar la persona (enviar ambos nulos) quita también el cliente.
7. **Datos existentes:** la migración completa las filas que tienen solo uno de los dos campos (reglas 2 y 3). No borra ni cambia vínculos ya coherentes.

## Scope

**In:**

**A. Escritura**
1. `CalendarEventCommandProcessor.ValidateLinksAsync` resuelve el par persona/cliente con las reglas 2–4, después de validar que cada id existe en la empresa (`CALENDAR_INVALID_LINK`, sin cambios).
2. `CalendarErrorCodes.PersonCustomerMismatch = "CALENDAR_PERSON_CUSTOMER_MISMATCH"` (400).

**B. Lectura**
3. `CalendarEventDto` suma `RequesterPersonName?`, `CustomerCode?` e `IsCustomerActive?` (nulos sin vínculo), para que el front muestre el vínculo elegido como en FE-27 sin llamadas extra. Aplica al detalle de `CalendarEvents` y `CalendarPanel`.

**C. Datos**
4. Migración `Spec67CalendarEventPersonCustomerLink`: solo SQL de datos (reglas 2 y 3 sobre `calendar.events`, incluidas las filas borradas lógicamente; la regla 2 solo toma clientes no borrados, `gcrecord = 0`). Sin cambios de esquema, pero lleva su `Designer.cs` con `[Migration]` para que EF Core la descubra y la aplique al arrancar.

**Out of scope:**
- Buscador de ticket y de proyecto de "Relacionado con": siguen como están (FE-22).
- Crear una persona nueva desde el buscador.
- Cambiar el vínculo de actividades ya cerradas por el usuario: la migración solo completa el campo vacío.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `CALENDAR_INVALID_LINK` | 400 | La persona o el cliente no existen en la empresa (sin cambios). |
| `CALENDAR_PERSON_CUSTOMER_MISMATCH` | 400 | Llegan persona y cliente y el cliente no es de esa persona. |

## Plan de implementación

1. Código de error y resolución del par persona/cliente en `CalendarEventCommandProcessor`; unit tests de las reglas 2–4 y 6.
2. Campos nuevos de `CalendarEventDto` en la consulta del detalle (`CalendarQueryReader`); unit tests de la proyección.
3. Migración de datos `Spec67CalendarEventPersonCustomerLink`.
4. Integration tests y `EXPLAIN` del SQL nuevo.

## Criterios de aceptación

1. Crear una actividad con solo `requesterPersonId` de una persona que es cliente guarda también su `customerId`; con una persona que no es cliente, `customerId` queda nulo.
2. Crear con solo `customerId` guarda también la persona del cliente (también por `CalendarChannelIntake`).
3. Persona y cliente que no se corresponden responden `400 CALENDAR_PERSON_CUSTOMER_MISMATCH`.
4. Un cliente inactivo se vincula igual (regla 2).
5. `GET /CalendarEvents/{id}` devuelve `requesterPersonName`, `customerCode` e `isCustomerActive`.
6. Tras la migración, ninguna actividad tiene `CustomerId` sin `RequesterPersonId`, ni una persona-cliente sin `CustomerId`.
7. Unit tests de las reglas y de la proyección; integración para 1–6; `EXPLAIN` del SQL nuevo.

## Decisiones

- **Se conservan las dos columnas** en vez de dejar solo la persona: los reportes, el agente de canales y SPEC 65 ya usan `CustomerId`. La coherencia la garantiza el backend (decisión del usuario: "Persona + su cliente").
- **El cliente inactivo se vincula:** un cliente inactivo sigue siendo cliente (BE-55, FE-27).
- **Nombres en el DTO** en vez de pedirlos aparte: el buscador unificado necesita mostrar "(cli)/(cli inactivo)/(per)" al editar, y el detalle ya los muestra.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Un cliente viejo de la API manda persona y cliente distintos a propósito. | Responde 400 con el código; el front ya no manda el cliente (FE-42). |
| La migración toca muchas filas. | Un `UPDATE … FROM` por regla, sobre columnas con índice de FK. |

## Impacto en el frontend

- **FE-42** (Borrador): un solo buscador "Persona/Cliente" en "Relacionado con" del formulario de actividad (FE-22), con el componente de FE-27.
