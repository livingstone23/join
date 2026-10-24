# SPEC 96 — Carga de datos por CSV: guía descargable, plantillas, validación previa y carga confirmada

> **Status:** Borrador
> **Depends on:** SPEC 49-post (almacenamiento), SPEC 54 y 55 (personas y clientes), **SPEC 57** (productos), SPEC 58 (identificación), **SPEC 81** (`CompanyCustomers`), SPEC 82 (grupos fiscales), SPEC 84 (generación de PDF), **SPEC 85** (almacenes y ajustes de inventario), SPEC 87 (líneas de crédito), SPEC 88 (perfil fiscal: validación de RUC), SPEC 90 (costo inicial), **SPEC 95** (unidades y precio con IVA). Rama de trabajo: `spec-96-csv-data-import-with-guide-and-validation`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que una empresa que empieza a operar cargue sus datos iniciales desde archivos CSV. Desde Administración descarga una **guía** y una **plantilla** por catálogo. En la sección de cada catálogo sube el archivo, el sistema lo **valida completo antes de cargar nada**, muestra los errores por fila y columna, y solo carga cuando el usuario confirma un archivo sin errores.
> **Valores de enums (revisión de consistencia):** `DataImports.Status`: `Uploaded = 1`, `Validated = 2`, `ValidatedWithErrors = 3`, `Imported = 4`, `Failed = 5`, `Discarded = 6`; `Mode`: `CreateOnly = 1`, `CreateOrUpdate = 2`.
> **Ajuste por revisión de consistencia (2026-10-24):** (1) Existencias iniciales admiten la columna opcional `UnitCost` (moneda base), que fija `LastCost` con `ProductCostHistory.Source = Import` (SPEC 90). (2) Se quita la dependencia de SPEC 93 (no se usa). (3) Seed en `JOIN-NI-001`.
> **Origen:** pedido del usuario (2026-10-24): "desde parametrización se pueda bajar una guía de cómo deben ser los CSV por los catálogos posibles a llenar: Productos, Servicios, Personas y Clientes, a menos que me indiques otros, y que en esas secciones se puedan subir, y antes de subir los datos validar, claro, antes de hacer las cargas".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Catálogos importables:**

   | Catálogo | Crea o actualiza | Clave para actualizar |
   |---|---|---|
   | Productos (`Kind = Product`) | `Catalog.Products` | `Code` |
   | Servicios (`Kind = Service`) | `Catalog.Products` | `Code` |
   | Personas | `Admin.Persons` (+ dirección y contacto principal) | tipo + número de identificación; sin identificación, siempre crea |
   | Clientes | Persona (si no existe) + `Customer.Customers` | identificación de la persona |
   | **Existencias iniciales** (propuesto) | Ajuste de inventario de entrada (SPEC 85) | — (solo crea) |
   | **Líneas de crédito** (propuesto) | `CustomerCreditLines` (SPEC 87) | cliente |

   Productos y Servicios tienen plantillas separadas para que cada una muestre solo sus columnas (los servicios no llevan existencias ni código de barras). Existencias iniciales y Líneas de crédito se proponen porque sin ellas no se puede empezar a vender a crédito ni con stock. Se pueden quitar al aprobar la spec.
2. **Guía descargable** por catálogo (PDF generado con el renderizador de SPEC 84, y la misma información en JSON para el front): columnas en orden, si son obligatorias, tipo, largo máximo, formato, valores permitidos y ejemplo. Los valores que dependen de la empresa (categorías, unidades de medida, grupos fiscales, almacenes, tipos de identificación, monedas) se listan **con los códigos actuales de la empresa** al momento de descargar.
3. **Plantilla CSV** por catálogo: encabezados exactos y dos filas de ejemplo (datos simulados de Nicaragua).
4. **Formato del archivo:** UTF-8 (con o sin BOM), separador `,` o `;` (detección automática), decimales con punto, fechas `yyyy-MM-dd`, booleanos `true`/`false` o `si`/`no`. Máximo **5.000 filas** y **5 MB** por archivo.
5. **Validación completa antes de cargar** (sin escribir nada en las tablas de negocio):
   - formato (columnas, tipos, largos, obligatorios);
   - referencias (códigos de categoría, unidad, grupo fiscal, almacén… que existan y estén activos);
   - duplicados dentro del archivo y contra la base;
   - reglas de negocio con **los mismos validadores** que usan los comandos de alta y edición (FluentValidation), incluida la validación de RUC del perfil fiscal (SPEC 88).

   El resultado lista cada error con fila, columna, valor y mensaje, y un resumen: filas válidas, con error, a crear y a actualizar.
6. **Modo de carga** elegido al subir: `CreateOnly` (una fila que ya existe es error) o `CreateOrUpdate` (actualiza por la clave de la regla 1).
7. **Carga confirmada y completa:** solo se puede confirmar un archivo con **cero errores**. La carga es **todo o nada** en una transacción. Si los datos cambiaron entre la validación y la confirmación (por ejemplo, alguien creó el mismo código), se vuelve a validar y, si aparecen errores, no se carga nada.
8. **Rastro:** cada importación (`DataImports`) guarda el archivo original, el catálogo, el modo, el usuario, las fechas, el resumen y los errores. Las entidades creadas o actualizadas registran el id de la importación. Las existencias iniciales generan un ajuste de inventario con el concepto "Carga inicial CSV <id>".
9. **Dónde:** la guía y la plantilla de todos los catálogos se descargan en Administración ("Importación de datos"). Cada sección (Productos, Personas, Clientes, Existencias, Líneas de crédito) tiene el botón para descargar su guía y subir su archivo.
10. **Permisos:** `DataImports` (`CanCreate`) más el permiso de crear (y de actualizar, en `CreateOrUpdate`) del recurso destino.

## Scope

**In:**

**A. Esquema (`Customer`)**
1. `DataImports` (`BaseTenantEntity`): `Catalog`, `Mode`, `Status` (`Uploaded`, `Validated`, `ValidatedWithErrors`, `Imported`, `Failed`, `Discarded`), `FileName`, `StorageKey`, `RowCount`, `ValidRows`, `ErrorRows`, `ToCreate`, `ToUpdate`, `UploadedByUserId`, `UploadedAt`, `ValidatedAt?`, `ImportedAt?`, `ImportedByUserId?`.
2. `DataImportErrors`: `DataImportId`, `RowNumber`, `Column?`, `Value?` (máx. 500), `Code`, `Message`.
3. Las tablas destino suman `DataImportId?` (`Catalog.Products`, `Admin.Persons`, `Customer.Customers`, `Customer.CustomerCreditLines`).

**B. Endpoints**
4. `GET /api/v1/DataImports/catalogs`: catálogos disponibles y si el usuario puede importarlos.
5. `GET /api/v1/DataImports/{catalog}/guide?format=pdf|json` y `GET /{catalog}/template` (CSV).
6. `POST /api/v1/DataImports/{catalog}` (multipart, `mode`): sube y valida. Devuelve el resumen y la primera página de errores.
7. `GET /api/v1/DataImports/{id}`, `GET /{id}/errors` (paginado, exportable a CSV), `POST /{id}/confirm` (carga), `POST /{id}/discard`.
8. `GET /api/v1/DataImports` (historial con filtros de catálogo, estado, usuario y fecha).
9. Cada catálogo tiene un `ICsvImportDefinition<TRow>` (Application) con columnas, parser, validador y escritor, para agregar catálogos nuevos sin tocar el flujo.

**C. Menú**
10. Administración: `DataImports` → `/administration/data-imports`.

**Out of scope:**
- Importar documentos históricos (facturas, pagos) y Excel (`.xlsx`).
- Importación en segundo plano para archivos grandes (se evaluará con Hangfire).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `IMPORT_CATALOG_NOT_FOUND` | 404 | Catálogo inexistente |
| `IMPORT_FILE_INVALID` | 400 | No es CSV, no es UTF-8, más de 5 MB o más de 5.000 filas |
| `IMPORT_HEADERS_MISMATCH` | 400 | Encabezados distintos de la plantilla (devuelve faltantes y sobrantes) |
| `IMPORT_HAS_ERRORS` | 409 | Confirmar una importación con errores |
| `IMPORT_ALREADY_PROCESSED` | 409 | Confirmar o descartar dos veces |
| `IMPORT_DATA_CHANGED` | 409 | La revalidación al confirmar encontró errores nuevos |

## Criterios de aceptación

1. La guía de Productos lista las columnas con sus reglas y los códigos actuales de categorías, unidades y grupos fiscales de la empresa.
2. Un archivo de 100 productos con 3 errores devuelve las 3 filas con columna y mensaje, y no crea nada.
3. Corregido y subido de nuevo, `confirm` crea los 100 productos en una transacción.
4. En `CreateOrUpdate`, un código existente actualiza el producto. En `CreateOnly` es error.
5. Un RUC inválido en Clientes da error en su fila con el mensaje del perfil fiscal.
6. Las existencias iniciales crean un ajuste de inventario por almacén con el concepto de la carga y sus movimientos en el kardex.
7. Si entre validar y confirmar otro usuario crea un código del archivo, `confirm` → 409 y no carga nada.
8. Unitarios: parser (separadores, BOM, comillas), cada definición de catálogo y validación cruzada. Integración: flujo completo de Productos y Clientes.

## Decisiones

- **Todo o nada:** una carga inicial a medias deja datos difíciles de corregir. Se corrige el archivo y se sube de nuevo.
- **Mismos validadores que los formularios:** un dato que no se puede crear a mano tampoco entra por CSV.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Plantillas con filas de ejemplo de Nicaragua (productos de mostrador, servicios por hora, personas de Managua y León, clientes con RUC `J0310000000xxx`).
- Una importación de ejemplo `Imported` (10 productos) y otra `ValidatedWithErrors` para mostrar el informe de errores.
