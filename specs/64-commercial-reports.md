# SPEC 64 — Reportes comerciales: resumen, embudo, ranking de grupos y usuarios, productos, orígenes y contactos

> **Status:** Implementado
> **Depends on:** **SPEC 60** (grupos y alcance), **SPEC 61** (leads), **SPEC 63** (ganar y perder). Rama de trabajo: `spec-64-commercial-reports`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Medir qué grupos y qué usuarios registran más clientes en un período, con qué productos, por qué origen llegaron y cómo se los contactó, en moneda base y respetando lo que cada usuario puede ver.
> **Origen:** pedido del usuario (2026-10-16): "identificar qué usuario por equipo ha creado más clientes, fechas y con qué productos". Vista "Resumen comercial" de la propuesta.

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **Moneda base:** todos los montos se suman en moneda base (`EstimatedValueBase` para el embudo, `WonValueBase` para lo ganado, SPEC 56).
2. **Un cliente cuenta para un solo grupo:** el guardado en el lead al ganar (`Leads.SalesTeamId`), no los grupos actuales del usuario. Leads sin grupo se muestran como "Sin grupo".
3. **Un cliente ganado cuenta para el dueño al ganar** (si fue reactivado por otro, para quien lo reactivó, SPEC 61 y 63).
4. **Alcance:** cada usuario ve en los reportes solo lo que cubre su alcance (SPEC 60). Un integrante ve sus propias cifras; un líder, las de su grupo; un supervisor con alcance `Company`, las de toda la empresa.
5. **Definiciones del período** (`from`, `to`, fechas inclusive, en UTC):
   - *Creados:* leads con alta en el período.
   - *Ganados:* leads con `Status = Won` y `ClosedAt` en el período.
   - *Conversión:* ganados del período / creados del período (porcentaje entero).
   - *Días al cierre:* promedio de `ClosedAt − Created` de los ganados del período.
   - *Abiertos y embudo:* foto actual (no depende del período).

## Scope

**In:**

Todos los endpoints bajo `GET /api/v1/LeadReports/...` con filtros `from`, `to`, `salesTeamId?`, `ownerUserId?`; Dapper; aplican `ILeadAccessScopeResolver`. Recurso `LeadReports`.

1. `summary` → `{ openCount, createdCount, wonCount, conversionPercent, pipelineValueBase, weightedPipelineValueBase, wonValueBase, avgDaysToClose, baseCurrencyCode }`. Ponderado = Σ `EstimatedValueBase` × probabilidad de la etapa.
2. `funnel` → por etapa abierta (orden de `SortOrder`): cantidad, valor base, valor ponderado y probabilidad.
3. `by-team` → por grupo (y "Sin grupo"): creados, ganados, conversión, valor ganado base, días al cierre.
4. `ranking` → por usuario: grupo(s) donde ganó, rol vigente, creados, ganados, conversión, valor ganado base, días al cierre, tipo de contacto más usado en sus ganados y producto principal (más líneas aceptadas).
5. `by-product` → líneas aceptadas en leads ganados: producto, cantidad de leads, cantidad total, valor base (línea convertida con `WonExchangeRate`).
6. `by-source` → ganados y creados por origen.
7. `by-contact-type` → contactos registrados en los leads ganados del período, por tipo de contacto; y promedio de contactos por lead ganado.
8. `won-leads` (paginado) → detalle de los ganados del período (código, persona, cliente creado, dueño, grupo, origen, fecha, valor base, productos aceptados), base para exportar.
9. Exportar lo hace el front con su componente de exportación (FE-14) sobre `won-leads` y `ranking`; el endpoint de detalle exige `[RequirePermission(PermissionFlags.CanExport)]` cuando llega `export=true` (sin paginar, máximo 10 000 filas).
10. `SystemOption` `LeadReports` (`/customers/sales-summary`, módulo `Customers`). Permisos: SuperAdmin, Admin, Manager y Supervisor con lectura y exportación; UsuarioSimple lectura (ve sus propias cifras por alcance).

**Out of scope:**

- Metas y cumplimiento.
- Reportes de ingresos reales (llegarán con Ventas).
- Reportes en otra moneda que no sea la base.
- Cacheo o tablas de resumen precalculadas (si hace falta por volumen, otra spec).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `REPORT_PERIOD_INVALID` | 400 | `to` anterior a `from`, o período mayor a 366 días |
| `INVALID_SALES_TEAM` | 400 | Grupo de otra empresa |
| `COMPANY_REQUIRED` / `USER_REQUIRED` | 401 | El token no trae empresa o usuario |

## Criterios de aceptación

1. Con el seed de desarrollo, `summary` del período de prueba coincide con la suma de `by-team`, y `ranking` suma los mismos ganados y creados.
2. Un usuario en dos grupos que gana un lead del grupo A no suma ese cliente al grupo B.
3. Un lead reactivado por otro usuario y ganado cuenta para quien lo ganó y su grupo.
4. Un integrante solo ve sus cifras aunque pida `salesTeamId` de su grupo; un supervisor con alcance `Company` ve todos los grupos.
5. Los montos de un lead en USD suman su valor base guardado, no un recálculo con la tasa actual.
6. `export=true` sin `CanExport` responde 403.
7. Unit tests de las definiciones (conversión, ponderado, días al cierre); integración para 1–6; `EXPLAIN` de cada query.

## Registro de implementación (2026-10-17)

Rama `spec-64-commercial-reports` (desde `spec-63-lead-win-loss-and-customer-conversion`). Sin migración: los reportes leen las tablas de SPEC 61 y 63.

- **Application:**
  - `LeadReportContext` valida el período (`REPORT_PERIOD_INVALID`: `to` anterior a `from` o más de 366 días; sin fechas, el mes en curso hasta hoy) y el grupo (`INVALID_SALES_TEAM` si es de otra empresa).
  - Arma el `WHERE` común: empresa, filas vivas, filtros de grupo y dueño, y el alcance de SPEC 60 con `LeadAccess.ReportFilter`. Es el mismo alcance de la bandeja, pero sin la regla "los inactivos los ve toda la empresa" (regla 4).
  - `LeadReportMath` reúne las definiciones de la regla 5: conversión entera, ponderado = valor base × probabilidad, y días al cierre con un decimal.
  - Ocho queries Dapper: `summary`, `funnel` (foto actual de las etapas abiertas), `by-team` (con "Sin grupo"), `ranking` (grupos donde ganó, rol vigente, tipo de contacto más usado y producto principal), `by-product` (líneas aceptadas convertidas con `WonExchangeRate`), `by-source`, `by-contact-type` (con promedio por lead ganado) y `won-leads` (paginado; con `Export`, hasta 10 000 filas).
  - Los montos ganados suman el `WonValueBase` guardado (criterio 5).
- **WebApi:** `LeadReportsController` (recurso `LeadReports`). Con `export=true`, `won-leads` comprueba `CanExport` con `IPermissionService` y responde 403 si falta (el SuperAdmin pasa siempre).
- **Seed:**
  - Opción `LeadReports` (`/customers/sales-summary`, lectura y exportación). Permisos: SuperAdmin, Manager y Supervisor con lectura y exportación; UsuarioSimple solo lectura.
  - En `JOIN-001` se suman un lead ganado (con su cliente de origen `Lead`) y uno perdido, para que los reportes de desarrollo tengan cifras de cierre (criterio 1).
- **Tests ajustados:** el test del seed de SPEC 54 cuenta solo los clientes que registra ese seed (el cliente del lead ganado es del manager).
- **Verificación:**
  - Build Release sin errores.
  - Unit tests: 3401 OK, cobertura de líneas 94,46 %.
  - Integración: 326/326 OK, 6 nuevas (criterios 1–6 y `EXPLAIN` de las ocho queries).

## Decisiones

- **Reportes calculados al vuelo desde los leads** (sin tablas de resumen): el volumen por empresa lo permite y evita datos desincronizados.
- **Grupo guardado en el lead** como base de atribución (decisión del usuario sobre varios grupos por usuario).

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Queries pesadas en períodos largos. | Período máximo de 366 días e índices de SPEC 61; `EXPLAIN` en el criterio 7. |

## Impacto en el frontend

- Specs FE (todas en Borrador, `join_frontb/specs`): **FE-39**. Base común del módulo: FE-29.
- Pantalla nueva Clientes → Resumen comercial (tarjetas, embudo, rankings, gráficos de barras y exportación).
