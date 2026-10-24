# Índice de procesos (Leads: documentos, inventario, crédito y venta)

Procesos automáticos que generan las specs 81–98. Se dividen en dos grupos (decisión del usuario, 2026-10-24):

- **Disparados por una acción:** ocurren dentro de la misma operación del usuario (o justo después del commit, en el caso del correo). Se implementan con cada spec.
- **Programados por tiempo:** dependen del paso del tiempo, no de una acción. **Pendientes:** se prepararán con Hangfire (pendiente abierto de SPEC 44). Mientras tanto, las pantallas calculan el estado al leer (vencido, días que faltan) sin generar alertas.

Las alertas usan la infraestructura compartida de SPEC 85 (`LeadAlerts`, destinatarios por usuario o rol, panel + correo).

## 1. Disparados por una acción

| # | Acción | Efecto automático | Alerta | Spec |
|---|---|---|---|---|
| A1 | Aprobar (recibir) una orden de compra | Entrada al almacén (`PurchaseReceipt`), kardex, actualización de `LastCost` e historial de costos | Resuelve `LowStock`/`OutOfStock` si la existencia supera el mínimo | 85, 90 |
| A2 | Emitir una factura | Número (rango autorizado si aplica), copia de emisor, receptor y desglose, PDF, salida de almacén (`SaleIssue`), costo y margen por línea | `LowStock` / `OutOfStock` si la existencia queda en el mínimo o debajo; `NumberingRangeLow` si quedan pocos números | 83, 84, 85, 88, 90 |
| A3 | Emitir una factura a crédito | Cargo a la línea de crédito, cuotas y eventos de calendario de cobro | `ThresholdReached` si el disponible queda en el umbral o debajo; `LimitExceeded` si se autorizó una excepción | 86, 87 |
| A4 | Aprobar una factura | Ganar el lead (si el tipo lo indica) y activar o confirmar al cliente | — | 83 |
| A5 | Registrar un pago | Saldo y estado de cobro, aplicación a cuotas, abono a la línea de crédito, cierre del evento de calendario de la cuota pagada | Resuelve `ThresholdReached` si el disponible supera el umbral | 86, 87 |
| A6 | Revertir un pago | Recalcula saldo, cuotas y crédito | `ThresholdReached` si vuelve a bajar del umbral | 86, 87 |
| A7 | Anular una factura | Nota de crédito total automática, devolución de existencias, liberación del crédito pendiente | Resuelve alertas de existencias o crédito que correspondan | 83, 85, 87 |
| A8 | Devolución con nota de crédito parcial | Existencias (`Restock`/`Discard`); aplicada a la factura: baja el saldo y sube el crédito; en efectivo: reembolso y salida de caja | Resuelve `LowStock` o `ThresholdReached` si corresponde | 92 |
| A9 | Completar un traslado entre almacenes | Salida del origen y entrada al destino | `LowStock`/`OutOfStock` en el origen; resuelve en el destino | 85 |
| A10 | Ajuste de inventario o carga inicial por CSV | Movimientos de ajuste en el kardex | `LowStock`/`OutOfStock` según el resultado | 85, 96 |
| A11 | Cambiar el mínimo de un producto o almacén | Recalcula el estado de existencia | Genera o resuelve `LowStock` | 85 |
| A12 | Cambiar límite o umbral de una línea de crédito | Movimiento `LimitChange` | `ThresholdReached` o `LimitExceeded` según el disponible | 87 |
| A13 | Registrar una constancia de exoneración | Queda pendiente de aprobación | `ExemptionPendingApproval` a quienes aprueban | 94 |
| A14 | Solicitar un descuento sobre el límite | Bloquea la emisión hasta resolver | `DiscountApprovalRequested` a quienes aprueban | 98 |
| A15 | Resolver una solicitud de descuento | Aplica o revierte los descuentos | `DiscountApprovalResolved` al solicitante | 98 |
| A16 | Cambiar la tasa de un impuesto | Recalcula la sección de precio de los productos con ese impuesto | — | 95 |
| A17 | Cobrar en el punto de venta | Emite, aprueba, descuenta existencias y registra pagos en una transacción (A2–A5) | Las de A2, A3 y A5 | 89 |
| A18 | Cualquier alerta nueva | Correo a sus destinatarios después del commit | — | 85 |

## 2. Programados por tiempo (pendientes, Hangfire)

| # | Proceso | Qué revisa | Alerta | Spec |
|---|---|---|---|---|
| T1 | Cuotas vencidas | Cuotas `Pending`/`Partial` con fecha vencida | `InstallmentOverdue` al responsable de cobro y destinatarios | 86, 87 |
| T2 | Cuotas por vencer | Cuotas que vencen en los próximos `N` días | `InstallmentDueSoon` | 86, 87 |
| T3 | Numeración por vencer | Autorizaciones con fecha límite en `D` días | `NumberingRangeExpiring` | 88 |
| T4 | Constancias por vencer | Constancias aprobadas que vencen en `N` días | `ExemptionExpiring` | 94 |
| T5 | Solicitudes de descuento vencidas | Solicitudes sin resolver después de `DiscountRequestExpiryHours` | Aviso al solicitante | 98 |
| T6 | Documentos vencidos | Cotizaciones y proformas cuya vigencia venció | Opcional: aviso al vendedor | 83 |

Al preparar Hangfire, cada proceso programado debe ser idempotente (no duplicar alertas abiertas, gracias al índice único de `LeadAlerts`) y ejecutarse una sola vez aunque haya varias instancias de la API.
