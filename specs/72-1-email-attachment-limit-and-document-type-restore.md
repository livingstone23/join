# SPEC 72.1 — Ajuste: tope de adjuntos sin permiso `EmailSettings` y tipos de documento de producto borrados

> **Status:** Implementado
> **Depends on:** SPEC 72 (envío de correos y `attachment-candidates`), SPEC 70 (tipos de documento de producto), SPEC 41-post (visibilidad de borrados y restauración para SuperAdmin). Front: **FE-47.1**.
> **Date:** 2026-10-24
> **Objective:** Que el modal "Enviar correo" conozca el tope de adjuntos de la empresa sin el permiso `EmailSettings`, y que un SuperAdmin pueda listar y restaurar los tipos de documento de producto borrados.
> **Ajuste por SPEC 101 (2026-10-24):** `PersonEmailSender` envía por `IEmailDispatcher` (`Module = Customers`, `Feature = Customers.PersonEmail`/`CustomerEmail`/`LeadEmail`); `EmailCompanySettings` suma la cuenta de SendGrid propia y las cuotas. Sin cambio de alcance de esta spec.
> **Origen:** pendientes de la revisión de FE-45 y FE-47 (2026-10-24). Es un ajuste de SPEC 72 y SPEC 70 que no cambia su alcance; no consume número (sub-número `72.1`).

---

## Por qué existe esta spec

- **Tope de adjuntos.** El modal de FE-47 lee el tope total de adjuntos de `GET /EmailCompanySettings`, que exige leer el recurso `EmailSettings`. SPEC 72 solo siembra ese permiso para SuperAdmin, Admin y Manager, así que un vendedor ve un tope por defecto (15 MB) que puede no coincidir con el de su empresa. El backend valida igual al enviar, pero el usuario se entera tarde.
- **Tipos de documento borrados.** `GET /ProductDocumentTypes` solo acepta `includeInactive` y nunca devuelve los borrados (`GcRecord = 0`). `POST /ProductDocumentTypes/{id}/restore` existe, pero desde el front solo se puede restaurar un tipo borrado en la misma sesión.

## Scope

**In:**

1. **`GET /Persons/{id}/emails/attachment-candidates` devuelve un objeto envuelto** en lugar de una lista:
   ```json
   { "items": [ /* PersonEmailAttachmentCandidateDto */ ], "maxAttachmentsTotalBytes": 15728640 }
   ```
   - `maxAttachmentsTotalBytes` es el de `EmailCompanySettings` de la empresa; sin fila, el valor por defecto de SPEC 72.
   - Mismos permisos y filtros de hoy (`leadId`, `search`). No exige `EmailSettings`.
   - Es un cambio de contrato que rompe: el único consumidor es FE-47, que se ajusta en FE-47.1 en el mismo ciclo.
2. **`GET /ProductDocumentTypes?includeDeleted=true`**, igual que `GET /EmailTemplateTypes` (SPEC 71) y SPEC 41-post:
   - Solo SuperAdmin; para cualquier otro usuario el parámetro se ignora (se devuelven los vivos).
   - Combinable con `includeInactive`.
   - `ProductDocumentTypeDto` hereda de `SoftDeletableDto` (`isDeleted`, `deletedOn`).
3. **`POST /ProductDocumentTypes/{id}/restore`** (ya existe): se confirma que restaurar un tipo cuyo código ya usa otro tipo vivo responde 409 `PRODUCT_DOCUMENT_TYPE_ALREADY_EXISTS`.

**Out of scope:**

- Cupo diario restante del usuario en el modal (`dailyLimit`, `sentToday`): el límite lo sigue avisando `EMAIL_DAILY_LIMIT_REACHED` al enviar.
- Cambiar los permisos sembrados de `EmailSettings`.
- Miniaturas reducidas de imágenes de producto (`?size=thumb`): FE-47.1 resuelve la carga diferida solo en el front.
- `includeDeleted` en otros catálogos de producto.

## Data model

```csharp
// src/2.Application.DTO/Common/Emails/PersonEmailDtos.cs
public sealed record PersonEmailAttachmentCandidatesDto
{
    public IReadOnlyList<PersonEmailAttachmentCandidateDto> Items { get; init; } = [];
    public long MaxAttachmentsTotalBytes { get; init; }
}

// src/2.Application.DTO/Catalog/CatalogDtos.cs
public sealed record ProductDocumentTypeDto : SoftDeletableDto { /* campos actuales */ }

// Queries
public sealed record GetProductDocumentTypesQuery(bool? IncludeInactive = null, bool? IncludeDeleted = null);
// GetPersonEmailAttachmentCandidatesQuery → Response<PersonEmailAttachmentCandidatesDto>
```

Sin cambios de tablas ni migraciones.

## Implementation plan

1. `PersonEmailAttachmentCandidatesDto` y el handler de `attachment-candidates`: lee `MaxAttachmentsTotalBytes` de la fila viva de `EmailCompanySettings` de la empresa o, sin fila, el valor por defecto de SPEC 72 (`EmailCompanySettings.DefaultMaxAttachmentsTotalBytes`). A diferencia de `PersonEmailSender`, que sin fila responde `EMAIL_SETTINGS_NOT_CONFIGURED`, este endpoint no falla: el selector de adjuntos debe abrir igual y el error de configuración lo informa el envío. Actualizar `ProducesResponseType`.
2. `ProductDocumentTypeDto : SoftDeletableDto`; `GetProductDocumentTypesQuery` con `IncludeDeleted` y filtro por `GcRecord` solo si el actor es SuperAdmin; parámetro `includeDeleted` en el controller.
3. Restaurar: verificar el 409 por código duplicado.
4. Tests y nota "Ajuste por SPEC 72.1" en SPEC 72 y SPEC 70; fila en `specs/README.md`.

## Criterios de aceptación

- [ ] Un usuario con permiso de envío y sin `EmailSettings` recibe `maxAttachmentsTotalBytes` de su empresa en `attachment-candidates`.
- [ ] Sin fila de `EmailCompanySettings`, `maxAttachmentsTotalBytes` es el valor por defecto de SPEC 72.
- [ ] `attachment-candidates` sigue filtrando por `leadId` y `search` dentro de `items`.
- [ ] Un SuperAdmin con `includeDeleted=true` recibe los tipos borrados con `isDeleted = true` y `deletedOn`.
- [ ] Un Admin con `includeDeleted=true` recibe solo los vivos.
- [ ] Restaurar un tipo borrado lo devuelve en el listado normal; con su código ocupado responde 409 `PRODUCT_DOCUMENT_TYPE_ALREADY_EXISTS`.

## Decisiones

- **Sí: envolver la respuesta de `attachment-candidates`** (usuario, 2026-10-24). El modal ya la llama al abrir el selector; no hace falta un endpoint ni un permiso nuevos. Rompe el contrato, pero el único consumidor es FE-47.
- **No: endpoint `compose-limits` aparte** (usuario, 2026-10-24). Una llamada más para un solo número.
- **No: sembrar lectura de `EmailSettings` para todos los roles.** Expondría el remitente y el resto de la configuración.
- **Sí: `includeDeleted` solo para SuperAdmin** (usuario, 2026-10-24), el mismo patrón de SPEC 41-post y SPEC 71.
- **No: cupo diario restante en el modal** (usuario, 2026-10-24).

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Un front viejo espera una lista en `attachment-candidates` y falla al deserializar. | Desplegar BE-72.1 y FE-47.1 juntos. |
| Restaurar un tipo choca con el índice único filtrado por código. | 409 `PRODUCT_DOCUMENT_TYPE_ALREADY_EXISTS` (SPEC 40-post). |

## Impacto en el frontend

FE-47.1: el modal lee el tope de `attachment-candidates` y deja de depender de `GET /EmailCompanySettings`; la parametrización de productos ofrece "Incluir borrados" y Restaurar para SuperAdmin.
