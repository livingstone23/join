# SPEC 49-post — Almacenamiento de adjuntos intercambiable: local, Azure Blob, Amazon S3 y Cloudflare R2

> **Status:** Borrador
> **Origen:** copia de SPEC 49 de `main` (estado allí al copiarla: Borrador), adaptada a PostgreSQL para el fork `main_postgresql`. Se implementa **desde cero** en el fork, sin traer commits de `main` (decisión del usuario, 2026-10-09).
> **Rama de trabajo:** `spec-49-post-ticket-attachment-storage-providers`, creada desde `main_postgresql`; PR hacia `main_postgresql`, nunca hacia `main`.
> **Lectura en el fork:** dentro de esta spec, toda referencia a una spec de la serie copiada (40–49, 51, 99) se lee como su versión `-post` ("SPEC 41" = SPEC 41-post), y aplican las convenciones de `specs/README.md` → "Serie `-post`". Donde el texto copiado de `main` y la sección "Adaptación a PostgreSQL" difieren, prevalece esta última.
> **Depends on:** SPEC 36 (`IFileStorageService`, `LocalDiskFileStorageAdapter`, `TicketDocument.StorageProvider`, `TicketAttachmentSettings` y su seed; ya en el fork). Esta spec ajusta tres decisiones de SPEC 36: agrega los adapters de nube que SPEC 36 dejó fuera, quita `.gif` del tipo `Image` y quita `Text` de los tipos sembrados por defecto. SPEC 36 conserva su estado con una nota "Ajuste por SPEC 49-post".
> **Date:** 2026-10-09 (copia `-post`; original: 2026-10-06)
> **Objective:** Que los adjuntos de tickets se guarden en el proveedor que indique la configuración —carpeta local (para desarrollo y pruebas), Azure Blob Storage, Amazon S3 o Cloudflare R2— sin tocar el resto del módulo; que cada adjunto se siga descargando desde el proveedor donde se guardó aunque la configuración cambie después; que la carpeta local sobreviva a un contenedor recreado; y que las empresas sembradas acepten por defecto PDF, Word, Excel, PNG y JPG.

---

## Adaptación a PostgreSQL (fork `main_postgresql`)

Casi todo es de Application e Infrastructure, sin relación con el motor. Lo que cambia en el fork:

| Tema | En `main` (SQL Server) | En esta versión |
|---|---|---|
| `TicketDocument.StorageProvider` | Columna entera, sin migración | Igual (`integer`). |
| `DownloadTicketDocumentQueryHandler` | `SELECT` agrega `td.StorageProvider` | Igual: el SQL del fork va sin comillas y PostgreSQL lo resuelve a `td.storageprovider`. |
| Seed de `TicketAttachmentSettings` | EF/LINQ, compara el valor del bitmask | Igual. |
| `Dockerfile` | Imagen de `main` | Imagen del fork (`aspnet:10.0-alpine`): `RUN mkdir -p /app/data/ticket-attachments` en la etapa final; si la imagen corre con un usuario sin privilegios, la carpeta se crea con ese dueño. |
| `.env.example` | Formato de `main` | En el fork todavía tiene la conexión de SQL Server (la corrige SPEC 50 C); esta spec **solo agrega** las variables `FileStorage__...` y no toca la conexión. |
| Smoke Docker (F7.3) | `docker-compose up -d --force-recreate` | `docker-compose.yml` no trae base: el `.env` local apunta al contenedor `postgres:17` (`DatabaseProvider=PostgreSQL`, `ConnectionStrings__DefaultConnection=Host=host.docker.internal;Port=5432;...`). |
| Tests | — | Los tests de esta spec son unitarios; la suite de integración que se corre es la PostgreSQL (`~PostgreSql`). |

---

## Por qué existe esta spec

SPEC 36 dejó el contrato `IFileStorageService` y un único adapter (`LocalDiskFileStorageAdapter`), y difirió explícitamente Azure Blob y S3 "hasta que negocio elija un proveedor". El usuario pidió ahora (2026-10-06) poder elegir entre **S3, Azure Blob Storage, Cloudflare o local para probar**, por configuración.

Al revisar el código para esta spec aparecieron tres problemas que impiden simplemente "agregar un adapter":

1. **Cambiar de proveedor rompe las descargas existentes.** `UploadTicketDocumentCommandHandler` graba siempre `StorageProvider = StorageProviderKind.Local`, y `DownloadTicketDocumentQueryHandler` ni siquiera lee esa columna: abre el archivo con el único `IFileStorageService` registrado. Si mañana se registrara un adapter de nube en su lugar, todo adjunto guardado en disco devolvería `FILE_NOT_FOUND_IN_STORAGE`. La columna por fila que SPEC 36 diseñó para resolver esto existe, pero nada la consume.
2. **La carpeta local se pierde en Docker.** `docker-compose.yml` no monta ningún volumen, así que `App_Data/ticket-attachments` vive dentro del contenedor y desaparece al recrearlo. Además `App_Data/` no está en `.gitignore`: un archivo subido al probar en local puede terminar en un commit.
3. **El seed no coincide con lo pedido y no se actualiza.** `SeedTicketAttachmentSettingsAsync` siembra `Pdf | Word | Excel | Text | Image` y solo inserta si no hay fila, así que las bases existentes (QA) nunca reciben un cambio de valores por defecto. El usuario pidió PDF, Word, Excel, PNG y JPG: sobra `Text` (`.txt`, `.csv`), y `Image` acepta también `.gif`.

**Cloudflare R2 no necesita un adapter propio.** R2 expone la API de S3, así que un único adapter S3 con `ServiceUrl` configurable cubre AWS S3 y R2. Lo que sí cambia es el **tipo de proveedor por fila**: un adjunto guardado en R2 y otro en AWS S3 viven en servicios distintos, con credenciales distintas, así que se registran con valores distintos de `StorageProviderKind`. Si ambos fueran `S3`, después de pasar de R2 a AWS (o al revés) los adjuntos viejos se buscarían en el servicio equivocado.

---

## Scope

**In:**

### A. Dominio

- `StorageProviderKind` gana `CloudflareR2 = 3`. Se actualizan los comentarios XML de `AzureBlob` y `S3`, que hoy dicen "adapter NO implementado". La columna `TicketDocument.StorageProvider` ya es entera: **no hay migración**.
- Sin cambios en `DocumentType`.

### B. Application — elegir proveedor por adjunto

- `src/2.Application/Interface/IFileStorageService.cs`: el contrato gana `StorageProviderKind Kind { get; }`, para que el handler grabe en la fila el proveedor que realmente usó.
- `src/2.Application/Interface/IFileStorageResolver.cs` (nuevo):

  ```csharp
  public interface IFileStorageResolver
  {
      /// <summary>Proveedor configurado en FileStorage:Provider; recibe las subidas nuevas.</summary>
      IFileStorageService Current { get; }

      /// <summary>Proveedor que guardó un adjunto existente; null si no está configurado en este entorno.</summary>
      IFileStorageService? Resolve(StorageProviderKind kind);
  }
  ```

- `UploadTicketDocumentCommandHandler`: inyecta `IFileStorageResolver` en lugar de `IFileStorageService`; guarda con `resolver.Current` y graba `StorageProvider = resolver.Current.Kind` (hoy literal `Local`).
- `DownloadTicketDocumentQueryHandler`: el `SELECT` agrega `td.StorageProvider`; abre el archivo con `resolver.Resolve(row.StorageProvider)`. Si devuelve `null` → `STORAGE_PROVIDER_NOT_CONFIGURED`.
- Ningún tipo de un SDK de nube aparece en Application (mismo criterio de SPEC 36).

### C. Application — tipos de documento

- `UploadTicketDocumentCommandHandler.TryMapExtension`: `Image` acepta solo `.png`, `.jpg` y `.jpeg`. `.gif` pasa a `UNSUPPORTED_DOCUMENT_TYPE` (400) para todas las empresas. Los `.gif` ya subidos siguen descargándose: la descarga no vuelve a validar el tipo.

### D. Infrastructure — adapters y registro

- **Paquetes nuevos** en `JOIN.Infrastructure.csproj`: `AWSSDK.S3` y `Azure.Storage.Blobs`.
- **`StorageKeyGuard`** (nuevo, interno de Infrastructure): extrae la validación de claves que hoy vive en `LocalDiskFileStorageAdapter.ResolveSafePath` (vacía, absoluta, segmento `..` con `/` o `\`) para que los cuatro adapters la apliquen igual. El adapter local conserva además su verificación de que la ruta final quede dentro de `RootPath`.
- **`LocalDiskFileStorageAdapter`**: `Kind = Local`. Sin otros cambios de comportamiento.
- **`AzureBlobFileStorageAdapter`** (nuevo, `src/3.Infrastructure/Storage/AzureBlob/`): `Kind = AzureBlob`. Un contenedor (`ContainerName`); la clave de almacenamiento es el nombre del blob. `SaveAsync` sube con el `ContentType` en los headers del blob y crea el contenedor si no existe solo cuando `CreateContainerIfNotExists = true`. `OpenReadAsync` devuelve `null` si el blob no existe (404 del servicio); cualquier otro error se propaga. `DeleteAsync` usa `DeleteIfExists`.
- **`S3CompatibleFileStorageAdapter`** (nuevo, `src/3.Infrastructure/Storage/S3/`): una sola clase para AWS S3 y Cloudflare R2, construida con su propio `IAmazonS3` y su `Kind`. `SaveAsync` hace `PutObject` con `ContentType`; si el stream no es seekable, lo copia primero a un buffer temporal para conocer el largo. `OpenReadAsync` devuelve `null` ante `NoSuchKey`/404. `DeleteAsync` hace `DeleteObject` (S3 no informa si existía; devuelve `true` salvo error).
- **Opciones** (`src/3.Infrastructure/Storage/FileStorageOptions.cs`, reemplaza la clase actual de `Storage/Local/`):

  ```csharp
  public sealed class FileStorageOptions
  {
      public StorageProviderKind Provider { get; set; } = StorageProviderKind.Local;
      public LocalStorageOptions Local { get; set; } = new();          // RootPath
      public AzureBlobStorageOptions AzureBlob { get; set; } = new();  // ConnectionString, ContainerName, CreateContainerIfNotExists
      public S3StorageOptions S3 { get; set; } = new();                // BucketName, Region, AccessKeyId, SecretAccessKey, ServiceUrl?, ForcePathStyle
      public CloudflareR2StorageOptions CloudflareR2 { get; set; } = new(); // AccountId, BucketName, AccessKeyId, SecretAccessKey
  }
  ```

  R2 se configura con `AccountId`; el adapter arma `ServiceUrl = https://{AccountId}.r2.cloudflarestorage.com` y usa región `auto`. `S3.ServiceUrl` opcional permite además apuntar a MinIO o a otro servicio compatible para pruebas.

- **Registro en `DependencyInjection.cs`**: cada adapter se registra como servicio con clave (`AddKeyedScoped<IFileStorageService>(kind, ...)`), y `FileStorageResolver` resuelve por clave.
  - `Local` se registra **siempre**: es el proveedor por defecto, el de pruebas, y el que permite seguir descargando los adjuntos locales después de pasar a la nube.
  - Cada proveedor de nube se registra **solo si su sección está completa** (bucket/contenedor y credenciales). Así un entorno que pasó de R2 a Azure sigue leyendo los adjuntos de R2 mientras conserve esa sección.
  - **Arranque falla rápido** (`ValidateOnStart`) si `FileStorage:Provider` apunta a un proveedor cuya sección está incompleta. Un error de configuración se ve al levantar la API, no en la primera subida de un usuario.
- **Secretos**: ninguna clave de acceso va en `appsettings.json` versionado. `appsettings.json` solo lleva `Provider` y `Local:RootPath`; las secciones de nube se documentan en `.env.example` (variables `FileStorage__...`) y se cargan por entorno o user-secrets.

### E. Carpeta local en Docker y en el repo

- `.gitignore`: agregar `App_Data/`.
- `Dockerfile`: crear `/app/data/ticket-attachments` en la imagen final.
- `docker-compose.yml`: volumen nombrado `join-attachments` montado en `/app/data/ticket-attachments`.
- `.env.example`: `FileStorage__Provider=Local` y `FileStorage__Local__RootPath=/app/data/ticket-attachments`, más las variables comentadas de cada proveedor de nube.
- Fuera de Docker no cambia nada: `RootPath` relativo sigue anclado a la carpeta de la API (`App_Data/ticket-attachments`).

### F. WebApi

- `TicketDocumentsController.Download`: mapea `STORAGE_PROVIDER_NOT_CONFIGURED` → **503**. El resto del mapeo de SPEC 36 no cambia.

### G. Seed

- `SeedTicketAttachmentSettingsAsync` siembra por defecto `Pdf | Word | Excel | Image` (sin `Text`), para Join y la empresa privada, como hoy.
- **Bases existentes**: si la fila activa de la empresa conserva **exactamente** el valor del seed anterior (`Pdf | Word | Excel | Text | Image`), el seeder la actualiza al nuevo valor. Si tiene cualquier otro valor, alguien la configuró y no se toca. Se compara el valor y no `LastModifiedBy` porque `UpdateTicketAttachmentSettings` no estampa ese campo: no hay otra forma confiable de distinguir una fila sembrada de una editada. Correr el seeder dos veces no cambia nada la segunda vez.
- `MaxFileSizeBytes`, `MaxFilesPerTicket` y `MaxFilesPerDay` no cambian.

**Out of scope (para specs futuras):**

- **Migrar archivos existentes entre proveedores** (copiar lo que está en disco a la nube y reescribir `StorageProvider`). Con la resolución por fila no hace falta para seguir operando; si negocio quiere dejar de mantener el disco, es una herramienta de migración aparte.
- **Proveedor distinto por empresa.** La elección es global del entorno (`FileStorage:Provider`), no por tenant.
- **Descarga directa desde la nube con URL firmada** (presigned URL / SAS). La descarga sigue pasando por la API, que valida permisos (`CanDownload`) y tenant.
- **Identidad administrada de Azure / roles IAM de AWS** en lugar de claves. Esta spec usa connection string y access keys; la autenticación sin secretos es una mejora aparte.
- **Borrado físico, antivirus, miniaturas, versionado**: siguen fuera, igual que en SPEC 36.
- **Separar `Image` en `Png`/`Jpeg`/`Gif` en el enum** para que cada empresa elija cuáles acepta. Se descartó a favor de quitar `.gif` del mapeo (ver Decisiones).

---

## Configuración de referencia

```jsonc
// appsettings.json (versionado) — sin secretos
"FileStorage": {
  "Provider": "Local",
  "Local": { "RootPath": "App_Data/ticket-attachments" }
}
```

```bash
# .env / variables de entorno — un proveedor activo; los demás pueden seguir configurados para leer adjuntos viejos
FileStorage__Provider=Local                      # Local | AzureBlob | S3 | CloudflareR2
FileStorage__Local__RootPath=/app/data/ticket-attachments

# Azure Blob Storage
# FileStorage__AzureBlob__ConnectionString=DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...
# FileStorage__AzureBlob__ContainerName=ticket-attachments
# FileStorage__AzureBlob__CreateContainerIfNotExists=false

# Amazon S3 (o compatible: MinIO con ServiceUrl + ForcePathStyle=true)
# FileStorage__S3__BucketName=join-ticket-attachments
# FileStorage__S3__Region=us-east-1
# FileStorage__S3__AccessKeyId=...
# FileStorage__S3__SecretAccessKey=...
# FileStorage__S3__ServiceUrl=
# FileStorage__S3__ForcePathStyle=false

# Cloudflare R2
# FileStorage__CloudflareR2__AccountId=...
# FileStorage__CloudflareR2__BucketName=join-ticket-attachments
# FileStorage__CloudflareR2__AccessKeyId=...
# FileStorage__CloudflareR2__SecretAccessKey=...
```

La clave de cada adjunto sigue siendo la de SPEC 36: `{companyId}/{ticketId}/{yyyyMMddHHmmss}_{guid}{ext}`. En la nube es el nombre del objeto o blob dentro del bucket o contenedor.

---

## Implementation plan

### F1 — Dominio y contrato

1. `StorageProviderKind.CloudflareR2 = 3`; actualizar comentarios.
2. `IFileStorageService.Kind`; crear `IFileStorageResolver`.
3. `dotnet build -c Release` → 0 errores.

### F2 — Upload y download por proveedor

1. `UploadTicketDocumentCommandHandler`: usar `resolver.Current` y grabar su `Kind`. Quitar `.gif` de `TryMapExtension`.
2. `DownloadTicketDocumentQueryHandler`: leer `td.StorageProvider` y resolver el adapter; `STORAGE_PROVIDER_NOT_CONFIGURED` si no hay.
3. `TicketDocumentsController.Download`: `STORAGE_PROVIDER_NOT_CONFIGURED` → 503.
4. Ajustar los tests existentes de ambos handlers al resolver.

### F3 — Infrastructure

1. `StorageKeyGuard` y refactor de `LocalDiskFileStorageAdapter` para usarlo (sus tests actuales deben seguir pasando sin cambios en las aserciones).
2. `FileStorageOptions` con las cuatro secciones y su validación.
3. `AzureBlobFileStorageAdapter` y `S3CompatibleFileStorageAdapter`.
4. `FileStorageResolver` y registro con claves en `DependencyInjection.cs`, con `ValidateOnStart`.
5. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.

### F4 — Carpeta local, Docker y documentación

1. `.gitignore`, `Dockerfile`, `docker-compose.yml`, `.env.example` según la sección E.
2. `CURL_REQUESTS.md`: el nuevo código 503 en la descarga, `.gif` fuera de la tabla de tipos, y una nota de configuración de proveedores.

### F5 — Seed

1. Nuevo valor por defecto y regla de actualización de la sección G.
2. Arrancar dos veces contra la misma base: la primera ajusta Join si conservaba el valor anterior; la segunda no cambia nada.

### F6 — Tests

- `FileStorageResolverTests`: `Current` devuelve el proveedor configurado; `Resolve(Local)` siempre disponible; `Resolve` de un proveedor sin sección devuelve `null`.
- `UploadTicketDocumentCommandHandlerTests`: graba el `Kind` del proveedor actual (caso con un proveedor distinto de `Local`); `.gif` → `UNSUPPORTED_DOCUMENT_TYPE`; `.png`/`.jpg`/`.jpeg` siguen siendo `Image`.
- `DownloadTicketDocumentQueryHandlerTests`: abre con el adapter de la fila, no con el actual (fila `Local` con proveedor actual `S3`); proveedor de la fila no configurado → `STORAGE_PROVIDER_NOT_CONFIGURED`.
- `StorageKeyGuardTests`: claves vacías, absolutas o con `..` (ambos separadores) rechazadas.
- `AzureBlobFileStorageAdapterTests` / `S3CompatibleFileStorageAdapterTests`: con el cliente del SDK simulado. Guardar envía clave y `ContentType`; abrir un objeto inexistente devuelve `null`; otro error se propaga; el adapter R2 arma el endpoint desde `AccountId`.
- `FileStorageOptions` validation: `Provider` apuntando a una sección incompleta falla al arrancar.
- Seed: lo cubre el smoke F7.4 (el seeder no tiene tests unitarios hoy).
- Gate: `JOIN.Application` ≥ 90 %.

### F7 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos; unitarios → 0 fallidos; integración PostgreSQL (`~PostgreSql`) → 0 fallidos.
2. Smoke **Local**: subir un `.png`, un `.jpg`, un `.pdf`, un `.docx` y un `.xlsx` a un ticket de Join → 201; descargarlos → contenido idéntico; el archivo aparece bajo `RootPath`. Subir `.gif` y `.txt` → 400.
3. Smoke **Docker**: subir un adjunto, recrear el contenedor (`docker-compose up -d --force-recreate`), descargarlo → 200.
4. Smoke **seed**: en una base con la fila de Join en el valor anterior, al arrancar queda en `Pdf, Word, Excel, Image`; una fila editada a mano no cambia.
5. Smoke **nube** (con credenciales reales que provea el usuario, uno por proveedor que se quiera habilitar): con `Provider` en ese proveedor, subir y descargar un adjunto; luego volver a `Local` y confirmar que ese adjunto se sigue descargando desde la nube y que uno subido antes en `Local` también.
6. Smoke **mal configurado**: `Provider=AzureBlob` sin `ContainerName` → la API no arranca y el log dice qué falta.

---

## Acceptance criteria

### Proveedores

- [ ] `StorageProviderKind` tiene `Local`, `AzureBlob`, `S3` y `CloudflareR2`, sin migración.
- [ ] `FileStorage:Provider` admite `Local`, `AzureBlob`, `S3` y `CloudflareR2`; el valor por defecto es `Local`.
- [ ] Un único adapter (`S3CompatibleFileStorageAdapter`) sirve a AWS S3 y a Cloudflare R2, con `Kind` distinto según la sección.
- [ ] `Local` está registrado siempre; cada proveedor de nube solo si su sección está completa.
- [ ] Con `Provider` apuntando a una sección incompleta, la API no arranca y el error nombra la configuración faltante.
- [ ] Application no referencia ningún paquete de Azure ni de AWS.
- [ ] Ningún secreto queda en `appsettings.json` versionado.

### Resolución por adjunto

- [ ] Una subida graba en `StorageProvider` el proveedor que la guardó (ya no el literal `Local`).
- [ ] Una descarga usa el proveedor de la fila, no el configurado actualmente.
- [ ] Un adjunto cuyo proveedor no está configurado devuelve 503 `STORAGE_PROVIDER_NOT_CONFIGURED`, no 404 ni 500.
- [ ] Los cuatro adapters rechazan claves vacías, absolutas o con `..` antes de tocar el almacenamiento.

### Tipos de documento y seed

- [ ] `.gif` devuelve 400 `UNSUPPORTED_DOCUMENT_TYPE`; `.png`, `.jpg` y `.jpeg` siguen siendo `Image`; los `.gif` ya subidos se siguen descargando.
- [ ] Una base nueva siembra Join (y la empresa privada) con `Pdf | Word | Excel | Image`.
- [ ] En una base existente, una fila con el valor anterior del seed pasa al nuevo; una fila con cualquier otro valor no se toca.
- [ ] Correr el seeder dos veces no cambia nada la segunda vez.

### Carpeta local

- [ ] `App_Data/` está en `.gitignore`.
- [ ] En Docker, los adjuntos viven en el volumen `join-attachments` y sobreviven a recrear el contenedor.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test` → 0 fallidos; `JOIN.Application` ≥ 90 % de líneas.
- [ ] `CURL_REQUESTS.md` y `.env.example` documentan la configuración y el código 503.
- [ ] SPEC 36 (copia del fork) tiene la nota "Ajuste por SPEC 49-post" en su encabezado.

---

## Decisions taken and discarded

- **Un adapter S3 para AWS y R2, con tipos de proveedor distintos** (elegido) vs. un adapter propio para R2 o un único tipo `S3` para ambos. R2 implementa la API de S3, así que un segundo adapter duplicaría código. Pero un único tipo `S3` haría ambiguas las filas: con dos servicios y dos juegos de credenciales, `StorageProvider` tiene que decir cuál de los dos guardó el archivo.
- **Proveedor por fila, resuelto en cada descarga** (elegido; es lo que SPEC 36 diseñó y no se había conectado) vs. un único proveedor global para leer y escribir. Con el global, cambiar de proveedor obligaría a migrar todos los archivos el mismo día.
- **`Local` siempre registrado** (elegido) vs. registrar solo el proveedor activo. Mantiene la carpeta local para pruebas en cualquier entorno y garantiza que los adjuntos locales sigan legibles después de pasar a la nube.
- **Validar la configuración al arrancar** (elegido) vs. fallar en la primera subida. Una sección incompleta es un error de despliegue; detectarlo al levantar la API evita que un usuario lo descubra al adjuntar evidencia.
- **Quitar `.gif` del tipo `Image`** (elegido por el usuario) vs. separar `Png`/`Jpeg`/`Gif` en el enum. Separar cambiaría el bitmask guardado en `TicketAttachmentSettings`, los nombres que ve el frontend y la pantalla de configuración (FE-19), para una necesidad que hoy es la misma para todas las empresas.
- **Actualizar la fila sembrada comparando su valor** (elegido) vs. comparar `LastModifiedBy` o forzar siempre. `LastModifiedBy` no se estampa en las ediciones por API, así que no distingue una fila editada; forzar siempre pisaría la configuración que haya hecho la empresa.
- **Volumen nombrado de Docker** (elegido) vs. bind mount a una carpeta del host. El volumen nombrado no depende de rutas ni permisos del host; quien quiera ver los archivos desde el host puede cambiarlo a bind mount en su `docker-compose.override.yml`.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **R2 rechaza encabezados de checksum** que las versiones recientes de `AWSSDK.S3` envían por defecto. | El adapter configura el cliente de R2 para calcular y validar checksums solo cuando la operación lo requiere. El smoke F7.5 contra R2 real lo confirma. |
| **Archivo huérfano en la nube** si la subida funciona pero falla el `INSERT` de la fila. | Mismo riesgo aceptado en SPEC 36; ahora el costo es almacenamiento de nube. Una spec de limpieza puede comparar el bucket con `Support.TicketDocuments`. |
| **Se quita la sección de un proveedor que todavía tiene adjuntos.** | Esos adjuntos responden 503 `STORAGE_PROVIDER_NOT_CONFIGURED` en vez de un error genérico, y vuelven a funcionar al restaurar la sección. Se documenta en `.env.example`: no borrar la sección de un proveedor con adjuntos. |
| **Latencia de descarga** al pasar el archivo por la API en vez de servirlo directo desde la nube. | Aceptado: mantiene el control de permisos y tenant de SPEC 36. URL firmadas quedan fuera de alcance. |
| **Los adapters de nube no se prueban contra el servicio real en CI** (no hay credenciales ni emuladores en el pipeline). | Tests unitarios con el cliente del SDK simulado, más el smoke manual F7.5 por proveedor antes de habilitarlo en un entorno. |
| **Dos paquetes nuevos** (`AWSSDK.S3`, `Azure.Storage.Blobs`) aumentan el tamaño de la imagen aunque el entorno use `Local`. | Aceptado a cambio de elegir proveedor solo por configuración, sin recompilar. |
