# SPEC 85 — Almacenes y existencias en Leads: entradas por orden de compra, salidas por factura, traslados entre almacenes y kardex

> **Status:** Borrador
> **Depends on:** SPEC 41-post (borrado lógico y bloqueo por uso), SPEC 43-post (menú), **SPEC 57** (catálogo de productos), **SPEC 81** (tipos de documento y configuración), **SPEC 83** (documentos comerciales del lead), SPEC 84 (PDF). Rama de trabajo: `spec-85-lead-warehouses-stock-and-transfers`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que la empresa registre sus almacenes y lleve las existencias de cada producto por almacén con un registro de cada movimiento (kardex). La orden de compra ingresa productos, la cotización indica de qué almacén saldrán, la factura los descuenta al emitirse, la nota de crédito los devuelve y un documento de traslado los mueve entre almacenes.
> **Ajuste (2026-10-24, pedido del usuario):** existencia mínima por producto y por almacén con alerta al bajar ("indicar un campo de producto de existencia mínima que permita ver cuándo hay en sucursales y al bajar emitir una alerta"). Esta spec crea la infraestructura de alertas de Leads (`LeadAlerts`), que SPEC 87 reutiliza para las alertas de crédito.
> **Origen:** pedido del usuario (2026-10-24): "registrar almacenes dentro de Leads para producto; al registrar una orden de compra se ingrese el producto al almacén; cuando se genere una cotización se identifique el almacén; al facturar se descuente del almacén; llevar un log en los productos de las facturas con que fue eliminado; … un documento para mover entre almacenes en el cual se indique todos los datos relevantes: fecha, usuario que traslada, un concepto". Momento del descuento elegido: **al emitir**.

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Dentro de Leads, parametrizado en Administración**, con el mismo interruptor `CompanyCustomers.DocumentsMode` (SPEC 81): en `Disabled` no se mueven existencias, pero se pueden consultar.
2. **Almacenes por empresa** (`Warehouses`): código, nombre, dirección, responsable (usuario), activo. **Un almacén predeterminado** (`WarehouseDefaultCoordinator`).
3. **Solo los productos llevan existencias.** Los de `Kind = Product` las llevan; los servicios (`Kind = Service`) no, y sus líneas no piden almacén.
4. **Existencia = suma de movimientos.** `WarehouseStocks` guarda la cantidad actual por producto y almacén (lectura rápida). Cada cambio se registra como un `StockMovement` **inmutable** (kardex): fecha, usuario, tipo, cantidad (+/−), saldo resultante, documento que lo causó (tipo, número, código público) y concepto. Así cada factura deja en el producto el registro de con qué documento salió la mercancía.
5. **Efecto en existencias por tipo de documento**, en la configuración de SPEC 81 (`StockEffect`: `None`, `In`, `Out`) y en un momento fijo:

   | `BaseKind` | Efecto por defecto | Momento |
   |---|---|---|
   | Orden de compra | `In` (entrada) | Al **aprobar** (la mercancía se recibió) |
   | Factura | `Out` (salida) | Al **emitir** (decisión del usuario) |
   | Nota de crédito | `In` (devolución) | Al emitir, con las cantidades de la nota |
   | Nota de entrega | `None` (configurable a `Out` si la empresa entrega antes de facturar; entonces la factura convertida desde ella no vuelve a descontar) | Al emitir |
   | Cotización, Proforma | `None`: solo indican el almacén | — |

6. **Almacén por línea:** cada línea de producto de un documento lleva `WarehouseId` (por defecto, el predeterminado). La cotización muestra la existencia disponible del almacén elegido, sin reservar. Al convertir (Cotización → Proforma → Factura) el almacén se conserva.
7. **Sin existencias negativas por defecto** (`CompanyCustomers.AllowNegativeStock = false`): emitir una factura sin existencias suficientes responde 409 `INSUFFICIENT_STOCK` con el detalle por línea. Si la empresa lo permite, se emite igual y el kardex muestra el saldo negativo.
8. **Traslado entre almacenes** (`WarehouseTransfers`): documento con número de control (tipo `BaseKind = WarehouseTransfer`, numeración, encabezado y pie de SPEC 81), fecha, **usuario que traslada**, almacén de origen y destino, **concepto** obligatorio, notas y líneas (producto, cantidad). Estados: `Draft` → `Completed` (genera la salida del origen y la entrada al destino en una sola transacción) o `Cancelled` (solo borradores). Tiene código público, código de barras y PDF como los demás documentos (SPEC 83, 84).
9. **Ajustes de inventario** (inventario inicial, mermas, conteos): `POST /StockAdjustments` con concepto obligatorio; permiso aparte (`StockAdjustments`).
10. **Concurrencia:** todo movimiento bloquea la fila `WarehouseStocks` del producto y almacén (`SELECT … FOR UPDATE`, convención 8) antes de validar y aplicar.
11. **Existencia mínima:** `Catalog.Products.MinimumStock?` es el mínimo general del producto. Cada almacén puede tener su propio mínimo (`WarehouseStocks.MinimumStock?`), que prevalece. Los almacenes representan también las **sucursales**: la vista de existencias muestra, por producto, la cantidad, el mínimo y el estado en cada almacén o sucursal (`Ok`, `Low` = en el mínimo o por debajo, `Out` = cero o menos).
12. **Alerta de existencia baja:** cuando un movimiento deja la existencia de un producto en un almacén **en su mínimo o por debajo**, se genera la alerta `LowStock`; si llega a cero o menos, `OutOfStock`. No se repite mientras siga por debajo. Se resuelve sola cuando la existencia vuelve a superar el mínimo (por ejemplo, con una orden de compra o un traslado). Cambiar el mínimo también evalúa la alerta.
13. **Alertas de Leads (infraestructura compartida):** las alertas se guardan en `LeadAlerts`, con categoría (`Stock`, y `Credit` desde SPEC 87), tipo, estado (`Open` → `Acknowledged` → `Resolved`) y referencias (producto, almacén, cliente, documento). Los **destinatarios** se configuran en Administración por tipo de alerta: usuarios concretos y/o un rol (`LeadAlertRecipientRules`). Cada alerta nueva aparece en el panel de sus destinatarios y se les **envía un correo** (`IEmailService`) después del commit. El fallo del correo no bloquea la operación y queda registrado en la entrega.
14. **Panel de existencias** (`StockPanel`): opción de menú del módulo Clientes que solo ven los roles con permiso (`RoleSystemOption`). Muestra mis alertas de existencias abiertas, los productos bajo el mínimo por almacén o sucursal y los productos sin existencias.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `Warehouses` (`BaseTenantEntity`): `Code` (máx. 20), `Name` (máx. 100), `Address?`, `ResponsibleUserId?`, `IsDefault`, `IsActive`. Índices únicos filtrados `(CompanyId, Code)` y `(CompanyId)` con `"isdefault" = TRUE AND "gcrecord" = 0`.
2. `WarehouseStocks` (`BaseTenantEntity`): `WarehouseId`, `ProductId`, `Quantity` (`numeric(18,4)`), `UpdatedAt`. Índice único `(WarehouseId, ProductId)`.
3. `StockMovements` (solo inserción): `WarehouseId`, `ProductId`, `MovementType` (`PurchaseReceipt = 1`, `SaleIssue = 2`, `SaleReturn = 3`, `TransferOut = 4`, `TransferIn = 5`, `AdjustmentIn = 6`, `AdjustmentOut = 7`, `DeliveryIssue = 8`), `Quantity` (con signo), `BalanceAfter`, `SourceKind` (`CommercialDocument`, `WarehouseTransfer`, `Adjustment`), `SourceId`, `SourceNumber?`, `SourcePublicCode?`, `Concept?` (máx. 300), `UserId`, `OccurredAt`. Índices `(CompanyId, ProductId, OccurredAt)` y `(CompanyId, WarehouseId, OccurredAt)`.
4. `WarehouseTransfers` (`BaseTenantEntity`): `DocumentTypeId`, `Number?`, `PublicCode` (índice único global, como SPEC 83), `Status`, `TransferDate` (`date`), `TransferredByUserId`, `FromWarehouseId`, `ToWarehouseId` (distintos), `Concept` (máx. 300), `Notes?`, `CompletedAt?`, `PdfStorageKey?`. `WarehouseTransferLines`: `TransferId`, `ProductId`, `Quantity` (`> 0`). Eventos en una bitácora propia (`WarehouseTransferEvents`).
5. `LeadDocumentTypeConfigurations` (SPEC 81) suma `StockEffect`. `LeadCommercialDocumentLines` (SPEC 83) suma `WarehouseId?`. `CompanyCustomers` suma `AllowNegativeStock`.
6. `Catalog.Products` suma `MinimumStock?` (`numeric(18,4)`, `>= 0`). `WarehouseStocks` suma `MinimumStock?` y `StockStatus` (`Ok`, `Low`, `Out`, guardado para filtrar rápido).
7. `LeadAlerts` (`BaseTenantEntity`): `Category` (`Stock = 1`, `Credit = 2`), `AlertType` (`LowStock = 1`, `OutOfStock = 2`; SPEC 87 suma los de crédito), `ProductId?`, `WarehouseId?`, `CustomerId?`, `DocumentId?`, `Message`, `Quantity?`, `Threshold?`, `Status`, `CreatedAt`, `AcknowledgedAt?`, `AcknowledgedByUserId?`, `ResolvedAt?`. Índice para no duplicar alertas abiertas: único filtrado `(CompanyId, AlertType, ProductId, WarehouseId)` con `"status" <> 3 AND "gcrecord" = 0` y `.AreNullsDistinct(false)`.
8. `LeadAlertRecipientRules` (`BaseTenantEntity`): `AlertType`, `UserId?`, `RoleId?` (uno de los dos), `IncludeResponsibleUser` (el responsable del almacén en las de existencias; el responsable de cobro en las de crédito). `LeadAlertDeliveries`: `AlertId`, `UserId`, `EmailStatus`, `EmailSentAt?`, `ReadAt?`.
9. `DocumentTypes.BaseKind` suma `WarehouseTransfer = 7` (seed `TRANSFER` / Traslado entre almacenes, prefijo `TRA-{YYYY}-`, dirección interna).

**B. Endpoints**
10. Administración: `GET/POST/PUT/DELETE /api/v1/Warehouses` + `restore` + `set-default` (no se borra un almacén con existencias distintas de cero ni con movimientos: `WAREHOUSE_IN_USE`, solo se desactiva).
11. Existencias (permiso `Stock`): `GET /api/v1/Stock?warehouseId=&productId=&search=&onlyWithStock=` (paginado, Dapper); `GET /api/v1/Stock/products/{productId}` (existencias del producto por almacén); `GET /api/v1/Stock/movements?productId=&warehouseId=&from=&to=&movementType=` (kardex paginado, con documento y usuario). Exportación CSV del kardex (`[RequirePermission(CanExport)]`).
12. Traslados (permiso `WarehouseTransfers`): CRUD de borradores, `POST /{id}/complete`, `POST /{id}/cancel`, `GET /{id}/pdf`, `GET /by-public-code/{code}`.
13. Ajustes (permiso `StockAdjustments`): `POST /api/v1/StockAdjustments` `{ warehouseId, concept, lines[{ productId, quantity (con signo) }] }`.
14. Los documentos de SPEC 83 aplican el efecto configurado al emitir o aprobar, dentro de la misma transacción. Al anular una factura, la nota de crédito automática devuelve las unidades a los almacenes de las líneas originales.
15. `products-lookup` del lead y el editor de documentos devuelven la existencia por almacén de cada producto, con su mínimo y su estado.
16. Mínimos: `PUT /api/v1/Products/{id}` acepta `MinimumStock`; `PUT /api/v1/Stock/minimums` `{ items[{ productId, warehouseId, minimumStock? }] }` fija o quita el mínimo por almacén (máx. 500). `GET /api/v1/Stock` suma el filtro `status` (`low`, `out`) y la vista `GET /api/v1/Stock/products/{productId}` muestra cada almacén o sucursal con cantidad, mínimo y estado.
17. Alertas (`LeadAlertService` en Application, compartido con SPEC 87): `GET /api/v1/LeadAlerts?category=&status=&type=` (las del usuario y sus roles), `POST /api/v1/LeadAlerts/{id}/acknowledge`. Panel (permiso `StockPanel`): `GET /api/v1/StockPanel/summary`.
18. Administración: `GET/PUT /api/v1/LeadAlertRecipientRules` (por tipo de alerta).

**C. Menú**
19. Administración: `Warehouses` → `/administration/warehouses`, `LeadAlertRecipients` → `/administration/alert-recipients`. Clientes: `StockPanel` → `/customers/stock-panel` (seed: SuperAdmin, Admin y Manager; la empresa lo asigna a otros roles). Clientes, además: `Stock` → `/customers/stock`, `WarehouseTransfers` → `/customers/warehouse-transfers`, `StockAdjustments` → `/customers/stock-adjustments`. Permisos: SuperAdmin y Admin con todo; Manager lectura de existencias y traslados.

**Out of scope:**
- Reservas por cotización, lotes, números de serie, ubicaciones dentro del almacén y costo promedio o valorización de inventario (módulo completo de Inventario, SPEC 999 pospuesta).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `WAREHOUSE_NOT_FOUND` / `WAREHOUSE_INACTIVE` | 404 / 409 | Almacén inexistente, de otra empresa o inactivo |
| `WAREHOUSE_IN_USE` | 409 | Borrar un almacén con existencias o movimientos |
| `WAREHOUSE_REQUIRED` | 400 | Línea de producto sin almacén y sin almacén predeterminado |
| `INSUFFICIENT_STOCK` | 409 | Salida mayor que la existencia sin `AllowNegativeStock` (detalle por línea) |
| `TRANSFER_SAME_WAREHOUSE` | 400 | Origen y destino iguales |
| `TRANSFER_NOT_EDITABLE` | 409 | Editar o completar un traslado que no está en `Draft` |
| `STOCK_CONCEPT_REQUIRED` | 400 | Ajuste o traslado sin concepto |
| `INVALID_MINIMUM_STOCK` | 400 | Mínimo negativo |
| `ALERT_RECIPIENT_INVALID` | 400 | Regla sin usuario ni rol, o con ambos |
| `LEAD_ALERT_NOT_FOUND` | 404 | Alerta inexistente o de la que el usuario no es destinatario |

## Criterios de aceptación

1. Aprobar una orden de compra de 10 unidades en el almacén A deja la existencia en 10 y un movimiento `PurchaseReceipt` con el número y el código público de la orden.
2. Emitir una factura de 3 unidades del almacén A deja 7 y un `SaleIssue` con el número de la factura. Emitir otra de 8 → 409 `INSUFFICIENT_STOCK`.
3. Anular la factura devuelve 3 unidades con `SaleReturn` y la referencia a la nota de crédito.
4. Un traslado completado de 5 unidades de A a B genera `TransferOut` y `TransferIn` con la fecha, el usuario y el concepto, y su PDF muestra todos esos datos.
5. Dos facturas concurrentes que juntas superan la existencia: una se emite y la otra → 409.
6. Una línea de servicio no pide almacén ni mueve existencias.
7. El kardex del producto muestra todos los movimientos en orden y el saldo de cada uno coincide con `WarehouseStocks`.
8. Producto con mínimo general 10 y mínimo 3 en la sucursal B: con 12 en A y 4 en B, una venta de 2 en A deja 10 y genera `LowStock` para A (destinatarios configurados, panel y correo). Una venta de 1 en B deja 3 y genera `LowStock` para B. Otra venta de 1 en A no repite la alerta. Una entrada que deja A en 15 la resuelve.
9. Bajar a 0 genera `OutOfStock`. La vista del producto muestra cada almacén con cantidad, mínimo y estado.
10. Un rol sin permiso `StockPanel` no ve el panel (403). Al darle el permiso, lo ve.
11. Unitarios: aplicación de efectos por tipo, validación de existencias, traslados, ajustes, mínimos y reglas de alerta (sin repetir, resolución, destinatarios por usuario y rol). Integración: flujo compra → cotización → factura → anulación, y concurrencia.

## Decisiones

- **Existencia guardada más kardex inmutable**, en lugar de calcularla sumando movimientos en cada lectura: lectura rápida y una fila que bloquear al validar.
- **El traslado es una entidad propia** (no un `LeadCommercialDocument`): no tiene lead, receptor ni importes, pero usa la misma numeración, el código público y el PDF de la serie.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para `JOIN-001`, idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Almacenes: `BOD-MGA` "Bodega Central Managua" (predeterminado, responsable: usuario administrador), `SUC-LEO` "Sucursal León", `SUC-GRA` "Sucursal Granada".
- Productos con existencias (precios de referencia en córdobas; nuevos en el seed de `JOIN-001`): `LAP-14` Laptop 14" (mínimo 5), `IMP-TER80` Impresora térmica 80 mm (mínimo 3), `LEC-CB` Lector de código de barras (mínimo 5), `PAP-80` Rollo de papel térmico 80 mm (mínimo 50), `GAV-DIN` Gaveta de dinero (mínimo 2). En `SUC-GRA`, mínimo propio de 10 para `PAP-80`.
- Movimientos: orden de compra recibida en `BOD-MGA` al proveedor **Distribuidora Tecnológica de Centroamérica, S.A.** (`J0310000000101`, ficticio); traslado `BOD-MGA → SUC-LEO` con concepto "Abastecimiento de sucursal"; ajuste de inventario inicial en `SUC-GRA`. `PAP-80` queda bajo el mínimo en `SUC-GRA` para mostrar una alerta `LowStock`.
- Reglas de alerta: `LowStock` y `OutOfStock` al rol Admin e `IncludeResponsibleUser`.
