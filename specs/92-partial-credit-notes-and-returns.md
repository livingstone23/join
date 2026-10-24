# SPEC 92 — Devoluciones con nota de crédito parcial: aplicada a la factura (y a la línea de crédito) o reembolsada en efectivo

> **Status:** Borrador
> **Depends on:** **SPEC 81** (`CompanyCustomers`, tipos y configuración), **SPEC 83** (documentos y nota de crédito), SPEC 84 (PDF y envío), **SPEC 85** (existencias), **SPEC 86** (pagos y saldo), **SPEC 87** (línea de crédito), SPEC 88 (numeración autorizada), SPEC 89 (punto de venta y caja), SPEC 90 (costo y margen), SPEC 91 (libros). Rama de trabajo: `spec-92-partial-credit-notes-and-returns`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Registrar devoluciones de mercancía (o de servicios no prestados) contra una factura emitida, con una **nota de crédito parcial** por las líneas y cantidades devueltas. Quien la registra elige cómo se liquida: **aplicada a la factura** (baja su saldo y, si la venta fue a crédito, devuelve disponible a la línea de crédito) o **reembolsada en efectivo** (no toca el saldo ni la línea de crédito, y queda indicado que se devolvió en efectivo).
> **Origen:** pedido del usuario (2026-10-24): "crea la opción de poder generar notas de crédito parcial al recibir una devolución, y poder indicar si la nota de crédito parcial reduce el valor de la venta en la factura y permite e incrementa, si tiene línea de crédito, esta; si la devolución es en efectivo no altera la línea de crédito y se indica que fue bajo efectivo la devolución".
> **Ajuste por SPEC 83:** SPEC 83 solo generaba la nota de crédito total al anular una factura. Esta spec agrega la parcial; la anulación total no cambia.

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Devolución contra una factura emitida** (`Issued` o `Approved`, no anulada). Se eligen líneas y cantidades. Cada línea no puede devolver más de lo facturado menos lo ya devuelto en notas anteriores (`SourceLineId`, SPEC 83). Las notas de crédito parciales pueden ser varias por factura.
2. **La nota de crédito** es un documento del tipo con `BaseKind = CreditNote` (SPEC 81), con `SourceDocumentId` = la factura, sus líneas en negativo con el mismo precio, descuento e impuestos de la línea original (copiados, no recalculados), número del rango autorizado si el perfil lo exige (SPEC 88), PDF, envío y bitácora como todo documento. Se **emite al registrarla**.
3. **Motivo obligatorio** de un catálogo por empresa (`ReturnReasons`, seed: "Producto dañado", "Error en el pedido", "Producto no conforme", "Servicio no prestado", "Cambio de opinión del cliente") más un comentario opcional.
4. **Forma de liquidación** (`Settlement`), elegida al registrar:
   - **`ApplyToInvoice` (aplicada a la factura):** el total de la nota **reduce el saldo** de la factura (SPEC 86). Si la factura fue **a crédito**, el disponible de la línea de crédito del cliente **aumenta** por ese monto (movimiento `CreditNote`, SPEC 87). Solo procede si el saldo de la factura alcanza (`CREDIT_NOTE_EXCEEDS_BALANCE`): una factura ya pagada no tiene saldo que reducir y su devolución se reembolsa.
   - **`CashRefund` (reembolso en efectivo):** se devuelve el dinero al cliente. Se registra un **reembolso** (`InvoiceRefunds`) con método de pago (por defecto efectivo), monto, fecha y usuario. **No cambia el saldo de la factura ni la línea de crédito.** La nota y la factura muestran "Devolución reembolsada en efectivo". Con caja activa (SPEC 89), el reembolso sale de la sesión abierta como `CashOut` con concepto "Reembolso NC <número>".
   - El monto reembolsado no puede superar lo efectivamente pagado de la factura menos lo ya reembolsado (`REFUND_EXCEEDS_PAID`).
5. **Valor de la venta:** con las dos formas, la nota de crédito **reduce la venta** en el libro de ventas (SPEC 91), en los reportes de margen (SPEC 90, el costo de las líneas devueltas se revierte) y en el histórico del cliente. Lo que cambia entre ellas es solo el saldo de la factura y la línea de crédito.
6. **Existencias:** cada línea de producto devuelta elige qué hacer con la mercancía:
   - `Restock` (por defecto): vuelve al almacén elegido (por defecto el de la línea original) con un movimiento `SaleReturn` (SPEC 85);
   - `Discard`: no vuelve al inventario (dañada); queda registrado en la nota y en la bitácora, sin movimiento de existencias.

   Las líneas de servicio no mueven existencias.
7. **Estado de la factura:** la factura sigue `Issued`/`Approved` y suma `ReturnedAmount`. Si se devuelve **todo**, se marca `IsFullyReturned`. Si además se aplicó a la factura, su estado de cobro queda `Voided` (SPEC 86).
8. **Una transacción:** emitir la nota, mover existencias, ajustar saldo y crédito o registrar el reembolso (y el movimiento de caja) ocurren juntos. Si algo falla, no queda nada.
9. **Dónde se registra:** desde la factura (lead o lista de documentos) y desde el **punto de venta** (SPEC 89), buscando la factura por número, código público o código de barras.
10. **Permisos:** `SalesReturns` (`CanCreate`) para registrar devoluciones. Reembolsar en efectivo exige además `CashRefunds` (`CanExecute`).

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `ReturnReasons` (`BaseTenantEntity`): `Code`, `Name`, `IsActive`, `SortOrder`.
2. `LeadCommercialDocuments` suma, en notas de crédito: `Settlement` (`Full = 0` para la anulación de SPEC 83, `ApplyToInvoice = 1`, `CashRefund = 2`), `ReturnReasonId?`, `ReturnComment?`. En facturas: `ReturnedAmount`, `RefundedAmount`, `IsFullyReturned`.
3. `LeadCommercialDocumentLines` suma, en notas de crédito: `RestockAction` (`Restock`, `Discard`) y `RestockWarehouseId?`.
4. `InvoiceRefunds` (`BaseTenantEntity`): `InvoiceId`, `CreditNoteId`, `RefundDate`, `Amount`, `AmountBase`, `PaymentMethodId`, `Reference?`, `CashSessionId?`, `UserId`.

**B. Endpoints**
5. `GET /api/v1/LeadCommercialDocuments/{invoiceId}/returnable`: por línea, cantidad facturada, devuelta y disponible para devolver; saldo de la factura; monto pagado y reembolsado; si tiene línea de crédito.
6. `POST /api/v1/LeadCommercialDocuments/{invoiceId}/returns` (permiso `SalesReturns`): `{ returnReasonId, comment?, settlement: ApplyToInvoice|CashRefund, refund?: { paymentMethodId, reference? }, lines[{ sourceLineId, quantity, restockAction, restockWarehouseId? }] }`. Emite la nota parcial y aplica la liquidación. Devuelve la nota, el nuevo saldo, el disponible de crédito (si aplica) y el PDF.
7. `GET /api/v1/LeadCommercialDocuments/{invoiceId}/returns`: notas de crédito de la factura con su forma de liquidación.
8. `POST /api/v1/PointOfSale/returns`: el mismo flujo desde el punto de venta, con la factura buscada por código.
9. Administración: `GET/POST/PUT/DELETE /api/v1/ReturnReasons` (`/administration/return-reasons`).

**C. Menú**
10. Clientes: `SalesReturns` → `/customers/sales-returns` (lista de devoluciones con filtros por fecha, cliente, motivo y forma de liquidación).

**Out of scope:**
- Cambio de producto en el mismo acto (devolver y llevar otro): se hace como devolución más una venta nueva.
- Reembolso por transferencia a cuenta del cliente con conciliación bancaria (se registra con el método `Transferencia`, sin conciliación).
- Saldo a favor del cliente para compras futuras.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `INVOICE_NOT_RETURNABLE` | 409 | La factura está en `Draft` o anulada, o el documento no es factura |
| `RETURN_QUANTITY_EXCEEDED` | 409 | Devolver más de lo facturado menos lo ya devuelto (detalle por línea) |
| `CREDIT_NOTE_EXCEEDS_BALANCE` | 409 | `ApplyToInvoice` por más del saldo de la factura |
| `REFUND_EXCEEDS_PAID` | 409 | `CashRefund` por más de lo pagado menos lo reembolsado |
| `RETURN_REASON_REQUIRED` | 400 | Sin motivo o con un motivo inactivo |
| `CASH_REFUND_NOT_ALLOWED` | 403 | Reembolso sin el permiso `CashRefunds` |
| `CASH_SESSION_REQUIRED` | 409 | Reembolso en efectivo con caja activa y sin sesión abierta |

## Criterios de aceptación

1. Factura a crédito de C$ 45.000 (saldo C$ 30.000) de Ferretería El Progreso: devolución de 1 de 3 unidades por C$ 5.000 con `ApplyToInvoice` deja saldo C$ 25.000, sube el disponible de su línea de crédito en C$ 5.000 y devuelve la unidad a `SUC-LEO`.
2. Venta de contado al consumidor final: devolución de 1 rollo con `CashRefund` emite la nota, registra el reembolso en efectivo (y el `CashOut` si la caja está activa), y **no** cambia saldo ni línea de crédito. La nota muestra "Devolución reembolsada en efectivo".
3. Las dos notas reducen la venta en el libro de ventas y el margen del período.
4. Una segunda devolución que supere lo pendiente por línea → 409 con el detalle.
5. `ApplyToInvoice` sobre una factura pagada → 409 `CREDIT_NOTE_EXCEEDS_BALANCE`.
6. Línea devuelta con `Discard` no mueve existencias y queda en la bitácora.
7. Devolver todas las unidades marca la factura `IsFullyReturned`.
8. Si falla el movimiento de existencias, no queda nota emitida ni número consumido.
9. Unitarios: cantidades devolvibles, cálculo de la nota con impuestos copiados, las dos liquidaciones, efectos en saldo, crédito, caja y existencias. Integración: flujos de los criterios 1 y 2 y la devolución desde el punto de venta.

## Decisiones

- **Las dos formas reducen la venta**; solo cambia qué se compensa: la deuda del cliente (`ApplyToInvoice`) o el dinero entregado (`CashRefund`). Así el libro de ventas es el mismo en ambos casos.
- **Impuestos copiados de la línea original**, no recalculados: la nota corresponde exactamente a lo facturado aunque las tasas hayan cambiado.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para `JOIN-001`, idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Los cinco motivos de devolución.
- Devolución parcial de Ferretería El Progreso aplicada a su factura a crédito (criterio 1) y devolución del consumidor final reembolsada en efectivo (criterio 2).
