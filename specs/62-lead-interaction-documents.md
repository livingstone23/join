# SPEC 62 — Documentos de los leads y sus contactos

> **Status:** Borrador
> **Depends on:** SPEC 36 (adjuntos de ticket: modelo y validaciones), SPEC 49-post (almacenamiento intercambiable: local, Azure Blob, S3, R2), **SPEC 61** (leads y contactos). Rama de trabajo: `spec-62-lead-interaction-documents`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Adjuntar documentos (propuestas, levantamientos, fotos) a un lead y, en particular, a cada momento de contacto, con límites por empresa y el mismo almacenamiento que ya usan los tickets.
> **Origen:** diseño del módulo de clientes (2026-10-16, pestaña "Documentos" del detalle y adjuntos en el modal de contacto).

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. Un documento pertenece a un lead y, opcionalmente, a uno de sus contactos (`LeadInteractionId`).
2. **Límites por empresa:** tipos permitidos, tamaño máximo por archivo, máximo por contacto y máximo por día.
3. **Mismo almacenamiento que los tickets:** `IFileStorageService` con el proveedor configurado (SPEC 49-post).
4. Solo se adjunta en leads `Open`. En leads inactivos, ganados o perdidos los documentos se consultan y descargan, no se agregan.
5. Borrar un documento es lógico y se puede restaurar (SPEC 41-post); el archivo físico se conserva mientras la fila exista.

## Scope

**In:**

1. `Customer.LeadDocuments` (`BaseTenantEntity`): `LeadId`, `LeadInteractionId?`, `DocumentType`, `OriginalName`, `NewName`, `Path`, `StorageProvider`, `ContentType`, `SizeBytes`, `UploadedByUserId`. Mismos campos de almacenamiento que `TicketDocument`.
2. `Customer.LeadAttachmentSettings` (`BaseTenantEntity`, una fila por empresa): `AllowedDocumentTypes` (`DocumentType` flags), `MaxFileSizeBytes`, `MaxFilesPerInteraction`, `MaxFilesPerDay?`. Sin fila, no se permite adjuntar (`LEAD_ATTACHMENT_SETTINGS_NOT_CONFIGURED`), igual que tickets; el seed crea la fila.
3. Validaciones (mismo mapeo de extensión a tipo que `UploadTicketDocumentCommandHandler`): tipo permitido, tamaño, máximo por contacto, máximo por día por empresa.
4. Endpoints (recurso `Leads`):
   - `GET /api/v1/leads/{leadId}/documents?leadInteractionId=` (lista con contacto, autor y tamaño).
   - `POST /api/v1/leads/{leadId}/documents` (multipart; `leadInteractionId` opcional; `CanUpdate`).
   - `GET /api/v1/leads/{leadId}/documents/{id}/download` (`[RequirePermission(PermissionFlags.CanDownload)]`).
   - `DELETE /api/v1/leads/{leadId}/documents/{id}` y `POST .../restore` (SuperAdmin restaura, SPEC 41-post).
5. `GET/POST/PUT /api/v1/LeadAttachmentSettings` bajo el recurso `LeadSettings` (pestaña "Adjuntos" de la parametrización).
6. La línea de tiempo de SPEC 61 incluye los documentos de cada contacto.
7. Seed por empresa de `LeadAttachmentSettings`: PDF, Word, Excel, imagen; 10 MB; 5 por contacto; 200 por día. Se aplica a empresas existentes en el arranque (como `SeedTicketAttachmentSettingsDefaultsAsync`) y a las nuevas desde `ICompanyCatalogSeeder.SeedDefaultCatalogsForCompanyAsync`.
8. El permiso `CanDownload` se siembra para los roles que tienen `Leads`.

**Out of scope:**

- Generar documentos desde plantillas (propuestas en PDF): lo puede traer el módulo de Presupuestos.
- Versionado de documentos.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `LEAD_ATTACHMENT_SETTINGS_NOT_CONFIGURED` | 400 | La empresa no tiene fila de límites |
| `LEAD_DOCUMENT_TYPE_NOT_ALLOWED` | 400 | Extensión o tipo no permitido |
| `LEAD_DOCUMENT_TOO_LARGE` | 400 | Supera `MaxFileSizeBytes` |
| `LEAD_DOCUMENT_LIMIT_PER_INTERACTION` / `LEAD_DOCUMENT_LIMIT_PER_DAY` | 400 | Supera el máximo por contacto o por día |
| `LEAD_NOT_OPEN` | 409 | Adjuntar a un lead que no está abierto |
| `LEAD_DOCUMENT_NOT_FOUND` | 404 | Documento de otra empresa, de otro lead o inexistente |
| `INVALID_LEAD_INTERACTION` | 400 | El contacto no pertenece al lead |

## Criterios de aceptación

1. Adjuntar un PDF a un contacto lo guarda con el proveedor de la empresa y lo lista bajo ese contacto y en la pestaña del lead.
2. Cada límite (tipo, tamaño, por contacto, por día) responde su código.
3. Descargar sin `CanDownload` responde 403; un documento de otra empresa responde 404.
4. En un lead inactivo se puede descargar pero no adjuntar (409).
5. Borrar y restaurar conserva el archivo físico.
6. Unit tests de validaciones y handlers; integración con el proveedor local para 1–5.

## Decisiones

- **Tabla propia en vez de generalizar `TicketDocument`:** los tickets y los leads tienen ciclos de vida y permisos distintos; se reutiliza el almacenamiento, no la tabla.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Archivos grandes en proveedores en la nube. | Mismos límites y streaming que SPEC 49-post. |

## Impacto en el frontend

- Specs FE (todas en Borrador, `join_frontb/specs`): **FE-37 (documentos) y FE-33 (pestaña Adjuntos)**. Base común del módulo: FE-29.
- Pestaña "Documentos" del detalle, zona de adjuntos en el modal de contacto y pestaña "Adjuntos" en Parametrización.
