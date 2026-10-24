# SPEC 98 — Límite de descuento por rol, con solicitud de aprobación que otro usuario toma y resuelve

> **Status:** Borrador
> **Depends on:** SPEC 43-post (roles y permisos), **SPEC 81** (`CompanyCustomers`), **SPEC 83** (documentos y bitácora), **SPEC 85** (alertas de Leads), **SPEC 89** (punto de venta), SPEC 90 (venta bajo costo). Rama de trabajo: `spec-98-discount-limits-by-role-and-approval-requests`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que cada rol tenga un descuento máximo. Un usuario que necesita dar más **solicita** la aprobación. Otro usuario con el permiso adecuado **toma** la solicitud y la **aprueba o rechaza**. Aprobada, el descuento se aplica y el documento se puede emitir.
> **Ajuste por revisión de consistencia (2026-10-24):** (1) **Sin descuento a nivel de documento:** solo descuento por línea (se quitan `MaxDocumentDiscountPercent`, `RequestedDocumentDiscountPercent`, `ApprovedDocumentDiscountPercent` y `documentPercent`). (2) Solo un descuento mayor que el aprobado (o un precio menor) invalida la aprobación; cambiar la cantidad no. (3) Destinatarios calculados (`RecipientMode = Computed`, SPEC 85). (4) Seed: el rol Admin con límite 100 %. (5) Opción `DiscountApprovalRequests` con `ControllerName = "DiscountApprovals"`. (6) `DiscountApprovalRequests.Status`: `Pending = 1`, `Taken = 2`, `Approved = 3`, `Rejected = 4`, `Cancelled = 5`. (7) Seed en `JOIN-NI-001` con roles de desarrollo de SPEC 100.
> **Origen:** pedido del usuario (2026-10-24): "agrega el límite de descuento por rol, para que un usuario lo pueda solicitar, otro lo pueda tomar con el permiso adecuado y lo pueda aplicar".
> **Cambia SPEC 89 y 90:** reemplaza `PosMaxDiscountPercent` y el permiso `PosDiscountOverride` de SPEC 89: el límite ahora es por rol y vale para todos los documentos y el punto de venta. La venta bajo costo bloqueada (SPEC 90) también se resuelve con esta aprobación.

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Límite por rol** (`RoleDiscountLimits`): descuento máximo **por línea** (%). El límite de un usuario es el **mayor** de sus roles en la empresa. Un rol sin regla usa `CompanyCustomers.DefaultMaxDiscountPercent` (por defecto 0: todo descuento requiere aprobación, salvo que la empresa lo cambie).
2. **Dónde aplica:** líneas de documentos comerciales (SPEC 83) y del punto de venta (SPEC 89). Las líneas de producto del lead (SPEC 61) son informativas y se validan al pasar a un documento.
3. **Dentro del límite:** el descuento se aplica directo.
4. **Sobre el límite:** el borrador guarda el descuento pedido con la marca `PendingApproval` y **no se puede emitir** (`DISCOUNT_APPROVAL_REQUIRED`). El usuario envía una **solicitud** (`DiscountApprovalRequests`) con las líneas, el descuento pedido por línea, el total resultante y un motivo.
5. **Tomar y resolver:**
   - La solicitud llega como alerta `DiscountApprovalRequested` (SPEC 85) a los usuarios con el permiso `DiscountApprovals` cuyo propio límite cubre lo pedido (o que tienen `DiscountApprovalsUnlimited`).
   - Uno de ellos la **toma** (`Taken`, queda asignada a él; los demás la ven tomada). Puede liberarla.
   - La **aprueba** (puede aprobar un porcentaje menor al pedido) o la **rechaza** con motivo. Nadie aprueba su propia solicitud.
   - Aprobada: los descuentos aprobados quedan aplicados en las líneas y se quita la marca. El solicitante recibe la alerta `DiscountApprovalResolved` y puede emitir.
   - Rechazada: las líneas vuelven al descuento máximo del solicitante (o al que tenían antes) y se notifica.
6. **Vigencia de la aprobación:** subir después el descuento de una línea por encima de lo aprobado, o bajar su precio, la invalida; cambiar la cantidad no (vuelve a `PendingApproval`). Una solicitud no resuelta se marca `Expired` al leerla después de `CompanyCustomers.DiscountRequestExpiryHours` (por defecto 24). El aviso de vencimiento por tiempo queda para el proceso programado (Hangfire).
7. **Punto de venta:** la solicitud se envía desde la venta. La venta queda en espera y se puede retomar al aprobarse (o cobrar con el descuento máximo del cajero).
8. **Venta bajo costo** (SPEC 90, `BlockSaleBelowCost`): una línea bajo costo se trata como descuento sobre el límite y sigue el mismo flujo, con el motivo "Venta bajo costo".
9. **Rastro:** cada solicitud guarda solicitante, quién la tomó, quién la resolvió, porcentajes pedidos y aprobados, motivos y fechas. Los eventos van a la bitácora del documento (`DiscountRequested`, `DiscountApproved`, `DiscountRejected`).

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `RoleDiscountLimits` (`BaseTenantEntity`): `RoleId`, `MaxLineDiscountPercent`. Índice único `(CompanyId, RoleId)`.
2. `DiscountApprovalRequests` (`BaseTenantEntity`): `DocumentId`, `RequestedByUserId`, `RequestedAt`, `Reason`, `Status` (`Pending`, `Taken`, `Approved`, `Rejected`, `Cancelled`), `TakenByUserId?`, `TakenAt?`, `ResolvedByUserId?`, `ResolvedAt?`, `ResolutionComment?`, `ExpiresAt`.
3. `DiscountApprovalRequestLines`: `RequestId`, `LineId`, `RequestedPercent`, `ApprovedPercent?`.
4. `LeadCommercialDocumentLines` suma `DiscountStatus` (`Applied`, `PendingApproval`) y `ApprovedDiscountPercent?`.
5. `CompanyCustomers` suma `DefaultMaxDiscountPercent` y `DiscountRequestExpiryHours`.
6. `LeadAlerts` (SPEC 85) suma `DiscountApprovalRequested` y `DiscountApprovalResolved`.

**B. Endpoints**
7. Administración: `GET/PUT /api/v1/RoleDiscountLimits` (`/administration/discount-limits`).
8. `GET /api/v1/DiscountLimits/me`: límite efectivo del usuario.
9. `POST /api/v1/LeadCommercialDocuments/{id}/discount-requests` `{ reason }` (toma las líneas en `PendingApproval`); `POST /api/v1/LeadCommercialDocuments/{id}/discount-requests/{requestId}/cancel` (solicitante).
10. Aprobación (permiso `DiscountApprovals`): `GET /api/v1/DiscountApprovalRequests?status=` (bandeja), `POST /{id}/take`, `POST /{id}/release`, `POST /{id}/approve` `{ lines[{ lineId, approvedPercent }], comment? }`, `POST /{id}/reject` `{ comment }`.

**C. Menú**
11. Clientes: `DiscountApprovalRequests` → `/customers/discount-approvals` (`ControllerName = "DiscountApprovals"`). Recursos sin pantalla: `DiscountApprovalsUnlimited`.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `DISCOUNT_APPROVAL_REQUIRED` | 409 | Emitir o cobrar con líneas en `PendingApproval` |
| `DISCOUNT_REQUEST_NOT_FOUND` | 404 | Solicitud inexistente o de otra empresa |
| `DISCOUNT_REQUEST_ALREADY_TAKEN` | 409 | Tomar una solicitud que ya tomó otro |
| `DISCOUNT_SELF_APPROVAL` | 403 | Aprobar la propia solicitud |
| `DISCOUNT_APPROVER_LIMIT_EXCEEDED` | 403 | Aprobar más de lo que cubre el límite propio |
| `DISCOUNT_REQUEST_EXPIRED` | 409 | Resolver una solicitud vencida |

## Criterios de aceptación

1. Rol Vendedor con 10 %: un descuento de 8 % se aplica directo; uno de 18 % queda pendiente y emitir → 409.
2. La solicitud llega a los supervisores con límite ≥ 18 %. Uno la toma y los demás la ven tomada.
3. El supervisor aprueba 15 %: la línea queda con 15 %, el vendedor recibe la alerta y emite.
4. Si el vendedor sube la cantidad de la línea aprobada sin cambiar el descuento, la aprobación sigue; si sube el descuento a 16 %, vuelve a pendiente.
5. Nadie aprueba su propia solicitud (403).
6. En el punto de venta, una venta con descuento pendiente no se cobra hasta la aprobación.
7. Unitarios: límite efectivo por roles, flujo de estados, invalidación, aprobación parcial. Integración: flujo completo en documento y en punto de venta.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Límites: Vendedor 10 %, Supervisor 20 %, Manager 35 %, Admin 100 % y `DiscountApprovalsUnlimited`.
- Una solicitud pendiente de 18 % en una cotización de Farmacia San Martín y otra aprobada al 15 %.
