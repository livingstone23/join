# SPEC 63 — Ganar, perder y reabrir leads; conversión a cliente; etapas de cliente post-venta; migración de clientes en LEAD/PROSPECT

> **Status:** Borrador
> **Depends on:** SPEC 54 (seed de personas y clientes), SPEC 55 (clientes, `ICustomerActivationService`, `CustomerStages`), **SPEC 58** (identificación opcional), **SPEC 59** (motivos y vinculación), **SPEC 61** (leads). Rama de trabajo: `spec-63-lead-win-loss-and-customer-conversion`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Cerrar un lead como ganado (desde cualquier etapa abierta) o perdido, con motivo; que ganar cree, reactive o confirme el cliente con el origen `Lead`; que las etapas de cliente pasen a describir la vida post-venta; y migrar los clientes que hoy están en `LEAD` o `PROSPECT` a leads abiertos.
> **Origen:** diseño del módulo de clientes (2026-10-16, modales "Ganar" y "Perder" y vista "Clientes").

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **Ganar desde cualquier etapa abierta.** No hace falta llegar a la última etapa: si el prospecto ya decidió, se cierra.
2. **Ganar exige un motivo** de uso `Won` y, si la empresa lo exige (`RequireIdentificationForCustomer`, SPEC 59), la identificación de la persona; si falta, se completa en la misma acción.
3. **Ganar crea el cliente** cuando la vinculación del lead tiene `OnWin = Customer`: llama a `ICustomerActivationService.EnsureActiveAsync` con el origen nuevo `CustomerStatusSource.Lead = 5` y `SourceReferenceId` = id del lead. Si la persona ya era cliente, se reactiva o confirma (SPEC 55); nunca se crea una segunda fila. Con `OnWin = None` (proveedor, socio, referidor) solo se cierra el lead.
4. **Productos aceptados:** al ganar se indican las líneas aceptadas; las demás quedan rechazadas. El valor ganado es la suma de las aceptadas.
5. **Tasa al ganar:** el valor ganado se fija con la tasa del día de cierre (`WonExchangeRate`, `WonValueBase`); el valor estimado del embudo no se toca.
6. **Atribución:** el cliente cuenta para el dueño y el grupo que tiene el lead al ganar (si fue reactivado por otro, cuenta para el que lo reactivó, SPEC 61).
7. **Perder exige un motivo** de uso `Lost`; competidor opcional. Un lead perdido se puede **reabrir**: vuelve a `Open` en su última etapa abierta. Un lead ganado no se reabre.
8. **Etapas de cliente post-venta.** `CustomerStages` deja de ser el embudo: describe la relación ya establecida (`CUSTOMER` Cliente — por defecto —, `RECURRING` Recurrente, `FORMER` Cliente anterior). `LEAD` y `PROSPECT` se retiran.
9. **Migración:** cada cliente que hoy está en etapa `LEAD` o `PROSPECT` pasa a ser un **lead abierto**. Como un cliente no se borra (SPEC 55), su fila queda **inactiva** y enlazada al lead (`Lead.CustomerId`); al ganar, `EnsureActiveAsync` la reactiva con el mismo `CustomerCode`.

## Contexto

- `CustomerStage` tiene los códigos `LEAD`, `PROSPECT`, `CUSTOMER`, `FORMER` (SPEC 55, `SeedCustomerStagesForCompanyAsync`); SPEC 55 regla 6 dice que la etapa es el embudo comercial y es independiente del estado.
- El seed de SPEC 54 crea clientes con etapa `LEAD` y `PROSPECT` (`LookupPersonSeed.CustomerStageCode`).
- `CustomerStatusSource` tiene `Manual`, `Seed`, `Migration`, `PersonDeactivated`.

## Scope

**In:**

**A. Cierre del lead**
1. `Leads` suma `WonValue?`, `WonExchangeRate?`, `WonValueBase?`, `CompetitorName?`. `LeadStatusHistory` registra `Won`, `Lost` y `Reopened`.
2. `POST /api/v1/Leads/{id}/win` `{ leadReasonId, acceptedLeadProductIds[], identificationTypeId?, identificationNumber?, notes? }` (`ITransactionalCommand`):
   1. Lead `Open` y visible para el usuario (alcance, SPEC 60); motivo `Won` activo.
   2. Identificación: si la persona no tiene y la empresa la exige, deben venir los dos campos; se guardan en la persona (unicidad, SPEC 58).
   3. Líneas: las indicadas pasan a `Accepted`, el resto a `Rejected`; `WonValue` = suma de aceptadas (si no hay líneas, `WonValue` = `EstimatedValue`).
   4. Tasa del día (`IExchangeRateResolver`) → `WonExchangeRate`, `WonValueBase` (`IMoneyConverter`).
   5. Etapa = la etapa `Won` de la empresa; `Status = Won`; `ClosedAt`; historiales de etapa y estado.
   6. Si `OnWin = Customer`: `EnsureActiveAsync(companyId, personId, Lead, userId, sourceReferenceId: leadId, notes)`; guarda `Lead.CustomerId`. La respuesta trae el cliente y la acción (`Created`, `Activated`, `Confirmed`).
3. `POST /api/v1/Leads/{id}/lose` `{ leadReasonId, competitorName?, notes? }`: lead `Open`, motivo `Lost`; etapa `Lost`, `Status = Lost`, `ClosedAt`.
4. `POST /api/v1/Leads/{id}/reopen` `{ notes? }`: solo `Lost`; vuelve a `Open` en la última etapa abierta de `LeadStageHistory` (si ya no está activa, la inicial); `ClosedAt = null`.
5. `CustomerStatusSource.Lead = 5`; `GET /Customers/{id}/status-history` muestra el código del lead en los eventos con ese origen.
6. `GET /Customers` y `GET /Customers/{id}` suman `OriginLeadId` y `OriginLeadCode` (primer evento `Created`/`Activated` con origen `Lead`), para la columna "Origen" de la pantalla de clientes.

**B. Etapas de cliente post-venta**
7. `SeedCustomerStagesForCompanyAsync` siembra `CUSTOMER` (Cliente, por defecto), `RECURRING` (Recurrente) y `FORMER` (Cliente anterior) para empresas nuevas; deja de sembrar `LEAD` y `PROSPECT`.
8. `CustomerStage.Codes` retira `Lead` y `Prospect` y agrega `Recurring`. El seed de SPEC 54 deja de asignar esas etapas: las personas que eran clientes en `LEAD`/`PROSPECT` pasan a ser leads abiertos del seed de SPEC 61.

**C. Migración de datos** (idempotente, en el arranque después de las migraciones, como `SeedTicketAttachmentSettingsDefaultsAsync`; condición natural: hay clientes vivos con etapa `LEAD` o `PROSPECT`)
9. Por cada cliente en `LEAD`/`PROSPECT` de cada empresa:
   - Lead `Open` con persona = la del cliente; etapa: `LEAD` → `NEW` y `PROSPECT` → `QUALIFIED` (si no existen, la inicial); origen `MIGRATION`; vinculación `PROSPECT_CUSTOMER`; dueño = `Customer.UserId`; grupo = grupo principal vigente del dueño (o ninguno); moneda base, tasa 1, valor 0; `CustomerId` = el cliente; historiales iniciales.
   - Si el cliente está activo, se desactiva con historial `Deactivated`, origen `Migration`, `SourceReferenceId` = id del lead.
   - El cliente pasa a la etapa por defecto de la empresa.
10. Luego `RECURRING` se crea si falta, y `LEAD` y `PROSPECT` se borran lógicamente (ya sin uso). Si alguna es la etapa por defecto, la por defecto pasa a `CUSTOMER` antes.
11. La migración escribe en el log cuántos leads creó por empresa. Correrla dos veces no crea nada (ya no quedan clientes en esas etapas).

**Out of scope:**

- Generar cotización o venta con las líneas aceptadas (módulos de Presupuestos y Ventas).
- Reabrir leads ganados.
- Cambiar automáticamente la etapa de cliente a `RECURRING` (queda manual con `PUT /Customers/{id}`).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `LEAD_NOT_OPEN` | 409 | Ganar o perder un lead que no está abierto |
| `LEAD_NOT_LOST` | 409 | Reabrir un lead que no está perdido |
| `INVALID_LEAD_REASON` | 400 | Motivo inactivo, de otra empresa o de otro uso |
| `PERSON_IDENTIFICATION_REQUIRED` | 400 | La empresa exige identificación, la persona no tiene y no llegó |
| `INVALID_LEAD_PRODUCT` | 400 | Una línea aceptada no pertenece al lead |
| `INVALID_PERSON` | 400 | La persona está inactiva (SPEC 55 no activa clientes de personas inactivas) |
| `DUPLICATE_KEY` | 409 | La identificación completada ya existe en la empresa |
| `LEAD_NOT_FOUND` | 404 | Fuera de alcance, de otra empresa o inexistente |

## Criterios de aceptación

1. Ganar un lead en `Contactado` (sin pasar por las demás etapas) funciona y lo deja en la etapa `Won`.
2. Ganar con vinculación `PROSPECT_CUSTOMER` crea el cliente con historial `Created`, origen `Lead`, referencia = id del lead; ganar otro lead de la misma persona lo confirma (`Confirmed`) sin fila nueva.
3. Ganar con vinculación `SUPPLIER` cierra el lead y no crea cliente.
4. Sin identificación y con la regla activa, ganar responde 400 hasta que llega la identificación; con la regla desactivada, gana sin ella.
5. `WonValue` es la suma de las líneas aceptadas; las no aceptadas quedan `Rejected`; un lead en USD guarda `WonExchangeRate` y `WonValueBase` con la tasa del día de cierre.
6. Un lead reactivado por otro usuario y luego ganado registra al cliente con `UserId` = quien ganó y el lead cuenta para su grupo.
7. Perder exige motivo `Lost`; reabrir vuelve a la última etapa abierta; reabrir un ganado responde 409.
8. Migración: un cliente activo en `PROSPECT` queda inactivo, en la etapa por defecto, con historial `Deactivated · Migration` y un lead abierto en `QUALIFIED` enlazado; al ganar ese lead, el cliente se reactiva con el mismo código. Correr la migración otra vez no crea leads.
9. Después de la migración ninguna empresa tiene etapas de cliente `LEAD` o `PROSPECT` vivas; las empresas nuevas nacen con `CUSTOMER`, `RECURRING` y `FORMER`.
10. `GET /Customers` muestra `OriginLeadCode` en los clientes que vinieron de un lead.
11. Unit tests de los comandos, la migración y el seed; integración para 1–10.

## Decisiones

- **Lead propio, cliente al ganar** (decisión del usuario): `Customers` solo tiene relaciones comerciales reales; el embudo vive en los leads.
- **Migrar a leads abiertos con el cliente inactivo** (decisión del usuario): conserva el `CustomerId` y el `CustomerCode` (SPEC 55 no permite borrar) y respeta que esas personas todavía no compraron.
- **Ganar desde cualquier etapa** (decisión del usuario).
- **Tasa al ganar separada de la del embudo** (decisión del usuario): los reportes de clientes ganados usan el valor real del cierre.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Clientes migrados aparecen como inactivos en la lista de clientes. | Es lo esperado (todavía no compraron); la columna "Origen" y el lead enlazado lo explican. |
| SPEC 55 regla 6 ("la etapa es el embudo") deja de ser cierta. | Se deja la nota "Ajuste por SPEC 63" en SPEC 55 y en FE-28 en el mismo cambio que crea esta spec. |

## Impacto en el frontend

- Specs FE (todas en Borrador, `join_frontb/specs`): **FE-38 (ganar, perder, reabrir), FE-32 (columna Origen) y FE-28**. Base común del módulo: FE-29.
- Modales "Ganar" y "Perder" y acción "Reabrir" en el detalle del lead; columna "Origen" en Clientes; FE-28 (etapas de cliente) pasa a etapas post-venta (nota "Ajuste por BE-63").
