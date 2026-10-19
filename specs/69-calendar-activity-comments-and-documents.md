# SPEC 69 — Calendario: comentarios y documentos de la actividad, con límites de adjuntos por empresa

> **Status:** Borrador
> **Depends on:** SPEC 36 (adjuntos de ticket: tabla de extensiones), SPEC 44-post (actividades, bitácora `CalendarEventLog`, recurso `CalendarSettings`), SPEC 49-post (almacenamiento intercambiable), SPEC 62 (patrón de documentos y `LeadAttachmentSettings`), **SPEC 68** (flag `CanViewOthersActivities` y servicio de visibilidad). Rama de trabajo: `spec-69-calendar-activity-comments-and-documents`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que en el detalle de una actividad se registren comentarios y se adjunten documentos que quedan como histórico (solo un administrador los borra), con tipos y tamaños permitidos parametrizados por empresa.
> **Origen:** pedido del usuario (2026-10-19) desde "Detalle de actividad" y "Historial". Se divide con SPEC 67 (Persona/Cliente) y SPEC 68 (índice de actividades). Front: FE-44.

---

## Reglas de negocio (decisión del usuario, 2026-10-19)

1. **Comentarios y documentos cuelgan de la actividad.** En una serie cuelgan del maestro y se ven en todas sus ocurrencias; una ocurrencia modificada (excepción, `SeriesMasterId` con valor) tiene los suyos propios. No se crean filas por ocurrencia.
2. **Un documento** pertenece a la actividad y, opcionalmente, a un comentario de la misma actividad (`CalendarEventCommentId`), como en leads (BE-62).
3. **Inmutables para el autor:** una vez registrado, un comentario o documento no se edita ni lo borra quien lo creó. Quedan como histórico.
4. **Solo un administrador borra:** permiso `CanDelete` sobre el recurso nuevo `CalendarActivityRecords` (sembrado a `SuperAdmin`; Admin y SuperAdminCompany por `PrivilegedRoleNames`). El borrado es **lógico** (el archivo se conserva), pide **motivo obligatorio** y deja fila en la bitácora.
5. **Quién comenta y adjunta: quien puede ver la actividad.** El dueño; quien tiene `CalendarOthers.CanRead` (Panel); y quien tiene `CanViewOthersActivities` (SPEC 68). Además necesita `CalendarActivityRecords.CanCreate`.
6. **Bitácora:** `CalendarEventLogAction` suma `CommentAdded = 9`, `DocumentAdded = 10`, `CommentDeleted = 11`, `DocumentDeleted = 12`; las filas llevan el id del comentario o documento y, en los borrados, el motivo en `Notes`.
7. **Límites por empresa** (`CalendarAttachmentSettings`, una fila por empresa): tipos permitidos, tamaño máximo por archivo, máximo por comentario y máximo por día. Sin fila no se adjunta (`CALENDAR_ATTACHMENT_SETTINGS_NOT_CONFIGURED`); el seed crea la fila con PDF, Word, Excel e imagen, 10 MB, 5 por comentario y 200 por día.
8. Se puede comentar y adjuntar en actividades de cualquier estado, también anuladas o completadas. En una actividad borrada no (`CALENDAR_EVENT_NOT_FOUND`).
9. Mismo almacenamiento que tickets y leads: `IFileStorageResolver.Current`, ruta `calendar/{empresa}/{actividad}/…`.

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Calendar`)
1. `CalendarEventComment` (`BaseTenantEntity`): `CalendarEventId`, `Body` (máx. 2000), `AuthorUserId`, `CreatedUtc`, `DeletedByUserId?`, `DeletedUtc?`, `DeleteReason?` (máx. 500).
2. `CalendarEventDocument` (`BaseTenantEntity`): `CalendarEventId`, `CalendarEventCommentId?`, `DocumentType`, `OriginalName`, `NewName`, `Path`, `StorageProvider`, `ContentType`, `SizeBytes`, `UploadedByUserId`, `DeletedByUserId?`, `DeletedUtc?`, `DeleteReason?`. Mismos campos de almacenamiento que `LeadDocument`.
3. `CalendarAttachmentSettings` (`BaseTenantEntity`, índice único filtrado por empresa): `AllowedDocumentTypes` (`DocumentType` flags), `MaxFileSizeBytes`, `MaxFilesPerComment`, `MaxFilesPerDay?`.
4. `CalendarEventLog` suma `CalendarEventCommentId?` y `CalendarEventDocumentId?`; `CalendarEventLogAction` suma las acciones de la regla 6.
5. Migración `Spec69CalendarActivityCommentsAndDocuments`: tablas, índices (`CalendarEventId, CreatedUtc`), columnas de la bitácora, seed de la opción y permisos.

**B. API** — `CalendarEventRecordsController`, recurso `CalendarActivityRecords`, rutas bajo `/api/v1/CalendarEvents/{eventId}`
6. `GET …/comments` (más reciente primero, con autor y sus documentos; los borrados salen marcados con quién, cuándo y motivo, sin el texto).
7. `POST …/comments` (`{ body }`, `CanCreate`).
8. `GET …/documents?commentId=` (lista con autor, tamaño y comentario).
9. `POST …/documents` (multipart; `commentId` opcional; `CanCreate`).
10. `GET …/documents/{id}/download` (`[RequirePermission(PermissionFlags.CanDownload)]`).
11. `DELETE …/comments/{id}` y `DELETE …/documents/{id}` con `{ reason }` (`CanDelete`). Borrar un comentario no borra sus documentos.
12. Todas aplican la visibilidad de la regla 5; una actividad no visible responde `CALENDAR_EVENT_NOT_FOUND`.
13. `GET/POST/PUT /api/v1/CalendarAttachmentSettings` bajo el recurso `CalendarSettings` (pestaña "Adjuntos" de la configuración del calendario).
14. `GET /CalendarEvents/{id}/logs` (y los de Panel e índice) devuelven las acciones nuevas con `CommentId`/`DocumentId`.

**C. Seed y permisos**
15. `SeedCalendarAttachmentSettingsForCompanyAsync` (regla 7): empresas nuevas (`ICompanyCatalogSeeder.SeedDefaultCatalogsForCompanyAsync`), `SeedAsync` y, sin migraciones pendientes, `Program.cs` (`SeedCalendarAttachmentSettingsDefaultsAsync`), igual que leads.
16. `SystemOption` `ActivityRecords` (ruta `/calendar/activity-records`, `IsVisibleMenu: false`, recurso `CalendarActivityRecords`). Roles con `MyCalendar`: `CanRead`, `CanCreate`, `CanDownload`. `CanDelete` solo `SuperAdmin` (y los privilegiados).

**Out of scope:**
- Restaurar comentarios o documentos borrados.
- Editar comentarios.
- Notificaciones (correo o tiempo real) por comentario nuevo.
- Comentarios por ocurrencia de una serie sin modificarla (regla 1).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `CALENDAR_EVENT_NOT_FOUND` | 404 | La actividad no existe, está borrada o el usuario no la ve (regla 5). |
| `CALENDAR_ATTACHMENT_SETTINGS_NOT_CONFIGURED` | 400 | La empresa no tiene fila de límites. |
| `CALENDAR_DOCUMENT_TYPE_NOT_ALLOWED` | 400 | Extensión o tipo no permitido. |
| `CALENDAR_DOCUMENT_TOO_LARGE` | 400 | Supera `MaxFileSizeBytes`. |
| `CALENDAR_DOCUMENT_LIMIT_PER_COMMENT` / `CALENDAR_DOCUMENT_LIMIT_PER_DAY` | 400 | Supera el máximo por comentario o por día de la empresa. |
| `CALENDAR_COMMENT_NOT_FOUND` / `CALENDAR_DOCUMENT_NOT_FOUND` | 404 | No existe, es de otra actividad o de otra empresa. |
| `CALENDAR_RECORD_ALREADY_DELETED` | 409 | Borrar algo ya borrado. |
| Validación | 400 | `body` vacío o > 2000; `reason` vacío o > 500. |

## Plan de implementación

1. Entidades, configuraciones EF, acciones de bitácora y migración; unit tests de dominio.
2. `CalendarAttachmentSettings`: CRUD, validadores y seed; unit tests.
3. Comentarios: alta, lista y borrado con bitácora y visibilidad (servicio de SPEC 68); unit tests.
4. Documentos: subida con límites y tipo inferido (tabla de `UploadTicketDocumentCommandHandler`), lista, descarga y borrado; unit tests.
5. Controladores, opción `ActivityRecords` y permisos por rol.
6. Integration tests con el proveedor local y `EXPLAIN` del SQL nuevo.

## Criterios de aceptación

1. El dueño agrega un comentario y un PDF ligado a él; ambos se listan y la bitácora muestra `CommentAdded` y `DocumentAdded`.
2. En una serie, un comentario del maestro se ve desde cualquier ocurrencia; uno de una ocurrencia modificada solo desde ella.
3. Un usuario con el flag de SPEC 68 (o con `CalendarOthers.CanRead`) comenta en una actividad ajena; sin ninguno de los dos responde 404.
4. El autor no puede borrar su comentario (403 sin `CanDelete`); un administrador lo borra con motivo, el texto deja de devolverse, el archivo se conserva y la bitácora registra el borrado con el motivo.
5. Cada límite (tipo, tamaño, por comentario, por día) responde su código; sin fila de límites, `CALENDAR_ATTACHMENT_SETTINGS_NOT_CONFIGURED`.
6. Descargar sin `CanDownload` responde 403; un documento de otra empresa responde 404.
7. Una empresa nueva nace con los límites por defecto, y `PUT /CalendarAttachmentSettings` los cambia con `CalendarSettings.CanUpdate`.
8. Unit tests de validaciones y handlers; integración para 1–7; `EXPLAIN` del SQL nuevo.

## Decisiones

- **Inmutables y borrado solo por admin** (decisión del usuario): son registro histórico de la actividad; el borrado lógico con motivo conserva la traza.
- **Permiso `CanDelete` en un recurso propio** en vez de rol fijo (decisión del usuario): se ajusta en "Roles y permisos" sin tocar código.
- **Del maestro en una serie** (decisión del usuario): evita materializar ocurrencias para comentar.
- **Tablas propias en el esquema `Calendar`** en vez de reutilizar `LeadDocument` o `TicketDocument`: ciclos de vida y permisos distintos; se reutiliza el almacenamiento y la tabla de extensiones (misma decisión que BE-62).
- **Documento opcionalmente ligado a un comentario**, como un contacto del lead (BE-62): el front adjunta en el mismo envío del comentario.
- **Alta y borrado en la bitácora** (decisión del usuario): el "Historial" cuenta todo lo que pasó con la actividad.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Archivos grandes en proveedores en la nube. | Mismos límites y streaming que SPEC 49-post. |
| Un comentario borrado guarda datos sensibles. | El texto deja de devolverse por la API; queda en la base para auditoría (borrado lógico, regla 4). |

## Impacto en el frontend

- **FE-44** (Borrador): pestaña "Comentarios y documentos" en el detalle de la actividad (FE-22), acciones nuevas en la pestaña "Historial", y pestaña "Adjuntos" en la configuración del calendario (FE-25).
