# SPEC 86 — Módulo Productos ampliado: catálogo propio del módulo y origen de productos en Leads

> **Status:** Pospuesto
> **Depends on:** SPEC 41-post (borrado lógico, restauración y bloqueo por uso), SPEC 43-post (módulos, menú y rutas), SPEC 48-post (bloqueo por módulo), SPEC 56 (moneda base), **SPEC 57** (catálogo general), **SPEC 61** (líneas de producto del lead), SPEC 64 (reporte de productos), **SPEC 81–85** (facturación sobre el catálogo general).
> **Date:** 2026-10-24
> **Objective:** Un módulo **Productos** de mayor tamaño, activable por empresa, con **su propio catálogo** (segunda tabla de productos, independiente del catálogo general de SPEC 57), donde más adelante se dimensionan las especialidades (unidades, variantes, inventario, listas de precios). Un parámetro de Leads elige de qué catálogo salen las líneas nuevas del lead.
> **Origen:** pedido del usuario (2026-10-24): "quiero 2 tablas de productos: la actual de Lead, que es muy general, y luego en otros spec dimensionaremos una especialidad de productos; quiero poder hacer el cambio, no importa que estén productos en ambas, el sistema es modular".
> **Etapa (decisión del usuario, 2026-10-24):** "prioricemos… ajustar lo que tenemos y luego ir por un módulo de mayor tamaño". Primero se implementan SPEC 81–85 sobre el catálogo general; esta spec queda `Pospuesto`. Al retomarla, se revisa contra el estado de ese momento y vuelve a `Borrador`.

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Módulo propio, no base.** `Products` es un `SystemModule` con `IsBase = false`, distinto del módulo de facturación `Billing` (SPEC 81).
2. **Dos catálogos independientes:**
   - **Catálogo general** (`Catalog.Products`, SPEC 57, 70 y grupo fiscal de SPEC 82): no cambia y lo siguen usando los leads por defecto.
   - **Catálogo del módulo** (`Products.ProductItems`, `Products.ProductItemCategories`): base mínima (código, nombre, tipo, categoría, precio neto en moneda base, grupo fiscal de SPEC 82, activo). Las especialidades se dimensionan en specs posteriores.
   - Un mismo producto puede existir en los dos catálogos; no hay sincronización.
3. **Origen de productos en Leads** (`LeadCompanySettings.ProductSource`): `General = 1` (por defecto) o `ProductsModule = 2` (requiere el módulo activo).
   - El cambio se permite en cualquier momento. Las líneas que ya existen conservan su producto y su catálogo; solo las nuevas usan el origen vigente. Un lead puede tener líneas de los dos catálogos.
   - **Origen efectivo:** si se desactiva el módulo, el origen efectivo vuelve a `General` sin tocar datos. Las líneas del módulo se siguen viendo, pero no se agregan nuevas.
4. **Facturación:** las proformas y facturas (SPEC 83, 84) aceptan también productos del catálogo del módulo.

## Scope (borrador inicial, a revisar al retomar)

1. Seed del módulo `Products` (`fa-solid fa-boxes-stacked`, `IsBase = false`) y de su menú (`/products`): `ProductItems` → `/products/items`, `ProductItemCategories` → `/products/categories`.
2. `Products.ProductItemCategories` y `Products.ProductItems` (código único por empresa dentro de este catálogo, `UnitPrice` neto, `TaxGroupId?`), con CRUD, `lookup`, restauración y bloqueo por uso como SPEC 57. `ProductItemUnitPriceAmountSource : IBaseCurrencyAmountSource`.
3. `LeadCompanySettings.ProductSource` y, en `GET/PUT /LeadCompanySettings`, `EffectiveProductSource` y `ProductsModuleEnabled`. `LeadProductSourceResolver` en Application.
4. `Customer.LeadProducts`: `ProductId` pasa a nullable, se agregan `ProductItemId?` y `ProductSource`, con `CHECK` de exactamente un producto informado. `products-lookup` y `POST /Leads/{id}/products` según el origen efectivo (`LEAD_PRODUCT_SOURCE_MISMATCH`).
5. Lecturas del lead (`LeadQueryHandlers`) y reporte de productos (`LeadReportQueries`, SPEC 64) con `LEFT JOIN` a los dos catálogos.
6. `Billing.BillingDocumentLines.ProductItemId?`, con `CHECK` de como máximo un producto por línea.

## Decisiones ya tomadas

- Dos tablas de líneas no: una sola tabla `LeadProducts` con dos FK opcionales y `CHECK`, para conservar la integridad y el bloqueo por uso de cada catálogo.

## Riesgos

- Hacer `LeadProducts.ProductId` nullable toca las lecturas de SPEC 61, 63 y 64 (`INNER JOIN` → `LEFT JOIN`).
