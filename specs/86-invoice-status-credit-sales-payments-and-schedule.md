# SPEC 86 — Estados de la factura, venta a crédito, pagos e historial, con calendario de cobro

> **Status:** Borrador
> **Depends on:** SPEC 44-post y 65 (calendario y eventos enlazados al lead), SPEC 48-post (acceso por módulo), SPEC 72 (correo), **SPEC 81** (configuración de tipos), **SPEC 83** (documentos y bitácora), **SPEC 84** (PDF y envío), **SPEC 87** (línea de crédito, que valida y descuenta las ventas a crédito). Rama de trabajo: `spec-86-invoice-status-credit-sales-payments-and-schedule`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Controlar el estado de cobro de cada factura. Distinguir ventas al contado y a crédito, registrar los pagos con su historial y, en las facturas a crédito, definir un calendario de cuotas que crea recordatorios en el calendario para dar seguimiento a si la persona paga.
> **Origen:** pedido del usuario (2026-10-24): "quiero llevar control de estados de facturas y, de ser de crédito, un historial… al recibir un pago pueda volver a incrementar la línea de crédito… las facturas de crédito podrán establecer un calendario para llevar controles si la persona paga".
> **Ajuste por revisión de consistencia (2026-10-24):** (1) `Voided` cubre la factura anulada y la factura devuelta del todo con la nota aplicada (SPEC 92). (2) Anular una factura con pagos exige reembolso (SPEC 83 regla 9). (3) Pagos y cuotas registran su `IBaseCurrencyAmountSource` (SPEC 56). (4) `InvoiceInstallments.Status`: `Pending = 1`, `Partial = 2`, `Paid = 3`. (5) Seed en `JOIN-NI-001` (SPEC 100).
> **Ajuste por SPEC 92 (2026-10-24):** la nota de crédito parcial aplicada a la factura reduce su saldo; la reembolsada en efectivo no lo cambia y se registra como reembolso (`InvoiceRefunds`).
> **Cambia SPEC 83:** SPEC 83 dejaba "cobros y pagos" fuera de alcance; esta spec los agrega.

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Condición de pago** de la factura: `Cash = 1` (contado) o `Credit = 2` (crédito). Por defecto `Cash`. `Credit` solo se puede elegir si el cliente tiene una línea de crédito activa (SPEC 87).
2. **Estado de cobro** (`PaymentStatus`), independiente del estado del documento (SPEC 83):

   | Estado | Cuándo |
   |---|---|
   | `Unpaid = 1` | Emitida a crédito sin pagos |
   | `PartiallyPaid = 2` | Pagos menores que el saldo |
   | `Paid = 3` | Saldo cero |
   | `Overdue` | No se guarda: `Unpaid` o `PartiallyPaid` con alguna cuota vencida (o `DueDate` vencida sin cuotas) |
   | `Voided = 4` | Factura anulada (nota de crédito total, SPEC 83) o devuelta del todo con la nota aplicada a la factura (SPEC 92) |

   Una factura **al contado** se emite con un pago automático por el total (método indicado al emitir) y queda `Paid`.
3. **Saldo** = total − pagos − notas de crédito aplicadas. Nunca negativo: un pago mayor que el saldo → 409 `PAYMENT_EXCEEDS_BALANCE`.
4. **Pagos** (`InvoicePayments`): fecha, monto (moneda de la factura), método (catálogo por empresa, seed: Efectivo, Transferencia, Tarjeta, Cheque), referencia, notas, usuario. Un pago se **revierte** (no se borra) con motivo, y el saldo y el crédito se recalculan. Cada pago y reversión queda en la bitácora de la factura (SPEC 83) y en el **historial de crédito** del cliente (SPEC 87).
5. **Efecto en la línea de crédito** (SPEC 87): emitir una factura a crédito **reduce** el disponible por su total, y cada pago lo **incrementa** por el monto pagado. Una nota de crédito sobre una factura a crédito devuelve el saldo pendiente anulado.
6. **Calendario de cobro** (solo crédito): al emitir, la factura lleva cuotas (`InvoiceInstallments`) con fecha de vencimiento y monto. Se generan con un plan (número de cuotas y frecuencia: semanal, quincenal o mensual, desde una fecha) o a mano. Las cuotas suman el total. Sin cuotas, `DueDate` es una cuota única. Los pagos se aplican a las cuotas de la más antigua a la más reciente.
7. **Recordatorios en el calendario:** con el módulo Calendario activo (SPEC 44-post), cada cuota crea un evento en el calendario del **responsable de cobro** (por defecto, el dueño del lead; editable), enlazado al lead y al cliente (`LeadId`, `CustomerId`, como SPEC 65), `N` días antes del vencimiento (`CompanyCustomers.InstallmentReminderDays`, por defecto 2). Cuando la cuota se paga, el evento se marca completado. Sin el módulo Calendario, las cuotas se ven igual y no se crean eventos.
8. **Plazo por defecto:** la configuración de la factura (SPEC 81, `DueDays`) da el vencimiento por defecto; la línea de crédito del cliente (SPEC 87) puede tener su propio plazo, que prevalece.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `LeadCommercialDocuments` suma `PaymentTerms` (`Cash`/`Credit`), `PaymentStatus`, `PaidAmount`, `Balance`, `CollectionUserId?` y `LastPaymentAt?`.
2. `PaymentMethods` (`BaseTenantEntity`): `Code`, `Name`, `IsActive`, `SortOrder`. Seed `CASH`, `TRANSFER`, `CARD`, `CHECK`.
3. `InvoicePayments` (`BaseTenantEntity`): `DocumentId`, `PaymentDate` (`date`), `Amount`, `AmountBase`, `PaymentMethodId`, `Reference?` (máx. 100), `Notes?`, `UserId`, `IsReversed`, `ReversedAt?`, `ReversedByUserId?`, `ReversalReason?`.
4. `InvoiceInstallments` (`BaseTenantEntity`): `DocumentId`, `Number`, `DueDate`, `Amount`, `PaidAmount`, `Status` (`Pending`, `Partial`, `Paid`), `CalendarEventId?`.
5. `CompanyCustomers` suma `InstallmentReminderDays`.

**B. Endpoints (permiso `Leads`)**
6. `POST /LeadCommercialDocuments/{id}/issue` (SPEC 83) acepta, en facturas, `{ paymentTerms, paymentMethodId? (contado), installments?[] | installmentPlan? { count, frequency, firstDueDate }, collectionUserId? }`. A crédito valida la línea de crédito (SPEC 87) en la misma transacción.
7. `GET /api/v1/LeadCommercialDocuments/{id}/payments`, `POST /{id}/payments` `{ paymentDate, amount, paymentMethodId, reference?, notes? }`, `POST /{id}/payments/{paymentId}/reverse` `{ reason }` (`[RequirePermission(CanDelete)]`).
8. `GET /{id}/installments`, `PUT /{id}/installments` (reprogramar fechas o montos de las cuotas pendientes; suman el saldo; queda en la bitácora y mueve los eventos del calendario).
9. `GET /api/v1/LeadCommercialDocuments` suma los filtros `paymentTerms` y `paymentStatus` (incluye `Overdue`) y devuelve los totales del filtro (facturado, cobrado, pendiente, vencido) en moneda base.
10. Histórico del cliente (SPEC 83) suma, por factura, la condición, el estado de cobro y el saldo, y la ficha muestra **total pendiente y vencido**.
11. `GET /api/v1/Customers/{id}/statement?from=&to=` (`[PermissionResource("Leads")]`, `CanRead`; CSV con `CanExport`): estado de cuenta (facturas, notas de crédito y pagos en orden, con saldo acumulado), exportable a CSV.
12. Administración: `GET/POST/PUT/DELETE /api/v1/PaymentMethods` (`/administration/payment-methods`).

**Out of scope:**
- Intereses por mora, conciliación bancaria y pasarelas de pago.
- Recordatorios automáticos por correo al cliente (los recordatorios de esta spec son internos, para el responsable de cobro).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `CREDIT_TERMS_NOT_ALLOWED` | 409 | Venta a crédito a un cliente sin línea de crédito activa (SPEC 87) |
| `PAYMENT_EXCEEDS_BALANCE` | 409 | Pago mayor que el saldo |
| `INSTALLMENTS_TOTAL_MISMATCH` | 400 | Las cuotas no suman el total (o el saldo, al reprogramar) |
| `INVOICE_NOT_PAYABLE` | 409 | Pagar una factura en `Draft` o anulada, o un documento que no es factura |
| `PAYMENT_ALREADY_REVERSED` | 409 | Revertir dos veces |
| `PAYMENT_METHOD_NOT_FOUND` / `PAYMENT_METHOD_INACTIVE` | 404 / 409 | Método inválido |
| Errores de SPEC 87 | — | `CREDIT_LIMIT_EXCEEDED`, etc. |

## Criterios de aceptación

1. Una factura al contado queda `Paid` al emitirse, con un pago por el total.
2. Una factura a crédito de 12.000 en 3 cuotas mensuales crea 3 cuotas de 4.000 y, con Calendario activo, 3 eventos en el calendario del responsable, 2 días antes de cada vencimiento.
3. Un pago de 5.000 deja la cuota 1 pagada y la 2 parcial, la factura `PartiallyPaid` con saldo 7.000, y el disponible de crédito sube 5.000 (SPEC 87).
4. Revertir ese pago restaura el saldo, el estado de las cuotas y el crédito, y queda en la bitácora con el motivo.
5. Con una cuota vencida, la factura aparece en el filtro `Overdue`.
6. El estado de cuenta cuadra: saldo final = facturas − notas de crédito − pagos vigentes.
7. Unitarios: aplicación de pagos a cuotas, plan de cuotas, estados y reversiones. Integración: emisión a crédito, pagos, reversión y eventos de calendario con el módulo activo e inactivo.

## Decisiones

- **Estado de cobro separado del estado del documento:** una factura puede estar `Approved` (SPEC 83) y a la vez `PartiallyPaid`.
- **Pagos que se revierten, no se borran:** el historial de crédito y el estado de cuenta nunca pierden un movimiento.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Métodos de pago: Efectivo, Transferencia, Tarjeta, Cheque.
- Factura a crédito a Ferretería El Progreso por C$ 45.000 en 3 cuotas mensuales, con un pago de C$ 15.000 por transferencia (referencia "BAC-000123", ficticia), y una cuota vencida para mostrar `Overdue`.
- Factura al contado a Clínica Médica Las Colinas (criterio 1 de SPEC 93: total a pagar C$ 61.600) pagada con tarjeta.
