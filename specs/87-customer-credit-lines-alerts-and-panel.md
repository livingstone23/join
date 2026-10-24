# SPEC 87 — Línea de crédito del cliente: límite, saldo mínimo de advertencia, historial, alertas configurables y panel de crédito

> **Status:** Borrador
> **Depends on:** SPEC 43-post (menú y permisos por rol), SPEC 55 (clientes), SPEC 56 (moneda base), SPEC 72 (correo), **SPEC 81** (configuración en Administración), **SPEC 83** (documentos), **SPEC 85** (alertas de Leads compartidas: `LeadAlerts`, destinatarios y entregas), **SPEC 86** (ventas a crédito y pagos). Rama de trabajo: `spec-87-customer-credit-lines-alerts-and-panel`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Definir una línea de crédito por cliente que las ventas a crédito reducen y los pagos restituyen, con un saldo mínimo que genera una advertencia, un historial de todos sus movimientos, alertas que se envían a los usuarios configurados y un panel de crédito visible para un rol específico del módulo de Leads.
> **Ajuste por revisión de consistencia (2026-10-24):** (1) **Primera venta a crédito a quien aún no es cliente:** se puede crear la línea de crédito desde la persona (`POST /Persons/{id}/credit-line`), lo que activa al cliente (`ICustomerActivationService`, fuente `CustomerStatusSource.CreditLine = 7`). (2) `EmailStatus` vive en `LeadAlertDeliveries` (SPEC 85). (3) Tipos de alerta con los valores de SPEC 85. (4) Líneas, movimientos y cargos registran su `IBaseCurrencyAmountSource`. (5) `CustomerCreditMovements.MovementType`: `Charge = 1`, `Payment = 2`, `PaymentReversal = 3`, `CreditNote = 4`, `LimitChange = 5`. (6) Seed en `JOIN-NI-001` (base NIO) con roles de desarrollo de SPEC 100.
> **Ajuste (2026-10-24, decisión del usuario):** sin servicio en segundo plano por ahora. Solo alertas disparadas por acciones; las de cuotas por fecha pasan al proceso programado con Hangfire (`specs/PROCESSES.md`).
> **Ajuste por SPEC 92 (2026-10-24):** el movimiento `CreditNote` solo ocurre cuando la nota de crédito se **aplica a la factura**. Una devolución reembolsada en efectivo no altera la línea de crédito.
> **Ajuste por SPEC 85 (2026-10-24):** las alertas de crédito usan la infraestructura compartida de SPEC 85 (`LeadAlerts` con categoría `Credit`, `LeadAlertRecipientRules`, `LeadAlertDeliveries`) en lugar de tablas `CreditAlert…` propias.
> **Origen:** pedido del usuario (2026-10-24): "poder definir una línea de crédito para el cliente, el cual al venderle a crédito pueda reducir su crédito y al recibir un pago pueda volver a incrementar… además de tener un monto establecido exista un mínimo de saldo: si la línea de crédito es de 50,000, al llegar a 10,000 de disponible generar una advertencia, y poder controlar en parametrización a qué usuario generar la alerta, para que un usuario con un rol específico del módulo de Leads pueda ver esos paneles". Superar el disponible: **bloquear** (con excepción autorizada). Alertas: **panel + correo**.

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Una línea de crédito por cliente** (`CustomerCreditLines`), en **moneda base** de la empresa: `CreditLimit` (ej. 50.000), `WarningThreshold` (saldo disponible mínimo, ej. 10.000; menor que el límite), `PaymentTermDays?` (plazo por defecto de sus facturas, SPEC 86), `IsActive`, `Notes`. Sin línea activa no se vende a crédito (`CREDIT_TERMS_NOT_ALLOWED`, SPEC 86).
2. **Disponible** = `CreditLimit` − saldo pendiente de las facturas a crédito vigentes (en moneda base, con la tasa guardada en cada factura). Se recalcula con cada movimiento y se guarda en `UsedAmount` y `AvailableAmount` para leer rápido.
3. **Movimientos que cambian el disponible** (`CustomerCreditMovements`, inmutable = **historial de crédito**):
   - factura a crédito emitida → `Charge` (−);
   - pago → `Payment` (+); pago revertido → `PaymentReversal` (−);
   - nota de crédito sobre una factura a crédito → `CreditNote` (+ saldo anulado);
   - cambio de límite o de umbral → `LimitChange` (registra el anterior y el nuevo, con usuario y motivo).

   Cada movimiento guarda el disponible resultante, el documento (número y código público), el usuario y la fecha.
4. **Bloqueo al superar el disponible:** emitir una factura a crédito por más del disponible → 409 `CREDIT_LIMIT_EXCEEDED` (devuelve el disponible y el faltante). Un usuario con el permiso `CreditOverride` (`CanExecute`) puede emitirla igual indicando un motivo; queda registrado quién la autorizó y por qué, y se genera la alerta `LimitExceeded`.
5. **Advertencia por saldo mínimo:** cuando un movimiento deja el disponible **en el umbral o por debajo** (ej. ≤ 10.000), se genera la alerta `ThresholdReached`. No se repite mientras el disponible siga por debajo; si vuelve a subir por encima del umbral, la siguiente bajada genera otra.
6. **Alertas** (`LeadAlerts` con categoría `Credit`, SPEC 85): `ThresholdReached`, `LimitExceeded`, `InstallmentOverdue` (cuota vencida sin pagar, SPEC 86) e `InstallmentDueSoon` (vence en `N` días). Estados: `Open` → `Acknowledged` (vista por un usuario, con fecha) → `Resolved` (automática cuando la causa desaparece: el disponible supera el umbral o la cuota se paga).
7. **Destinatarios configurables en Administración** (`LeadAlertRecipientRules`, SPEC 85), por tipo de alerta: usuarios concretos y/o un **rol**. Opción: incluir siempre al responsable de cobro de la factura (SPEC 86). Cada alerta nueva se registra para sus destinatarios y se les **envía un correo** (`IEmailService`) con el cliente, el motivo y los montos. El fallo del correo no bloquea la operación y queda en la entrega de la alerta (`LeadAlertDeliveries.EmailStatus`, SPEC 85).
8. **Panel de crédito** (`CreditPanel`): una opción de menú del módulo Clientes. **Solo la ven los roles a los que la empresa da permiso** (mecanismo actual `RoleSystemOption`; el seed se lo da a SuperAdmin, Admin y Manager, y la empresa lo asigna a otro rol, ej. "Cobranza"). Muestra:
   - mis alertas abiertas (las del usuario y las de sus roles);
   - clientes en advertencia o sobre el límite (disponible, límite, umbral, % usado);
   - facturas vencidas y cuotas que vencen en los próximos 7 días;
   - totales: crédito otorgado, usado, disponible y vencido, en moneda base.
9. **Alertas por fecha, para después:** `InstallmentOverdue` e `InstallmentDueSoon` dependen del paso del tiempo, no de una acción. Por decisión del usuario (2026-10-24) se implementan después con un **proceso programado en Hangfire** (ver `specs/PROCESSES.md`). Mientras tanto, el panel calcula al leer las cuotas vencidas y próximas, sin generar alertas.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `CustomerCreditLines` (`BaseTenantEntity`): `CustomerId` (índice único filtrado por empresa y cliente), `CreditLimit`, `WarningThreshold`, `UsedAmount`, `AvailableAmount`, `PaymentTermDays?`, `IsActive`, `Notes?`, `IsBelowThreshold`.
2. `CustomerCreditMovements` (solo inserción): `CreditLineId`, `MovementType`, `Amount` (con signo), `AvailableAfter`, `DocumentId?`, `DocumentNumber?`, `DocumentPublicCode?`, `PaymentId?`, `Reason?`, `UserId`, `OccurredAt`, `IsOverride`.
3. `LeadAlerts` (SPEC 85) suma los tipos `ThresholdReached = 3`, `LimitExceeded = 4`, `InstallmentOverdue = 5` e `InstallmentDueSoon = 6`, y las columnas `CreditLineId?`, `InstallmentId?` y `AvailableAmount?`. Las reglas de destinatarios y las entregas son las de SPEC 85 (`IncludeResponsibleUser` = responsable de cobro de la factura).

**B. Endpoints**
5. Clientes (permiso `CustomerCreditLines`): `GET /api/v1/Customers/{id}/credit-line`, `PUT` (crear o modificar límite y umbral, con `reason`, que genera `LimitChange`), `POST /deactivate`, `GET /api/v1/Customers/{id}/credit-line/movements` (historial paginado, exportable).
5.1 `POST /api/v1/Persons/{id}/credit-line` (permiso `CustomerCreditLines`, `CanCreate`): crea la línea de crédito de una persona que todavía no es cliente y la activa como cliente (`CustomerStatusSource.CreditLine = 7`) en la misma transacción.
6. Lista: `GET /api/v1/CustomerCreditLines?status=all|warning|exceeded&search=` (paginado).
7. Panel (permiso `CreditPanel`): `GET /api/v1/CreditPanel/summary`. Las alertas se leen y se marcan como vistas con los endpoints compartidos de SPEC 85 (`GET /api/v1/LeadAlerts?category=credit`, `POST /api/v1/LeadAlerts/{id}/acknowledge`).
8. Administración: las reglas de destinatarios de los tipos de crédito se configuran en `GET/PUT /api/v1/LeadAlertRecipientRules` (SPEC 85).
9. La emisión a crédito (SPEC 86) llama a `CustomerCreditCoordinator`, que valida, registra el movimiento y genera las alertas en la misma transacción. El correo se envía después del commit.
10. `POST /LeadCommercialDocuments/{id}/issue` acepta `creditOverrideReason?` (exige `CreditOverride`).

**C. Menú y permisos**
11. Clientes: `CustomerCreditLines` → `/customers/credit-lines`, `CreditPanel` → `/customers/credit-panel`. Recurso sin pantalla: `CreditOverride` (`CanExecute`). Seed de permisos: SuperAdmin y Admin con todo; Manager con panel y lectura de líneas.

**Out of scope:**
- Notificaciones en tiempo real (SignalR) y notificaciones push.
- Calificación de riesgo automática o bloqueo automático de clientes morosos.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `CREDIT_LINE_NOT_FOUND` | 404 | El cliente no tiene línea |
| `CREDIT_LIMIT_EXCEEDED` | 409 | Venta a crédito mayor que el disponible sin autorización (devuelve el disponible y el faltante) |
| `CREDIT_OVERRIDE_NOT_ALLOWED` | 403 | `creditOverrideReason` sin el permiso `CreditOverride` |
| `INVALID_CREDIT_THRESHOLD` | 400 | Umbral negativo o mayor o igual que el límite |
| `CREDIT_LIMIT_BELOW_USED` | 409 | Bajar el límite por debajo de lo usado sin `reason` (se permite con motivo y genera `LimitExceeded`) |

## Criterios de aceptación

1. Línea de 50.000 con umbral 10.000: una factura a crédito de 38.000 deja 12.000 disponibles y no alerta. Otra de 3.000 deja 9.000 y genera `ThresholdReached` para los destinatarios configurados, en el panel y por correo.
2. Una factura de 15.000 con 9.000 disponibles → 409 `CREDIT_LIMIT_EXCEEDED`. Con `CreditOverride` y motivo se emite, y el historial registra quién la autorizó.
3. Partiendo del estado del criterio 1 (9.000 disponibles, sin la factura del criterio 2), un pago de 5.000 sube el disponible a 14.000 y resuelve la alerta de umbral.
4. El historial muestra cada cargo, pago, reversión, nota de crédito y cambio de límite con el disponible resultante.
5. Un usuario con un rol sin permiso `CreditPanel` no ve la opción en el menú y la API responde 403. Al dar el permiso a ese rol, lo ve.
6. Una cuota vencida aparece en el panel como vencida (calculado al leer). La alerta `InstallmentOverdue` se prueba con el proceso programado (pendiente).
7. Unitarios: cálculo del disponible, reglas de umbral (sin repetir), override, destinatarios por usuario y rol, y el servicio idempotente. Integración: flujo de crédito completo con alertas y panel.

## Decisiones

- **Línea en moneda base:** facturas en distintas monedas se suman en moneda base con la tasa de cada factura.
- **Alertas por fecha con Hangfire, más adelante** (decisión del usuario, 2026-10-24): esta spec solo genera alertas disparadas por una acción (venta a crédito, pago, cambio de límite). Las de cuotas vencidas o próximas esperan el proceso programado.
- **El panel usa el permiso por rol que ya existe** (`RoleSystemOption`), sin un mecanismo de roles nuevo.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Líneas de crédito: Farmacia San Martín C$ 50.000 con umbral C$ 10.000 y plazo 30 días; Ferretería El Progreso C$ 60.000 con umbral C$ 12.000. El saldo de la factura de SPEC 86 deja a Ferretería El Progreso con su historial de cargo y pago.
- Alertas de crédito: `ThresholdReached` y `LimitExceeded` con reglas al rol Manager; `InstallmentOverdue` con `IncludeResponsibleUser = true` en `LeadAlertTypeSettings` y regla al rol Cobranza.
- Permiso `CreditPanel` para el rol de desarrollo "Cobranza" (SPEC 100).
