# SPEC 89 — Punto de venta: venta rápida sin lead, consumidor final, convertir cotizaciones y cobrar facturas, con caja opcional

> **Status:** Borrador
> **Depends on:** SPEC 55 (clientes), SPEC 57 (productos), **SPEC 81** (`CompanyCustomers`, tipos y configuración), **SPEC 82** (impuestos), **SPEC 83** (documentos), **SPEC 84** (PDF y envío), **SPEC 85** (existencias), **SPEC 86** (pagos), **SPEC 87** (crédito), **SPEC 88** (perfil fiscal y numeración autorizada), SPEC 90 (costo de la línea, si se implementa antes). Rama de trabajo: `spec-89-point-of-sale-quick-sale-and-collection`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Una pantalla de **venta rápida** con forma de factura para vender en mostrador sin crear un lead, a un cliente identificado o al **consumidor final**. La misma pantalla carga una **cotización o proforma** (por número, código público o código de barras) y la convierte en factura si sigue vigente, o carga una **factura ya emitida en un lead** para cobrarla. La caja con apertura, cierre y arqueo es **opcional**: solo se exige si la empresa la activa.
> **Origen:** pedido del usuario (2026-10-24): "una pantalla que pueda vender, que simule una factura, para realización de venta rápida, o pueda recibir un presupuesto que lo pueda volver factura si es válido, o una factura realizada en un lead para proceder al cobro"; "la opción de solo cobrar, pero que no sea obligatoria [la caja] a menos que la parametrice".
> **Valores de enums (revisión de consistencia):** `Origin`: `Lead = 1`, `PointOfSale = 2`; `PosReceiptFormat`: `Document = 1`, `Ticket80mm = 2`; `CashSessions.Status`: `Open = 1`, `Closed = 2`; `CashMovements.MovementType`: `CashIn = 1`, `CashOut = 2`.
> **Ajuste por verificación de consistencia (2026-10-24):** `CashSessions`, `CashMovements` y los pagos de la venta registran su `IBaseCurrencyAmountSource` (SPEC 56).
> **Ajuste por revisión de consistencia (2026-10-24):** (1) Moneda de la venta: por defecto la moneda local del perfil fiscal (SPEC 88) o, sin perfil, la base; el cajero puede elegir otra moneda activa de la empresa. (2) El almacén sale de la caja o del predeterminado de la empresa (no hay almacén por usuario). (3) Recursos sin pantalla declarados. (4) Fuente de activación de cliente: `CustomerStatusSource.CommercialDocument = 6`. (5) Seed en `JOIN-NI-001` (SPEC 100).
> **Ajuste por SPEC 98 (2026-10-24):** el límite de descuento es por rol, con solicitud de aprobación; se quitan `PosMaxDiscountPercent` y `PosDiscountOverride`.
> **Ajuste por SPEC 94 (2026-10-24):** la venta sugiere la constancia vigente del cliente; aplicarla exige `TaxExemptionsApply`. El consumidor final no admite exención.
> **Ajuste por SPEC 93 (2026-10-24):** la pantalla y el ticket muestran el bloque de totales de SPEC 93. Las retenciones del cliente se aplican solas; el consumidor final no tiene.
> **Ajuste por SPEC 92 (2026-10-24):** el punto de venta registra devoluciones parciales (`POST /PointOfSale/returns`), con reembolso en efectivo desde la caja si está activa.
> **Cambia SPEC 83:** los documentos dejan de exigir un lead. `LeadCommercialDocuments.LeadId` pasa a opcional y se agrega `Origin` (`Lead = 1`, `PointOfSale = 2`).

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Venta directa sin lead:** el documento nace en el punto de venta (`Origin = PointOfSale`), sin `LeadId`. Usa la misma numeración, impuestos, existencias, PDF, envío y bitácora que SPEC 83–88. No gana leads porque no hay lead. Si la persona tiene cliente, lo confirma igual que la aprobación de SPEC 83 (`ApprovalConfirmsCustomer`).
2. **Consumidor final:** persona genérica por empresa (`CompanyCustomers.FinalConsumerPersonId`), sembrada con el nombre "Consumidor final" y sin identificación. Reglas:
   - no se le vende a crédito (`FINAL_CONSUMER_CREDIT_NOT_ALLOWED`);
   - no se le exige identificación tributaria, aunque el perfil fiscal la pida para las facturas (SPEC 88);
   - no se activa como cliente ni entra en el histórico de clientes;
   - el cajero puede escribir un nombre o una identificación opcional solo para imprimir (`RecipientDisplayName?`, `RecipientDisplayTaxId?`), sin crear una persona.
3. **Tres formas de empezar en la misma pantalla:**
   - **Venta nueva:** borrador de factura con el consumidor final o con una persona buscada (nombre, identificación o código de cliente). Las líneas se agregan por búsqueda o por **lector de código de barras** del producto (`Catalog.Products.Barcode?`, nuevo campo opcional).
   - **Desde una cotización o proforma:** se busca por número, código público o código de barras del documento (SPEC 83). Si está `Issued` o `Approved`, **no vencida** y su tipo permite convertir a factura (`ConvertibleTo`, SPEC 81), crea el borrador de factura con sus líneas (conversión de SPEC 83). Vencida → 409 `DOCUMENT_EXPIRED`, con opción de **actualizar precios** y crear una venta nueva con las mismas líneas.
   - **Cobrar una factura existente:** se busca por número, código público o código de barras. Si es una factura emitida con saldo (SPEC 86), muestra el saldo y registra el pago. Sirve para las facturas emitidas en un lead.
4. **Un solo paso "Cobrar":** en una venta nueva o convertida, *Cobrar* emite la factura, la aprueba si su tipo lo exige, descuenta existencias (SPEC 85) y registra los pagos, todo en **una transacción**. Si algo falla, no queda nada emitido ni número consumido.
5. **Pagos mixtos:** varios métodos en una misma venta (ej. efectivo + tarjeta). En efectivo se registra el **monto recibido** y se calcula el **vuelto**. La suma de pagos debe cubrir el total al contado (`PAYMENT_INSUFFICIENT`). La venta **a crédito** solo con cliente identificado con línea de crédito (SPEC 87), con su plan de cuotas (SPEC 86).
6. **Caja opcional** (`CompanyCustomers.RequireCashSession`, por defecto `false`):
   - `false`: el punto de venta solo cobra. No hay apertura ni cierre.
   - `true`: para cobrar en el punto de venta, el usuario debe tener una **sesión de caja abierta** (`CashSessions`) en una **caja** (`CashRegisters`, por empresa, con su almacén predeterminado). La apertura registra el fondo inicial. El cierre registra lo contado por método de pago. El sistema compara lo esperado (fondo + cobros − vueltos − retiros) contra lo contado y guarda la diferencia. También se registran **entradas y retiros** de efectivo con concepto. Una sesión cerrada no se modifica.
   - Con la caja activa, los cobros hechos fuera del punto de venta (SPEC 86) no se exigen en caja, salvo que se marquen como cobro en caja.
7. **Almacén del punto de venta:** la venta descuenta del almacén de la caja o, sin caja, del almacén predeterminado de la empresa (SPEC 85). La línea muestra la existencia y avisa si está bajo el mínimo.
8. **Impresión:** al cobrar, devuelve el PDF de la factura (SPEC 84) en tamaño carta o A4 y, opcionalmente, un **ticket** de 80 mm (`CompanyCustomers.PosReceiptFormat`: `Document` o `Ticket80mm`) con el mismo contenido resumido, el código público y el código de barras.
9. **Permisos:** `PointOfSale` (vender y cobrar), `CashSessions` (abrir y cerrar su propia caja), `CashSessionsAdmin` (ver todas las sesiones y reabrir con motivo). Descuentos: límite por rol con solicitud de aprobación (SPEC 98).

## Scope

**In:**

**A. Esquema (`Customer`, salvo `Catalog.Products`)**
1. `LeadCommercialDocuments` (SPEC 83): `LeadId` pasa a nullable; suma `Origin`, `RecipientDisplayName?`, `RecipientDisplayTaxId?`, `CashSessionId?`. `CHECK`: `Origin = Lead` exige `LeadId`.
2. `InvoicePayments` (SPEC 86) suma `CashSessionId?`, `ReceivedAmount?` y `ChangeAmount?`.
3. `CompanyCustomers` (SPEC 81) suma `FinalConsumerPersonId?`, `RequireCashSession` (por defecto `false`), `PosReceiptFormat`.
4. `Catalog.Products` suma `Barcode?` (máx. 50; índice único filtrado `(CompanyId, Barcode)` con `"barcode" IS NOT NULL AND "gcrecord" = 0`).
5. `CashRegisters` (`BaseTenantEntity`): `Code`, `Name`, `WarehouseId?`, `IsActive`.
6. `CashSessions` (`BaseTenantEntity`): `CashRegisterId`, `OpenedByUserId`, `OpenedAt`, `OpeningAmount`, `ClosedByUserId?`, `ClosedAt?`, `Status` (`Open`, `Closed`), `ExpectedCash`, `CountedCash?`, `Difference?`, `Notes?`. Una sesión abierta por caja y una por usuario (índices únicos filtrados `"status" = 1`).
7. `CashSessionCounts`: `CashSessionId`, `PaymentMethodId`, `Expected`, `Counted`, `Difference`.
8. `CashMovements` (solo inserción): `CashSessionId`, `MovementType` (`CashIn`, `CashOut`), `Amount`, `Concept`, `UserId`, `OccurredAt`.

**B. Endpoints**
9. Punto de venta (permiso `PointOfSale`):
   - `GET /api/v1/PointOfSale/lookup?code=`: busca por número, código público o código de barras. Devuelve qué es (cotización, proforma, factura o producto), su estado y la acción posible (`Convert`, `Collect`, `AddLine`).
   - `POST /api/v1/PointOfSale/sales`: crea el borrador de venta directa `{ personId? (por defecto consumidor final), recipientDisplayName?, recipientDisplayTaxId?, warehouseId?, lines[] }`. `PUT /sales/{id}` lo actualiza.
   - `POST /api/v1/PointOfSale/sales/from-document/{documentId}`: borrador de factura desde una cotización o proforma vigente. `POST /api/v1/PointOfSale/sales/from-document/{documentId}/refresh-prices`: venta nueva con precios actuales.
   - `POST /api/v1/PointOfSale/sales/{id}/checkout` `{ paymentTerms, payments[{ paymentMethodId, amount, receivedAmount?, reference? }], installmentPlan? }`: emite, aprueba, descuenta y cobra en una transacción. Devuelve la factura, el vuelto y los enlaces al PDF y al ticket.
   - `POST /api/v1/PointOfSale/invoices/{documentId}/collect` `{ payments[] }`: cobra el saldo de una factura emitida.
   - `GET /api/v1/PointOfSale/sales/{id}/ticket`: ticket de 80 mm (PDF).
10. Caja (permiso `CashSessions`): `POST /api/v1/CashSessions/open` `{ cashRegisterId, openingAmount }`, `GET /current`, `POST /{id}/movements` `{ type, amount, concept }`, `POST /{id}/close` `{ counts[{ paymentMethodId, counted }], notes? }`, `GET /{id}/report` (PDF del cierre). Administración (`CashSessionsAdmin`): `GET /api/v1/CashSessions` (filtros: caja, usuario, fechas, con diferencia), `POST /{id}/reopen` `{ reason }`.
11. Administración: `GET/POST/PUT/DELETE /api/v1/CashRegisters`.

**C. Menú**
12. Clientes: `PointOfSale` → `/customers/point-of-sale`, `CashSessions` → `/customers/cash-sessions`. Administración: `CashRegisters` → `/administration/cash-registers`. Recurso sin pantalla: `CashSessionsAdmin`. Seed de permisos: SuperAdmin, Admin y Manager con todo; Supervisor lectura; roles de venta con `PointOfSale` y `CashSessions`.

**Out of scope:**
- Funcionamiento sin conexión (offline) y hardware fiscal (impresoras fiscales, gavetas).
- Devoluciones: SPEC 92.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `DOCUMENT_EXPIRED` | 409 | Convertir una cotización o proforma vencida |
| `DOCUMENT_NOT_CONVERTIBLE` | 409 | El documento no está emitido o aprobado, o su tipo no se convierte a factura |
| `INVOICE_NOT_COLLECTABLE` | 409 | Cobrar algo que no es una factura emitida con saldo |
| `PAYMENT_INSUFFICIENT` | 400 | Los pagos al contado no cubren el total |
| `FINAL_CONSUMER_CREDIT_NOT_ALLOWED` | 409 | Venta a crédito al consumidor final |
| `CASH_SESSION_REQUIRED` | 409 | Cobrar sin sesión abierta con `RequireCashSession = true` |
| `CASH_SESSION_ALREADY_OPEN` | 409 | Abrir una segunda sesión en la caja o para el usuario |
| `CASH_SESSION_CLOSED` | 409 | Registrar movimientos en una sesión cerrada |
| `DISCOUNT_APPROVAL_REQUIRED` | 409 | Cobrar con descuentos pendientes de aprobación (SPEC 98) |
| `PRODUCT_BARCODE_NOT_FOUND` | 404 | Código de barras sin producto |

## Criterios de aceptación

1. Venta al consumidor final de 2 productos con efectivo C$ 1.000 sobre un total de C$ 862,50 emite la factura, descuenta existencias y devuelve vuelto C$ 137,50, sin crear lead ni cliente.
2. Escanear el código de barras de una cotización vigente crea la factura con sus líneas. Con la cotización vencida → 409 y `refresh-prices` crea una venta nueva con precios actuales.
3. Escanear una factura emitida en un lead con saldo permite cobrarla y actualiza su estado de cobro (SPEC 86) y el crédito (SPEC 87).
4. Con `RequireCashSession = false` se cobra sin abrir caja. Con `true`, cobrar sin sesión → 409.
5. Cerrar una sesión con fondo 500, cobros en efectivo 3.000 y retiro 1.000, contando 2.480, guarda una diferencia de −20.
6. Si falla el descuento de existencias en el *checkout*, no queda factura emitida ni número consumido.
7. Unitarios: armado de la venta, conversión con vigencia, pagos mixtos y vuelto, reglas del consumidor final, cálculo del arqueo. Integración: venta completa con y sin caja, conversión y cobro de factura de lead.

## Decisiones

- **Mismo documento que los leads** (`LeadCommercialDocuments` con `Origin`), no una tabla de ventas aparte: numeración, impuestos, existencias, crédito, PDF y libro de ventas (SPEC 91) quedan iguales para las dos vías.
- **Caja apagada por defecto:** una empresa pequeña empieza a vender sin configurar cajas, y la activa cuando la necesita.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Persona "Consumidor final" como `FinalConsumerPersonId`.
- Cajas `CAJA-MGA-01` (almacén `BOD-MGA`) y `CAJA-LEO-01` (almacén `SUC-LEO`), con `RequireCashSession = false`.
- Códigos de barras EAN-13 simulados en los productos con existencias de SPEC 85 (prefijo 743, el de Nicaragua, con números ficticios).
- Una venta de mostrador al consumidor final pagada en efectivo con vuelto, y una sesión de caja cerrada de ejemplo con una diferencia de C$ −20.
