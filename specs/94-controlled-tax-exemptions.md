# SPEC 94 — Facturas exentas de impuestos con control: constancia de exoneración adjunta, aprobación, permiso y vigencia

> **Status:** Borrador
> **Depends on:** SPEC 49-post (almacenamiento), SPEC 55 (clientes), **SPEC 81** (`CompanyCustomers`), **SPEC 82** (impuestos y grupos fiscales), **SPEC 83** (documentos y bitácora), SPEC 84 (PDF), **SPEC 85** (alertas de Leads), SPEC 88 (perfil fiscal), SPEC 89 (punto de venta), SPEC 91 (libros), SPEC 92 (notas de crédito), **SPEC 93** (presentación de totales). Rama de trabajo: `spec-94-controlled-tax-exemptions`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Permitir facturas (y cotizaciones, proformas y notas de crédito) **exentas de impuestos** para clientes exonerados, solo con una **constancia de exoneración adjunta**, aprobada, vigente y aplicada por un usuario con permiso. Además, cerrar la vía de quitar el IVA cambiando el grupo fiscal de una línea sin control.
> **Ajuste por revisión de consistencia (2026-10-24):** (1) La leyenda de exención es una leyenda del tipo factura con `Condition = WhenExempt` (SPEC 81) y variables `Exencion.*`. (2) Permisos: `CustomerTaxExemptions` (constancias, opción de menú), `TaxExemptionApprovals` (bandeja de aprobación, opción de menú), `TaxExemptionsApply` y `TaxGroupOverride` (recursos sin pantalla, `CanExecute`). El reporte de ventas exentas usa `[PermissionResource("TaxBooks")]`. (3) Al aprobar o rechazar, quien registró recibe `ExemptionResolved` (SPEC 85). (4) `CustomerTaxExemptions.Status`: `Pending = 1`, `Approved = 2`, `Rejected = 3`, `Revoked = 4`. (5) Seed en `JOIN-NI-001`.
> **Ajuste (2026-10-24, decisión del usuario):** el aviso de vencimiento por fecha pasa al proceso programado (Hangfire).
> **Origen:** pedido del usuario (2026-10-24): "considera que algunas facturas podrán ser exentas de impuestos y que esto esté sujeto a un control y permiso con un documento que se adjunte".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **La exención es del cliente y se respalda con una constancia** (`CustomerTaxExemptions`): tipo de exoneración, número de constancia o resolución, entidad que la emite, fechas de vigencia (desde–hasta), impuestos que exonera (por defecto todos los de tipo `Tax`; las retenciones nunca se exoneran aquí) y **al menos un archivo adjunto obligatorio** (PDF, JPG o PNG, validado por firma, tamaño máximo `CompanyCustomers.MaxExemptionFileBytes`, por defecto 5 MB).
2. **Control con aprobación:** una constancia nace `Pending`. La aprueba o rechaza (con motivo) un usuario con el permiso `TaxExemptionApprovals`, que **no puede ser quien la registró** (`EXEMPTION_SELF_APPROVAL`). Estados: `Pending` → `Approved` / `Rejected`. `Expired` no se guarda: es `Approved` con vigencia vencida. Una constancia aprobada no se edita; se revoca (`Revoked`, con motivo) y se registra otra.
   - Parámetro `CompanyCustomers.RequireExemptionApproval` (por defecto `true`). Con `false`, la constancia queda aprobada al registrarla, pero el adjunto sigue siendo obligatorio.
3. **Aplicar la exención a un documento** exige el permiso `TaxExemptionsApply` y una constancia del cliente `Approved` y vigente en la fecha del documento. Al aplicarla, los impuestos exonerados de todas las líneas quedan en 0 (la línea se marca "Exento") y el documento guarda la referencia y una copia de los datos de la constancia. En borrador se puede quitar; al emitir queda fija.
4. **Sugerencia automática, aplicación explícita:** si el cliente tiene una constancia aprobada y vigente, el editor y el punto de venta la **sugieren**, pero no la aplican solos: la aplica un usuario con permiso.
5. **Al emitir se vuelve a validar** que la constancia siga aprobada y vigente en la fecha de emisión (`EXEMPTION_NOT_VALID`). La conversión de una cotización exenta a factura revalida.
6. **Cierre de la vía manual:** cambiar el grupo fiscal de una línea a uno con **menos impuestos** que el del producto (por ejemplo, de "General" a "Exento") exige el permiso `TaxGroupOverride` y un motivo, que queda en la bitácora del documento. Los productos que por naturaleza son exentos (grupo "Exento" en el catálogo) no necesitan permiso.
7. **Rastro completo:**
   - en la bitácora del documento: `ExemptionApplied`, `ExemptionRemoved` y `TaxGroupOverridden`, con usuario, constancia y motivo;
   - en la bitácora de la constancia (`CustomerTaxExemptionEvents`): registro, aprobación, rechazo, revocación y cada documento donde se usó.
8. **Presentación (SPEC 93):** las líneas exentas muestran "Exento"; el bloque de totales muestra la base en la fila "Exento"; el PDF agrega la leyenda configurable "Venta exenta de IVA según {{Exencion.Tipo}} N° {{Exencion.Numero}}, emitida por {{Exencion.Entidad}}, vigente hasta {{Exencion.Vence}}".
9. **Notas de crédito** (SPEC 83, 92) de una factura exenta copian la exención de la factura, sin pedir permiso de nuevo.
10. **Consumidor final:** no admite exención (`FINAL_CONSUMER_EXEMPTION_NOT_ALLOWED`).
11. **Avisos:** una constancia pendiente de aprobar genera `ExemptionPendingApproval` al registrarla (acción) para quienes tienen `TaxExemptionApprovals`. El aviso de vencimiento `ExemptionExpiring` (`N` días antes, `CompanyCustomers.ExemptionAlertDays`, por defecto 30) depende de la fecha: lo generará el proceso programado (Hangfire, `specs/PROCESSES.md`); mientras tanto, la lista de constancias muestra los días que faltan.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `TaxExemptionTypes` (`BaseTenantEntity`): `Code`, `Name`, `IsActive`, `SortOrder`.
2. `CustomerTaxExemptions` (`BaseTenantEntity`): `CustomerId`, `TaxExemptionTypeId`, `CertificateNumber` (máx. 100), `IssuingEntity` (máx. 200), `ValidFrom` (`date`), `ValidUntil` (`date`), `Status`, `Notes?`, `RegisteredByUserId`, `ApprovedByUserId?`, `ApprovedAt?`, `RejectionReason?`, `RevokedByUserId?`, `RevokedAt?`, `RevocationReason?`.
3. `CustomerTaxExemptionTaxes`: `ExemptionId`, `TaxId` (solo tipo `Tax`).
4. `CustomerTaxExemptionFiles` (`BaseTenantEntity`): `ExemptionId`, `FileName`, `ContentType`, `SizeBytes`, `StorageKey`, `UploadedByUserId`.
5. `CustomerTaxExemptionEvents` (solo inserción): `ExemptionId`, `EventType`, `DocumentId?`, `UserId`, `OccurredAt`, `Details?` (`jsonb`).
6. `LeadCommercialDocuments` suma `TaxExemptionId?` y la copia `ExemptionTypeName?`, `ExemptionCertificateNumber?`, `ExemptionIssuingEntity?`, `ExemptionValidUntil?`.
7. `LeadCommercialDocumentLines` suma `IsExemptByCertificate` y `TaxGroupOverrideReason?`.
8. `CompanyCustomers` suma `RequireExemptionApproval`, `MaxExemptionFileBytes`, `ExemptionAlertDays`.
9. `LeadAlerts` (SPEC 85) suma los tipos `ExemptionExpiring` y `ExemptionPendingApproval`.

**B. Endpoints**
10. Constancias (permiso `CustomerTaxExemptions`): `GET /api/v1/Customers/{id}/tax-exemptions`, `POST` (multipart: datos + archivos), `POST /{exemptionId}/files`, `GET /{exemptionId}/files/{fileId}` (`CanDownload`), `POST /{exemptionId}/revoke` `{ reason }`.
11. Aprobación (permiso `TaxExemptionApprovals`): `GET /api/v1/TaxExemptionApprovals/pending`, `POST /api/v1/TaxExemptionApprovals/{id}/approve`, `POST /api/v1/TaxExemptionApprovals/{id}/reject` `{ reason }`.
12. Documentos (permiso `TaxExemptionsApply`): `POST /api/v1/LeadCommercialDocuments/{id}/tax-exemption` `{ exemptionId }`, `DELETE /{id}/tax-exemption` (solo `Draft`). El punto de venta acepta `exemptionId` en la venta (SPEC 89).
13. `PUT /LeadCommercialDocuments/{id}` (SPEC 83) exige `TaxGroupOverride` y `taxGroupOverrideReason` en las líneas cuyo grupo tiene menos impuestos que el del producto.
14. Reporte: `GET /api/v1/Reports/exempt-sales?from=&to=` (ventas exentas por constancia y cliente, exportable). El libro de ventas (SPEC 91) suma las columnas de la constancia.
15. Administración: `GET/POST/PUT/DELETE /api/v1/TaxExemptionTypes` (`/administration/tax-exemption-types`).

**C. Menú**
16. Clientes: `CustomerTaxExemptions` → `/customers/tax-exemptions` (constancias por estado), `TaxExemptionApprovals` → `/customers/tax-exemption-approvals` (bandeja de quien aprueba). Recursos sin pantalla: `TaxExemptionsApply`, `TaxGroupOverride`.

**Out of scope:**
- Verificar la constancia contra un servicio de la autoridad tributaria.
- Exoneraciones parciales por porcentaje (ej. 50 % del IVA): solo exención total de los impuestos elegidos.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `EXEMPTION_FILE_REQUIRED` | 400 | Registrar una constancia sin archivo |
| `INVALID_EXEMPTION_FILE` | 400 | Tipo o firma no permitidos, o mayor que el máximo |
| `EXEMPTION_SELF_APPROVAL` | 403 | Aprobar una constancia registrada por uno mismo |
| `EXEMPTION_NOT_APPROVED` | 409 | Aplicar una constancia `Pending`, `Rejected` o `Revoked` |
| `EXEMPTION_NOT_VALID` | 409 | Constancia vencida o fuera de vigencia en la fecha del documento |
| `EXEMPTION_CUSTOMER_MISMATCH` | 409 | Constancia de otro cliente |
| `TAX_GROUP_OVERRIDE_NOT_ALLOWED` | 403 | Bajar impuestos de una línea sin `TaxGroupOverride` |
| `TAX_GROUP_OVERRIDE_REASON_REQUIRED` | 400 | Bajar impuestos sin motivo |
| `FINAL_CONSUMER_EXEMPTION_NOT_ALLOWED` | 409 | Exención al consumidor final |

## Criterios de aceptación

1. Registrar una constancia sin archivo → 400. Con archivo queda `Pending` y avisa a quienes aprueban.
2. Quien la registró no puede aprobarla (403). Otro usuario con permiso la aprueba.
3. Aplicarla a una factura de C$ 55.000 deja IVA 0, la base en "Exento", total con IVA = C$ 55.000, y el PDF con la leyenda de la constancia.
4. Un usuario sin `TaxExemptionsApply` no puede aplicarla (403), aunque la constancia esté aprobada.
5. Emitir con la constancia vencida a la fecha de emisión → 409.
6. Cambiar una línea de "General" a "Exento" sin `TaxGroupOverride` → 403. Con permiso y motivo se permite y queda en la bitácora.
7. La nota de crédito de una factura exenta sale exenta sin pedir permiso.
8. La lista de constancias muestra los días para el vencimiento. La alerta `ExemptionExpiring` se prueba con el proceso programado (pendiente).
9. Unitarios: estados y aprobación, vigencia, aplicación y recálculo, control de grupo fiscal, permisos. Integración: flujo completo constancia → aprobación → factura exenta → libro de ventas.

## Decisiones

- **Constancia del cliente reutilizable** en lugar de un adjunto por factura: se aprueba una vez y se usa mientras esté vigente, con el rastro de cada documento donde se aplicó.
- **Aprobación por una segunda persona** por defecto: separa quien registra de quien autoriza.
- **Control del grupo fiscal por línea:** sin él, la exención controlada se podría evitar cambiando el grupo de cada línea.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Tipos de exoneración: "Exoneración del MHCP", "Zona franca", "Organización sin fines de lucro", "Misión diplomática", "Institución del Estado".
- Cliente nuevo **Asociación Salud para Todos** (`J0310000000204`, ficticio, organización sin fines de lucro) con constancia aprobada `EXO-SIM-2026-0101` (archivo PDF de ejemplo, vigente hasta el 31/12/2026) y una factura exenta emitida.
- Una constancia `Pending` de Farmacia San Martín para mostrar la bandeja de aprobación.
