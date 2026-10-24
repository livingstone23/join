# SPEC 82 — Impuestos, retenciones y grupos fiscales; grupo fiscal en el catálogo actual de productos

> **Status:** Borrador
> **Depends on:** SPEC 41-post (borrado lógico, restauración y bloqueo por uso), SPEC 43-post (menú), **SPEC 57** (catálogo de productos), SPEC 61 (líneas de producto del lead), **SPEC 81** (modo de documentos del lead). Rama de trabajo: `spec-82-taxes-withholdings-and-tax-groups`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que cada empresa configure en Administración sus impuestos y retenciones, los combine en grupos fiscales y asigne un grupo a cada producto o servicio del **catálogo actual** (`Catalog.Products`), para indicar qué impuestos se aplican al cotizar y facturar desde un lead. El catálogo se puede filtrar y agrupar por grupo fiscal.
> **Ajuste por SPEC 94 (2026-10-24):** cambiar el grupo fiscal de una línea a uno con menos impuestos que el del producto exige el permiso `TaxGroupOverride` y un motivo.
> **Ajuste por SPEC 93 (2026-10-24):** las retenciones salen del grupo fiscal y pasan a ser del cliente, a nivel de documento. El grupo fiscal del producto queda solo para impuestos. `TaxCalculator` devuelve además total con IVA, retenciones de documento y total a pagar (SPEC 93).
> **Origen:** pedido del usuario (2026-10-24): "los productos pueden aplicar diferentes retenciones o impuestos; quiero poder configurar estos y poder agrupar estos productos y servicios por ello"; "en el catálogo actual de productos igual quiero poder agrupar para indicar qué tipo de impuestos deben aplicarse por producto al momento de cotizar y facturar".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Impuesto** (`Customer.Taxes`), por empresa: código, nombre, tipo `Tax = 1` (suma) o `Withholding = 2` (resta) y porcentaje (0–100, hasta 4 decimales). Ejemplos (perfil `NI`, SPEC 88): `IVA15`, `IVA0` (exento), `IVA0-EXP` (exportación), `IR2` y `IR10` (retenciones de IR), `IMI1` (retención municipal).
2. **Grupo fiscal** (`Customer.TaxGroups`): conjunto de impuestos con nombre, ej. "General" = IVA 15 %, "Exento" = IVA 0 %, "Exportación" = IVA 0 % de exportación. Desde SPEC 93 los grupos llevan solo impuestos; las retenciones son del cliente. Un grupo puede no tener impuestos y no repite un impuesto.
3. **Un grupo predeterminado por empresa** (`TaxGroupDefaultCoordinator`). Un producto sin grupo y una línea libre usan el predeterminado.
4. **Producto → grupo fiscal:** `Catalog.Products.TaxGroupId?`, editable en la pantalla actual de productos (`/administration/products`).
5. **Precios sin impuestos:** `ReferencePrice` y los precios de línea son netos.
6. **Cambios no retroactivos:** los documentos copian los impuestos al crear la línea (SPEC 83). Un impuesto o grupo usado se desactiva en lugar de borrarse.
7. **Visible solo con documentos activos:** con `CompanyCustomers.DocumentsMode = Disabled` (SPEC 81), el grupo fiscal no se muestra en productos ni en el lead, y se conserva al guardar. Los impuestos y grupos sí se pueden configurar antes de activar.
8. **Cotizar en el lead:** con el modo `Leads`, la línea de producto del lead toma el grupo fiscal del producto (editable) y muestra sus impuestos y el total con impuestos. El **valor estimado del lead sigue siendo neto** (SPEC 61 no cambia).

## Scope

**In:**

**A. Esquema**
1. `Customer.Taxes` (`BaseTenantEntity`): `Code` (máx. 20, mayúsculas sin espacios), `Name` (máx. 100), `Kind`, `Rate` (`numeric(7,4)`), `IsActive`. Índice único filtrado `(CompanyId, Code)` con `"gcrecord" = 0`.
2. `Customer.TaxGroups` (`BaseTenantEntity`): `Code`, `Name`, `Description?`, `IsDefault`, `IsActive`, `SortOrder`. Índices únicos filtrados `(CompanyId, Code)` y `(CompanyId)` con `"isdefault" = TRUE AND "gcrecord" = 0`.
3. `Customer.TaxGroupTaxes` (`BaseTenantEntity`): `TaxGroupId`, `TaxId`, `SortOrder`. Índice único filtrado `(TaxGroupId, TaxId)`.
4. `Catalog.Products.TaxGroupId?` y `Customer.LeadProducts.TaxGroupId?` (se copia del producto al agregar la línea en modo `Leads`; editable en `PUT /Leads/{id}/products/{lineId}`).

**B. Endpoints (Administración)**
5. `GET/POST/PUT/DELETE /api/v1/Taxes` + `restore`. Lista paginada con `search` (`ILIKE`), `kind` e `isActive`.
6. `GET/POST/PUT/DELETE /api/v1/TaxGroups` + `restore` + `POST /{id}/set-default` + `GET /lookup`. El cuerpo lleva `TaxIds` en orden. La respuesta trae los impuestos, la tasa total de impuestos y de retenciones y `ProductCount`.
7. Productos: `GET /Products` y `GET /Products/{id}` suman `TaxGroupId`, `TaxGroupName` y `EffectiveTaxGroupId`. `GET /Products` acepta el filtro `taxGroupId` (`none` = sin grupo). `POST/PUT /Products` aceptan `TaxGroupId`.
8. `GET /api/v1/Products/by-tax-group`: resumen agrupado (grupo, número de productos y servicios, activos e inactivos, fila "Sin grupo").
9. `POST /api/v1/Products/assign-tax-group` `{ productIds[], taxGroupId? }`: asignación masiva (máx. 500; `[RequirePermission(CanUpdate)]`).
10. Las líneas del lead (`GET /Leads/{id}`) suman, en modo `Leads`, el grupo, sus impuestos y `LineTotalWithTaxes`.

**C. Cálculo compartido**
11. `TaxCalculator` (Application, puro): impuestos por línea y **desglose por impuesto y tasa sobre la base acumulada**, redondeado a 2 decimales (`MidpointRounding.AwayFromZero`). Totales: `Subtotal`, `TaxTotal`, `WithholdingTotal`, `Total = Subtotal + TaxTotal − WithholdingTotal`. Lo usan el lead y los documentos (SPEC 83).

**D. Menú y seed**
12. Opciones en Administración: `Taxes` → `/administration/taxes`, `TaxGroups` → `/administration/tax-groups`. Permisos como SPEC 81.
13. Sin impuestos sembrados por defecto: los siembra el perfil fiscal del país (SPEC 88). En desarrollo, ver "Seed de desarrollo (Nicaragua, datos simulados)".

**Out of scope:**
- Recargo de equivalencia, impuestos en cascada, impuestos de importe fijo y reglas automáticas por país del receptor.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `TAX_NOT_FOUND` / `TAX_GROUP_NOT_FOUND` | 404 | De otra empresa o inexistente |
| `TAX_INACTIVE` / `TAX_GROUP_INACTIVE` | 409 | Asignar un impuesto o grupo inactivo |
| `TAX_IN_USE` | 409 | Borrar un impuesto que está en un grupo vivo o en documentos |
| `TAX_GROUP_IN_USE` | 409 | Borrar un grupo con productos, líneas de lead o documentos (`"Active Products: N"`, …) |
| `TAX_GROUP_DUPLICATE_TAX` | 400 | El mismo impuesto dos veces en el grupo |
| `INVALID_TAX_RATE` | 400 | Porcentaje fuera de 0–100 o con más de 4 decimales |

## Criterios de aceptación

1. "General" (IVA 15 %) sobre 1.000,00 → IVA 150,00, total con IVA 1.150,00. Las retenciones se prueban en SPEC 93.
2. Dos líneas de 100,00 y 33,33 con IVA 15 % → base 133,33 y cuota 20,00 (sobre la base acumulada: 19,9995 redondea a 20,00; por línea serían 15,00 + 5,00 = 20,00).
3. `set-default` deja un solo grupo predeterminado vivo.
4. `GET /Products?taxGroupId=none` devuelve solo los productos sin grupo, y `by-tax-group` cuadra con el total de productos.
5. Modo `Disabled`: productos y líneas del lead no exponen impuestos. Modo `Leads`: la línea muestra el grupo, los impuestos y el total con impuestos, y el valor estimado sigue siendo neto.
6. Unitarios: `TaxCalculator`, coordinador del predeterminado, validadores y handlers. Integración: CRUD, bloqueos y filtros.

## Impacto en el frontend

FE nueva: impuestos y grupos fiscales en Administración. Ajustes a FE-31 (productos: selector de grupo, filtro, vista agrupada, asignación masiva) y a las líneas del lead (grupo y total con impuestos), visibles solo en modo `Leads`.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para `JOIN-001`, idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Los impuestos, retenciones y grupos del perfil `NI` (SPEC 88): `IVA15`, `IVA0`, `IVA0-EXP`, `IR2`, `IR10`, `IMI1` y los tres grupos.
- Grupo fiscal en los productos de ejemplo: equipos y suministros en "General"; servicios de software en "General"; capacitación en "Exento" solo como ejemplo de filtro.
