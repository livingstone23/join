# SPEC 36 — Adjuntos de ticket: parámetros por empresa, `TicketDocuments` y storage plugueable

> **Status:** Implementado
> **Depends on:** SPEC 35 (`TicketLog` es el punto de anclaje de cada adjunto vía `TicketLogsId`; `AddTicketNote` es la vía recomendada para adjuntar evidencia fuera de creación/finalización).
> **Date:** 2026-09-25
> **Objective:** Permitir adjuntar documentos a un ticket (creación, gestión o finalización) respetando cupos configurables por empresa — tipo de documento, tamaño máximo, máximo de archivos por ticket y cuota diaria opcional — persistiendo cada adjunto en `TicketDocuments` vinculado al `TicketLog` exacto donde se subió, detrás de una abstracción de almacenamiento (`IFileStorageService`) que hoy resuelve a disco local y queda lista para un adapter de Azure Blob o S3 sin tocar el resto del módulo.


> **Ajuste por SPEC 44 (2026-10-01):** esta spec se implementó antes que SPEC 44, así que sus `SystemOptionSeed` (`TicketAttachmentSettings`, `TicketDocuments`) quedaron bajo el padre `ManejoTickets` con rutas `/ManejoTickets/...`, igual que el resto de opciones de tickets. SPEC 44 las renombra junto con todas las demás: padre `TicketManagement`, `ModuleName = "Tickets"` y rutas `/tickets/<recurso-con-guion>` (ver su tabla de opciones de Tickets). Los `ControllerName` no cambian.

---

## Por qué existe esta spec

Es la tercera pieza del módulo de tickets extendido (después de SPEC 34, el roster de agentes, y SPEC 35, las acciones de ciclo de vida gateadas). El brief original la pide en detalle:

> "Deberá existir una tabla de parámetros para documentos adjuntos que controlará: Id de empresa a controlar, Tipo de documentos a adjuntar (word, excel, txt etc), Tamaño de documentos, Máximo de archivo permitidos, Tasa máxima de documentos (puede ser nulo si hay un máximo de cuota por documentos para llevar un control)."
> "Se podrán adjuntar documentos al ticket para la justificación tanto de su creación como de su solución. **TicketDocuments**: Id, TicketLogsId (con esta vinculación al adjuntar documentos o prueba lo trazamos en un momento del ticket, sea creación, finalización, o durante la gestión), DocumentType, OriginalName, NewName, Path (aquí guardar... indicar el proyecto por si utilizamos azure blob, s3 de amazon o una ruta de servidor — consultar con agente de IA)."

El repo confirmó, al auditar esta parte, que es la única de las cuatro brechas originales que es **100% greenfield**: no existe ninguna entidad de documentos, ninguna tabla de parámetros, y —más importante— **no existe ninguna abstracción de almacenamiento de archivos en todo el proyecto** (`grep` de `IFormFile`, `IStorageService`, `BlobServiceClient`, `S3` sobre `src/` no devuelve nada). El único precedente de "adapter para un servicio externo intercambiable por configuración" es `IEmailService` (`LoggingEmailAdapter` en dev, `SendGridEmailAdapter` en producción, switch por `"Email:Provider"` en `DependencyInjection.AddInfrastructureServices`). Esta spec replica exactamente ese patrón para `IFileStorageService`, con un único adapter implementado (`LocalDiskFileStorageAdapter`) y el seam listo para Azure Blob/S3 — que **no** se implementan acá (ver Decisiones: construir dos SDKs de nube sin un requerimiento de negocio concreto sobre cuál usar es la abstracción prematura que `CLAUDE.md` pide evitar).

La nota "consultar con agente de IA" del brief pedía explícitamente una decisión de diseño sobre `Path`. La resolución que se tomó: cada fila de `TicketDocuments` guarda **su propio** proveedor de almacenamiento (`StorageProvider`) junto al `Path`, en vez de asumir que todas las filas de la tabla viven bajo el mismo proveedor configurado globalmente. Así, si mañana la empresa migra de disco local a Azure Blob, los adjuntos viejos siguen resolviendo correctamente por su proveedor original sin necesidad de una migración de datos.

---

## Scope

**In:**

### A. Dominio

- `src/1.Domain/Enums/DocumentType.cs` (nuevo): `[Flags] enum DocumentType { None = 0, Pdf = 1, Word = 2, Excel = 4, Text = 8, Image = 16, Other = 32 }`. `[Flags]` porque el mismo enum sirve dos roles: valor único en `TicketDocument.DocumentType` (qué es este archivo) y bitmask en `TicketAttachmentSettings.AllowedDocumentTypes` (qué tipos acepta la empresa) — mismo patrón que `PermissionFlags`.
- `src/1.Domain/Enums/StorageProviderKind.cs` (nuevo): `enum StorageProviderKind { Local = 0, AzureBlob = 1, S3 = 2 }`. Solo `Local` tiene adapter en esta spec; los otros dos valores existen para que el dato quede completo cuando se implementen (ver Out of scope).
- `src/1.Domain/Messaging/TicketAttachmentSettings.cs` (nuevo): `BaseTenantEntity` con `AllowedDocumentTypes` (`DocumentType`), `MaxFileSizeBytes` (long), `MaxFilesPerTicket` (int), `MaxFilesPerDay` (int?, null = sin límite). Vive en `Messaging`, junto a `TicketCompanyDefault` — es el mismo tipo de "configuración de empresa para tickets".
- `src/1.Domain/Support/TicketDocument.cs` (nuevo): `BaseTenantEntity` con los campos del brief (`TicketLogsId`, `DocumentType`, `OriginalName`, `NewName`, `Path`) más tres columnas agregadas por esta spec y justificadas en Decisiones: `TicketId` (denormalizado, mismo criterio que ya usa `TicketLog.CompanyId`), `StorageProvider` (`StorageProviderKind`), `ContentType` (MIME, nvarchar) y `SizeBytes` (long). Vive en `Support`, junto a `TicketLog` — mismo criterio de agrupación que ya usa el proyecto para "trazas del ciclo de vida de un ticket".

### B. Application — abstracción de almacenamiento

- `src/2.Application/Interface/IFileStorageService.cs` (nuevo): contrato agnóstico de proveedor, sin ningún tipo de ASP.NET Core ni de un SDK de nube en la firma (mismo criterio que `IEmailService`).
  ```csharp
  public interface IFileStorageService
  {
      Task<FileStorageSaveResult> SaveAsync(Stream content, string storageKey, string contentType, CancellationToken cancellationToken);
      Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
      Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken);
  }
  public sealed record FileStorageSaveResult(string StorageKey, long SizeBytes);
  ```
  El **handler** (no el adapter) genera `storageKey` — el `NewName` con su prefijo de empresa/ticket — para que la base de datos y el disco queden sincronizados sin un round-trip adicional para "preguntarle" al storage qué nombre usó.
- `src/3.Infrastructure/Storage/Local/FileStorageOptions.cs` (nuevo): `{ string RootPath }`, bind de `"FileStorage:Local"`.
- `src/3.Infrastructure/Storage/Local/LocalDiskFileStorageAdapter.cs` (nuevo): implementación sobre el filesystem del servidor bajo `RootPath`. Valida que `storageKey` no contenga segmentos `..` (path traversal) antes de combinar rutas. Crea subdirectorios según haga falta en `SaveAsync`.
- `src/3.Infrastructure/DependencyInjection.cs`: bloque de registro nuevo, mismo estilo que el de `IEmailService`:
  ```csharp
  // FileStorage:Provider — "Local" es la única implementación de esta spec.
  services.Configure<FileStorageOptions>(configuration.GetSection("FileStorage:Local"));
  services.AddScoped<IFileStorageService, LocalDiskFileStorageAdapter>();
  ```
- `appsettings.json`: nueva sección
  ```json
  "FileStorage": {
    "Provider": "Local",
    "Local": { "RootPath": "App_Data/ticket-attachments" }
  }
  ```
  `Provider` se deja presente en config aunque el switch todavía no tenga una segunda rama — documenta la intención sin construir la rama que nadie pidió todavía (ver Decisiones).

### C. Application — `TicketAttachmentSettings` (config por empresa, mismo patrón que `TicketCompanyDefault`)

- DTOs en `src/2.Application.DTO/Messaging/TicketAttachmentSettings/`: `TicketAttachmentSettingsDto` (incluye `IsDeleted` computado desde `GcRecord`, igual que `TicketCompanyDefaultDto`, y `AllowedDocumentTypeNames` computado — lista de strings legible derivada del bitmask), `CreateTicketAttachmentSettingsDto`, `UpdateTicketAttachmentSettingsDto`.
- `src/2.Application/Mappings/Messaging/TicketAttachmentSettings/ITicketAttachmentSettingsMapper.cs` + `TicketAttachmentSettingsMapper.cs` (Mapperly, `ToEntity`/`ApplyUpdate`) — mismo patrón que `ITicketCompanyDefaultMapper`, el sibling estructural más cercano (config singleton por empresa).
- Commands: `CreateTicketAttachmentSettingsCommand`, `UpdateTicketAttachmentSettingsCommand`, `DeleteTicketAttachmentSettingsCommand` en `src/2.Application/UseCases/Messaging/TicketAttachmentSettings/Commands/`. `Create` rechaza con `409 CONFIG_ALREADY_EXISTS` si ya hay una fila activa para la empresa (una sola configuración por tenant, igual que `TicketCompanyDefault`).
- Queries: `GetTicketAttachmentSettingsQuery` (lista plana tenant-scoped, Dapper, sin paginar — 0 o 1 fila por empresa), `GetTicketAttachmentSettingByIdQuery`, `GetSystemWideTicketAttachmentSettingsQuery` (SuperAdmin, cross-tenant) en `src/2.Application/UseCases/Messaging/TicketAttachmentSettings/Queries/`.

### D. WebApi — `TicketAttachmentSettingsController`

- `src/4.Services.WebApi/Controllers/Messaging/TicketAttachmentSettingsController.cs` (nuevo), mismo layout que `TicketCompanyDefaultsController`: `[PermissionResource("TicketAttachmentSettings")]`, `GET` / `GET {id}` / `GET system-wide` (`[Authorize(Roles = "SuperAdmin")]`) / `POST` / `PUT {id}` / `DELETE {id}`.

### E. Application — `TicketDocument` (subida, listado, descarga, borrado)

- `src/2.Application.DTO/Messaging/Tickets/TicketDocumentDto.cs` (nuevo): `{ Guid Id, Guid TicketId, Guid TicketLogsId, string DocumentType, string OriginalName, string ContentType, long SizeBytes, Guid? CreatedByUserId, string? CreatedByUserName, DateTime CreatedAt }`. **Sin** `NewName`, `Path` ni `StorageProvider` — son detalle interno de almacenamiento; el cliente descarga por `Id` a través del endpoint dedicado, nunca construye la ruta.
- `src/2.Application/UseCases/Messaging/Tickets/InboundAttachment.cs` (nuevo): `public sealed record InboundAttachment(Stream Content, string FileName, string ContentType, long Length);` — el shape de "un archivo que entra al sistema", agnóstico de transporte. **Esta spec es la que lo declara**, y SPEC 48 (antes 37, etapa posterior) lo consume tal cual para los adjuntos que llegan por WhatsApp/correo (su sección C decía "se declara acá y SPEC 36 puede adoptarlo"; se resolvió al revés para que el tipo viva junto a la entidad que persiste, y para que ambas specs puedan implementarse en el mismo PR sin duplicarlo).
- `src/2.Application/UseCases/Messaging/Tickets/Commands/UploadTicketDocument/UploadTicketDocumentCommand.cs`: `sealed record UploadTicketDocumentCommand(Guid TicketId, Guid? TicketLogsId, InboundAttachment File) : ITransactionalCommand<Response<TicketDocumentDto>>`. Recibe el `Stream` dentro de `InboundAttachment`, provisto por el controller (`IFormFile.OpenReadStream()`), no un `IFormFile` — el Application layer no referencia `Microsoft.AspNetCore.Http`, mismo principio que ya mantiene `IEmailService` framework-agnostic.
- `CommandHandler` (ver flujo completo en Data model) + `CommandValidator` (`File.FileName`/`File.ContentType` `NotEmpty`, `File.Length > 0`, `TicketId != Guid.Empty`).
- `TicketLogsId` es **opcional**: si se omite, el handler resuelve automáticamente el log activo más reciente del ticket (`ORDER BY Created DESC` límite 1). Esto cubre el caso común —adjuntar evidencia inmediatamente después de crear, reasignar, finalizar o notar un ticket— sin obligar al frontend a hacer un round-trip a `GetTicketById` solo para descubrir el `TicketLogsId` recién generado. Si se provee explícitamente, debe pertenecer al mismo `TicketId` (si no, `400 INVALID_TICKET_LOG`).
- `src/2.Application/UseCases/Messaging/Tickets/Commands/DeleteTicketDocument/DeleteTicketDocumentCommand.cs` + `CommandHandler`: soft delete de la fila de metadata únicamente — **no** borra el archivo físico (ver Decisiones).
- `src/2.Application/UseCases/Messaging/Tickets/Queries/GetTicketDocuments/GetTicketDocumentsQuery.cs` + `QueryHandler`: lista plana (Dapper) de documentos activos de un ticket, sin paginar (acotado por `MaxFilesPerTicket`).
- `src/2.Application/UseCases/Messaging/Tickets/Queries/DownloadTicketDocument/DownloadTicketDocumentQuery.cs` + `QueryHandler`: valida metadata (tenant, existencia) vía Dapper, luego llama `IFileStorageService.OpenReadAsync(path)`. Devuelve `Response<TicketDocumentDownloadResult>` con `{ Stream Content, string ContentType, string OriginalName }`. Si el stream es `null` (fila en base sin archivo en disco — inconsistencia) → `404 FILE_NOT_FOUND_IN_STORAGE`. El controller es dueño del ciclo de vida del `Stream` devuelto (lo pasa a `File(stream, contentType, fileName)`, que ASP.NET Core dispone al terminar la respuesta).

### F. WebApi — `TicketDocumentsController` (ruta anidada bajo `Tickets`)

- `src/4.Services.WebApi/Controllers/Messaging/TicketDocumentsController.cs` (nuevo). **Única desviación del template `[Route("api/v{version:apiVersion}/[controller]")]`** que usa el resto del módulo: acá la ruta necesita `{ticketId}` en el path, así que se declara explícita: `[Route("api/v{version:apiVersion}/tickets/{ticketId:guid}/documents")]`. `[PermissionResource("TicketDocuments")]` — recurso de permisos separado de `Tickets`, para que una empresa pueda otorgar lectura de tickets sin otorgar automáticamente la descarga de sus adjuntos.
  - `GET /api/v1/tickets/{ticketId:guid}/documents` → default de verbo, `CanRead`.
  - `GET /api/v1/tickets/{ticketId:guid}/documents/{id:guid}/download` → `[RequirePermission(PermissionFlags.CanDownload)]` — primer uso real de este flag en el repo (existe en `PermissionFlags` desde SPEC 12/16 pero ningún controller lo había usado todavía).
  - `POST /api/v1/tickets/{ticketId:guid}/documents` (`[Consumes("multipart/form-data")]`, `[FromForm] IFormFile file`, `[FromForm] Guid? ticketLogsId`) → default, `CanCreate`. `201`.
  - `DELETE /api/v1/tickets/{ticketId:guid}/documents/{id:guid}` → default, `CanDelete`.
  - Mapeo de errores:

    | Código | HTTP |
    |---|---|
    | `COMPANY_REQUIRED` / `USER_REQUIRED` | 401 |
    | `TICKET_NOT_FOUND` / `TICKET_DOCUMENT_NOT_FOUND` / `FILE_NOT_FOUND_IN_STORAGE` | 404 |
    | `ATTACHMENT_SETTINGS_NOT_CONFIGURED` / `INVALID_TICKET_LOG` / `UNSUPPORTED_DOCUMENT_TYPE` / `DOCUMENT_TYPE_NOT_ALLOWED` / `FILE_TOO_LARGE` | 400 |
    | `MAX_FILES_PER_TICKET_REACHED` | 409 |
    | `DAILY_ATTACHMENT_QUOTA_REACHED` | 429 |

### G. Seed

- `SystemOptionSeed` en `GetAdministrativeSystemOptionSeeds()` para `TicketAttachmentSettings` (`CanRead/CanCreate/CanUpdate/CanDelete = true`, sin `CanDownload`) y para `TicketDocuments` (los cuatro CRUD en `true` **más** `CanDownload: true` explícito), ambos bajo el grupo de menú `ManejoTickets`, rutas `/ManejoTickets/ticket-attachment-settings` y `/ManejoTickets/ticket-documents` (SPEC 44 los mueve a `TicketManagement`, `ModuleName = "Tickets"`, rutas `/tickets/ticket-attachment-settings` y `/tickets/ticket-documents`).
- **Y las filas de `RoleSystemOptionSeed` en `GetRoleSystemOptionSeeds()`** (mismo motivo detallado en SPEC 34 sección H: el `SystemOptionSeed` declara el recurso, no otorga permiso a ningún rol; solo `Admin`/`SuperAdminCompany` reciben todo automáticamente):
  ```csharp
  new("Manager", "TicketAttachmentSettings", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
  new("Manager", "TicketDocuments",          true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
  new("Supervisor", "TicketAttachmentSettings", true, false, false, false),
  new("Supervisor", "TicketDocuments",          true, true, false, false, CanDownload: true),
  new("UsuarioSimple", "TicketAttachmentSettings", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
  new("UsuarioSimple", "TicketDocuments",          true, true, false, false, CanDownload: true, CanExport: false, CanExecute: false),
  ```
  `CanDownload` importa especialmente acá: es el único recurso del sistema que lo usa de verdad (`[RequirePermission(PermissionFlags.CanDownload)]` en el endpoint de descarga). Un rol con `CanDownload: false` obtiene 403 en `GET .../download` aunque pueda listar los adjuntos — que es justamente el smoke test F11.9.
- Fila `TicketAttachmentSettings` por defecto para las empresas sembradas de desarrollo (JOIN + la empresa privada): `AllowedDocumentTypes = Pdf | Word | Excel | Text | Image`, `MaxFileSizeBytes = 10_485_760` (10 MB), `MaxFilesPerTicket = 10`, `MaxFilesPerDay = null`. Sin esta fila, **ningún** upload funciona en el ambiente de desarrollo (`ATTACHMENT_SETTINGS_NOT_CONFIGURED`), así que sembrarla es obligatorio para que la feature sea usable out of the box.

### H. Tests

- Cobertura ≥ 90% en clases nuevas (gate de CI). Detalle completo en Implementation plan / F10.

**Out of scope (para specs futuras):**

- **Adapters de Azure Blob Storage y Amazon S3.** El seam (`IFileStorageService`, `StorageProviderKind`, `Path` guardado por fila) queda listo; construir dos SDKs de nube sin que negocio haya elegido cuál usar en producción sería la abstracción prematura que `CLAUDE.md` pide evitar. Cuando se elija un proveedor real, es una spec de una sola clase (`AzureBlobFileStorageAdapter` o `S3FileStorageAdapter`) más el branch en `DependencyInjection`.
- **Borrado físico del archivo al hacer soft-delete de `TicketDocument`.** Ver Decisiones — se prioriza no destruir evidencia sobre ahorrar almacenamiento.
- **Escaneo antivirus / sanitización de contenido** de los archivos subidos. Fuera de alcance; si negocio lo requiere, es una integración aparte (ClamAV, Azure Defender for Storage, etc.) que se engancha en el mismo punto donde hoy se llama `IFileStorageService.SaveAsync`.
- **Miniaturas / previsualización** de imágenes o PDFs.
- **Versionado de un mismo documento** (subir una v2 del mismo justificante). Cada subida es un `TicketDocument` nuevo e independiente.
- **Cuota diaria por usuario** (`MaxFilesPerDay` es por empresa, no por usuario individual — el brief lo describe como "tasa máxima de documentos" a nivel de la tabla de parámetros de empresa, no de usuario).
- **Adjuntar documentos a una nota en el mismo request que la crea** (`AddTicketNote` + upload combinados). Son dos llamadas: `POST /Tickets/{id}/notes` (SPEC 35) devuelve el `TicketLogsId`, después `POST /tickets/{id}/documents` lo referencia (o se omite y se resuelve automático al log más reciente).
- **Exponer `TicketAttachmentSettings` como parte de `GET /TicketCompanyDefaults`** (unificar ambas configuraciones en una sola tabla/endpoint). Son conceptualmente distintas (una define valores por defecto de campos del ticket, la otra gobierna adjuntos) y el brief las describe como entidades separadas.

---

## Data model

### `DocumentType` / `StorageProviderKind`

```csharp
// src/1.Domain/Enums/DocumentType.cs
[Flags]
public enum DocumentType
{
    None = 0,
    Pdf = 1 << 0,
    Word = 1 << 1,
    Excel = 1 << 2,
    Text = 1 << 3,
    Image = 1 << 4,
    Other = 1 << 5
}

// src/1.Domain/Enums/StorageProviderKind.cs
public enum StorageProviderKind
{
    Local = 0,
    AzureBlob = 1,
    S3 = 2
}
```

Mapeo servidor de extensión → `DocumentType` (usado por el handler de upload, **nunca** confía en un `DocumentType` enviado por el cliente):

| Extensión | `DocumentType` |
|---|---|
| `.pdf` | `Pdf` |
| `.doc`, `.docx` | `Word` |
| `.xls`, `.xlsx` | `Excel` |
| `.txt`, `.csv` | `Text` |
| `.png`, `.jpg`, `.jpeg`, `.gif` | `Image` |
| `.zip` | `Other` |
| cualquier otra | rechazo `UNSUPPORTED_DOCUMENT_TYPE` (400) — no cae en `Other` por defecto |

### `TicketAttachmentSettings` (nuevo, schema `Messaging`)

```csharp
// src/1.Domain/Messaging/TicketAttachmentSettings.cs
public class TicketAttachmentSettings : BaseTenantEntity
{
    public DocumentType AllowedDocumentTypes { get; set; }
    public long MaxFileSizeBytes { get; set; }
    public int MaxFilesPerTicket { get; set; }
    public int? MaxFilesPerDay { get; set; }
}
```

`AllowedDocumentTypes = DocumentType.None` es un valor válido: significa "esta empresa no acepta ningún adjunto todavía" — no hace falta un `IsEnabled` separado, el bitmask en cero ya deshabilita toda subida por construcción (todo chequeo `settings.AllowedDocumentTypes.HasFlag(type)` falla).

EF config (`TicketAttachmentSettingsConfiguration`, tabla `Messaging.TicketAttachmentSettings`): índice único sobre `CompanyId` **filtrado `WHERE GcRecord = 0`**, igual que el de SPEC 34 sobre `(UserId, CompanyId)` — **no** el índice sin filtro de `TicketCompanyDefaultConfiguration.cs:32`.

Esta es una desviación deliberada del sibling estructural, y el motivo es que acá el patrón sin filtro produce un callejón sin salida funcional, no solo una molestia administrativa: si alguien llama `DELETE /TicketAttachmentSettings/{id}`, el soft delete deja la fila en la tabla, el índice único sin filtro impide insertar una nueva para esa `CompanyId`, y el `CreateTicketAttachmentSettingsCommandHandler` tampoco resucita la vieja (su chequeo de duplicado mira solo filas activas, así que pasa la validación y después revienta contra el índice). Resultado: esa empresa queda **permanentemente sin poder subir adjuntos y sin ninguna vía de API para arreglarlo** — el upload responde `ATTACHMENT_SETTINGS_NOT_CONFIGURED` para siempre. En `TicketCompanyDefault` el mismo bug es recuperable operativamente (los tickets se siguen creando con el formato estándar); acá no lo es. Se filtra el índice en la tabla nueva y se deja el de `TicketCompanyDefault` como está (corregirlo es una spec de saneamiento aparte, no un efecto colateral de esta).

### `TicketDocument` (nuevo, schema `Support`)

```csharp
// src/1.Domain/Support/TicketDocument.cs
public class TicketDocument : BaseTenantEntity
{
    /// <summary>Denormalized for cheap per-ticket listing/counting without a join through TicketLogs.</summary>
    public Guid TicketId { get; set; }

    public Guid TicketLogsId { get; set; }

    public DocumentType DocumentType { get; set; }

    public string OriginalName { get; set; } = string.Empty; // nvarchar(150)

    /// <summary>Guid + timestamp based stored filename, unique regardless of tenant.</summary>
    public string NewName { get; set; } = string.Empty;       // nvarchar(150)

    /// <summary>Storage key/path, relative to the resolved provider's root.</summary>
    public string Path { get; set; } = string.Empty;          // nvarchar(500)

    /// <summary>Which provider this specific row's Path resolves against.</summary>
    public StorageProviderKind StorageProvider { get; set; }

    public string ContentType { get; set; } = string.Empty;   // nvarchar(150)

    public long SizeBytes { get; set; }

    public virtual Ticket Ticket { get; set; } = null!;
    public virtual TicketLog TicketLog { get; set; } = null!;
}
```

EF config (`TicketDocumentConfiguration`, tabla `Support.TicketDocuments`): FK `TicketId → Messaging.Tickets` Restrict, FK `TicketLogsId → Support.TicketLogs` Restrict, índice `(TicketId, Created)` para el listado por ticket, índice `(CompanyId, Created)` para el conteo de cuota diaria, `OriginalName`/`NewName` `HasMaxLength(150)`, `Path` `HasMaxLength(500)`, `ContentType` `HasMaxLength(150)`, query filter `GcRecord == 0`.

### `IFileStorageService` / `LocalDiskFileStorageAdapter`

```csharp
// src/2.Application/Interface/IFileStorageService.cs
public interface IFileStorageService
{
    Task<FileStorageSaveResult> SaveAsync(Stream content, string storageKey, string contentType, CancellationToken cancellationToken);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public sealed record FileStorageSaveResult(string StorageKey, long SizeBytes);
```

```csharp
// src/3.Infrastructure/Storage/Local/LocalDiskFileStorageAdapter.cs
public sealed class LocalDiskFileStorageAdapter(IOptions<FileStorageOptions> options, ILogger<LocalDiskFileStorageAdapter> logger)
    : IFileStorageService
{
    public async Task<FileStorageSaveResult> SaveAsync(Stream content, string storageKey, string contentType, CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafePath(storageKey); // throws on ".." traversal attempts
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, cancellationToken);

        return new FileStorageSaveResult(storageKey, fileStream.Length);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafePath(storageKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(File.OpenRead(fullPath));
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var fullPath = ResolveSafePath(storageKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    private string ResolveSafePath(string storageKey)
    {
        var combined = Path.GetFullPath(Path.Combine(options.Value.RootPath, storageKey));
        var root = Path.GetFullPath(options.Value.RootPath);

        if (!combined.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Storage key '{storageKey}' resolves outside the configured root.");
        }

        return combined;
    }
}
```

`DeleteAsync` está implementado y probado (ver F10) aunque el flujo de `DeleteTicketDocumentCommand` de esta spec no lo invoque todavía (soft delete de metadata solamente) — queda disponible para cuando una spec de limpieza/retención lo necesite, sin tener que tocar la interfaz.

### `UploadTicketDocumentCommandHandler` — flujo completo

1. `CompanyId == Guid.Empty` → `COMPANY_REQUIRED` (401).
2. `UserId` no parseable → `USER_REQUIRED` (401).
3. Cargar `Ticket` por `TicketId` + tenant; `null` → `TICKET_NOT_FOUND` (404).
4. Resolver `TicketLogsId`: si viene informado, cargar el `TicketLog` y validar `TicketId` coincide y `CompanyId` coincide → si no, `INVALID_TICKET_LOG` (400). Si viene `null`, tomar el `TicketLog` más reciente activo del ticket (`GetAllAsync()` filtrado + `OrderByDescending(x => x.Created).FirstOrDefault()` — mismo patrón in-memory que ya usa el módulo). Si el ticket no tiene ningún log todavía (no debería pasar — `Creation` siempre genera uno), `INVALID_TICKET_LOG`.
5. Cargar `TicketAttachmentSettings` activo de la empresa; `null` → `ATTACHMENT_SETTINGS_NOT_CONFIGURED` (400).
6. Extraer la extensión de `request.File.FileName`, resolver `DocumentType` con la tabla de la sección Data model; sin match → `UNSUPPORTED_DOCUMENT_TYPE` (400).
7. `!settings.AllowedDocumentTypes.HasFlag(documentType)` → `DOCUMENT_TYPE_NOT_ALLOWED` (400).
8. `request.File.Length > settings.MaxFileSizeBytes` → `FILE_TOO_LARGE` (400).
9. Contar `TicketDocument` activos con `TicketId == request.TicketId`; `>= settings.MaxFilesPerTicket` → `MAX_FILES_PER_TICKET_REACHED` (409).
10. Si `settings.MaxFilesPerDay.HasValue`: contar `TicketDocument` activos de la empresa con `Created >= DateTime.UtcNow.Date`; `>= settings.MaxFilesPerDay.Value` → `DAILY_ATTACHMENT_QUOTA_REACHED` (429).
11. `newName = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}{extension}"`; `storageKey = $"{companyId}/{ticketId}/{newName}"`.
12. `await fileStorageService.SaveAsync(request.File.Content, storageKey, request.File.ContentType, cancellationToken)`.
13. Construir `TicketDocument { TicketId, TicketLogsId (resuelto), DocumentType, OriginalName = request.File.FileName, NewName = newName, Path = storageKey, StorageProvider = StorageProviderKind.Local, ContentType = request.File.ContentType, SizeBytes = saveResult.SizeBytes }`.
14. `InsertAsync` + `SaveChangesAsync`. `result <= 0` → `UPLOAD_FAILED` (400). (Riesgo de archivo huérfano si el paso 14 falla después de un 12 exitoso — ver Riesgos, aceptado sin compensación transaccional.)
15. Mapear a `TicketDocumentDto` y retornar.

### `GetTicketDocumentsQuery` — SQL

```sql
SELECT
    td.Id, td.TicketId, td.TicketLogsId,
    CASE td.DocumentType
        WHEN 1 THEN 'Pdf' WHEN 2 THEN 'Word' WHEN 4 THEN 'Excel'
        WHEN 8 THEN 'Text' WHEN 16 THEN 'Image' WHEN 32 THEN 'Other'
        ELSE CONCAT('Unknown(', td.DocumentType, ')')
    END AS DocumentType,
    td.OriginalName, td.ContentType, td.SizeBytes,
    u.Id AS CreatedByUserId, CONCAT(u.FirstName, ' ', u.LastName) AS CreatedByUserName,
    td.Created AS CreatedAt
FROM Support.TicketDocuments td
LEFT JOIN Security.Users u ON td.CreatedBy = CAST(u.Id AS NVARCHAR(64))
WHERE td.TicketId = @TicketId AND td.CompanyId = @TenantId AND td.GcRecord = 0
ORDER BY td.Created DESC;
```

### `DownloadTicketDocumentQuery` — flujo

1. `COMPANY_REQUIRED` si falta tenant.
2. Cargar metadata (`Id`, `TicketId`, `CompanyId`, `Path`, `ContentType`, `OriginalName`) por `Id` + `TicketId` + `CompanyId`; sin match → `TICKET_DOCUMENT_NOT_FOUND` (404).
3. `stream = await fileStorageService.OpenReadAsync(row.Path, cancellationToken)`; `null` → `FILE_NOT_FOUND_IN_STORAGE` (404).
4. Retornar `Response<TicketDocumentDownloadResult>.Ok(new(stream, row.ContentType, row.OriginalName))`.

---

## Implementation plan

### F1 — Dominio

1. Crear `DocumentType.cs`, `StorageProviderKind.cs` en `src/1.Domain/Enums/`.
2. Crear `TicketAttachmentSettings.cs` en `src/1.Domain/Messaging/`.
3. Crear `TicketDocument.cs` en `src/1.Domain/Support/`.
4. `dotnet build -c Release` → 0 errores.

### F2 — Persistencia y migración

1. Crear `TicketAttachmentSettingsConfiguration.cs` (`src/3.Persistence/Configuration/Messaging/`) y `TicketDocumentConfiguration.cs` (`src/3.Persistence/Configuration/Support/`) con la sección Data model.
2. Agregar `DbSet<TicketAttachmentSettings>` y `DbSet<TicketDocument>` a `ApplicationDbContext`.
3. `dotnet ef migrations add AddTicketAttachmentsAndDocuments --project ../3.Persistence --startup-project .`. Verificar: ambos índices, FKs `Restrict`, longitudes de columna, sin query filter faltante.
4. `dotnet ef database update`.

### F3 — `IFileStorageService` + adapter local

1. Crear `IFileStorageService.cs` en `src/2.Application/Interface/`.
2. Crear `FileStorageOptions.cs` y `LocalDiskFileStorageAdapter.cs` en `src/3.Infrastructure/Storage/Local/`.
3. Registrar en `DependencyInjection.cs` (`services.Configure<FileStorageOptions>(...)`, `services.AddScoped<IFileStorageService, LocalDiskFileStorageAdapter>();`).
4. Agregar la sección `FileStorage` a `appsettings.json` (y `appsettings.Development.json` si usa un `RootPath` distinto para dev).
5. `dotnet build` → 0 errores.

### F4 — `TicketAttachmentSettings` CRUD

1. DTOs, `ITicketAttachmentSettingsMapper`/`TicketAttachmentSettingsMapper`, commands (`Create`/`Update`/`Delete`) y queries (`GetTicketAttachmentSettings`/`GetTicketAttachmentSettingById`/`GetSystemWideTicketAttachmentSettings`), calcados de `TicketCompanyDefault` — mismos nombres de error (`COMPANY_REQUIRED`, `CONFIG_ALREADY_EXISTS`, `TICKET_ATTACHMENT_SETTINGS_NOT_FOUND`).
2. `dotnet build` → 0 errores.

### F5 — `TicketAttachmentSettingsController`

1. Crear el controller con los 6 endpoints, mismo layout que `TicketCompanyDefaultsController`.
2. `dotnet build` → 0 errores.

### F6 — `UploadTicketDocument` / `DeleteTicketDocument`

1. Crear `TicketDocumentDto.cs`.
2. Crear `UploadTicketDocumentCommand`, `UploadTicketDocumentCommandValidator`, `UploadTicketDocumentCommandHandler` con el flujo completo de la sección Data model.
3. Crear `DeleteTicketDocumentCommand`/`CommandHandler` (soft delete de metadata, `TICKET_DOCUMENT_NOT_FOUND` si no existe o es de otro tenant/ticket).
4. `dotnet build` → 0 errores.

### F7 — Queries de lectura/descarga

1. Crear `GetTicketDocumentsQuery`/`QueryHandler` (SQL de la sección Data model).
2. Crear `DownloadTicketDocumentQuery`/`QueryHandler` (flujo de la sección Data model), con el record `TicketDocumentDownloadResult(Stream Content, string ContentType, string OriginalName)`.
3. `dotnet build` → 0 errores.

### F8 — `TicketDocumentsController`

1. Crear el controller con la ruta explícita anidada, los 4 endpoints, `[RequirePermission(PermissionFlags.CanDownload)]` en `download`, y el mapeo de errores de la sección Scope.
2. El action de `Upload` recibe `[FromForm] IFormFile file` y `[FromForm] Guid? ticketLogsId`; construye el comando con `new InboundAttachment(file.OpenReadStream(), file.FileName, file.ContentType, file.Length)` — la adaptación de `IFormFile` al tipo agnóstico ocurre acá, en la capa que sí puede referenciar `Microsoft.AspNetCore.Http`.
3. El action de `Download` retorna `File(result.Data!.Content, result.Data.ContentType, result.Data.OriginalName)`.
4. `dotnet build` → 0 errores.

### F9 — Seed

1. Agregar los dos `SystemOptionSeed` (`TicketAttachmentSettings`, `TicketDocuments` con `CanDownload: true`) a `GetAdministrativeSystemOptionSeeds()`, grupo `ManejoTickets` (SPEC 44 lo renombra a `TicketManagement`, `ModuleName = "Tickets"`, rutas `/tickets/...`).
1b. Agregar las seis filas de `RoleSystemOptionSeed` de la sección Scope G a `GetRoleSystemOptionSeeds()`.
2. Crear `SeedTicketAttachmentSettingsAsync(Guid companyId)`, idempotente, con los valores por defecto de la sección Scope, invocada junto al resto del seed de mensajería de las empresas de desarrollo.
3. `dotnet build` → 0 errores. Arrancar contra base limpia y confirmar que el seed corre sin error.

### F10 — Tests (~38 casos)

- `LocalDiskFileStorageAdapterTests` (6): `SaveAsync` escribe el archivo y devuelve el tamaño correcto; `OpenReadAsync` devuelve el contenido esperado; `OpenReadAsync` sobre una clave inexistente devuelve `null`; `DeleteAsync` borra y devuelve `true`; `DeleteAsync` sobre clave inexistente devuelve `false`; `storageKey` con `..` lanza `InvalidOperationException` (path traversal bloqueado).
- `TicketAttachmentSettings` commands/queries (10, calcados de los de `TicketCompanyDefault`): `CompanyId` vacío → 401 en cada uno; `Create` duplicado → 409; `Update`/`Delete` no encontrado → 404; happy paths.
- `UploadTicketDocumentCommandHandlerTests` (12): `CompanyId` vacío → 401; ticket no encontrado → 404; `TicketLogsId` explícito de otro ticket → 400 `INVALID_TICKET_LOG`; `TicketLogsId` omitido resuelve al log más reciente; sin `TicketAttachmentSettings` → 400 `ATTACHMENT_SETTINGS_NOT_CONFIGURED`; extensión no reconocida → 400 `UNSUPPORTED_DOCUMENT_TYPE`; tipo reconocido pero no permitido por `AllowedDocumentTypes` → 400 `DOCUMENT_TYPE_NOT_ALLOWED`; `Length` mayor a `MaxFileSizeBytes` → 400 `FILE_TOO_LARGE`; alcanzar `MaxFilesPerTicket` → 409; alcanzar `MaxFilesPerDay` → 429; `MaxFilesPerDay = null` nunca corta por cuota diaria; happy path llama `IFileStorageService.SaveAsync` con el `storageKey` esperado y persiste `TicketDocument` con `StorageProvider = Local`.
- `UploadTicketDocumentCommandValidatorTests` (3): `File.FileName`/`File.ContentType` vacíos fallan; `File.Length <= 0` falla.
- `DeleteTicketDocumentCommandHandlerTests` (3): no encontrado → 404; cross-tenant → 404; happy path soft-deletea sin llamar `IFileStorageService.DeleteAsync`.
- `GetTicketDocumentsQueryHandlerTests` (2): lista solo documentos activos del ticket pedido; orden `Created DESC`.
- `DownloadTicketDocumentQueryHandlerTests` (3): no encontrado → 404; `IFileStorageService.OpenReadAsync` devuelve `null` → 404 `FILE_NOT_FOUND_IN_STORAGE`; happy path devuelve el `Stream`/`ContentType`/`OriginalName` esperados.
- `dotnet test --filter "FullyQualifiedName~TicketAttachment|FullyQualifiedName~TicketDocument|FullyQualifiedName~FileStorage"` → 0 fallidos.

### F11 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → gate de 90% en `JOIN.Application` sigue en verde.
3. `dotnet ef database update` sobre base limpia → migración aplica y el seeder corre con la configuración por defecto de adjuntos.
4. Smoke: subir un `.pdf` de 1 MB a un ticket recién creado sin pasar `ticketLogsId` → 201, el documento queda vinculado al log `Creation`. Descargarlo → 200 con el contenido idéntico. Listar documentos del ticket → aparece.
5. Smoke: subir un `.exe` → 400 `UNSUPPORTED_DOCUMENT_TYPE`.
6. Smoke: subir un archivo de 11 MB con `MaxFileSizeBytes = 10 MB` → 400 `FILE_TOO_LARGE`.
7. Smoke: subir `MaxFilesPerTicket + 1` archivos al mismo ticket → el último → 409.
8. Smoke: borrar un documento → desaparece de `GET .../documents`, pero el archivo sigue en disco (verificar manualmente bajo `RootPath`).
9. Smoke: usuario con `CanRead` sobre `TicketDocuments` pero sin `CanDownload` → `GET .../download` → 403.
10. `CURL_REQUESTS.md` — agregar los 4 endpoints de `TicketDocuments` (incluyendo el ejemplo `multipart/form-data`) y los 6 de `TicketAttachmentSettings`.

---

## Acceptance criteria

### F1/F2 — Dominio y persistencia

- [ ] `DocumentType` es `[Flags]` con los 6 valores listados; `StorageProviderKind` tiene `Local`, `AzureBlob`, `S3`.
- [ ] `TicketAttachmentSettings` hereda `BaseTenantEntity` con los 4 campos de negocio del brief.
- [ ] `TicketDocument` hereda `BaseTenantEntity`, tiene los 5 campos del brief (`TicketLogsId`, `DocumentType`, `OriginalName`, `NewName`, `Path`) más `TicketId`, `StorageProvider`, `ContentType`, `SizeBytes`.
- [ ] Migración `AddTicketAttachmentsAndDocuments` única, aplica limpio sobre base vacía, con los índices y FKs de la sección Data model.
- [ ] El índice único de `TicketAttachmentSettings` sobre `CompanyId` está **filtrado** `WHERE GcRecord = 0`: borrar la configuración de una empresa y volver a crearla funciona (201, no violación de índice).

### F3 — Storage

- [ ] `IFileStorageService` no referencia ningún tipo de `Microsoft.AspNetCore.*` ni de un SDK de nube.
- [ ] `LocalDiskFileStorageAdapter.SaveAsync`/`OpenReadAsync`/`DeleteAsync` funcionan contra el filesystem real (verificado por tests de integración de archivo, no mocks).
- [ ] Un `storageKey` con `..` es rechazado antes de tocar el filesystem.
- [ ] Registrado como `Scoped` en `DependencyInjection.cs`, bindeado a `FileStorage:Local` en `appsettings.json`.

### F4/F5 — `TicketAttachmentSettings`

- [ ] `Create` retorna 409 `CONFIG_ALREADY_EXISTS` si ya hay una fila activa para la empresa.
- [ ] `AllowedDocumentTypes = DocumentType.None` es un valor válido, no rechazado por el validator.
- [ ] Los 6 endpoints existen con el layout de `TicketCompanyDefaultsController`.

### F6/F7 — Upload/Download/List/Delete

- [ ] `UploadTicketDocumentCommandHandler` rechaza con `ATTACHMENT_SETTINGS_NOT_CONFIGURED` si la empresa no tiene fila de configuración.
- [ ] Infiera `DocumentType` de la extensión del archivo, **ignorando** cualquier `DocumentType` que el cliente pudiera enviar (el comando no lo recibe como parámetro en absoluto).
- [ ] Rechaza con `DOCUMENT_TYPE_NOT_ALLOWED` un tipo reconocido pero fuera del bitmask `AllowedDocumentTypes` de la empresa.
- [ ] Rechaza con `FILE_TOO_LARGE` cuando `Length > MaxFileSizeBytes`.
- [ ] Rechaza con `MAX_FILES_PER_TICKET_REACHED` (409) al alcanzar el máximo por ticket.
- [ ] Rechaza con `DAILY_ATTACHMENT_QUOTA_REACHED` (429) al alcanzar `MaxFilesPerDay`, y **nunca** corta por esta razón cuando `MaxFilesPerDay` es `null`.
- [ ] `TicketLogsId` omitido resuelve al log activo más reciente del ticket; informado y de otro ticket → 400 `INVALID_TICKET_LOG`.
- [ ] `NewName` sigue el formato `yyyyMMddHHmmss_<guid sin guiones><extensión>`.
- [ ] `storageKey` persistido en `Path` tiene el formato `{companyId}/{ticketId}/{newName}`.
- [ ] `TicketDocumentDto` **no** expone `NewName`, `Path` ni `StorageProvider`.
- [ ] `DeleteTicketDocumentCommandHandler` soft-deletea la fila y **no** llama `IFileStorageService.DeleteAsync`.
- [ ] `DownloadTicketDocumentQueryHandler` retorna 404 `FILE_NOT_FOUND_IN_STORAGE` cuando la fila existe pero `OpenReadAsync` devuelve `null`.
- [ ] `GetTicketDocumentsQueryHandler` filtra por `TicketId` y `CompanyId`, ordena `Created DESC`, y solo trae filas activas.

### F8 — Controller

- [ ] `TicketDocumentsController` usa la ruta explícita `api/v{version:apiVersion}/tickets/{ticketId:guid}/documents`.
- [ ] `[PermissionResource("TicketDocuments")]` a nivel de clase, distinto de `Tickets`.
- [ ] El endpoint de descarga tiene `[RequirePermission(PermissionFlags.CanDownload)]`.
- [ ] `POST` acepta `multipart/form-data` y retorna 201.

### F9 — Seed

- [ ] `SystemOption` de `TicketAttachmentSettings` y de `TicketDocuments` existen, esta última con `CanDownload = true`.
- [ ] `GetRoleSystemOptionSeeds()` incluye las seis filas de `Manager`/`Supervisor`/`UsuarioSimple` para ambos recursos.
- [ ] `manager@join.com` (rol `Manager`, no privilegiado) obtiene 200 en `GET /api/v1/tickets/{id}/documents` y en `GET .../download` tras el seed; `simpleuser@join.com` obtiene 200 en el listado y 200 en la descarga, pero 403 en `TicketAttachmentSettings`.
- [ ] Las empresas sembradas de desarrollo tienen una fila `TicketAttachmentSettings` activa tras correr el seeder.
- [ ] Correr el seeder dos veces no duplica nada.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test` → 0 fallidos, ≥ 38 tests nuevos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate de CI).
- [ ] `CURL_REQUESTS.md` documenta los 10 endpoints nuevos.

---

## Decisions taken and discarded

- **Un único adapter (`LocalDiskFileStorageAdapter`) implementado, Azure Blob/S3 solo como seam** (elegido) vs. construir los tres adapters de una vez. Sin que negocio haya decidido a qué nube (si a alguna) migrar en producción, construir dos SDKs de proveedor sería exactamente la "abstracción para requerimientos hipotéticos" que `CLAUDE.md` pide evitar. El contrato (`IFileStorageService`) y el dato (`StorageProvider` por fila) son el 90% del trabajo de portabilidad; el adapter concreto es una clase chica cuando haga falta.
- **`Path` acompañado de `StorageProvider` por fila** (elegido, resuelve la nota "consultar con agente de IA" del brief) vs. un único proveedor global asumido para toda la tabla. Permite migrar de proveedor sin una migración de datos que reescriba filas históricas — los adjuntos viejos siguen sabiendo dónde viven.
- **`TicketId` denormalizado en `TicketDocument`** (agregado por esta spec, no estaba en la lista literal del brief) vs. solo `TicketLogsId` y resolver `TicketId` con un join a `TicketLogs` en cada query. Mismo criterio que ya usa `TicketLog.CompanyId` (también denormalizado pese a ser derivable de `Ticket`): las consultas de listado, conteo de cupo por ticket y cuota diaria son frecuentes y no deberían pagar un join extra por un dato que no cambia una vez creada la fila.
- **`ContentType`/`SizeBytes` agregados** (no estaban en la lista del brief) vs. mantener solo los 5 campos literales. Necesarios para servir la descarga con el `Content-Type` correcto y para validar `MaxFileSizeBytes` sin tener que volver a golpear el storage o el filesystem. Bajo costo, alto valor.
- **`DocumentType` se infiere server-side de la extensión, el cliente no lo envía** (elegido) vs. aceptar el valor que mande el formulario. Aceptarlo del cliente abriría un vector trivial: subir un ejecutable declarando `DocumentType=Pdf`. Inferir del lado servidor cierra esa clase de problema sin necesitar inspección de contenido (magic bytes) — que queda fuera de alcance (ver "escaneo antivirus" en Out of scope) por ser una capa de seguridad más profunda que lo que esta spec se propone resolver.
- **Soft delete de metadata sin borrar el archivo físico** (elegido) vs. borrado físico inmediato. `TicketDocuments` es evidencia de creación/solución de un ticket ("justificación", palabra textual del brief); borrar el binario junto con la fila sería destruir esa evidencia de forma irreversible en el mismo gesto que el resto del sistema trata como reversible (soft delete). Queda `IFileStorageService.DeleteAsync` implementado y testeado para cuando una spec de retención/purga lo necesite deliberadamente.
- **`TicketAttachmentSettings.AllowedDocumentTypes = None` es válido y no exige un `IsEnabled` separado** (elegido) vs. agregar un booleano de habilitación. El bitmask en cero ya produce el efecto deseado (ningún tipo pasa la validación) sin una columna redundante.
- **`TicketLogsId` opcional en el upload, con fallback al log más reciente** (elegido) vs. obligar siempre a un valor explícito. Sin este fallback, el flujo natural "crear ticket → adjuntar evidencia" obligaría a un round-trip extra a `GetTicketById` solo para descubrir el `Id` del log `Creation` recién generado (ni `CreateTicket` ni `ReassignTicket`/`FinishTicket` de SPEC 35 devuelven `Logs` embebidos en su `TicketDto`). El fallback resuelve el caso común sin tocar los contratos ya definidos en SPEC 35.
- **`[RequirePermission(PermissionFlags.CanDownload)]` en el endpoint de descarga** (elegido) vs. dejarlo bajo el default `CanRead` de `GET`. `PermissionFlags.CanDownload` existe en el proyecto desde SPEC 12/16 sin ningún consumidor real — es exactamente el caso que `CLAUDE.md` describe para `[RequirePermission]`: un endpoint cuyo verbo HTTP (`GET`) no refleja el flag semántico correcto (leer metadata de adjuntos no debería implicar poder descargar el contenido).
- **Ruta anidada explícita (`tickets/{ticketId}/documents`) en vez de un `TicketDocumentsController` plano con `ticketId` como query param** (elegido). Semánticamente un documento no existe sin su ticket — la URL debe reflejar esa pertenencia. Es la primera vez que el módulo usa una ruta anidada explícita en vez del template `[controller]`; se documenta como la excepción, no como un nuevo patrón general.
- **`MaxFilesPerDay` es una cuota de empresa, no de usuario** (elegido, literal del brief: "tabla de parámetros... Id de empresa a controlar"). Una cuota por usuario sería una dimensión de control distinta, no pedida.
- **Command recibe `Stream` en vez de `IFormFile`** (elegido, arquitectura). `IFormFile` es un tipo de `Microsoft.AspNetCore.Http`; dejarlo llegar a `2.Application` rompería la regla de dependencias de `CLAUDE.md` (Application solo depende de Domain + DTO). El controller hace la adaptación.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **Archivo huérfano en disco** si `SaveAsync` (paso 12) tiene éxito pero el `INSERT` de `TicketDocument` (paso 14) falla después. | Aceptado sin compensación transaccional (ver Decisiones de SPEC 34/35 sobre el mismo tipo de trade-off). El costo de un blob huérfano ocasional es bajo comparado con la complejidad de una saga; si el volumen de fallos lo justifica, una spec de limpieza puede recorrer `Support.TicketDocuments` vs. el filesystem y purgar huérfanos. |
| **`RootPath` de `LocalDiskFileStorageAdapter` en un disco no compartido entre instancias** — si la API corre en más de una réplica sin almacenamiento compartido, una descarga puede caer en una instancia que no tiene el archivo que otra instancia subió. | Documentado explícitamente: esta spec asume un único proceso de API o un `RootPath` sobre un volumen compartido (NFS, Azure Files, etc.) entre réplicas. Es exactamente el escenario que un adapter de Azure Blob/S3 resolvería de raíz — motivo adicional (no el único) para dejar el seam listo. |
| **Empresas sin `TicketAttachmentSettings` sembrada** (cualquier empresa creada después del seed de desarrollo, o en producción) no pueden subir ningún adjunto hasta que alguien cree la fila de configuración. | Comportamiento fail-closed deliberado (ver Decisiones de SPEC 34, mismo criterio). Es responsabilidad operativa configurar la empresa antes de habilitar adjuntos; el error `ATTACHMENT_SETTINGS_NOT_CONFIGURED` es explícito, no un 500 silencioso. |
| **Un usuario cambia la extensión del archivo para evadir el whitelist** (ej. renombra un `.exe` a `.pdf`). | Fuera de alcance de esta spec (ver "escaneo antivirus / sanitización" en Out of scope). La inferencia por extensión cierra el vector obvio (declarar un tipo falso vía el formulario) pero no reemplaza una inspección de magic bytes o antivirus si negocio la requiere. |
| **Conteo de `MaxFilesPerTicket`/`MaxFilesPerDay` vía `GetAllAsync()` en memoria** no escala indefinidamente. | Mismo patrón ya aceptado en todo el módulo (ver riesgos de SPEC 34/35). El volumen esperado (adjuntos por ticket, adjuntos por empresa por día) es órdenes de magnitud menor al de tickets o logs. |
| **`TicketDocumentsController` es la primera ruta anidada del módulo** — un cliente que asuma el template `[controller]` para construir la URL a mano se equivoca. | Documentado explícitamente en Decisiones y en `CURL_REQUESTS.md`. No hay forma de evitarlo sin renunciar a que la URL refleje la jerarquía real del recurso. |

---

## What is **not** in this spec

- Adapters de Azure Blob Storage o Amazon S3 (solo el seam).
- Borrado físico del archivo al soft-deletear la metadata.
- Escaneo antivirus / sanitización de contenido.
- Miniaturas o previsualización.
- Versionado de un mismo documento.
- Cuota diaria por usuario individual (solo por empresa).
- Combinar la creación de una nota (`AddTicketNote`, SPEC 35) con la subida de su adjunto en un solo request.
- Unificar `TicketAttachmentSettings` con `TicketCompanyDefault` en una sola tabla/endpoint.
- Ingesta multicanal (WhatsApp, correo) — SPEC 48 (etapa posterior; antes SPEC 37). Workflow/SLA parametrizable — SPEC 38.

Cada uno, si llega, va en su propia spec.
