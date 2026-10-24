# SPEC 93 — Presentación de importes en proformas y facturas: valor de cada línea, IVA cuantificado por tasa, retenciones del cliente y total a pagar

> **Status:** Borrador
> **Depends on:** **SPEC 81** (configuración por tipo de documento, `CompanyCustomers`), **SPEC 82** (impuestos, retenciones y `TaxCalculator`), **SPEC 83** (documentos), **SPEC 84** (PDF), SPEC 86 (saldo y pagos), SPEC 87 (crédito), **SPEC 88** (perfil fiscal), SPEC 89 (punto de venta), SPEC 91 (libros), SPEC 92 (notas de crédito). Rama de trabajo: `spec-93-document-totals-tax-and-withholding-presentation`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que el cliente vea en la cotización, la proforma, la factura (PDF, pantalla y punto de venta) el valor de cada producto o servicio, y al final el IVA **cuantificado por tasa y sumado**, el total con IVA, cada **retención** que le corresponde aplicar y el **total a pagar**. Las retenciones dejan de depender del grupo fiscal del producto y pasan a ser **del cliente** (agente retenedor), aplicadas al documento completo.
> **Ajuste por revisión de consistencia (2026-10-24):** ajuste por SPEC 95: el precio puede ingresarse con IVA incluido (SPEC 95); `GET` de retenciones del cliente con `CanRead`; "monto del impuesto" en lugar de "cuota"; seed en `JOIN-NI-001`.
> **Ajuste por SPEC 94 (2026-10-24):** la fila "Exento" incluye las líneas exentas por constancia del cliente.
> **Origen:** pedido del usuario (2026-10-24): "en la proforma y factura debe poderse ver el valor de los productos y al final, en dependencia del IVA, poder cuantificarse y sumarse para que el cliente a pagar tenga una visión del valor del producto o servicio y las diferentes retenciones que va a pagar".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Cada línea muestra su valor sin IVA:** cantidad, código, descripción, precio unitario, descuento, **importe neto** y la **tasa de IVA** que le aplica (ej. "15 %") o "Exento". Opción por tipo de documento (`ShowLineAmountsWithTax`, por defecto `false`): agrega las columnas **precio unitario con IVA** e **importe con IVA**, para que el cliente vea el valor final de cada producto o servicio.
2. **Bloque de totales**, siempre en este orden (las filas en cero no se imprimen, salvo el total):

   | Fila | Contenido |
   |---|---|
   | Subtotal | Suma de cantidad × precio, antes de descuentos |
   | Descuentos | Suma de descuentos de línea (negativo) |
   | Subtotal neto | Subtotal − descuentos |
   | Exento | Base de las líneas exentas o con tasa 0 |
   | Base gravada IVA 15 % | Una fila por cada tasa usada |
   | IVA 15 % | Monto del IVA por tasa, sobre la base acumulada (SPEC 82) |
   | **Total con IVA** | Subtotal neto + suma de IVA. Es el valor de la venta |
   | Retención IR 2 % (sobre C$ base) | Una fila por retención, negativa |
   | Retención municipal 1 % (sobre C$ base) | |
   | **Total a pagar** | Total con IVA − retenciones. Es lo que el cliente paga |
   | Equivalente en moneda local | Si el documento está en otra moneda (SPEC 88) |
   | Monto en letras | "Son: sesenta y un mil seiscientos córdobas con 00/100" |

3. **Retenciones del cliente, no del producto:** un cliente que es **agente retenedor** tiene sus retenciones (`CustomerWithholdings`): impuesto de tipo `Withholding` (SPEC 82), a qué líneas aplica (`All`, `ProductsOnly`, `ServicesOnly`) y monto mínimo de base para aplicar (opcional). Al crear un documento para ese cliente, el sistema agrega las retenciones **a nivel de documento**, sobre la base neta (sin IVA) de las líneas que correspondan. En borrador se pueden quitar o agregar; al emitir se copian (`LeadCommercialDocumentWithholdings`).
   - El grupo fiscal del producto queda **solo para impuestos** (IVA). Los grupos "a agente retenedor" del perfil `NI` (SPEC 88) se eliminan del seed.
   - El consumidor final (SPEC 89) nunca tiene retenciones.
4. **Totales guardados en el documento:** `Subtotal`, `DiscountTotal`, `NetSubtotal`, `ExemptAmount`, `TaxTotal`, `TotalWithTaxes`, `WithholdingTotal` y `Total` (= **total a pagar**). Saldo, pagos y crédito (SPEC 86, 87) se calculan sobre `Total`. El libro de ventas (SPEC 91) informa `TotalWithTaxes` como valor de la venta y las retenciones aparte.
5. **Misma presentación en todas partes:** PDF (SPEC 84), editor de documentos y pestaña del lead (SPEC 83), punto de venta y ticket (SPEC 89) y notas de crédito (SPEC 92, con signo negativo). La vista previa de totales (`preview-totals`) devuelve las mismas filas.
6. **Monto en letras** según idioma y moneda: el perfil del país (SPEC 88) define el idioma (`es`) y el nombre de cada moneda en singular y plural ("córdoba"/"córdobas", "dólar"/"dólares"). `AmountInWordsFormatter` (Application) para español; otros idiomas se agregan con su formateador.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `CustomerWithholdings` (`BaseTenantEntity`): `CustomerId`, `TaxId` (de tipo `Withholding`), `AppliesTo` (`All = 1`, `ProductsOnly = 2`, `ServicesOnly = 3`), `MinimumBase?`, `IsActive`. Índice único filtrado `(CustomerId, TaxId)`.
2. `LeadCommercialDocumentWithholdings`: `DocumentId`, `TaxId?`, `TaxCode`, `TaxName`, `Rate`, `AppliesTo`, `BaseAmount`, `Amount`.
3. `LeadCommercialDocuments` suma `DiscountTotal`, `NetSubtotal`, `ExemptAmount`, `TotalWithTaxes`, `AmountInWords` (texto guardado al emitir).
4. `LeadDocumentTypeConfigurations` suma `ShowLineAmountsWithTax`.
5. `FiscalCountryProfiles` suma `Language` y `CurrencyWordNames` (`jsonb`).

**B. Cálculo**
6. `TaxCalculator` (SPEC 82) devuelve las filas de la regla 2: impuestos por línea (sin retenciones), desglose por tasa, exento, total con IVA, retenciones de documento con su base y total a pagar. Redondeo a 2 decimales por fila (`MidpointRounding.AwayFromZero`).

**C. Endpoints**
7. `GET /api/v1/Customers/{id}/withholdings` (`Customers`, `CanRead`) y `PUT` (`Customers`, `CanUpdate`).
8. `POST /api/v1/LeadCommercialDocuments/{id}/withholdings` y `DELETE /{id}/withholdings/{withholdingId}` (solo `Draft`). Excepción: en órdenes de compra aprobadas, las retenciones efectuadas se registran con la factura del proveedor (SPEC 91, `PUT /supplier-invoice`).
9. Los DTO de documento, `preview-totals` y el punto de venta devuelven el bloque `Totals` con las filas de la regla 2, y por línea `NetAmount`, `TaxRate`, `IsExempt` y, si el tipo lo pide, `UnitPriceWithTax` y `AmountWithTax`.

**Out of scope:**
- (Ingresar precios con IVA incluido: lo agrega SPEC 95.)

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `WITHHOLDING_TAX_REQUIRED` | 400 | Asignar a un cliente un impuesto que no es de tipo `Withholding` |
| `FINAL_CONSUMER_WITHHOLDING_NOT_ALLOWED` | 409 | Retenciones en un documento del consumidor final |
| `LEAD_COMMERCIAL_DOCUMENT_NOT_EDITABLE` | 409 | Cambiar retenciones de un documento emitido |

## Criterios de aceptación

1. Factura a Clínica Las Colinas (agente retenedor con IR 2 % e IMI 1 %, `All`): 2 laptops (`LAP-14`) × C$ 25.000 + implementación básica (`IMP-BAS`, servicio) con precio manual de la línea C$ 5.000 (su precio de referencia convertido es mayor). Muestra subtotal neto C$ 55.000, base gravada IVA 15 % C$ 55.000, IVA C$ 8.250, **total con IVA C$ 63.250**, retención IR 2 % C$ 1.100, retención municipal 1 % C$ 550 y **total a pagar C$ 61.600**, con el monto en letras.
2. La misma factura a un cliente sin retenciones muestra total con IVA = total a pagar = C$ 63.250.
3. Una línea exenta aparece con "Exento" y su base en la fila Exento, sin IVA.
4. Con `ShowLineAmountsWithTax = true`, cada línea muestra precio e importe con IVA (laptop C$ 28.750 por unidad).
5. El saldo de la factura a crédito (SPEC 86) y el cargo a la línea de crédito (SPEC 87) usan el total a pagar (C$ 61.600).
6. Una factura en USD muestra el bloque en USD y el equivalente en córdobas.
7. Unitarios: `TaxCalculator` (filas, exento, varias tasas, retenciones `ProductsOnly`/`ServicesOnly`, base mínima), `AmountInWordsFormatter` (enteros, centavos, millones, singular y plural). Integración: documento con retenciones del cliente y PDF.

## Decisiones

- **Retenciones por cliente a nivel de documento:** la retención depende de quién compra (agente retenedor), no del producto. Calcularla sobre la base del documento evita tener grupos fiscales duplicados por cada combinación.
- **`Total` = total a pagar:** es lo que el cliente debe, así que saldo, pagos y crédito no cambian de significado. El valor de la venta queda en `TotalWithTaxes`.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Clínica Médica Las Colinas con retenciones IR 2 % e IMI 1 % (`All`). Su factura del seed queda como el criterio 1.
- `ShowLineAmountsWithTax = true` en la cotización y la proforma; `false` en la factura.
- Nombres de moneda del perfil `NI`: córdoba/córdobas, dólar/dólares.
