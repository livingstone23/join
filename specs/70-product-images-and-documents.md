# SPEC 70 — Productos y servicios: imágenes (hasta 5) y documentos, con límites por empresa

> **Status:** Implementado
> **Depends on:** SPEC 36 (tabla de extensiones de adjuntos), SPEC 41-post (borrado lógico y restauración), SPEC 43-post (módulos, menú y rutas), SPEC 49-post (almacenamiento intercambiable), **SPEC 57** (catálogo de productos), SPEC 62 (patrón de documentos y `LeadAttachmentSettings`). Rama de trabajo: `spec-70-product-images-and-documents`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que cada producto o servicio tenga de 0 a 5 imágenes para mostrarlo y documentos clasificados (fichas técnicas, propuestas, etc.), con tipos, tamaños y cantidades que controla el administrador de la empresa.
> **Origen:** pedido del usuario (2026-10-19). SPEC 57 dejó "Imágenes de producto" fuera de alcance; esta spec las agrega. Los documentos los reutiliza SPEC 72 como adjuntos de correo. Front: FE-45.
> **Ajuste en implementación (2026-10-21):** precisa cómo quedó el código donde el texto era ambiguo; no cambia el alcance. (1) La migración `Spec70ProductImagesAndDocuments` crea solo tablas e índices; la opción `ProductSettings` y sus permisos los siembra `DatabaseSeeder`, como en SPEC 62 y SPEC 69 (el seed corre al aplicar la migración). (2) Subir imagen o documento responde **201**; subir, reordenar, marcar principal y borrar exigen `CanUpdate` sobre `Products` (`[RequirePermission]`, porque `POST` y `DELETE` mapearían a `CanCreate`/`CanDelete`). (3) El `Content-Type` de la imagen lo fija el servidor según la firma; la extensión debe coincidir con la firma y no hay respaldo por extensión. (4) Restaurar una imagen o un documento respeta el máximo por producto (`PRODUCT_IMAGE_LIMIT` / `PRODUCT_DOCUMENT_LIMIT`) y no procede con el producto borrado (`PARENT_DELETED`); la imagen restaurada va al final y solo conserva la marca de principal si ninguna imagen viva la tiene. (5) `displayName` es opcional en el formulario: por defecto, el nombre del archivo sin extensión (FE-45). (6) Errores añadidos a la tabla: `PRODUCT_DOCUMENT_TYPE_ALREADY_EXISTS`, `PRODUCT_ATTACHMENT_SETTINGS_ALREADY_EXISTS`, `FILE_NOT_FOUND_IN_STORAGE` y los de restauración. FE-45 se ajusta en el mismo cambio (nombres de campos de `ProductDocumentDto`, 201 y errores).

---

## Reglas de negocio (decisión del usuario, 2026-10-19)

1. **Imágenes opcionales, de 0 a 5** por producto o servicio. El tope 5 es fijo; la empresa puede bajarlo (`MaxImagesPerProduct` entre 1 y 5).
2. **Una imagen principal:** la primera que se sube queda como principal. Se puede cambiar. Al borrar la principal, pasa a serlo la siguiente por `SortOrder`. Las imágenes tienen orden.
3. **Se suben después de guardar el producto:** el alta (`POST /Products`) no cambia. Imágenes y documentos usan endpoints propios sobre un producto existente.
4. **Documentos clasificados** por un catálogo por empresa, `ProductDocumentTypes` (seed: Ficha técnica, Propuesta, Manual, Otro), con nombre visible y descripción opcional.
5. **Límites por empresa** (`ProductAttachmentSettings`, una fila por empresa): tipos de documento permitidos, tamaño máximo por documento, máximo de documentos por producto, formatos de imagen permitidos y tamaño máximo por imagen. Los gestiona quien tenga permiso sobre el recurso nuevo **`ProductSettings`**, separado de quien edita productos. Sin fila no se sube nada (`PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED`); el seed crea la fila.
6. **Mismo almacenamiento** que tickets y leads: `IFileStorageResolver.Current`, rutas `products/{empresa}/{producto}/images/…` y `products/{empresa}/{producto}/documents/…`.
7. **Producto inactivo:** conserva y muestra sus imágenes y documentos y se pueden descargar. Para agregar más hay que reactivarlo (`PRODUCT_NOT_ACTIVE`).
8. **Borrado lógico y restauración** (SPEC 41-post): borrar una imagen o un documento conserva el archivo físico. Solo SuperAdmin restaura. Borrar un producto borra lógicamente sus imágenes y documentos, y restaurarlo los restaura (cascada, `PersonCascadeCoordinator` como patrón).

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Catalog`)
1. `ProductImage` (`BaseTenantEntity`): `ProductId`, `OriginalName`, `NewName`, `Path`, `StorageProvider`, `ContentType`, `SizeBytes`, `Width?`, `Height?`, `SortOrder`, `IsPrimary`, `UploadedByUserId`. Índices: `(CompanyId, ProductId, SortOrder)`, y único filtrado `(ProductId)` con `"isprimary" = TRUE AND "gcrecord" = 0` (una principal por producto).
2. `ProductDocument` (`BaseTenantEntity`): `ProductId`, `ProductDocumentTypeId`, `DisplayName` (máx. 200), `Description?` (máx. 1000), `DocumentType` (flags de SPEC 36), `OriginalName`, `NewName`, `Path`, `StorageProvider`, `ContentType`, `SizeBytes`, `UploadedByUserId`. Índice `(CompanyId, ProductId)`.
3. `ProductDocumentType` (`BaseTenantEntity`): `Code` (máx. 30, mayúsculas), `Name` (máx. 100), `SortOrder`, `IsActive`. Índice único filtrado `(CompanyId, Code)` con `"gcrecord" = 0`.
4. `ProductAttachmentSettings` (`BaseTenantEntity`, índice único filtrado por empresa): `AllowedDocumentTypes` (`DocumentType` flags), `MaxDocumentSizeBytes`, `MaxDocumentsPerProduct`, `AllowedImageFormats` (`ProductImageFormat` flags), `MaxImageSizeBytes`, `MaxImagesPerProduct` (1–5).
5. Enum nuevo `ProductImageFormat` (`[Flags]`): `Jpeg = 1` (`.jpg`, `.jpeg`), `Png = 2`, `Webp = 4`. Es propio de las imágenes porque `DocumentType.Image` no incluye `.webp`.
6. Migración `Spec70ProductImagesAndDocuments`: tablas e índices. La opción `ProductSettings` y sus permisos los siembra `DatabaseSeeder` (D.18–D.20).

**B. Application** (`UseCases/Catalog/Products/Images`, `…/Documents`, `UseCases/Catalog/ProductDocumentTypes`, `UseCases/Catalog/ProductAttachmentSettings`)
7. `UploadProductImageCommand`: aplica, en este orden: producto de la empresa, producto activo, límites configurados, formato (por extensión **y** por firma de los primeros bytes; deben coincidir), tamaño y cantidad. Guarda el archivo con el `Content-Type` que corresponde a la firma y lee ancho y alto de la cabecera (PNG, JPEG, WEBP `VP8`/`VP8L`/`VP8X`). Si ninguna imagen viva es principal, queda como principal.
8. `ProductImagePrimaryCoordinator` (invariante "una principal por producto", patrón de `PersonAddressDefaultCoordinator`): lo usan subir, marcar principal, borrar la principal y restaurar. Guarda la baja de la principal anterior antes de marcar la nueva, porque PostgreSQL revisa el índice único en cada sentencia.
9. `ReorderProductImagesCommand` `{ imageIds[] }`: debe traer exactamente las imágenes vivas del producto; reasigna `SortOrder` 1..n.
10. `UploadProductDocumentCommand`: producto de la empresa y activo, tipo de documento activo, límites, tipo por extensión (la misma tabla que `UploadTicketDocumentCommandHandler.TryMapExtension`), tamaño y máximo por producto.
11. Borrado y restauración de imágenes y documentos; la cascada del producto (`ProductAttachmentCascadeCoordinator`) se suma a `DeleteProductCommand` y `RestoreProductCommand`. Restaurar una imagen o un documento: solo SuperAdmin, no con el producto borrado (`PARENT_DELETED`) y sin pasar el máximo por producto; la imagen restaurada va al final y es principal solo si ninguna viva lo es.
12. Queries con Dapper: lista de imágenes y de documentos con autor; `GetProducts`, `GetProductById`, `Products/lookup` y `Leads/products-lookup` (SPEC 61) agregan `PrimaryImageId?` e `ImageCount`.
13. CRUD de `ProductDocumentTypes` con bloqueo por uso (`PRODUCT_DOCUMENT_TYPE_IN_USE`, `"Active ProductDocuments: N"`).
14. `ProductAttachmentSettings`: `GET`, `POST` (409 si existe) y `PUT`, como `LeadAttachmentSettings`.

**C. Endpoints**
15. Recurso `Products` (`ProductImagesController`, `ProductDocumentsController`):
    - `GET /api/v1/Products/{id}/images`
    - `POST /api/v1/Products/{id}/images` (multipart, `CanUpdate`, responde 201)
    - `PUT /api/v1/Products/{id}/images/order` (`CanUpdate`)
    - `PUT /api/v1/Products/{id}/images/{imageId}/primary` (`CanUpdate`)
    - `GET /api/v1/Products/{id}/images/{imageId}/content` (`Content-Disposition: inline`, `CanRead`, `Cache-Control: private, max-age=3600`, `X-Content-Type-Options: nosniff`)
    - `DELETE /api/v1/Products/{id}/images/{imageId}` (`CanUpdate`)
    - `POST /api/v1/Products/{id}/images/{imageId}/restore` (SuperAdmin)
    - `GET /api/v1/Products/{id}/documents`
    - `POST /api/v1/Products/{id}/documents` (multipart: archivo, `productDocumentTypeId`, `displayName?` —por defecto el nombre del archivo—, `description?`; `CanUpdate`, responde 201)
    - `GET /api/v1/Products/{id}/documents/{docId}/download` (`[RequirePermission(PermissionFlags.CanDownload)]`)
    - `DELETE /api/v1/Products/{id}/documents/{docId}` (`CanUpdate`) y `POST …/restore` (SuperAdmin)
16. `Leads/products-lookup/{productId}/images/{imageId}/content` bajo el recurso `Leads`, para que un vendedor sin permiso `Products` vea la miniatura (patrón de la Decisión "Lectura para vendedores" de SPEC 57).
17. Recurso `ProductSettings`: `GET/POST/PUT /api/v1/ProductAttachmentSettings` y `GET/POST/PUT/DELETE /api/v1/ProductDocumentTypes` + `POST /{id}/restore`.

**D. Menú, permisos y seed**
18. `SystemOption` `ProductSettings` (`/administration/product-settings`, módulo `Administration`, `ControllerName` `ProductSettings`). Permisos: SuperAdmin, Admin y Manager con todo; Supervisor solo lectura.
19. `CanDownload` en la opción `Products` para los roles que ya la tienen (Supervisor incluido).
20. `SeedProductAttachmentSettingsForCompanyAsync`: PDF, Word, Excel e imagen; 10 MB por documento; 20 documentos por producto; JPG, PNG y WEBP; 5 MB por imagen; 5 imágenes. Se aplica a las empresas nuevas (`ICompanyCatalogSeeder.SeedDefaultCatalogsForCompanyAsync`), en `SeedAsync` y, sin migraciones pendientes, desde `Program.cs` (`SeedProductAttachmentSettingsDefaultsAsync`), igual que leads. Siembra también los cuatro `ProductDocumentTypes` (`TECH_SHEET`, `PROPOSAL`, `MANUAL`, `OTHER`).

**Out of scope:**
- Redimensionar imágenes o generar miniaturas en el servidor (se sirven tal cual).
- Subir imágenes o documentos en el mismo `POST /Products`.
- Versionado de documentos.
- Imágenes por variante de producto.
- URLs públicas o firmadas para mostrar imágenes fuera del sistema (catálogo web, correos con imágenes incrustadas).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `PRODUCT_NOT_FOUND` | 404 | Producto de otra empresa o inexistente (SPEC 57) |
| `PRODUCT_NOT_ACTIVE` | 409 | Subir una imagen o un documento a un producto inactivo |
| `PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED` | 400 | La empresa no tiene fila de límites |
| `PRODUCT_IMAGE_FORMAT_NOT_ALLOWED` | 400 | La extensión o la firma no es un formato permitido |
| `PRODUCT_IMAGE_TOO_LARGE` | 400 | Supera `MaxImageSizeBytes` |
| `PRODUCT_IMAGE_LIMIT` | 400 | El producto ya tiene `MaxImagesPerProduct` imágenes vivas (al subir o al restaurar) |
| `PRODUCT_IMAGE_NOT_FOUND` | 404 | Imagen de otra empresa, de otro producto o inexistente |
| `INVALID_PRODUCT_IMAGE_ORDER` | 400 | El reordenamiento no trae exactamente las imágenes vivas |
| `PRODUCT_DOCUMENT_FILE_TYPE_NOT_ALLOWED` | 400 | Extensión o tipo de archivo no permitido |
| `PRODUCT_DOCUMENT_TOO_LARGE` | 400 | Supera `MaxDocumentSizeBytes` |
| `PRODUCT_DOCUMENT_LIMIT` | 400 | Supera `MaxDocumentsPerProduct` (al subir o al restaurar) |
| `PRODUCT_DOCUMENT_NOT_FOUND` | 404 | Documento de otra empresa, de otro producto o inexistente |
| `PRODUCT_DOCUMENT_TYPE_NOT_FOUND` | 404 | `productDocumentTypeId` inexistente, de otra empresa o inactivo |
| `PRODUCT_DOCUMENT_TYPE_IN_USE` | 409 | Borrar un tipo de documento con documentos vivos |
| `PRODUCT_DOCUMENT_TYPE_ALREADY_EXISTS` | 409 | Crear o renombrar un tipo de documento con un `Code` que ya usa otro tipo vivo de la empresa |
| `PRODUCT_ATTACHMENT_SETTINGS_ALREADY_EXISTS` | 409 | `POST /ProductAttachmentSettings` cuando la empresa ya tiene la fila |
| `PARENT_DELETED` / `NOT_DELETED` / `ACTIVE_DUPLICATE_EXISTS` | 409 | Restaurar con el producto borrado, algo que no está borrado o un tipo cuyo `Code` ya usa otro (SPEC 41-post) |
| `STORAGE_PROVIDER_NOT_CONFIGURED` | 503 | Igual que tickets y leads |
| `FILE_NOT_FOUND_IN_STORAGE` | 404 | La fila existe pero el archivo ya no está en el almacenamiento |

## Implementation plan

1. Dominio, configuraciones EF, enum `ProductImageFormat` y migración (solo tablas e índices). Build verde.
2. `ProductDocumentTypes` (CRUD, bloqueo por uso) y `ProductAttachmentSettings` (GET/POST/PUT), con controladores bajo `ProductSettings`, opción de menú, permisos y seed. Tests unitarios.
3. Imágenes: subir, listar, contenido, principal (`ProductImagePrimaryCoordinator`), reordenar, borrar y restaurar. Tests unitarios.
4. Documentos: subir, listar, descargar, borrar y restaurar. Tests unitarios.
5. Cascada en el borrado y la restauración del producto. `PrimaryImageId` e `ImageCount` en las queries de lista, detalle y lookup, y el endpoint de miniatura bajo `Leads`.
6. Pruebas de integración con el proveedor local y `EXPLAIN` del SQL nuevo. Ajustar `CURL_REQUESTS.md`.

## Criterios de aceptación

- [ ] Un producto se crea sin imágenes; se le suben 5 y la sexta responde 400 `PRODUCT_IMAGE_LIMIT`.
- [ ] La primera imagen queda como principal; marcar otra deja una sola principal en la base (índice único).
- [ ] Borrar la principal deja como principal la siguiente por `SortOrder`.
- [ ] Un `.png` renombrado a `.jpg` con contenido de PDF responde `PRODUCT_IMAGE_FORMAT_NOT_ALLOWED`.
- [ ] Con `AllowedImageFormats = Jpeg`, un `.webp` responde `PRODUCT_IMAGE_FORMAT_NOT_ALLOWED`.
- [ ] Un documento supera el tamaño y responde `PRODUCT_DOCUMENT_TOO_LARGE`; un `.exe` responde `PRODUCT_DOCUMENT_FILE_TYPE_NOT_ALLOWED`.
- [ ] Descargar un documento sin `CanDownload` responde 403; uno de otra empresa responde 404.
- [ ] Subir a un producto inactivo responde 409 `PRODUCT_NOT_ACTIVE`; ver y descargar sigue funcionando.
- [ ] Sin fila de límites, subir responde `PRODUCT_ATTACHMENT_SETTINGS_NOT_CONFIGURED`; una empresa nueva nace con la fila y los 4 tipos de documento.
- [ ] Un usuario con `Products` pero sin `ProductSettings` no puede cambiar los límites (403).
- [ ] Borrar un producto borra lógicamente sus imágenes y documentos; restaurarlo los restaura; los archivos físicos se conservan.
- [ ] `GET /Products` y `Leads/products-lookup` devuelven `PrimaryImageId`.
- [ ] Unit tests de handlers, validadores y coordinador con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: imágenes opcionales (0 a 5)** (usuario, 2026-10-19). No bloquea productos existentes ni el seed de SPEC 57.
- **Sí: subir después de guardar** (usuario, 2026-10-19). El `POST /Products` no pasa a multipart y cada subida tiene su propio error.
- **Sí: recurso nuevo `ProductSettings`** (usuario, 2026-10-19). Quien edita productos no cambia los límites.
- **Sí: catálogo por empresa para el tipo de documento** (usuario, 2026-10-19), con seed de cuatro tipos.
- **Sí: tablas propias** (`ProductImage`, `ProductDocument`) en vez de generalizar `LeadDocument`, igual que la decisión de SPEC 62.
- **Sí: validar la firma de la imagen**, además de la extensión: las imágenes se muestran inline en el navegador.
- **No: miniaturas en el servidor.** Con 5 MB y 5 imágenes por producto no hace falta todavía; si se necesita, irá en otra spec.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Listas de productos lentas por cargar imágenes completas. | La lista solo trae `PrimaryImageId`; el front pide `…/content` con caché privada y `loading="lazy"`. |
| Un archivo malicioso subido como imagen. | Validación por firma, `Content-Type` fijado por el servidor y `X-Content-Type-Options: nosniff`. |

## Impacto en el frontend

- **FE-45** (Borrador): ficha de producto con pestañas General / Imágenes / Documentos, miniatura en lista y buscadores, y parametrización "Adjuntos de productos" y "Tipos de documento". Ajusta FE-31 y FE-37 (buscador de productos del lead).

## Lo que **no** entra en esta spec

- Miniaturas o redimensionado en el servidor.
- Subida en el alta del producto.
- Versionado de documentos e imágenes por variante.
- URLs públicas de imágenes.
