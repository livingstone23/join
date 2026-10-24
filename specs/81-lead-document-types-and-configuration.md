# SPEC 81 — Documentos comerciales del lead: tipos de documento, configuración por tipo y datos del emisor

> **Status:** Aprobado
> **Depends on:** SPEC 41-post (borrado lógico, restauración y bloqueo por uso), SPEC 43-post (menú y rutas), SPEC 49-post (almacenamiento, para el logo), SPEC 55 (`ICustomerActivationService`), SPEC 59 (motivos de lead), SPEC 61 (leads), SPEC 63 (ganar un lead), SPEC 70 (validación de imágenes por firma), SPEC 71 (sintaxis de variables e `IHtmlSanitizer`). Rama de trabajo: `spec-81-lead-document-types-and-configuration`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Dar a Leads una función **básica** de documentos comerciales (cotizaciones, proformas, facturas, órdenes de compra…) que se parametriza en **Administración**: qué tipos de documento existen, cómo se numera cada uno, su encabezado y pie de página, sus leyendas y qué pasa al aprobarlo. Cuando existan los módulos completos (Ventas, Facturación, Productos), esta función se desconecta y se conectan aquellos.
> **Origen:** pedido del usuario (2026-10-24): "quiero que todo lo que creemos nuevo esté dentro de la sección de Leads y parametrizable en la sección de Administración, pero que una vez que estén los módulos nuevos esto lo podamos desconectar y conectar los sistemas más completos… creemos una entidad `DocumentTypes` y luego una entidad `LeadDocumentTypeConfiguration`". Serie: **81** (tipos y configuración) → **82** (impuestos) → **83** (documentos en el lead) → **84** (PDF, envío y bitácora) → **85** (almacenes y existencias), **87** (línea de crédito) y **86** (estados de factura, crédito y pagos). **999** (`Pospuesto`, no se implementa): módulo Productos con catálogo propio. Front: pendiente (FE-57 en adelante).
> **Ajuste por revisión de consistencia (2026-10-24):** (1) `LeadCompanySettings` (SPEC 59) se mantiene para lo propio de los leads (códigos, duplicados, identificación al ganar); `CompanyCustomers` reúne la parametrización comercial y fiscal de la empresa; no se fusionan. (2) La clase de dominio se llama `CommercialDocumentType` (tabla `DocumentTypes`) para no chocar con el enum existente `JOIN.Domain.Enums.DocumentType`. (3) Son **siete** tipos (incluido `TRANSFER`, que siembra esta spec; SPEC 85 le suma `StockEffect`). (4) La orden de compra se siembra con `RequiresApproval = true` (aprobar = recibir). (5) Variables y leyendas condicionales para SPEC 88 y 94. (6) El seed de desarrollo va a la empresa `JOIN-NI-001` (SPEC 100).
> **Ajuste (2026-10-24, decisión del usuario):** la parametrización por empresa vive en `Customer.CompanyCustomers`, extensión 1:1 de la empresa (antes `LeadDocumentSettings`).
> **Ajuste por SPEC 88 (2026-10-24):** lo que depende del país (impuestos base, identificación tributaria, numeración autorizada por rangos, moneda de referencia, factura electrónica) lo define el perfil fiscal por país de SPEC 88. Con un perfil que exige numeración autorizada, el número sale del rango autorizado y no del correlativo de esta spec.
> **Ajuste por SPEC 85, 86 y 87 (2026-10-24):** `BaseKind` suma `WarehouseTransfer = 7`. La configuración por tipo suma `StockEffect` (SPEC 85). `CompanyCustomers` suma `AllowNegativeStock` (SPEC 85) e `InstallmentReminderDays` (SPEC 86). Cada campo se define en su spec.
> **Historia (2026-10-24):** reemplaza la primera redacción de la serie 81–86 (módulo `Billing` aparte, plantillas HTML completas), que no llegó a commit. El usuario pidió replantearla dentro de Leads.

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Dentro de Leads, parametrizado en Administración.** Los documentos se crean y se consultan en Clientes → Leads (SPEC 83). Los tipos, su configuración, las leyendas, los impuestos y los datos del emisor se configuran en Administración.
2. **Conectable y desconectable.** `CompanyCustomers.DocumentsMode` por empresa:
   - `Disabled = 0` (por defecto en empresas existentes y nuevas): Leads funciona como hoy.
   - `Leads = 1`: función básica de esta serie activa.
   - `External = 2`: **reservado** para cuando existan los módulos completos. No se puede elegir hasta que una spec futura lo implemente (`LEAD_DOCUMENTS_MODE_NOT_AVAILABLE`).
   - Pasar de `Leads` a `Disabled` (o luego a `External`) **no borra nada**: los documentos ya creados se siguen viendo y descargando en el lead, pero no se crean ni se emiten nuevos (`LEAD_DOCUMENTS_DISABLED`).
3. **`DocumentTypes`** (tipos de documento), por empresa: catálogo de los documentos que la empresa usa, **para vender y para gestionar productos**. Cada tipo tiene un **comportamiento base** (`BaseKind`) que el sistema interpreta. El nombre y el código los edita la empresa:

   | `BaseKind` | Seed (código / nombre) | Dirección |
   |---|---|---|
   | `Quote = 1` | `QUOTE` / Cotización | Venta |
   | `Proforma = 2` | `PROFORMA` / Proforma | Venta |
   | `Invoice = 3` | `INVOICE` / Factura | Venta |
   | `CreditNote = 4` | `CREDIT_NOTE` / Nota de crédito | Venta |
   | `PurchaseOrder = 5` | `PURCHASE_ORDER` / Orden de compra | Compra |
   | `DeliveryNote = 6` | `DELIVERY_NOTE` / Nota de entrega | Venta |
   | `WarehouseTransfer = 7` | `TRANSFER` / Traslado entre almacenes | Interna (SPEC 85) |

   La empresa puede crear más tipos sobre un `BaseKind` existente (ej. "Cotización de servicios" sobre `Quote`). Los tipos sembrados no se borran (`IsSystem`), pero se pueden desactivar.
4. **`LeadDocumentTypeConfiguration`**: una configuración por empresa y tipo de documento, que define:
   - **Numeración de control:** prefijo (admite `{YYYY}`), largo del correlativo y siguiente número. El número se asigna **al emitir** (SPEC 83), sin huecos.
   - **Encabezado y pie de página** en HTML saneado, con variables de lista cerrada (regla 6).
   - **Leyendas** parametrizables: textos que se imprimen en el documento, ej. "Cotización válida por {{Documento.DiasValidez}} días", "Sujeto a existencia". Cada leyenda está marcada o no por defecto. Al crear el documento, el usuario puede quitar o agregar leyendas activas del tipo.
   - **Vigencia y vencimiento:** `ValidityDays?` (cotizaciones y proformas → `ValidUntil`) y `DueDays?` (facturas → `DueDate`).
   - **Comportamiento:**
     - `RequiresApproval`: el documento emitido se aprueba o se rechaza.
     - `ApprovalCompletesLead`: al aprobarlo **se gana el lead** (SPEC 63) con el motivo configurado `WinLeadReasonId` (motivo de uso `Won`, SPEC 59) y con las líneas del documento como productos aceptados.
     - `ApprovalConfirmsCustomer`: al aprobarlo **se registra o confirma al cliente** de la persona (`ICustomerActivationService`, SPEC 55).
     - `CountsInCustomerHistory`: el documento aparece en el **histórico del cliente**. Por defecto, solo en facturas y notas de crédito.
     - `CanBeSentToCustomer`: se puede enviar por correo (SPEC 84).
     - `ShowsTaxes`: calcula e imprime impuestos (SPEC 82). Por defecto sí, excepto en la nota de entrega.
     - `ConvertibleTo`: a qué tipos se puede convertir (ej. Cotización → Proforma → Factura; Factura → Nota de crédito).
   - Seed con valores razonables por `BaseKind`: la factura tiene `RequiresApproval`, `ApprovalCompletesLead`, `ApprovalConfirmsCustomer` y `CountsInCustomerHistory`; la **orden de compra** tiene `RequiresApproval` (aprobar = mercancía recibida, SPEC 85) sin efectos sobre el lead ni el cliente; la cotización tiene `ValidityDays = 15` y las leyendas de ejemplo.
5. **Datos del emisor** (`CompanyCustomers`): razón social, identificación fiscal, dirección, contacto, logo y datos de pago de la empresa que imprimen los documentos. Sin los datos obligatorios no se emite (`LEAD_DOCUMENT_ISSUER_INCOMPLETE`).
6. **Variables del encabezado, el pie y las leyendas** (sintaxis de SPEC 71, `{{Grupo.Campo}}`): `Emisor.*` (`RazonSocial`, `IdentificacionFiscal`, `Direccion`, `Ciudad`, `Pais`, `Telefono`, `Correo`, `Web`, `Logo`, `InstruccionesPago`), `Receptor.*` (`Nombre`, `Identificacion`, `Direccion`, `Correo`, `CodigoCliente`), `Documento.*` (`Tipo`, `Numero`, `CodigoPublico`, `CodigoBarras`, `FechaEmision`, `ValidoHasta`, `DiasValidez`, `Vencimiento`, `Moneda`, `Total`), `Lead.Codigo`, `Usuario.Nombre`, `Fecha.Hoy`; y, con sus specs, `Autorizacion.*` (`Numero`, `Fecha`, `Rango`, `Vence`; SPEC 88) y `Exencion.*` (`Tipo`, `Numero`, `Entidad`, `Vence`; SPEC 94). Una variable desconocida invalida el texto al guardar (`LEAD_DOCUMENT_UNKNOWN_VARIABLE`). El cuerpo del documento (líneas, impuestos y totales) lo arma siempre el sistema (SPEC 84).

## Scope

**In:**

**A. Esquema (`Customer`, junto a los leads)**
1. `DocumentTypes` (`BaseTenantEntity`, clase `CommercialDocumentType`): `Code` (máx. 30, mayúsculas sin espacios), `Name` (máx. 100), `BaseKind`, `Direction` (`Sale = 1`, `Purchase = 2`, `Internal = 3`), `IsSystem`, `IsActive`, `SortOrder`. Índice único filtrado `(CompanyId, Code)` con `"gcrecord" = 0`.
2. `LeadDocumentTypeConfigurations` (`BaseTenantEntity`): `DocumentTypeId` (índice único filtrado por empresa y tipo), `NumberPrefix` (máx. 20), `NumberLength` (1–10), `NextNumber`, `HeaderHtml?` (`text`), `FooterHtml?` (`text`), `ValidityDays?`, `DueDays?`, `RequiresApproval`, `ApprovalCompletesLead`, `WinLeadReasonId?` (obligatorio si `ApprovalCompletesLead`), `ApprovalConfirmsCustomer`, `CountsInCustomerHistory`, `CanBeSentToCustomer`, `ShowsTaxes`, `IsActive`.
3. `LeadDocumentTypeYearCounters`: `ConfigurationId`, `Year`, `NextNumber` (con `{YYYY}` el correlativo se reinicia cada año). Índice único `(ConfigurationId, Year)`.
4. `LeadDocumentTypeConversions`: `FromDocumentTypeId`, `ToDocumentTypeId` (índice único).
5. `LeadDocumentLegends` (`BaseTenantEntity`): `DocumentTypeId`, `Text` (máx. 300, con variables), `Condition` (`Always = 1`; `WhenExempt = 2`, SPEC 94; `WhenAuthorizedNumbering = 3`, SPEC 88), `IsDefault`, `SortOrder`, `IsActive`. Las leyendas condicionales se imprimen solo si se cumple su condición.
6. `CompanyCustomers` (`BaseTenantEntity`, **extensión 1:1 de `Common.Companies`** en el esquema `Customer`, índice único por `CompanyId`): toda la parametrización comercial y fiscal de la empresa, para que cambiar de país o de configuración se haga en un solo lugar. Esta spec crea `DocumentsMode`, `IssuerLegalName`, `IssuerTaxId`, `IssuerTaxRegimeId?`, `IssuerAddressLine`, `IssuerCity`, `IssuerRegion?`, `IssuerPostalCode?`, `IssuerCountryCode`, `IssuerPhone?`, `IssuerEmail?`, `IssuerWebsite?`, `LogoStorageKey?`, `LogoContentType?`, `PaymentInstructions?`, `PageSize` (`A4 = 1`, `Letter = 2`). Las specs siguientes le suman sus parámetros (inventario, cobro, país, punto de venta).

**B. Endpoints (Administración)**
7. `GET/POST/PUT/DELETE /api/v1/DocumentTypes` + `restore` (`DOCUMENT_TYPE_SYSTEM` al borrar uno sembrado; `DOCUMENT_TYPE_IN_USE` si tiene documentos).
8. `GET /api/v1/LeadDocumentTypeConfigurations`, `GET /{documentTypeId}`, `PUT /{documentTypeId}` (crea o actualiza; valida HTML con `IHtmlSanitizer` y variables), `PUT /{documentTypeId}/conversions` `{ toDocumentTypeIds[] }`. Cambiar el prefijo de un tipo que ya numeró documentos → 409 `LEAD_DOCUMENT_NUMBERING_IN_USE`, salvo que el prefijo nuevo no choque con números existentes.
9. `GET/POST/PUT/DELETE /api/v1/LeadDocumentLegends?documentTypeId=` + `restore`.
10. `GET/PUT /api/v1/CompanyCustomer` (la de la empresa actual; devuelve `IsIssuerComplete`), `PUT/GET/DELETE /api/v1/CompanyCustomer/logo` (PNG/JPEG por firma, máx. 1 MB).
11. `GET /api/v1/LeadDocumentTypeConfigurations/variables` (`[SkipDynamicAuthorization]`, como SPEC 71).

**C. Menú, permisos y seed**
12. Opciones en el módulo Administración (SPEC 43): `DocumentTypes` → `/administration/document-types`, `LeadDocumentTypeConfigurations` → `/administration/lead-document-configurations`, `LeadDocumentLegends` → `/administration/lead-document-legends`, `CompanyCustomer` → `/administration/company-customer`. Permisos: SuperAdmin y Admin con todo; Manager lectura.
13. Seed por empresa en `SeedDefaultCatalogsForCompanyAsync` y, para las empresas existentes, en el arranque (solo filas que faltan): los siete tipos, su configuración, las conversiones (`QUOTE→PROFORMA`, `QUOTE→INVOICE`, `PROFORMA→INVOICE`, `INVOICE→CREDIT_NOTE`, `QUOTE→DELIVERY_NOTE`, `INVOICE→DELIVERY_NOTE`), las leyendas de ejemplo de la cotización y `CompanyCustomers` en `Disabled`. Prefijos: `COT-{YYYY}-`, `PRF-{YYYY}-`, `FAC-{YYYY}-`, `NC-{YYYY}-`, `OC-{YYYY}-`, `NE-{YYYY}-`, `TRA-{YYYY}-`, largo 6. En desarrollo, ver "Seed de desarrollo (Nicaragua, datos simulados)".

**Out of scope:**
- El modo `External` (spec futura junto con los módulos completos).
- Plantillas completas por documento (diseño libre del cuerpo). El cuerpo es fijo; se personalizan el encabezado, el pie y las leyendas.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `DOCUMENT_TYPE_NOT_FOUND` | 404 | De otra empresa o inexistente |
| `DOCUMENT_TYPE_SYSTEM` | 409 | Borrar un tipo sembrado |
| `DOCUMENT_TYPE_IN_USE` | 409 | Borrar un tipo con documentos (`"Active LeadCommercialDocuments: N"`) |
| `LEAD_DOCUMENT_UNKNOWN_VARIABLE` | 400 | Variables fuera de la lista (devuelve cuáles) |
| `LEAD_DOCUMENT_UNSAFE_HTML` | 400 | Scripts o recursos externos en el encabezado o el pie |
| `LEAD_DOCUMENT_NUMBERING_IN_USE` | 409 | Cambio de numeración que choca con números emitidos |
| `WIN_LEAD_REASON_REQUIRED` | 400 | `ApprovalCompletesLead` sin `WinLeadReasonId`, o con un motivo que no es de uso `Won` |
| `LEAD_DOCUMENTS_MODE_NOT_AVAILABLE` | 409 | Elegir `External` |
| `INVALID_LOGO` | 400 | Formato o firma no permitidos, o más de 1 MB |
| `DUPLICATE_KEY` | 409 | Código repetido |

## Criterios de aceptación

1. Una empresa nueva recibe los siete tipos con su configuración y queda en `Disabled`. En ese modo, Leads funciona igual que hoy.
2. Una configuración con `{{Documento.Inventado}}` en el pie → 400 con la variable. Un `<script>` → 400.
3. `ApprovalCompletesLead = true` sin motivo, o con un motivo de uso `Lost` → 400.
4. Pasar a `Disabled` con documentos creados no borra nada (lo prueba SPEC 83).
5. Las leyendas se ordenan por `SortOrder` y solo las activas se ofrecen al crear un documento.
6. Unitarios: validadores (HTML, variables, numeración, comportamiento), handlers y seed idempotente. Integración: CRUD, seed de empresa nueva y modos.

## Decisiones

- **Comportamiento por `BaseKind`, no por nombre:** la empresa renombra o crea tipos sin romper las reglas (mismo criterio que `CommunicationChannel.Code`, SPEC 00).
- **Tablas en el esquema `Customer`** y nombres con prefijo `Lead…`: la función pertenece a Leads y no choca con los módulos futuros. `LeadDocument` ya existe (adjuntos del lead, SPEC 62), por eso los documentos comerciales se llaman `LeadCommercialDocument` (SPEC 83).
- **Desconexión por modo y no por módulo de menú:** la función vive en el módulo Clientes. El interruptor `DocumentsMode` deja preparada la conexión con los módulos completos sin mover datos.
- **`CompanyCustomer` en singular** en la ruta y el recurso: hay una sola fila por empresa (la de la empresa actual); la tabla se llama `CompanyCustomers` como todas.

## Impacto en el frontend

FE nueva (número pendiente): cuatro pantallas de Administración (tipos de documento, configuración por tipo con editor de encabezado y pie y vista de variables, leyendas, y datos del emisor con logo y selector de modo).

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- `CompanyCustomers` de `JOIN-NI-001`: `DocumentsMode = Leads`; emisor **JOIN Soluciones Empresariales, S.A.**, RUC `J0310000000001` (ficticio), Km 4 Carretera a Masaya, Managua, `NI`; teléfono +505 2222-0000; correo `facturacion@join.example`; logo de ejemplo; instrucciones de pago "Transferencia a BAC Credomatic C$ 000-000000-0 / USD 000-000000-1 (cuentas ficticias)"; `PageSize = Letter`.
- Leyendas: cotización "Cotización válida por {{Documento.DiasValidez}} días", "Sujeto a existencia", "Precios no incluyen IVA"; factura "Gracias por su compra"; orden de compra "Favor indicar el número de orden en su factura".
- Encabezado y pie de ejemplo en las siete configuraciones, con logo, RUC y dirección.
