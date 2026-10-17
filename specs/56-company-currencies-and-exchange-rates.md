# SPEC 56 — Monedas por empresa: moneda base, monedas paralelas y tasas de cambio

> **Status:** Aprobado
> **Depends on:** SPEC 39 (fork PostgreSQL), SPEC 41-post (borrado lógico y restauración), SPEC 43-post (módulos, menú y rutas en inglés). Rama de trabajo: `spec-56-company-currencies-and-exchange-rates`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Que cada empresa tenga una moneda base, pueda usar monedas paralelas y registre tasas de cambio fechadas, para que los montos del módulo de clientes (y luego de ventas, compras y presupuestos) se guarden en su moneda y se sumen en moneda base.
> **Origen:** diseño del módulo de clientes (2026-10-16, propuesta visual "Clientes JOIN", vista "Parametrización → Monedas y tasas"). Primera de la serie 56–65.

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **Una moneda base por empresa, exactamente una.** Se marca con `IsBase` en la moneda de la empresa. La base no lleva tasa: vale 1.
2. **Monedas paralelas** con tasas de cambio fechadas, todo por empresa.
3. **Convención de la tasa:** 1 unidad de la moneda paralela = `Rate` unidades de la moneda base (1 USD = 59.36 DOP). `Rate` con 6 decimales; los montos convertidos a moneda base se redondean a 2 decimales (`MidpointRounding.AwayFromZero`).
4. **Tasa del día con respaldo:** para convertir en una fecha se usa la tasa de esa fecha; si no hay, la última anterior, y la respuesta dice de qué fecha es. Si la moneda nunca tuvo tasa, no se puede usar (`EXCHANGE_RATE_NOT_FOUND`).
5. **Carga manual** de tasas por empresa (una por moneda y fecha). Traerlas de un servicio externo queda fuera.
6. **La moneda base no cambia una vez que hay montos.** Solo se puede cambiar mientras ningún registro de la empresa guarde montos (ver `CompanyBaseCurrencyChangeGuard`).
7. **Catálogo global de monedas** (ISO 4217) que mantiene el SuperAdmin. Cada empresa solo activa las que usa y marca su base.
8. **Reportes en moneda base** (las specs que reportan montos suman el monto en moneda base guardado).

## Contexto

- No existe ningún concepto de moneda en el sistema. Los montos que hay (`Ticket.EstimatedTime`, `ConsumedTime`) son tiempos, no dinero.
- Los módulos futuros (ventas, compras, presupuestos, inventario) y el módulo de clientes (SPEC 57, 61, 63, 64) necesitan guardar montos con moneda.

## Scope

**In:**

**A. Esquema `Finance` (compartido)**
1. `Finance.Currencies` (global, `BaseAuditableEntity`, sin `CompanyId`): `Code` (ISO, 3 letras, mayúsculas, único entre vivos), `Name`, `Symbol`, `Decimals` (0–4), `IsActive`. Índice único filtrado `UX_Currencies_Code` con `"gcrecord" = 0`.
2. `Finance.CompanyCurrencies` (`BaseTenantEntity`): `CurrencyId`, `IsBase`, `IsActive`. Índices únicos filtrados: `(CompanyId, CurrencyId)` con `"gcrecord" = 0`, y `(CompanyId)` con `"isbase" = TRUE AND "gcrecord" = 0` (una sola base viva por empresa).
3. `Finance.ExchangeRates` (`BaseTenantEntity`): `CurrencyId`, `RateDate` (`date`), `Rate` (`numeric(18,6)`, `> 0`). Índice único filtrado `(CompanyId, CurrencyId, RateDate)` con `"gcrecord" = 0`. No se aceptan tasas para la moneda base de la empresa.

**B. Reglas en Application**
4. `CompanyBaseCurrencyCoordinator` (patrón de coordinadores, CLAUDE.md): al marcar una moneda como base desmarca la anterior en la misma transacción; impide dejar la empresa sin base viva y activa; la base no se puede desactivar ni borrar.
5. `CompanyBaseCurrencyChangeGuard`: antes de cambiar la base, pregunta a cada `IBaseCurrencyAmountSource` registrado si la empresa tiene montos. Esta spec define la interfaz y no registra ninguna fuente; SPEC 57 (precio de referencia) y SPEC 61 (montos del lead) registran las suyas. Con montos responde 409 `BASE_CURRENCY_IN_USE`.
6. `IExchangeRateResolver.ResolveAsync(companyId, currencyId, dateUtc)` → `(Rate, RateDate, IsBase)`: base → `1`; si no, la tasa de `dateUtc` o la última anterior (regla 4). Lo usan SPEC 57, 61 y 63. Lectura con Dapper.
7. `IMoneyConverter.ToBase(amount, rate)` con el redondeo de la regla 3, para que todas las specs conviertan igual.

**C. Endpoints**
8. `GET/POST/PUT/DELETE /api/v1/Currencies` + `POST /{id}/restore` — catálogo global, solo `SuperAdmin` (`[Authorize(Roles = "SuperAdmin")]`, mismo patrón que los endpoints `system-wide`). Una moneda usada por alguna empresa no se borra (409 `CURRENCY_IN_USE`).
9. `GET/POST/PUT/DELETE /api/v1/CompanyCurrencies` + `POST /{id}/restore` — monedas de la empresa activa (tenant del token). `PUT` cambia `IsBase` e `IsActive`. Recurso `CompanyCurrencies`.
10. `GET /api/v1/ExchangeRates?currencyId=&from=&to=` (paginado), `POST`, `PUT /{id}`, `DELETE /{id}` + `POST /{id}/restore`. Recurso `ExchangeRates`.
11. `GET /api/v1/ExchangeRates/resolve?currencyId=&date=` → `{ rate, rateDate, isBase }` para que el front muestre la tasa que se aplicará. Recurso `ExchangeRates`, `CanRead`.

**D. Menú, permisos y seed (SPEC 43)**
12. `SystemOption` en el módulo base `Administration`: `Currencies` (`/administration/currencies`, solo SuperAdmin), `CompanyCurrencies` (`/administration/company-currencies`) y `ExchangeRates` (`/administration/exchange-rates`). Permisos sembrados para SuperAdmin, Admin y Manager (lectura y escritura); Supervisor solo lectura de `ExchangeRates`.
13. Seed del catálogo global: `DOP`, `USD`, `EUR`, `COP`, `MXN`, `PEN`, `CLP`, `ARS`, `GTQ`, `CRC` (código, nombre, símbolo, decimales).
14. Seed por empresa (`SeedCompanyCurrenciesForCompanyAsync`, idempotente, mismo patrón que `SeedCustomerStagesForCompanyAsync`): `DOP` como base para las empresas existentes y para las nuevas: se llama desde `ICompanyCatalogSeeder.SeedDefaultCatalogsForCompanyAsync`, que ya invoca `CreateCompanyCommandHandler` (`POST /Companies`). En desarrollo, `JOIN-001` también activa `USD` y `EUR` con una tasa de ejemplo.

**Out of scope:**

- Tasas automáticas desde un servicio externo.
- Listas de precios por moneda (con el módulo de Ventas).
- Reportes en otra moneda que no sea la base.
- Elegir la moneda base al crear la empresa (por ahora `DOP`; se cambia en `CompanyCurrencies` mientras no haya montos).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `CURRENCY_NOT_FOUND` | 404 | Moneda global inexistente |
| `CURRENCY_IN_USE` | 409 | Borrar una moneda global que usa alguna empresa |
| `COMPANY_CURRENCY_NOT_FOUND` | 404 | Moneda de otra empresa o inexistente |
| `BASE_CURRENCY_REQUIRED` | 400 | Dejar la empresa sin base (desmarcar, desactivar o borrar la base) |
| `BASE_CURRENCY_IN_USE` | 409 | Cambiar la base cuando la empresa ya tiene montos |
| `EXCHANGE_RATE_FOR_BASE` | 400 | Registrar una tasa para la moneda base |
| `EXCHANGE_RATE_INVALID` | 400 | `Rate <= 0` o moneda inactiva o de otra empresa |
| `EXCHANGE_RATE_NOT_FOUND` | 400 | La moneda no tiene ninguna tasa en o antes de la fecha pedida |
| `DUPLICATE_KEY` | 409 | Segunda tasa viva para la misma moneda y fecha, o segunda base (índice único, SPEC 39) |
| `COMPANY_REQUIRED` / `USER_REQUIRED` | 401 | El token no trae empresa o usuario |

## Criterios de aceptación

1. Cada empresa existente tiene `DOP` como base después de la migración y el seed; una empresa nueva nace con `DOP` base.
2. Marcar `USD` como base desmarca `DOP` en la misma transacción mientras no haya montos; nunca hay dos bases ni ninguna (también con dos requests concurrentes: el índice único lo impide).
3. Desactivar o borrar la base responde 400 `BASE_CURRENCY_REQUIRED`.
4. Con una fuente de montos registrada que responde "sí" (fake en el test), cambiar la base responde 409 `BASE_CURRENCY_IN_USE`.
5. `resolve` devuelve `1` e `isBase = true` para la base; la tasa exacta de la fecha si existe; la última anterior con su `rateDate` si no; `EXCHANGE_RATE_NOT_FOUND` si no hay ninguna.
6. Registrar una tasa para la base responde 400 `EXCHANGE_RATE_FOR_BASE`; dos tasas para la misma moneda y fecha, 409.
7. `IMoneyConverter.ToBase(12500, 59.36)` = `742000.00`; los casos de punto medio redondean hacia arriba.
8. `Currencies` solo responde a SuperAdmin (403 al resto); las monedas y tasas de una empresa no se ven desde otra.
9. Unit tests de handlers, coordinador, guard, resolver y convertidor; integración (`CustomWebApplicationFactory`, `postgres:17`) para 1–6 y 8; `EXPLAIN` del SQL nuevo (convención 10).

## Decisiones

- **Esquema `Finance` compartido** (valor por defecto aceptado por el usuario): ventas, compras y presupuestos usarán las mismas monedas y tasas; no pertenecen al módulo de clientes.
- **Menú en `Administration`:** son datos maestros de la empresa, como `CompanyModules`.
- **Catálogo global + activación por empresa** (decisión del usuario): evita códigos inventados y mantiene un solo nombre y símbolo por moneda.
- **Guard por fuentes registradas** en vez de consultar tablas conocidas: Finance no depende de los módulos que guardan montos; cada módulo declara su tabla.
- **`DOP` como base inicial:** todas las empresas de desarrollo y QA son dominicanas. Se cambia sin costo mientras no haya montos.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Una empresa empieza a cargar montos antes de corregir su base. | El seed deja `DOP`; la pantalla de monedas muestra la base y el aviso de que queda fija con el primer monto. |
| Tasas desactualizadas. | `resolve` devuelve la fecha de la tasa usada y el front la muestra; las specs que guardan montos guardan también la tasa y su fecha. |

## Impacto en el frontend

- Specs FE (todas en Borrador, `join_frontb/specs`): **FE-30**. Base común del módulo: FE-29.
- Pantallas nuevas: catálogo global de monedas (SuperAdmin), monedas de la empresa (con indicador de base) y tasas.
