# SPEC 91 — Libro de ventas, libro de compras y resumen de impuestos y retenciones por período, con formato por país

> **Status:** Borrador
> **Depends on:** **SPEC 82** (impuestos y retenciones), **SPEC 83** (documentos y desglose), SPEC 85 (orden de compra recibida), **SPEC 88** (perfil fiscal por país), SPEC 89 (ventas del punto de venta). Rama de trabajo: `spec-91-sales-and-purchase-tax-books`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Dar al contador, por período, el **libro de ventas** (facturas y notas de crédito con su desglose de impuestos), el **libro de compras** (facturas de proveedor registradas al recibir las órdenes de compra) y el **resumen de impuestos y retenciones** (débito fiscal, crédito fiscal, retenciones sufridas y efectuadas) para preparar la declaración. El contenido es estándar; el **formato de exportación** lo define el perfil del país.
> **Ajuste por SPEC 94 (2026-10-24):** el libro de ventas suma las columnas de la constancia de exoneración de cada venta exenta.
> **Ajuste por SPEC 93 (2026-10-24):** el valor de la venta es `TotalWithTaxes`; las retenciones sufridas salen de `LeadCommercialDocumentWithholdings`.
> **Origen:** pedido del usuario (2026-10-24), punto 4 de la revisión para empezar a operar: "libro de ventas e impuestos; exportar IVA por período para declarar".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Libro de ventas:** todas las facturas y notas de crédito **emitidas** en el período (por `IssueDate`), de leads y del punto de venta, incluidas las anuladas (anulada + su nota de crédito, para que el período cuadre). Por documento: fecha, tipo, número, autorización (SPEC 88), receptor y su identificación (o "Consumidor final"), moneda, tasa, base por cada tasa de impuesto, exento, cuota por impuesto, retenciones y total, en moneda del documento y en moneda local.
2. **Libro de compras:** las órdenes de compra recibidas no son facturas. Al aprobar (recibir) una orden de compra se pueden registrar los datos de la **factura del proveedor** (`SupplierInvoiceNumber`, `SupplierInvoiceDate`, `SupplierTaxId`) y sus impuestos (copiados de las líneas). Solo las órdenes con factura de proveedor entran al libro de compras, por `SupplierInvoiceDate`.
3. **Retenciones:**
   - **Sufridas:** las que un cliente agente de retención aplica a nuestras facturas (impuestos `Withholding` en documentos de venta). Se puede registrar el **número de constancia de retención** que entrega el cliente (`WithholdingCertificateNumber` por documento).
   - **Efectuadas:** las que la empresa aplica como agente retenedor en sus compras (impuestos `Withholding` en órdenes de compra).
4. **Resumen del período:** débito fiscal (impuestos de ventas − notas de crédito), crédito fiscal (impuestos de compras), saldo, y retenciones sufridas y efectuadas por tipo. Todo en moneda local.
5. **Cierre de período (opcional):** `TaxPeriodLocks` por mes. Un período cerrado no admite documentos nuevos con fecha en él, ni anulaciones, ni cambios de datos fiscales de compras (`TAX_PERIOD_LOCKED`). Solo `TaxPeriodsAdmin` lo reabre, con motivo.
6. **Formato por país:** el contenido de los reportes es el mismo en todos los países. Cada perfil fiscal (SPEC 88) puede definir **plantillas de exportación** (`FiscalCountryProfiles.ExportLayouts`, `jsonb`: columnas, orden, títulos, separador, formato de fecha y números) para producir el archivo que pide su autoridad. Nicaragua arranca con un layout CSV estándar y columnas en español. Cuando se confirme el formato exacto que pide la DGI, se ajusta el layout sin tocar código.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `LeadCommercialDocuments` suma `SupplierInvoiceNumber?` (máx. 50), `SupplierInvoiceDate?`, `SupplierTaxId?` (máx. 50) para órdenes de compra, y `WithholdingCertificateNumber?` (máx. 50) para ventas.
2. `TaxPeriodLocks` (`BaseTenantEntity`): `Year`, `Month`, `LockedByUserId`, `LockedAt`, `ReopenedByUserId?`, `ReopenedAt?`, `ReopenReason?`, `IsLocked`. Índice único `(CompanyId, Year, Month)`.
3. `FiscalCountryProfiles` (SPEC 88) suma `ExportLayouts` (`jsonb`).

**B. Endpoints (permiso `TaxBooks`, exportables con `CanExport`)**
4. `GET /api/v1/TaxBooks/sales?from=&to=&format=json|csv|layout:{code}`.
5. `GET /api/v1/TaxBooks/purchases?from=&to=&format=…`.
6. `GET /api/v1/TaxBooks/summary?year=&month=`: resumen del período.
7. `GET /api/v1/TaxBooks/withholdings?from=&to=&kind=suffered|applied`.
8. `PUT /api/v1/LeadCommercialDocuments/{id}/supplier-invoice` (órdenes de compra recibidas) y `PUT /{id}/withholding-certificate` (ventas). Respetan el cierre de período.
9. Cierre (permiso `TaxPeriodsAdmin`): `GET /api/v1/TaxPeriods`, `POST /{year}/{month}/lock`, `POST /{year}/{month}/reopen` `{ reason }`.
10. Todas las consultas con Dapper sobre los datos **copiados al emitir** (desglose guardado de SPEC 83), no recalculados.

**C. Menú**
11. Clientes: `TaxBooks` → `/customers/reports/tax-books`. Administración: `TaxPeriods` → `/administration/tax-periods`. Permisos: SuperAdmin y Admin; el rol del contador se asigna por empresa.

**Out of scope:**
- Presentar la declaración a la autoridad (DGI u otra): el sistema entrega los datos y el archivo.
- Contabilidad general (asientos, mayor, balance).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `TAX_PERIOD_LOCKED` | 409 | Emitir, anular o cambiar datos fiscales en un período cerrado |
| `INVALID_PERIOD` | 400 | Rango de fechas inválido o mayor a 12 meses |
| `EXPORT_LAYOUT_NOT_FOUND` | 404 | Layout que el perfil del país no define |

## Criterios de aceptación

1. En un mes con 3 facturas (una en USD) y una nota de crédito, el libro de ventas muestra cuatro filas con base y cuota por tasa en moneda del documento y en córdobas, y el resumen da el débito fiscal neto.
2. Una factura con IR 2 % retenido por el cliente aparece en retenciones sufridas con su constancia.
3. Una orden de compra recibida con factura de proveedor entra al libro de compras por la fecha de la factura del proveedor, y su IVA suma al crédito fiscal.
4. Con el mes cerrado, emitir una factura con fecha de ese mes → 409. Reabrir con motivo lo permite y queda registrado.
5. La exportación con el layout de `NI` produce el CSV con las columnas y el formato definidos en el perfil.
6. Unitarios: armado de los libros, resumen, cierre de período y aplicación de layouts. Integración: período completo con ventas, compras y retenciones.

## Decisiones

- **Contenido estándar y layout por país como datos:** al pasar a otro país se agrega su layout en el perfil, sin cambiar las consultas.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para `JOIN-001`, idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Datos de factura de proveedor en la orden de compra del seed (`SupplierInvoiceNumber` "F-000458", ficticio), la constancia de retención IR 2 % de Clínica Las Colinas y el layout CSV `NI-ESTANDAR` en el perfil `NI`.
- Ningún período cerrado.
