# SPEC 83 — Documentos comerciales en el lead: crear, emitir, aprobar, convertir; código público, código de barras e histórico del cliente

> **Status:** Borrador
> **Depends on:** SPEC 55 (clientes y `ICustomerActivationService`), SPEC 56 (monedas y tasas), SPEC 57 (catálogo de productos), SPEC 58 (identificación), SPEC 59 (motivos y `RequireIdentificationForCustomer`), SPEC 60 (visibilidad de leads), **SPEC 61** (leads y líneas), **SPEC 63** (ganar un lead), **SPEC 81** (tipos de documento y configuración), **SPEC 82** (grupos fiscales y `TaxCalculator`). Rama de trabajo: `spec-83-lead-commercial-documents`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que desde un lead se creen, numeren, emitan, aprueben y conviertan documentos comerciales (cotización, proforma, factura, nota de crédito, orden de compra, nota de entrega) según la configuración de SPEC 81. Cada documento lleva un Id, un código público y un código de barras. Aprobar una factura gana el lead y confirma al cliente, y el cliente tiene su histórico de documentos.
> **Ajuste por revisión de consistencia (2026-10-24):** (1) `LeadId` es opcional (SPEC 89) y el esquema remite a las columnas que suman SPEC 84–98. (2) **Ganar el lead al aprobar:** las líneas propuestas desde el lead guardan `LeadProductId`; al aprobar se pasan como `AcceptedLeadProductIds` solo las que siguen vivas en el lead (las líneas libres o agregadas no cuentan) y el valor ganado lo calcula `WinLeadCommand` como hoy. La activación del cliente por `RelationshipType.OnWin` (SPEC 63) se mantiene; `ApprovalConfirmsCustomer` activa además al cliente si no quedó activo (idempotente). (3) `SellerUserId` se define aquí (SPEC 97 lo usa en paneles). (4) `issue` acepta `issueDate`. (5) Anular una factura con pagos exige liquidar lo pagado. (6) `CustomerStatusSource.CommercialDocument = 6` (nombre neutral: también lo usan las ventas sin lead). (7) Los documentos registran su `IBaseCurrencyAmountSource` (SPEC 56).
> **Ajuste por SPEC 94 (2026-10-24):** un documento puede quedar exento con una constancia de exoneración del cliente, aprobada y vigente, aplicada por un usuario con `TaxExemptionsApply`; se revalida al emitir.
> **Ajuste por SPEC 93 (2026-10-24):** el documento guarda `TotalWithTaxes` (valor de la venta) y `Total` (= total a pagar, después de retenciones), más las retenciones del cliente a nivel de documento (`LeadCommercialDocumentWithholdings`).
> **Ajuste por SPEC 92 (2026-10-24):** además de la nota de crédito total al anular (regla 9), una factura admite notas de crédito **parciales** por devolución, aplicadas a la factura o reembolsadas en efectivo (SPEC 92).
> **Ajuste por SPEC 89 y 90 (2026-10-24):** un documento puede nacer en el punto de venta sin lead (`LeadId` opcional, `Origin = PointOfSale`, consumidor final; SPEC 89). La regla 2 aplica a los documentos con `Origin = Lead`. Las líneas guardan costo y margen al emitir (SPEC 90).
> **Ajuste por SPEC 88 (2026-10-24):** al emitir, el número sale del rango autorizado cuando el perfil fiscal lo exige, se valida la identificación tributaria del receptor y se guarda el equivalente en moneda local. Los documentos llevan `ElectronicStatus` para la factura electrónica futura.
> **Ajuste por SPEC 85, 86 y 87 (2026-10-24):** las líneas de producto llevan almacén y los documentos mueven existencias según su tipo (SPEC 85). Las facturas tienen condición de pago, estado de cobro, pagos y cuotas (SPEC 86), y las ventas a crédito validan la línea de crédito del cliente (SPEC 87). El código de barras se genera solo en SVG con el núcleo de ZXing.Net, sin paquetes de imagen.
> **Origen:** pedido del usuario (2026-10-24): "poder indicar que al aprobar una facturación se da por completado el lead, se registra o confirma al cliente y llevar un histórico de facturas del cliente; quiero el id y un código público que pueda llevar el sistema y generar un código de barras por cada documento".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Solo con el modo `Leads`** (SPEC 81). En `Disabled`, crear, editar, emitir, aprobar o convertir responde 409 `LEAD_DOCUMENTS_DISABLED`. Consultar y descargar documentos existentes siempre se permite.
2. **Pertenecen a un lead.** Todo documento nace en un lead visible para el usuario (SPEC 60) y toma la persona, el cliente (si existe) y la moneda del lead. El destinatario es la persona del lead; en las órdenes de compra, el destinatario es un proveedor (cualquier persona de la empresa) que se elige al crear.
3. **Identificadores de cada documento:**
   - `Id` (`Guid`), como toda entidad.
   - `PublicCode`: código público generado por el sistema **al crear**, de 12 caracteres alfanuméricos sin ambigüedades (Crockford base32, sin `I`, `L`, `O`, `U`), mostrado como `XXXX-XXXX-XXXX`. Es único en todo el sistema y no es secuencial ni adivinable.
   - `Number`: número de control según la configuración del tipo (ej. `COT-2026-000015`), asignado **al emitir**.
   - **Código de barras Code 128** con el `PublicCode`, que se imprime en el PDF (SPEC 84) y se descarga por separado.
4. **Líneas:** producto del catálogo (activo) o línea libre, cantidad, precio neto, descuento %, grupo fiscal (el del producto o el predeterminado, editable) y **copia de sus impuestos**. Totales con `TaxCalculator` (SPEC 82). Con `ShowsTaxes = false` no se calculan impuestos. Al crear desde el lead, se proponen las líneas `Offered` y `Accepted` (no las `Rejected`).
5. **Leyendas:** al crear se copian las leyendas activas del tipo marcadas por defecto. El usuario puede quitar o agregar leyendas del tipo mientras el documento está en `Draft`. Al emitir se guardan ya renderizadas (texto final).
6. **Estados:** `Draft = 1` (editable, sin número) → `Issued = 2` (numerado, inmutable). Si el tipo tiene `RequiresApproval`, el documento emitido pasa a `Approved = 3` o `Rejected = 4`. `Cancelled = 5` (regla 9). `Expired` no se guarda: se calcula al leer cuando un documento `Issued` tiene `ValidUntil` vencida.
7. **Al emitir** se copian los datos del emisor (`CompanyCustomers`) y del receptor, el encabezado, el pie y las leyendas renderizados, y el desglose de impuestos. Cambios posteriores en la configuración, el emisor, la persona o los impuestos **no alteran** un documento emitido.
8. **Al aprobar**, según la configuración del tipo:
   - `ApprovalConfirmsCustomer`: activa al cliente de la persona con `ICustomerActivationService` (SPEC 55, nueva fuente `CustomerStatusSource.CommercialDocument = 6`); si ya es cliente activo, no cambia nada.
   - `ApprovalCompletesLead`: si el lead está `Open`, se gana con `WinLeadCommand` (SPEC 63) y el `WinLeadReasonId` del tipo. Como productos aceptados se pasan los `LeadProductId` de las líneas del documento que vienen del lead y siguen vivas; las líneas libres o agregadas no cuentan. Si la persona no tiene identificación y la empresa la exige (`RequireIdentificationForCustomer`, SPEC 59), la aprobación pide el tipo y el número de identificación igual que `win` (`IDENTIFICATION_REQUIRED`). Un lead ya ganado no se vuelve a ganar.
   - Todo ocurre en **una sola transacción**: si falla ganar el lead, el documento no queda aprobado.
9. **Anulación:** un documento `Draft` se borra (borrado lógico). Un documento emitido no se borra ni se edita: se **anula** con motivo (`Cancelled`). Si es una factura (`BaseKind = Invoice`) y la empresa tiene activo un tipo `CreditNote`, anularla **emite automáticamente la nota de crédito** total, con las líneas en negativo y `SourceDocumentId` apuntando a la factura. Si la factura tiene pagos, al anularla se elige cómo liquidar lo pagado: **reembolso** (como la devolución en efectivo de SPEC 92) del total pagado; sin esa elección → 409 `CANCELLATION_SETTLEMENT_REQUIRED`. Sin un tipo `CreditNote` activo no se puede anular una factura emitida (`CREDIT_NOTE_TYPE_REQUIRED`).
10. **Conversión:** un documento emitido o aprobado se convierte en otro tipo permitido (`ConvertibleTo`, SPEC 81). Se crea un borrador nuevo con las mismas líneas y `SourceDocumentId`. Ej.: Cotización → Proforma → Factura.
11. **Histórico del cliente:** los documentos de tipos con `CountsInCustomerHistory` (facturas y notas de crédito por defecto) se listan en la ficha del cliente y de la persona, con totales en moneda base y saldo neto (facturas − notas de crédito).
12. **Bitácora:** cada acción queda en `LeadCommercialDocumentEvents` (regla de SPEC 84): creado, editado, emitido, aprobado, rechazado, anulado, convertido, PDF descargado, enviado y envío fallido, con usuario, fecha y detalle. Los eventos no se editan ni se borran.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `LeadCommercialDocuments` (`BaseTenantEntity`; SPEC 84–98 le suman columnas, ver cada una): `DocumentTypeId`, `BaseKind` (copia), `LeadId?` (obligatorio si `Origin = Lead`, SPEC 89), `SellerUserId` (por defecto el dueño del lead; SPEC 97), `PersonId`, `CustomerId?`, `SourceDocumentId?`, `PublicCode` (char 12, **índice único global** sin filtro de empresa), `Number?`, `Status`, `IssueDate?` (`date`), `ValidUntil?`, `DueDate?`, `CurrencyId`, `ExchangeRateToBase`, `Subtotal`, `TaxTotal`, `WithholdingTotal`, `Total`, `TotalBase`, `Notes?`, `InternalNotes?`, `CancellationReason?`, emisor y receptor copiados (`Issuer…`, `Recipient…`), `HeaderHtml?` y `FooterHtml?` renderizados, `IssuedByUserId?`, `IssuedAt?`, `ApprovedByUserId?`, `ApprovedAt?`. Índice único filtrado `(CompanyId, DocumentTypeId, Number)` con `"number" IS NOT NULL AND "gcrecord" = 0`.
2. `LeadCommercialDocumentLines` (`BaseTenantEntity`): `DocumentId`, `LineNumber`, `LeadProductId?` (línea propuesta desde el lead), `ProductId?`, `ProductCode?`, `Description` (máx. 500), `Quantity` (`numeric(18,4)`, distinta de 0; negativa solo en notas de crédito), `UnitPrice` (`numeric(18,4)`), `DiscountPercent`, `NetAmount`, `TaxGroupId?`, `TaxGroupName?`, `SourceLineId?`.
3. `LeadCommercialDocumentLineTaxes`: `LineId`, `TaxId?`, `TaxCode`, `TaxName`, `TaxKind`, `Rate`, `BaseAmount`, `Amount`.
4. `LeadCommercialDocumentTaxSummaries`: desglose por impuesto y tasa, guardado al emitir.
5. `LeadCommercialDocumentLegends`: `DocumentId`, `LegendId?`, `Text` (renderizado al emitir), `SortOrder`.
6. `LeadCommercialDocumentEvents` (solo inserción): `DocumentId`, `EventType`, `UserId`, `OccurredAt`, `Details?` (`jsonb`: destinatarios, motivo, documento convertido, error).

**B. Endpoints (permiso `Leads`, Clientes)**
7. `GET /api/v1/Leads/{leadId}/commercial-documents`: documentos del lead.
8. `POST /api/v1/Leads/{leadId}/commercial-documents` `{ documentTypeId, recipientPersonId? (solo compras), lines?, legendIds?, notes? }`: crea un borrador; sin `lines`, propone las del lead.
9. `GET /api/v1/LeadCommercialDocuments` (paginado, Dapper): `search` (número, código público, receptor; `ILIKE`), `documentTypeId`, `baseKind`, `status` (incluye `Expired`), `leadId`, `personId`, `customerId` y rango de fechas. Visibilidad: documentos con lead según la visibilidad de leads (SPEC 60); documentos sin lead según `SellerUserId` (el vendedor ve los suyos) o el permiso `CommercialDocumentsViewAll`.
10. `GET /api/v1/LeadCommercialDocuments/{id}` con líneas, impuestos, desglose, leyendas, documento de origen, derivados y eventos.
11. `GET /api/v1/LeadCommercialDocuments/by-public-code/{publicCode}`: busca por código público (con o sin guiones) dentro de la empresa. Lo usa el lector de códigos de barras.
12. `PUT /{id}` y `DELETE /{id}` (solo `Draft`). `POST /{id}/issue` `{ issueDate? }` (por defecto hoy en la zona horaria de la empresa; no futura ni anterior a la última emitida del mismo tipo (`INVALID_ISSUE_DATE`); en un período cerrado responde `TAX_PERIOD_LOCKED` (SPEC 91); numeración con `SELECT … FOR UPDATE` sobre la configuración o su contador anual; transaccional). `POST /{id}/approve` `{ identificationTypeId?, identificationNumber?, notes? }` y `POST /{id}/reject` `{ reason }`. `POST /{id}/cancel` `{ reason, refund?: { paymentMethodId, reference? } }` (el reembolso es obligatorio si la factura tiene pagos; exige `CashRefunds` y, con caja activa, sesión abierta, como SPEC 92) (`[RequirePermission(CanDelete)]`). `POST /{id}/convert` `{ toDocumentTypeId }`.
13. `GET /{id}/barcode` (`[RequirePermission(CanDownload)]`): Code 128 con el `PublicCode`, en SVG.
14. `POST /api/v1/LeadCommercialDocuments/preview-totals`: totales de unas líneas sin guardar.
15. Histórico (`[PermissionResource("Leads")]`, `CanRead`): `GET /api/v1/Customers/{id}/commercial-documents` y `GET /api/v1/Persons/{id}/commercial-documents` (paginados, `historyOnly=true` por defecto, con totales en moneda base y saldo).
16. La línea de tiempo del lead (SPEC 61) muestra los eventos `Issued`, `Approved`, `Rejected`, `Cancelled` y `Sent` de sus documentos.

**C. Infraestructura**
17. `IBarcodeGenerator` (Application) → implementación con el **núcleo de ZXing.Net** (Apache 2.0, unos 500 KB) en Infrastructure: codifica Code 128 y dibuja las barras como SVG vectorial, sin paquetes de imagen.
18. `PublicCodeGenerator`: genera el código con `RandomNumberGenerator` y reintenta ante choque del índice único (23505, convención 9).

**D. Menú**
19. Opción en Clientes: `LeadCommercialDocuments` → `/customers/commercial-documents` (`ControllerName = "Leads"`: mismo permiso que los leads). Recurso sin pantalla: `CommercialDocumentsViewAll`.

**Out of scope:**
- Cobros, pagos y cuotas: SPEC 86. Existencias: SPEC 85. Conciliación bancaria: módulo futuro.
- Documentos sin lead: SPEC 89.
- Facturación electrónica por país.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `LEAD_DOCUMENTS_DISABLED` | 409 | Modo `Disabled` |
| `LEAD_COMMERCIAL_DOCUMENT_NOT_FOUND` | 404 | De otra empresa, inexistente o de un lead no visible |
| `LEAD_COMMERCIAL_DOCUMENT_NOT_EDITABLE` | 409 | Editar o borrar uno que no está en `Draft` |
| `INVALID_STATUS_TRANSITION` | 409 | Aprobar sin `RequiresApproval`, aprobar dos veces, etc. |
| `LEAD_DOCUMENT_ISSUER_INCOMPLETE` | 409 | Emitir sin los datos obligatorios del emisor |
| `LEAD_COMMERCIAL_DOCUMENT_EMPTY` | 400 | Emitir sin líneas |
| `DOCUMENT_TYPE_INACTIVE` / `DOCUMENT_TYPE_NOT_CONFIGURED` | 409 | Tipo inactivo o sin configuración activa |
| `CONVERSION_NOT_ALLOWED` | 409 | Conversión no permitida por la configuración |
| `IDENTIFICATION_REQUIRED` | 400 | Aprobar con `ApprovalCompletesLead` cuando la persona no tiene identificación y la empresa la exige |
| `LEAD_LOST` | 409 | Crear un documento en un lead perdido (en un lead ganado sí se permite) |
| `CANCELLATION_SETTLEMENT_REQUIRED` | 409 | Anular una factura con pagos sin indicar el reembolso |
| `CREDIT_NOTE_TYPE_REQUIRED` | 409 | Anular una factura sin un tipo de nota de crédito activo |
| `INVALID_ISSUE_DATE` | 400 | Fecha de emisión futura o anterior a la última emitida del mismo tipo (el período cerrado es `TAX_PERIOD_LOCKED`, SPEC 91) |
| `PRODUCT_NOT_FOUND` / `TAX_GROUP_NOT_FOUND` / `PERSON_NOT_FOUND` | 404 | Referencias inválidas |

## Criterios de aceptación

1. En modo `Disabled`, crear → 409. Pasar a `Disabled` deja leer y descargar los documentos ya creados.
2. Cada documento tiene un `PublicCode` único de 12 caracteres desde que se crea, y `by-public-code` lo encuentra con o sin guiones. El SVG del código de barras decodifica al mismo código.
3. Dos emisiones concurrentes del mismo tipo obtienen números consecutivos distintos, y los borradores borrados no dejan huecos.
4. Aprobar una factura de un lead abierto gana el lead (con el motivo del tipo y las líneas como aceptadas) y crea o activa al cliente en la misma transacción. Si falla el `win`, el documento sigue `Issued`.
5. Anular una factura aprobada emite la nota de crédito con importes negativos, y el saldo del histórico del cliente queda en cero.
6. Cotización → Proforma → Factura conserva líneas y `SourceDocumentId`, y una conversión no configurada → 409.
7. Cambiar la tasa de un impuesto o una leyenda después de emitir no altera el documento emitido.
8. El histórico del cliente lista solo los tipos con `CountsInCustomerHistory`.
9. Unitarios: handlers, validadores, numeración (formato y reinicio anual), código público, transiciones y efectos de aprobar. Integración: flujo completo desde un lead, concurrencia y modo `Disabled`.

## Decisiones

- **Código público aleatorio y no secuencial**, separado del número de control: se puede imprimir y escanear sin revelar volumen ni permitir adivinar documentos de otros.
- **Code 128** (lineal), que leen los lectores de mano habituales. Si más adelante se quiere enlazar a una verificación pública, se agrega QR sobre el mismo código.
- **Anular factura = nota de crédito automática**: una factura emitida nunca desaparece; se compensa.
- **Bitácora propia** en lugar de `Security.AuditLogs`: esa tabla solo cubre las seis entidades de seguridad (`AuditedEntity`), y la bitácora del documento se muestra al usuario en la ficha.

## Impacto en el frontend

FE nueva: pestaña "Documentos" en el lead (crear desde las líneas, editar el borrador, emitir, aprobar, rechazar, anular, convertir), lista general en Clientes, búsqueda por código público o lector de código de barras, e histórico en la ficha del cliente y de la persona.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Clientes de ejemplo (personas jurídicas ficticias con RUC simulado): **Farmacia San Martín, S.A.** (`J0310000000201`, Managua), **Ferretería El Progreso** (`J0310000000202`, León), **Clínica Médica Las Colinas, S.A.** (`J0310000000203`, Managua, agente retenedor).
- Un lead abierto de Farmacia San Martín con una **cotización emitida** (2 laptops y 1 lector), un lead ganado de Ferretería El Progreso con **proforma → factura aprobada**, y la factura de Clínica Médica Las Colinas de SPEC 93 (criterio 1: retenciones IR 2 % e IMI 1 %, total a pagar C$ 61.600, al contado con tarjeta, SPEC 86).
