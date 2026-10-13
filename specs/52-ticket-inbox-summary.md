# SPEC 52 — Tickets: resumen de la bandeja (`GET /Tickets/summary`) y filtro "sin asignar"

> **Status:** Aprobado
> **Depends on:** SPEC 37 (SLA e inactividad, `TicketSlaCalculator`), SPEC 38 (aislamiento por empresa), SPEC 41-post (borrado lógico), SPEC 43-post (rutas y permisos de tickets). Rama de trabajo: `spec-52-ticket-inbox-summary`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`.
> **Date:** 2026-10-13
> **Objective:** Dar a la bandeja de tickets (FE-16) los totales de la empresa activa —abiertos, SLA vencido, sin actividad y sin asignar— y un filtro de servidor para "sin asignar", para que el front no calcule números sobre la página visible.
> **Origen:** brechas 2 y 4 de FE-15 (`join_frontb/specs/15-tickets-comun.md`). La brecha 1 (canal en el listado) está `Pospuesta` por decisión del usuario (2026-10-13) y queda fuera.

---

## Scope

**In:**

- **A. `GET /api/v1/Tickets/summary`** en `TicketsController` (`[PermissionResource("Tickets")]`, GET → `CanRead`, igual que `GetPaged`). Sin parámetros: siempre cuenta la empresa activa del token (misma regla de tenant que `GET /Tickets`; el SuperAdmin ve la empresa activa, no todas).
- Responde `Response<TicketSummaryDto>`:

  | Campo | Regla (solo tickets no borrados, `GcRecord = 0`) |
  |---|---|
  | `OpenCount` | Estado con `IsFinal = false` |
  | `SlaBreachedCount` | Abiertos con `IsSlaBreached = true` según `TicketSlaCalculator` |
  | `InactiveCount` | Abiertos con `IsInactive = true` según `TicketSlaCalculator` |
  | `UnassignedCount` | Abiertos con `AssignedToUserId IS NULL` |
  | `GeneratedAt` | `DateTime` UTC del cálculo |

- **B. `?unassigned=true` en `GET /Tickets`** (y en `GetTicketsQuery`): agrega `AND t.AssignedToUserId IS NULL`. Si llega junto con `assignedToUserId`, responde 400 `INVALID_ASSIGNEE_FILTER`.

**Out of scope:**

- Canal en el listado o en el resumen (brecha 1 de FE-15, `Pospuesta`).
- Totales que respeten los filtros de la bandeja: las tarjetas muestran la carga de la empresa, no la de la búsqueda.
- Filtros de servidor por SLA vencido o inactividad en `GET /Tickets` (SPEC 37 los deja fuera; siguen fuera).
- Vista de todas las empresas (`system-wide`).
- Persistir `SlaDueAt` en la tabla.

---

## Diseño

### DTO

```csharp
// src/2.Application.DTO/Messaging/Tickets/TicketSummaryDto.cs
public record TicketSummaryDto
{
    public int OpenCount { get; init; }
    public int SlaBreachedCount { get; init; }
    public int InactiveCount { get; init; }
    public int UnassignedCount { get; init; }
    public DateTime GeneratedAt { get; init; }
}
```

### Query

`GetTicketSummaryQuery` → `GetTicketSummaryQueryHandler` (Dapper, PostgreSQL), en `UseCases/Messaging/Tickets/Queries/GetTicketSummary/`.

1. Un solo SQL que trae **solo los tickets abiertos** de la empresa activa con los mismos insumos de SLA que usa `GetTicketsQueryHandler`: `t.Created`, `t.AssignedToUserId`, `tc.ResolutionTimeUnits`, `tcu.Code`, `tcd.MaxDayTicketInactivity`, `MAX(TicketLogs.Created)` como última actividad (fallback `t.Created`, igual que el listado). No hace falta `FinishedAt`: los abiertos se miden contra "ahora".
2. Los conteos se hacen en C# con `TicketSlaCalculator.Compute(..., isFinalStatus: false, ..., nowUtc)`: una sola fuente de verdad, así las tarjetas y las columnas de la grilla nunca difieren.
3. La subconsulta de última actividad pasa a un `LEFT JOIN` agregado (`GROUP BY TicketId`) para no ejecutar un subselect por fila.
4. SQL según las convenciones PostgreSQL del README (serie `-post`, convención 6): booleanos `= TRUE`/`= FALSE`, `COALESCE`, nombres lógicos que `ApplyLowerCaseNaming` traduce a minúsculas.

### Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_ASSIGNEE_FILTER` | 400 | `unassigned=true` junto con `assignedToUserId` |
| (estándar) | 401 / 403 | Sin token / sin `CanRead` sobre `Tickets` |

---

## Implementation plan

1. `TicketSummaryDto` + `GetTicketSummaryQuery`/`Handler` + acción `GetSummary` (`[HttpGet("summary")]`) en `TicketsController`, declarada **antes** de `[HttpGet("{id:guid}")]`.
2. `GetTicketsQuery`: parámetro `Unassigned`, validación cruzada con `AssignedToUserId`, filtro en el `WHERE` (comparte `whereClause` con el `COUNT`).
3. `EXPLAIN` del SQL nuevo contra la base migrada (convención 10 de la serie `-post`).
4. Actualizar `CURL_REQUESTS.md` y el índice de specs.

## Acceptance criteria

- [ ] `GET /Tickets/summary` devuelve los 4 conteos de la empresa activa, sin contar tickets borrados ni de otras empresas.
- [ ] Un ticket con estado `IsFinal` no cuenta en ningún total, aunque haya cerrado fuera de plazo.
- [ ] Para el mismo conjunto de tickets, `SlaBreachedCount` e `InactiveCount` coinciden con las filas que `GET /Tickets` marca con `isSlaBreached`/`isInactive` (test que compara ambos).
- [ ] Una empresa sin `TicketCompanyDefaults.MaxDayTicketInactivity` devuelve `InactiveCount = 0`.
- [ ] Un usuario sin `CanRead` sobre `Tickets` recibe 403.
- [ ] `GET /Tickets?unassigned=true` devuelve solo tickets sin asignar y su `TotalCount` es coherente; con `assignedToUserId` responde 400 `INVALID_ASSIGNEE_FILTER`.
- [ ] Tests unitarios del handler y de integración (`CustomWebApplicationFactory`, `postgres:17`).

## Decisions

- **Conteo en C# con `TicketSlaCalculator`** (elegido) frente a replicar la fórmula en SQL: evita dos implementaciones del SLA que pueden divergir. Costo: trae una fila por ticket abierto; aceptable por el volumen por empresa. Si crece, se reemplaza por SQL con un test de equivalencia.
- **Sin filtros en el resumen:** las tarjetas son la carga de la empresa; aplicar los filtros confundiría al cambiar de búsqueda.
- **Solo abiertos en SLA vencido:** la tarjeta es para actuar; los cerrados fuera de plazo son un reporte, no trabajo pendiente.

## Risks

| Riesgo | Mitigación |
|---|---|
| Empresas con muchos tickets abiertos hacen lento el resumen. | Índice por `(CompanyId, GcRecord, TicketStatusId)` si el `EXPLAIN` lo pide; plan B de SQL puro con test de equivalencia. |
| El front llama `summary` muy seguido (badge del sidenav en cada navegación). | FE-16 lo pide al entrar a la bandeja y tras restaurar o cambiar de empresa, no en cada navegación. |

## Impacto en el frontend

- FE-15: brechas 2 y 4 cerradas → nota "Ajuste por BE-52".
- FE-16: tarjetas resumen, badge de SLA vencido en el sidenav y "Sin asignar" filtrado en el servidor (se quita la nota "Filtra solo la página actual").
