# SPEC 90 — Costo del producto y margen: costo de última compra, historial de costos, margen por venta e inventario valorizado

> **Status:** Borrador
> **Depends on:** SPEC 56 (moneda base y tasas), SPEC 57 (productos), **SPEC 81** (`CompanyCustomers`), **SPEC 83** (documentos), **SPEC 85** (existencias y kardex), SPEC 89 (punto de venta). Rama de trabajo: `spec-90-product-cost-and-margin`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Saber cuánto cuesta cada producto, cuánto se gana en cada venta y cuánto vale el inventario. El costo sale de la **última compra** (orden de compra recibida). Cada línea vendida guarda su costo y su margen.
> **Ajuste por SPEC 98 (2026-10-24):** la venta bajo costo bloqueada se desbloquea con la solicitud de aprobación de descuento (SPEC 98).
> **Origen:** pedido del usuario (2026-10-24), punto 3 de la revisión para empezar a operar: "costo de última compra (desde orden de compra) y margen por venta".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Costo en moneda base** de la empresa (SPEC 56). `Catalog.Products` guarda `LastCost`, `LastCostDate` y `LastCostSourceId` (la orden de compra).
2. **Origen del costo:** al aprobar (recibir) una orden de compra (SPEC 85), cada línea de producto actualiza `LastCost` con su precio unitario neto después del descuento, **sin impuestos recuperables**, convertido a moneda base con la tasa de la orden.
3. **Costo manual:** los servicios y los productos sin compras pueden tener un `StandardCost` editable. El costo efectivo es `LastCost` si existe, si no `StandardCost`, si no cero (y el margen se marca "sin costo").
4. **Historial de costos** (`ProductCostHistory`, solo inserción): cada cambio con el costo anterior y el nuevo, el origen (orden de compra o ajuste manual), el usuario y la fecha.
5. **Costo y margen por línea vendida:** al emitir una factura o nota de crédito (SPEC 83), cada línea guarda `UnitCost` (costo efectivo del momento), `CostAmount` (costo × cantidad) y `MarginAmount` (neto de la línea en moneda base − costo). El documento guarda `CostTotal` y `MarginTotal`. Un cambio de costo posterior **no altera** documentos emitidos.
6. **Visibilidad:** costos y márgenes solo los ven los roles con el permiso `ProductCosts`. El cajero del punto de venta (SPEC 89) no los ve.
7. **Alerta de venta bajo costo (opcional):** `CompanyCustomers.WarnSaleBelowCost` (por defecto `true`). Una línea con precio neto menor que su costo muestra una advertencia al vender. Si además `BlockSaleBelowCost = true`, se bloquea hasta que se apruebe como descuento sobre el límite (SPEC 98).
8. **Inventario valorizado** = existencia × costo efectivo, por almacén o sucursal y total.

## Scope

**In:**

**A. Esquema**
1. `Catalog.Products` suma `LastCost?`, `LastCostDate?`, `LastCostSourceId?`, `StandardCost?` (todos `numeric(18,4)` salvo fecha y id).
2. `Customer.ProductCostHistory` (solo inserción): `ProductId`, `PreviousCost?`, `NewCost`, `Source` (`PurchaseOrder = 1`, `Manual = 2`), `SourceDocumentId?`, `SourceNumber?`, `UserId`, `OccurredAt`.
3. `Customer.LeadCommercialDocumentLines` suma `UnitCost?`, `CostAmount?`, `MarginAmount?`. `LeadCommercialDocuments` suma `CostTotal?` y `MarginTotal?`.
4. `Customer.CompanyCustomers` suma `WarnSaleBelowCost` y `BlockSaleBelowCost`.

**B. Endpoints**
5. `PUT /api/v1/Products/{id}/standard-cost` `{ standardCost, reason? }` (permiso `ProductCosts`, `CanUpdate`).
6. `GET /api/v1/Products/{id}/cost-history` (permiso `ProductCosts`).
7. `GET /api/v1/Products` y `GET /{id}` suman `EffectiveCost`, `LastCost`, `LastCostDate`, `StandardCost` y el margen sobre `ReferencePrice`, solo si el usuario tiene `ProductCosts`.
8. Reportes (permiso `ProductCosts`, exportables a CSV con `CanExport`):
   - `GET /api/v1/Reports/sales-margin?from=&to=&groupBy=product|category|seller|customer|warehouse|day`: ventas netas, costo, margen y % de margen (facturas menos notas de crédito).
   - `GET /api/v1/Reports/inventory-valuation?warehouseId=&asOf=`: existencia × costo efectivo por producto y almacén, con total. `asOf` reconstruye la existencia desde el kardex (SPEC 85) y usa el costo vigente en esa fecha según el historial.

**C. Menú**
9. Clientes: `SalesMarginReport` → `/customers/reports/sales-margin`, `InventoryValuationReport` → `/customers/reports/inventory-valuation`. Permiso nuevo `ProductCosts`: SuperAdmin, Admin y Manager.

**Out of scope:**
- Costo promedio ponderado, FIFO y costos de importación prorrateados (flete, aduana): módulo completo de Inventario.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_COST` | 400 | Costo negativo |
| `SALE_BELOW_COST` | 409 | Línea bajo costo con `BlockSaleBelowCost` y sin autorización |

## Criterios de aceptación

1. Recibir una orden de compra en USD con precio 10,00 y tasa 36,6243 deja `LastCost` = C$ 366,24 y un registro en el historial.
2. Una factura emitida guarda el costo y el margen por línea. Cambiar el costo después no altera esa factura.
3. Un usuario sin `ProductCosts` no ve costos en productos, documentos, punto de venta ni reportes (las columnas no vienen en la respuesta).
4. El reporte de margen de un período cuadra con la suma de las líneas de facturas menos notas de crédito.
5. El inventario valorizado a una fecha pasada usa la existencia y el costo de esa fecha.
6. Unitarios: actualización de costo, costo efectivo, margen por línea, advertencia y bloqueo bajo costo. Integración: compra → venta → reporte de margen.

## Decisiones

- **Costo de última compra** (decisión del usuario): simple y suficiente para empezar. El modelo guarda el costo por línea vendida, así que pasar a costo promedio más adelante no cambia los documentos emitidos.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para `JOIN-001`, idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- `LastCost` de los productos con existencias tomado de la orden de compra del seed (en USD convertidos a NIO), `StandardCost` en los servicios (`CAP-HR`, `SOP-12`, `MNT-PRE`) y las facturas del seed con costo y margen por línea.
