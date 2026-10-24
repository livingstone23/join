# SPEC 95 — Producto: unidad de medida opcional por empresa y precio registrado con o sin IVA

> **Status:** Borrador
> **Depends on:** **SPEC 57** (productos), SPEC 81 (`CompanyCustomers`), **SPEC 82** (grupos fiscales y `TaxCalculator`), **SPEC 83** (documentos), SPEC 84 (PDF), SPEC 89 (punto de venta), SPEC 91 (libros), **SPEC 93** (presentación de totales), SPEC 94 (exenciones). Rama de trabajo: `spec-95-product-unit-of-measure-and-tax-inclusive-price`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que cada empresa defina sus unidades de medida y que el producto tenga una unidad **opcional**. Que el precio del producto se registre **con o sin IVA**, según un parámetro del producto, y que el producto muestre y guarde su precio sin IVA, el valor del IVA y el precio con IVA, para usarlo en documentos, cobros e informes.
> **Ajuste por revisión de consistencia (2026-10-24):** la lista de precios usa el recurso `Products` (`CanRead`, exportar con `CanExport`); seed en `JOIN-NI-001` con precios en NIO.
> **Origen:** pedido del usuario (2026-10-24): "crear las unidades de medida por empresa y crearlo como una propiedad al registrar el producto, que no sea obligatoria"; "en la parametrización del producto se indica si se muestra con IVA o sin IVA, es decir, si se le registra IVA como precio de venta, al generar los documentos reflejarlo en las áreas correspondientes a precio y a cobros a aplicar"; "una sección, si se requiere, del valor del IVA del producto por si requiere llenar luego un informe".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Unidades de medida por empresa** (`UnitsOfMeasure`): código, nombre, símbolo y si admite decimales (ej. `KG` sí, `UND` no). Se usan en productos y servicios.
2. **Unidad opcional en el producto:** `Catalog.Products.UnitOfMeasureId?`. Sin unidad, el producto funciona igual que hoy. Con unidad que no admite decimales, las cantidades de sus líneas deben ser enteras (`QUANTITY_DECIMALS_NOT_ALLOWED`).
3. **La unidad se copia en la línea** del documento (código y símbolo) y se imprime junto a la cantidad ("2 UND", "1,5 KG", "3 H"). Cambiar la unidad del producto no altera documentos emitidos. Sin conversiones entre unidades (fuera de alcance).
4. **Precio con o sin IVA** (`Catalog.Products.PriceIncludesTax`, por defecto el valor de `CompanyCustomers.DefaultPriceIncludesTax`, que arranca en `false`):
   - `false`: `ReferencePrice` es el precio **sin IVA** (como hoy).
   - `true`: `ReferencePrice` es el precio **con IVA** (precio de venta final). El sistema calcula el precio sin IVA dividiendo entre (1 + suma de tasas de su grupo fiscal), con 4 decimales.
5. **Sección de precio del producto** (siempre visible en la ficha, con el módulo de documentos activo): precio sin IVA, **IVA por impuesto y total**, y precio con IVA. Se guardan (`NetReferencePrice`, `ReferenceTaxAmount`, `GrossReferencePrice`) para informes y listas de precios. Se recalculan al cambiar el precio, el grupo fiscal o la tasa de un impuesto del grupo.
6. **En los documentos**, la línea de un producto con `PriceIncludesTax = true`:
   - muestra el **precio con IVA** en las columnas de precio e importe (como si `ShowLineAmountsWithTax` estuviera activo para esa línea, SPEC 93);
   - guarda el precio sin IVA para el cálculo (`UnitPrice`) y el precio con IVA ingresado (`UnitPriceWithTax`), con la marca `PriceEnteredWithTax`;
   - **cuadre exacto:** el total con IVA de esas líneas es exactamente cantidad × precio con IVA − descuento. El IVA de esas líneas se calcula como importe con IVA − importe sin IVA, y la diferencia de redondeo queda en el IVA, nunca en el total que paga el cliente;
   - el bloque de totales (SPEC 93) sigue desglosando base, IVA por tasa, retenciones y total a pagar. Las retenciones se calculan sobre la base sin IVA.
7. **Al editar el precio en la línea**, el usuario escribe el precio en el mismo modo que el producto (con o sin IVA). Las líneas libres usan `CompanyCustomers.DefaultPriceIncludesTax`.
8. **Exención (SPEC 94):** si el documento está exento, una línea con precio con IVA cobra el **precio sin IVA** (se quita el IVA incluido) y lo indica en la línea.
9. **Punto de venta (SPEC 89):** usa el modo de cada producto. Los productos de mostrador suelen registrarse con IVA para que el precio de la etiqueta sea el que se cobra.

## Scope

**In:**

**A. Esquema**
1. `Customer.UnitsOfMeasure` (`BaseTenantEntity`): `Code` (máx. 10, mayúsculas), `Name` (máx. 50), `Symbol` (máx. 10), `AllowsDecimals`, `IsActive`, `SortOrder`. Índice único filtrado `(CompanyId, Code)` con `"gcrecord" = 0`.
2. `Catalog.Products` suma `UnitOfMeasureId?`, `PriceIncludesTax`, `NetReferencePrice`, `ReferenceTaxAmount`, `GrossReferencePrice` (`numeric(18,4)`).
3. `Customer.LeadCommercialDocumentLines` suma `UnitOfMeasureCode?`, `UnitOfMeasureSymbol?`, `PriceEnteredWithTax`, `UnitPriceWithTax?`.
4. `Customer.CompanyCustomers` suma `DefaultPriceIncludesTax`.

**B. Endpoints**
5. Administración: `GET/POST/PUT/DELETE /api/v1/UnitsOfMeasure` + `restore` + `GET /lookup` (`/administration/units-of-measure`). No se borra una unidad usada en productos vivos (`UNIT_OF_MEASURE_IN_USE`; se desactiva).
6. `POST/PUT /api/v1/Products` aceptan `UnitOfMeasureId` y `PriceIncludesTax`. `GET /Products` y `GET /{id}` devuelven la unidad y la sección de precio (sin IVA, IVA por impuesto, con IVA).
7. `TaxCalculator` (SPEC 82, 93) admite líneas con precio con IVA y aplica el cuadre exacto de la regla 6.
8. `GET /api/v1/Reports/price-list?categoryId=&taxGroupId=` (exportable): lista de precios con unidad, precio sin IVA, IVA y precio con IVA.

**Out of scope:**
- Conversiones entre unidades (caja ↔ unidad), varias unidades por producto y listas de precios por cliente.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `UNIT_OF_MEASURE_NOT_FOUND` / `UNIT_OF_MEASURE_INACTIVE` | 404 / 409 | Unidad inexistente o inactiva |
| `UNIT_OF_MEASURE_IN_USE` | 409 | Borrar una unidad usada |
| `QUANTITY_DECIMALS_NOT_ALLOWED` | 400 | Cantidad con decimales en una unidad que no los admite |

## Criterios de aceptación

1. Un producto sin unidad se crea y se vende igual que hoy.
2. `PAP-80` con unidad `UND`, `PriceIncludesTax = true` y precio C$ 115 (grupo IVA 15 %) muestra sin IVA C$ 100,00, IVA C$ 15,00, con IVA C$ 115,00.
3. Vender 3 unidades de `PAP-80` da total con IVA exactamente C$ 345,00 (IVA C$ 45,00) y la línea muestra C$ 115,00 por unidad.
4. Un precio con IVA de C$ 99,99 por 7 unidades da total con IVA C$ 699,93 exacto, con el redondeo absorbido en el IVA.
5. Una cantidad de 1,5 en `UND` → 400. En `KG` se acepta.
6. En una factura exenta (SPEC 94), `PAP-80` se cobra a C$ 100,00.
7. Cambiar la tasa del IVA recalcula la sección de precio de los productos con ese impuesto, sin tocar documentos emitidos.
8. Unitarios: cálculo de neto desde bruto, cuadre exacto, decimales por unidad, recálculo de la sección de precio. Integración: documento mixto con líneas con y sin IVA incluido.

## Decisiones

- **Se guarda siempre el neto para el cálculo** y el bruto para presentar: el núcleo de impuestos (SPEC 82, 93) no cambia de lógica.
- **El redondeo va al IVA, no al total:** el cliente paga exactamente el precio de etiqueta.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Unidades: Unidad (`UND`), Caja (`CJ`), Paquete (`PQ`), Kilogramo (`KG`, decimales), Libra (`LB`, decimales), Litro (`LT`, decimales), Galón (`GAL`, decimales), Metro (`M`, decimales), Hora (`H`, decimales), Servicio (`SERV`).
- Productos de mostrador (`PAP-80`, `LEC-CB`, `GAV-DIN`) con `UND` y precio con IVA; equipos (`LAP-14`, `IMP-TER80`) con `UND` y precio sin IVA; servicios (`CAP-HR` en `H`, `SOP-12` y `MNT-PRE` en `SERV`) sin IVA incluido.
