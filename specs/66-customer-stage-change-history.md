# SPEC 66 — Clientes: registrar los cambios de etapa en el historial, con comentario opcional

> **Status:** Implementado
> **Depends on:** SPEC 55 (clientes, `CustomerStatusHistory`, `PUT /Customers/{id}`), SPEC 63 (etapas de cliente post-venta). Rama de trabajo: `spec-66-customer-stage-change-history`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que cada cambio de etapa de un cliente (por ejemplo "Cliente" → "Recurrente") quede en su historial, con quién lo hizo, cuándo, de qué etapa a cuál y un comentario opcional, y se vea en el "Historial de estados" del detalle del cliente (FE-32).
> **Origen:** pedido del usuario (2026-10-19) desde el detalle del cliente: hoy `Customer.ChangeStage` solo actualiza `CustomerStageId` y el cambio no deja rastro.
> **Ajuste (2026-10-19, al implementar):** el comentario es de máximo **500** caracteres, no 1000: así lo validan activar y desactivar y así mide la columna `CustomerStatusHistory.Notes` (decisión del usuario). `Customer.ChangeStage` devuelve la fila `StageChanged` a guardar (o `null` sin cambio) y el handler la inserta en la misma transacción. La migración de clientes LEAD/PROSPECT del seed (SPEC 63) también registra su cambio de etapa, con origen `Migration` (regla 1).

---

## Reglas de negocio (decisión del usuario, 2026-10-19)

1. **Todo cambio de etapa se registra** en el historial del cliente, venga de donde venga (`PUT /Customers/{id}` hoy; cualquier flujo futuro que cambie la etapa).
2. **Comentario opcional** al cambiar la etapa (máximo 500 caracteres, igual que activar o desactivar y que la columna `Notes`).
3. **Mismo historial que los estados:** el cambio se guarda en `Customer.CustomerStatusHistory` con una acción nueva, para que una sola línea de tiempo muestre estado y etapa en orden. La etapa sigue siendo independiente del estado (BE-55, regla 6): registrar un cambio de etapa no activa ni desactiva al cliente.
4. **Sin cambio, sin fila:** un `PUT` con la misma etapa responde 200 sin cambios y no escribe historial (BE-55, ajuste 4).
5. El alta de un cliente (con su etapa inicial) no escribe una fila de cambio de etapa: la etapa inicial ya se ve en la fila `Created`.

## Scope

**In:**

**A. Dominio y persistencia**
1. `CustomerStatusAction` suma `StageChanged = 5`.
2. `CustomerStatusHistory` suma `FromCustomerStageId?` y `ToCustomerStageId?` (`uuid`, FK a `Customer.CustomerStages`, `Restrict`), solo con valor en las filas `StageChanged`.
3. `Customer.ChangeStage(customerStageId, userId, notes?)` devuelve si hubo cambio; el handler escribe la fila `StageChanged` (origen `Manual`) en la misma transacción.
4. Migración `Spec66CustomerStageChangeHistory`: solo agrega las dos columnas y sus índices de FK. Sin backfill (los cambios anteriores no se conocen).

**B. API**
5. `UpdateCustomerCommand` suma `Notes?` (máx. 500). `PUT /Customers/{id}` con `{ id, customerStageId, notes }`.
6. `CustomerStatusHistoryDto` suma `FromStageName?` y `ToStageName?` (nombres vigentes de las etapas, aunque luego se renombren o desactiven); `ActionName` = `StageChanged`.
7. `CustomerResponseDto.LastStatus*` sigue mostrando el último **estado** (filas que no son `StageChanged`), para no confundir "por qué está activo o inactivo".

**Out of scope:**
- Historial de etapas de los leads (ya existe en `LeadStageHistory`).
- Reconstruir los cambios de etapa anteriores a esta spec.

## Errores

Los de BE-55 (`INVALID_CUSTOMER_STAGE`, `CUSTOMER_NOT_FOUND`) más la validación de `Notes` (longitud).

## Criterios de aceptación

1. Cambiar la etapa de "Cliente" a "Recurrente" con comentario crea una fila `StageChanged` con ambas etapas, el comentario, el usuario y la fecha; sin comentario, la fila se crea igual con `Notes = null`.
2. Un `PUT` con la misma etapa no crea fila.
3. `GET /Customers/{id}/status-history` devuelve la fila con `FromStageName` y `ToStageName`, ordenada junto a las de estado.
4. `LastStatusAction` no cambia al cambiar solo la etapa.
5. Unit tests del handler y validador; integración para 1–4; `EXPLAIN` del SQL nuevo.

## Decisiones

- **Reusar `CustomerStatusHistory`** en vez de una tabla `CustomerStageHistory`: el usuario quiere verlo en el mismo "Historial de estados"; una tabla aparte obligaría al front a unir dos listas paginadas. El nombre de la tabla queda por compatibilidad.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Lecturas que asumen que toda fila del historial es un cambio de estado. | Regla 7: `LastStatus*` filtra `StageChanged`; revisar las queries de BE-55 y BE-63 que leen el historial. |

## Impacto en el frontend

- **FE-41** (Borrador): botón "Guardar" de la etapa con comentario opcional y el cambio de etapa en la línea de tiempo del detalle del cliente (FE-32).
