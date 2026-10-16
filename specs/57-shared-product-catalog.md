# SPEC 57 — Catálogo compartido de productos y servicios por empresa

> **Status:** Borrador
> **Depends on:** SPEC 41-post (borrado lógico, restauración y bloqueo por uso), SPEC 43-post (módulos, menú y rutas), **SPEC 56** (moneda base de la empresa). Rama de trabajo: `spec-57-shared-product-catalog`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Que cada empresa defina los productos y servicios que ofrece, en un catálogo que hoy usa el módulo de clientes (productos ofrecidos en un lead, SPEC 61) y mañana reutilizarán inventario, compras, ventas y presupuestos.
> **Origen:** diseño del módulo de clientes (2026-10-16, vista "Parametrización → Productos y servicios").

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **Por empresa.** Cada empresa tiene su propio catálogo; el código es único dentro de la empresa.
2. **Producto o servicio** (`Kind`), con categoría opcional.
3. **Precio de referencia en moneda base** de la empresa (SPEC 56). Al ofrecer un producto en un lead se convierte a la moneda del lead (SPEC 61). Las listas de precios por moneda llegan con el módulo de Ventas.
4. **Esquema compartido `Catalog`:** no pertenece a un módulo de negocio en particular.
5. Un producto en uso no se borra: se desactiva. Un producto inactivo no se puede ofrecer en leads nuevos, pero los leads que ya lo tienen lo conservan.

## Scope

**In:**

**A. Esquema `Catalog`**
1. `Catalog.ProductCategories` (`BaseTenantEntity`): `Name`, `ParentId?` (un nivel o más, sin ciclos), `SortOrder`, `IsActive`. Índice único filtrado `(CompanyId, ParentId, Name)` con `"gcrecord" = 0` y `.AreNullsDistinct(false)` (convención 3: dos categorías raíz con el mismo nombre chocan).
2. `Catalog.Products` (`BaseTenantEntity`): `Code` (máx. 30, normalizado en mayúsculas sin espacios), `Name` (máx. 200), `Description?`, `Kind` (`Product = 1`, `Service = 2`), `ProductCategoryId?`, `ReferencePrice` (`numeric(18,2)`, `>= 0`, en moneda base), `IsActive`. Índice único filtrado `(CompanyId, Code)` con `"gcrecord" = 0`.

**B. Endpoints**
3. `GET /api/v1/Products` (paginado; filtros `search` con `ILIKE` sobre código y nombre, `kind`, `productCategoryId`, `isActive`, `includeDeleted` solo SuperAdmin), `GET /{id}`, `POST`, `PUT /{id}`, `DELETE /{id}`, `POST /{id}/restore`. La respuesta trae `BaseCurrencyCode` para mostrar el precio.
4. `GET /api/v1/Products/lookup?search=` (sin paginar, máx. 20, solo activos) para los buscadores de los formularios de lead.
5. `GET/POST/PUT/DELETE /api/v1/ProductCategories` + `POST /{id}/restore`. Borrar una categoría con productos o subcategorías vivas: 409 `PRODUCT_CATEGORY_IN_USE`.
6. Queries con Dapper (convención 6), commands con `IUnitOfWork`.

**C. Montos y moneda base**
7. `ProductReferencePriceAmountSource : IBaseCurrencyAmountSource` (SPEC 56): la empresa "tiene montos" si algún producto vivo tiene `ReferencePrice > 0`. Así la moneda base queda fija en cuanto hay precios.

**D. Menú, permisos y seed**
8. `SystemOption` en `Administration`: `Products` (`/administration/products`) y `ProductCategories` (`/administration/product-categories`), con sus `ControllerName`. Permisos sembrados: SuperAdmin, Admin y Manager con todo; Supervisor solo lectura. Integrantes: lectura por `Products/lookup` vía SPEC 61 (ver Decisiones).
9. En desarrollo, `JOIN-001` siembra las categorías y los seis productos del ejemplo de la propuesta (`CRM-USR`, `IMP-BAS`, `CAP-HR`, `SOP-12`, `POS-01`, `MNT-PRE`).

**Out of scope:**

- Unidades de medida, stock, almacenes y costos (los agrega Inventario).
- Precios por moneda o por cliente, impuestos y descuentos por producto.
- Imágenes de producto.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `PRODUCT_NOT_FOUND` | 404 | Producto de otra empresa o inexistente |
| `PRODUCT_CATEGORY_NOT_FOUND` | 404 | Categoría de otra empresa o inexistente |
| `PRODUCT_CATEGORY_CYCLE` | 400 | `ParentId` crea un ciclo |
| `PRODUCT_CATEGORY_IN_USE` | 409 | Borrar una categoría con productos o subcategorías vivas |
| `PRODUCT_IN_USE` | 409 | Borrar un producto usado (lo registra SPEC 61 con `LeadProducts`; formato `"Active LeadProducts: N"`, SPEC 41-post) |
| `BASE_CURRENCY_REQUIRED` | 400 | La empresa no tiene moneda base (no debería pasar tras el seed de SPEC 56) |
| `DUPLICATE_KEY` | 409 | Código o nombre de categoría repetido (índice único) |

## Criterios de aceptación

1. Dos empresas pueden tener el mismo código de producto; dentro de una empresa, no (409).
2. El código se guarda normalizado (`" crm-usr "` → `CRM-USR`).
3. `lookup` devuelve solo activos de la empresa, filtrando por código o nombre sin distinguir mayúsculas.
4. Desactivar un producto no cambia los leads que ya lo tienen; borrar uno usado responde 409 `PRODUCT_IN_USE` (se verifica en SPEC 61).
5. Con un producto con precio, cambiar la moneda base responde 409 `BASE_CURRENCY_IN_USE` (SPEC 56).
6. Una categoría no puede ser su propia ancestra; borrar una categoría en uso responde 409.
7. Unit tests de handlers y validadores; integración para 1–3, 5 y 6; `EXPLAIN` del SQL nuevo.

## Decisiones

- **Esquema `Catalog` y menú en `Administration`** (valor por defecto aceptado por el usuario): el catálogo lo comparten varios módulos; ponerlo dentro de Clientes obligaría a moverlo cuando llegue Inventario.
- **Precio solo de referencia:** el precio real queda en cada línea del lead (y luego de la cotización o la venta), con su moneda y tasa.
- **Lectura para vendedores:** quien no tenga permiso sobre `Products` igual elige productos en un lead mediante el buscador que expone SPEC 61 bajo el permiso `Leads`, como `GET /TicketUserCompanies/assignable` (SPEC 53).

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Inventario necesitará unidades y otros campos. | Se agregan como columnas nuevas o tablas hijas; el código y el id del producto no cambian. |

## Impacto en el frontend

- Pantallas nuevas en Administración: Productos y Categorías. Requiere una spec FE nueva.
