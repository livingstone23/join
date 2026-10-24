# SPEC 84 — Documentos comerciales del lead: PDF, descarga, envío al cliente y registro en la bitácora

> **Status:** Borrador
> **Depends on:** SPEC 49-post (almacenamiento), SPEC 71 (`IHtmlSanitizer`, plantillas de correo), **SPEC 72** (envío de correos e historial de la persona), **SPEC 81** (configuración: encabezado, pie, leyendas, emisor y logo), **SPEC 83** (documentos, código público, código de barras y bitácora). Rama de trabajo: `spec-84-lead-commercial-documents-pdf-send-and-log`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que cada documento comercial se descargue en PDF (con encabezado, pie, leyendas, logo, número, código público y código de barras) y se envíe al cliente por correo, tanto proformas y cotizaciones como facturas, y que cada descarga y envío quede en la bitácora del documento.
> **Ajuste por SPEC 94 (2026-10-24):** el PDF de un documento exento imprime la leyenda con los datos de la constancia.
> **Ajuste por SPEC 93 (2026-10-24):** el bloque de líneas y totales del PDF sigue la presentación de SPEC 93 (importe neto por línea, IVA por tasa, total con IVA, retenciones, total a pagar y monto en letras).
> **Ajuste por SPEC 88 (2026-10-24):** el PDF imprime los datos de la numeración autorizada y el equivalente en moneda local cuando corresponde.
> **Origen:** pedido del usuario (2026-10-24): "quiero que estos se puedan descargar en PDF o enviar al cliente y registrar esto; tanto las proformas como las facturas se pueden enviar al cliente y quiero que esto quede registrado en los logs".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Diseño fijo del cuerpo, personalizable por tipo:** de arriba abajo, el encabezado del tipo (SPEC 81), un bloque del sistema con el título del tipo, número, fechas, emisor, receptor, código público y código de barras, la tabla de líneas, el desglose de impuestos y los totales, las leyendas, las notas y el pie del tipo. El tamaño de página sale de `CompanyCustomers.PageSize`.
2. **PDF congelado al emitir:** al emitir (SPEC 83) se genera el PDF y se guarda en el almacenamiento (`PdfStorageKey`). Cada descarga devuelve ese archivo. Si después el documento cambia de estado (aprobado, anulado), el PDF se regenera con una marca del estado ("ANULADO") y se conserva la versión anterior.
3. **Borradores:** vista previa generada al momento, con la marca de agua "BORRADOR" y sin número.
4. **Envío al cliente:** solo documentos emitidos de tipos con `CanBeSentToCustomer`. Se usa el flujo de SPEC 72 (destinatario por defecto: el correo principal de la persona; CC; plantilla de correo opcional de SPEC 71; texto editable) con el PDF adjunto. Respeta los límites diarios de SPEC 72.
5. **Registro de cada envío:**
   - en la **bitácora del documento** (`LeadCommercialDocumentEvents`, SPEC 83): `Sent` con destinatarios, asunto, id del correo y usuario, o `SendFailed` con el error;
   - en el **historial de correos de la persona** (`PersonEmailMessages`, SPEC 72), con el adjunto `SourceKind = LeadCommercialDocument = 3`;
   - en la **línea de tiempo del lead** (SPEC 61).
6. **Registro de cada descarga:** `PdfDownloaded` en la bitácora, con usuario y fecha. Las vistas previas de borradores no se registran.
7. **El primer envío marca el documento** con `FirstSentAt` y `SendCount`, para filtrarlo en la lista ("enviado / no enviado").

## Scope

**In:**

**A. Esquema**
1. `LeadCommercialDocuments` suma `PdfStorageKey?`, `PdfGeneratedAt?`, `FirstSentAt?` y `SendCount`.
2. `LeadCommercialDocumentPdfVersions`: `DocumentId`, `StorageKey`, `Reason` (`Issued`, `Approved`, `Cancelled`, `Regenerated`), `GeneratedAt`.
3. `PersonEmailMessageAttachment.SourceKind` suma `LeadCommercialDocument = 3`.

**B. Generación del PDF**
4. `IPdfRenderer` (Application) → `RenderAsync(html, pageSize)` devuelve `byte[]`. Implementación en Infrastructure con **Chromium sin interfaz (PuppeteerSharp)**, sin acceso a la red (se bloquean todas las peticiones externas; el logo y el código de barras van embebidos como `data:`). Navegador compartido (singleton) con un máximo de páginas simultáneas (`Pdf:MaxConcurrentRenders`, por defecto 2).
5. `Dockerfile`: `chromium` y fuentes (`font-noto`, `ttf-dejavu`) en la imagen alpine y `PUPPETEER_EXECUTABLE_PATH`. Sin descarga en tiempo de ejecución.
6. `LeadCommercialDocumentHtmlComposer` (Application): arma el HTML del diseño fijo con los datos copiados del documento (Dapper), el código de barras SVG (SPEC 83) y los textos del encabezado, el pie y las leyendas.

**C. Endpoints (permiso `Leads`)**
7. `GET /api/v1/LeadCommercialDocuments/{id}/pdf` (`[RequirePermission(CanDownload)]`): el PDF guardado si está emitido (registra `PdfDownloaded`), o la vista previa si es borrador.
8. `POST /api/v1/LeadCommercialDocuments/{id}/send` `{ to, cc?, emailTemplateId?, subject?, bodyHtml? }`: reutiliza `PersonEmailSender` (SPEC 72) y registra según la regla 5. Responde con el resultado del envío y el evento creado.
9. `GET /api/v1/LeadCommercialDocuments/{id}/events`: bitácora paginada.
10. `GET /api/v1/LeadCommercialDocuments` suma el filtro `sent` (`true`/`false`).
11. `POST /api/v1/LeadCommercialDocuments/{id}/regenerate-pdf` (solo SuperAdmin): nueva versión con `Reason = Regenerated`.
12. `POST /api/v1/LeadDocumentTypeConfigurations/{documentTypeId}/preview` (Administración): PDF de ejemplo con el encabezado, el pie y las leyendas sin guardar, para ver el resultado mientras se configuran.

**Out of scope:**
- Enlace público para que el cliente vea o acepte el documento sin iniciar sesión.
- Firma digital del PDF.
- Envío por WhatsApp (se podrá agregar con SPEC 75).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `LEAD_COMMERCIAL_DOCUMENT_NOT_ISSUED` | 409 | Enviar un borrador |
| `LEAD_DOCUMENT_TYPE_NOT_SENDABLE` | 409 | Enviar un tipo sin `CanBeSentToCustomer` |
| `PDF_RENDER_FAILED` | 500 | Falló la generación; al emitir, la emisión se revierte y el documento sigue en `Draft` sin consumir número |
| Errores de SPEC 72 | — | Límite diario, correo inválido, etc. (también quedan como `SendFailed`) |

## Criterios de aceptación

1. El PDF de una cotización emitida muestra el encabezado, el número, el código público, un código de barras legible, las líneas, el desglose, el total, las leyendas ("Cotización válida por 15 días", "Sujeto a existencia") y el pie.
2. Cambiar el encabezado del tipo después de emitir no cambia el PDF descargado.
3. Anular una factura genera una versión nueva con la marca "ANULADO" y conserva la anterior.
4. Enviar una proforma y una factura adjunta el PDF y crea el evento `Sent`, el registro en el historial de correos de la persona y la entrada en la línea de tiempo del lead.
5. Un envío que supera el límite diario responde el error de SPEC 72 y registra `SendFailed`.
6. Cada descarga de un emitido crea un `PdfDownloaded`.
7. Si falla el PDF al emitir, el documento queda en `Draft` sin número consumido.
8. Smoke con la imagen Docker: emitir y descargar un PDF dentro del contenedor.
9. Unitarios: composición del HTML, handlers de envío y descarga y registro de eventos. Integración con un `IPdfRenderer` falso; un test con Chromium real queda para el smoke.

## Decisiones

- **HTML + Chromium, confirmado por el usuario (2026-10-24)** después de compararlo con PDFsharp-MigraDoc (MIT, ligera, sin HTML) y QuestPDF (ligera, sin HTML, licencia comercial a partir de 1 M USD de ingreso anual): se prioriza que el encabezado y el pie sean HTML editable por la empresa. El código de barras va como SVG vectorial embebido (núcleo de ZXing.Net, SPEC 83).
- Los traslados entre almacenes (SPEC 85) usan el mismo compositor y renderizador con su propio diseño (origen, destino, fecha, usuario que traslada, concepto y líneas).

## Riesgos

- La imagen Docker crece unos 150–200 MB por Chromium y las fuentes.
- Generar PDF consume CPU y memoria; el límite de páginas simultáneas evita saturar el servidor. Con más volumen, se pasa a una cola en segundo plano (pendiente junto a Hangfire, SPEC 44).

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para `JOIN-001`, idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- PDF ya generado para los documentos emitidos del seed y un evento `Sent` simulado a `compras@farmaciasanmartin.example` en la bitácora (sin envío real).
