# SPEC 97 — Panel de ventas por período y vendedor, y panel de cuentas por cobrar con antigüedad de saldos, con visibilidad por usuario

> **Status:** Borrador
> **Depends on:** SPEC 43-post (permisos por rol), **SPEC 60** (grupos de venta, líder y alcance de visibilidad), **SPEC 83** (documentos), SPEC 86 (estado de cobro, cuotas, responsable de cobro), SPEC 87 (crédito), SPEC 89 (punto de venta), SPEC 90 (margen), SPEC 92 (devoluciones), SPEC 93 (totales). Rama de trabajo: `spec-97-sales-and-receivables-panels-with-visibility`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Dos paneles con reporte exportable: **ventas por período y vendedor**, y **cuentas por cobrar con antigüedad de saldos**. Cada usuario ve **solo lo suyo** (sus ventas o las cuentas que tiene asignadas). Un líder ve su grupo. Un supervisor o gerente, con el permiso adecuado, ve **todo**.
> **Origen:** pedido del usuario (2026-10-24): "el panel y el reporte que un usuario pueda ver; parametrizar que un usuario pueda ver los que tenga asignados y un supervisor o gerente pueda ver todos"; "panel de ventas por período y vendedor, donde los que son vendedores puedan ver solo sus ventas y otros, con los permisos adecuados, ver el total de ventas".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Vendedor del documento** (`SellerUserId`): por defecto, el dueño del lead (documentos de lead) o el usuario que vende (punto de venta). Editable en borrador por quien tiene `SellerReassign`. Se copia a las conversiones y a las notas de crédito.
2. **Alcance de visibilidad** (mismo criterio que los leads, SPEC 60), por permiso:

   | Permiso (recurso) | Ve |
   |---|---|
   | `SalesPanel` | Sus ventas (`SellerUserId` = él) |
   | `SalesPanel` + líder de un grupo de venta | Las de su grupo |
   | `SalesPanelViewAll` | Todas las de la empresa |
   | `ReceivablesPanel` | Las cuentas asignadas a él |
   | `ReceivablesPanel` + líder | Las de su grupo |
   | `ReceivablesViewAll` | Todas |

   Los permisos se dan por rol en Administración (mecanismo actual). Seed: vendedores con `SalesPanel` y `ReceivablesPanel`; Supervisor y Manager también con `…ViewAll`.
3. **Cuenta asignada** (`CompanyCustomers.ReceivablesAssignment`): `Seller` (el vendedor de la factura), `CollectionUser` (el responsable de cobro, SPEC 86) o `Both` (cualquiera de los dos; por defecto).
4. **Panel de ventas** (período: hoy, semana, mes, año o rango; zona horaria de la empresa):
   - ventas con IVA y netas (`TotalWithTaxes`, `NetSubtotal`, SPEC 93), menos devoluciones (SPEC 92);
   - número de facturas, ticket promedio, contado vs crédito, por origen (lead o punto de venta);
   - cotizaciones emitidas y convertidas a factura (tasa de conversión);
   - por vendedor (ranking, solo dentro del alcance), por día, por producto o servicio y por categoría;
   - margen solo si además tiene `ProductCosts` (SPEC 90).
5. **Panel de cuentas por cobrar:**
   - saldo total, vencido y por vencer, en moneda base;
   - **antigüedad de saldos** por días de vencimiento de cada cuota (SPEC 86): Por vencer, 1–30, 31–60, 61–90 y más de 90; por cliente y por responsable;
   - clientes cerca o sobre su límite de crédito (SPEC 87);
   - cuotas que vencen en los próximos 7 días.
6. **Reportes exportables** (CSV) con el mismo alcance que el panel: ventas detalladas por documento y antigüedad de saldos por cliente y factura. El alcance se aplica **en el servidor**; un usuario sin `…ViewAll` nunca recibe datos de otros, aunque cambie filtros.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `LeadCommercialDocuments` suma `SellerUserId` (obligatorio en documentos de venta; migración de datos: dueño del lead o `IssuedByUserId`). Índice `(CompanyId, SellerUserId, IssueDate)`.
2. `CompanyCustomers` suma `ReceivablesAssignment`.

**B. Endpoints**
3. `SalesScopeResolver` (Application): calcula el alcance (propio, grupo, todo) con SPEC 60 y los permisos. Lo usan los dos paneles y sus reportes.
4. Ventas (permiso `SalesPanel`): `GET /api/v1/SalesPanel/summary?from=&to=&sellerUserId=&origin=`, `GET /by-seller`, `GET /by-day`, `GET /by-product`, `GET /export` (`CanExport`). Un `sellerUserId` fuera del alcance → 403.
5. Cuentas por cobrar (permiso `ReceivablesPanel`): `GET /api/v1/ReceivablesPanel/summary`, `GET /aging?groupBy=customer|user&asOf=`, `GET /aging/customers/{customerId}` (detalle por factura y cuota), `GET /export` (`CanExport`).
6. `PUT /LeadCommercialDocuments/{id}` acepta `sellerUserId` (solo `Draft`, permiso `SellerReassign`).

**C. Menú**
7. Clientes: `SalesPanel` → `/customers/sales-panel`, `ReceivablesPanel` → `/customers/receivables-panel`. Recursos sin pantalla: `SalesPanelViewAll`, `ReceivablesViewAll`, `SellerReassign`.

**Out of scope:**
- Metas de venta y comisiones.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `SCOPE_FORBIDDEN` | 403 | Filtrar por un vendedor, responsable o cliente fuera del alcance |
| `INVALID_PERIOD` | 400 | Rango inválido o mayor a 24 meses |

## Criterios de aceptación

1. Un vendedor con solo `SalesPanel` ve sus ventas del mes; pedir las de otro vendedor → 403.
2. Un líder de grupo ve las de su grupo. Un gerente con `SalesPanelViewAll` ve todas y el ranking completo.
3. Las ventas del panel cuadran con el libro de ventas del período (SPEC 91) para el usuario con `ViewAll`.
4. La antigüedad de saldos clasifica una cuota vencida hace 45 días en 31–60.
5. Con `ReceivablesAssignment = CollectionUser`, un vendedor sin cobranzas asignadas no ve saldos.
6. El CSV exportado contiene solo filas del alcance del usuario.
7. Unitarios: resolver de alcance, clasificación por antigüedad, cálculos del panel. Integración: alcance por permiso y grupo.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para `JOIN-001`, idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Dos vendedores de ejemplo (si existen usuarios de prueba) con facturas del seed asignadas, para mostrar el alcance propio y el total.
- Facturas a crédito con cuotas vencidas a 15, 45 y 95 días para poblar la antigüedad de saldos.
