# SPEC 53 — Tickets: mis capacidades, usuarios asignables e inactividad en los valores por defecto

> **Status:** Borrador
> **Depends on:** SPEC 34 (roster `TicketUserCompany`), SPEC 35 (acciones de ciclo de vida y `TicketUserCompanyCapabilityResolver`), SPEC 37 (SLA e inactividad), SPEC 43-post (permisos por ruta). Rama de trabajo: `spec-53-ticket-capabilities-and-inactivity`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`.
> **Date:** 2026-10-13
> **Objective:** Darle al frontend tres datos que hoy no puede obtener sin permisos de administración: las capacidades de ticket del usuario autenticado, la lista de usuarios a los que se puede asignar un ticket y el máximo de días sin actividad de la empresa.
> **Origen:** fase B de FE-18 (detalle: "Finalizar", "Reasignar", hueco de inactividad), FE-17 ("Asignar a" del alta) y brecha 3 de FE-15. SPEC 34 ya dejaba `GET /TicketUserCompanies/me` como pendiente "si SPEC 35 lo necesita"; SPEC 35 descartó `GET /Tickets/my-capabilities` y remitió a esa ruta.

---

## Contexto

- `FinishTicket` exige `CanFinishTicket` o `IsSuperAdminTicket` (`TICKET_FINISH_FORBIDDEN`); `ReassignTicket` exige `IsSuperAdminTicket` en el actor (`TICKET_REASSIGN_FORBIDDEN`) y `CanResolveTicket` en el destino; `CreateTicket` exige `CanResolveTicket` en el asignado inicial (`INVALID_ASSIGNED_USER_NOT_RESOLVER`). Todas usan `TicketUserCompanyCapabilityResolver`.
- El frontend no tiene cómo saber esas capacidades: `GET /TicketUserCompanies` exige permiso sobre `TicketUserCompanies` (la pantalla de administración del equipo), que un agente normalmente no tiene. Mostrar "Finalizar" o "Reasignar" a todos y esperar el 403 da una mala experiencia; listar como asignables a usuarios que no son resolvers hace fallar el alta.
- `TicketCompanyDefault.MaxDayTicketInactivity` existe en la entidad y lo usa `TicketSlaCalculator` (SPEC 37), pero ningún DTO de `TicketCompanyDefaults` lo expone ni lo recibe: no se puede configurar desde la web ni mostrar "Supera el máximo de {n} días".

---

## Scope

**In:**

- **A. `GET /api/v1/TicketUserCompanies/me`** en `TicketUserCompaniesController`, con `[PermissionResource("Tickets")]` a nivel de acción (mismo patrón que `GET /CalendarUserConfigurations/me` con `Calendar`): basta con poder leer tickets. Sin parámetros; usa el usuario y la empresa del token.
  - Responde `Response<TicketMyCapabilitiesDto>` con los flags que devuelve `TicketUserCompanyCapabilityResolver.ResolveAsync` (única fuente de verdad), más `IsMember`.
  - Un usuario que no está en el roster recibe **200** con todo en `false` (no 404): el frontend solo oculta acciones.
- **B. `GET /api/v1/TicketUserCompanies/assignable`** en el mismo controlador, también con `[PermissionResource("Tickets")]`: los miembros activos (`GcRecord = 0`) del roster de la empresa activa con `CanResolveTicket = true`, ordenados por nombre. Es el mismo criterio que validan `CreateTicket` (asignado inicial) y `ReassignTicket` (destino).
  - Responde `Response<IReadOnlyList<TicketAssignableUserDto>>`. Sin paginación (un roster por empresa es chico); si crece, se pagina en otra spec.
- **C. `MaxDayTicketInactivity` en los DTO de `TicketCompanyDefaults`:** se agrega a `TicketCompanyDefaultDto` (lectura), `CreateTicketCompanyDefaultDto` y `UpdateTicketCompanyDefaultDto` (escritura), con sus comandos, mapeos y validadores. `null` = nunca se marca inactivo (comportamiento actual de `TicketSlaCalculator`).

**Out of scope:**

- Capacidades de otros usuarios o de otras empresas (para eso está `GET /TicketUserCompanies`, con su permiso).
- Cambiar las reglas de `FinishTicket`, `ReassignTicket` o `CreateTicket`.
- Auto-asignación de un agente sin `IsSuperAdminTicket` (descartada en SPEC 35).
- Aviso automático por inactividad (recordatorios, SPEC 44 pendiente).
- Tiempo consumido en las notas: `TicketLog.ConsumedTime` es una copia del acumulado del ticket (SPEC 35) y el tiempo se cambia editando el ticket. No se toca.

---

## Diseño

### DTOs

```csharp
// src/2.Application.DTO/Messaging/TicketUserCompanies/TicketMyCapabilitiesDto.cs
public record TicketMyCapabilitiesDto
{
    public bool IsMember { get; init; }            // hay fila activa en el roster
    public bool IsSuperAdminTicket { get; init; }  // puede reasignar (y finalizar)
    public bool CanFinishTicket { get; init; }
    public bool CanResolveTicket { get; init; }    // puede recibir asignaciones
}

// src/2.Application.DTO/Messaging/TicketUserCompanies/TicketAssignableUserDto.cs
public record TicketAssignableUserDto
{
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;   // mismo formato que TicketUserCompanyDto.UserName
    public string? UserEmail { get; init; }
}
```

`TicketCompanyDefaultDto`, `CreateTicketCompanyDefaultDto` y `UpdateTicketCompanyDefaultDto` suman `public int? MaxDayTicketInactivity { get; init; }`.

### Queries

- `GetMyTicketCapabilitiesQuery` → handler que llama a `TicketUserCompanyCapabilityResolver.ResolveAsync(currentUserId, companyId)` y arma el DTO (`IsMember` = existe la fila activa; el resolver hoy devuelve `default` sin fila, así que el handler distingue "sin fila" de "fila con todo en false" con una consulta propia o extendiendo el resolver con `IsMember`, sin cambiar sus usos actuales).
- `GetAssignableTicketUsersQuery` → Dapper sobre `Messaging.TicketUserCompanies` + `Security.Users` filtrando `CompanyId`, `GcRecord = 0` y `CanResolveTicket = TRUE`, mismo armado de nombre que `GetTicketUserCompaniesQueryHandler`.
- Ambas rutas se declaran **antes** de `[HttpGet("{id:guid}")]`.

### Validación de `MaxDayTicketInactivity`

`null` o entero `>= 1`. Mensaje: `"MaxDayTicketInactivity must be null or greater than zero."`. Al editar, `null` desactiva la marca de inactividad.

### Errores

| Código | HTTP | Cuándo |
|---|---|---|
| (estándar) | 401 / 403 | Sin token / sin `CanRead` sobre `Tickets` |
| `COMPANY_REQUIRED`, `USER_REQUIRED` | 400 | Token sin empresa o sin usuario (mismos códigos que los handlers de SPEC 35) |

---

## Implementation plan

1. DTOs nuevos y `MaxDayTicketInactivity` en los tres DTO de defaults, comandos, Mapperly y validadores.
2. `GetMyTicketCapabilitiesQuery` / `GetAssignableTicketUsersQuery` con sus handlers.
3. Acciones `GetMine` (`[HttpGet("me")]`) y `GetAssignable` (`[HttpGet("assignable")]`) con `[PermissionResource("Tickets")]` en `TicketUserCompaniesController`.
4. `EXPLAIN` del SQL nuevo (convención 10 de la serie `-post`), `CURL_REQUESTS.md` e índice de specs.

## Acceptance criteria

- [ ] Un usuario con `CanRead` sobre `Tickets` y **sin** permiso sobre `TicketUserCompanies` obtiene 200 en `/me` y en `/assignable`.
- [ ] `/me` devuelve los mismos flags que usa `FinishTicket`/`ReassignTicket` para ese usuario (test que compara con el resolver); un usuario fuera del roster recibe `IsMember = false` y todo en `false`.
- [ ] `/assignable` devuelve solo miembros activos con `CanResolveTicket` de la empresa activa; no incluye borrados ni usuarios de otra empresa.
- [ ] Asignar al crear o reasignar a cualquier usuario de `/assignable` no da `INVALID_ASSIGNED_USER_NOT_RESOLVER` (test de integración).
- [ ] `TicketCompanyDefaultDto` incluye `MaxDayTicketInactivity`; crear y editar lo guardan; `null` lo desactiva y `0` o negativo da 400.
- [ ] Un ticket inactivo según el valor guardado aparece con `isInactive = true` en `GET /Tickets` y `GET /Tickets/{id}` (sin cambios en el cálculo, solo se verifica la configuración desde la API).
- [ ] Tests unitarios de los handlers y de integración (`CustomWebApplicationFactory`, `postgres:17`).

## Decisions

- **`/me` y `/assignable` bajo el permiso `Tickets`** (elegido) en vez del de `TicketUserCompanies`: son datos que necesita cualquiera que trabaje tickets; el permiso de `TicketUserCompanies` sigue protegiendo la administración del equipo (alta, baja, cambio de flags). Mismo criterio que `CalendarUserConfigurations/me` bajo `Calendar`.
- **`/assignable` devuelve solo resolvers** (elegido): evita que el frontend ofrezca destinos que el backend rechaza.
- **200 con todo en `false` para quien no está en el roster** (elegido) en vez de 404: no es un error, es "no puede hacer nada de esto".

## Risks

| Riesgo | Mitigación |
|---|---|
| `/assignable` expone nombres y correos de los resolvers a cualquiera que lea tickets. | Es la misma información que ya ve en la bandeja y el detalle ("Asignado a"); no incluye flags ni datos de administración. |
| Capacidades en caché del frontend desactualizadas tras un cambio en el roster. | El frontend las pide al entrar al detalle y el backend sigue validando cada acción (403 / códigos de SPEC 35). |

## Impacto en el frontend

- **FE-18 (fase B):** "Finalizar" visible con `CanFinishTicket || IsSuperAdminTicket`; "Reasignar" con `IsSuperAdminTicket` y destinos de `/assignable`; hueco de inactividad con "Supera el máximo de {n} días".
- **FE-17:** "Asignar a" usa `/assignable` (hoy lista todo el roster) y "Asignarme a mí" solo si `/me` trae `CanResolveTicket`; mapear `INVALID_ASSIGNED_USER_NOT_RESOLVER`.
- **FE-19:** campo "Días sin actividad para marcar un ticket como inactivo" en Valores por defecto.
- **FE-15:** brecha 3 cerrada; brecha 5 cerrada para `/TicketUserCompanies/me`.
