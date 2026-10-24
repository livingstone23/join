# Specs del backend (BE)

Índice de todas las specs de este repositorio. Se actualiza **en el mismo cambio** que crea una spec o le cambia el estado, la etapa o las dependencias.

Las specs del frontend viven en `join_frontb/specs` y tienen su propia numeración. Para evitar ambigüedad, entre repositorios se citan con prefijo: **BE-43** (esta carpeta) y **FE-15** (frontend). Dentro de una misma carpeta basta "SPEC 43".

---

## Reglas

### Numeración
- Una spec nueva toma el **siguiente número libre** (hoy: **102**; la 99 no se usa porque existió hasta 2026-10-18 y pasó a 00; la 51 es de `main` —su copia en el fork es `51-post`— y de la 52 a la 98, la 100 y la 101 son propias del fork; la **999** es un número reservado para la spec pospuesta del módulo Productos ampliado).
- Los números **no se reutilizan ni se reordenan**. Si una spec se divide, la parte que sale toma un número nuevo y ambas lo dicen en su encabezado (ejemplo: SPEC 00 → SPEC 47).
- **Excepción autorizada (2026-10-05):** por decisión del usuario se renumeró una sola vez: la 37 (canales `WEB`/`APP`) pasó a **99** y las 38–49 bajaron un número (38→37 … 49→48). Las referencias se actualizaron en ambos repositorios. Una spec citada con su número anterior en un commit o conversación previa a esa fecha debe traducirse con esta tabla.
- **Excepción autorizada (2026-10-18):** por decisión del usuario, la 99 y la 99-post (canales `WEB`/`APP`) pasaron a **00** y **00-post**. Las referencias se actualizaron en ambos repositorios (BE-99 → BE-00); los nombres ya aplicados no cambian (migración `Spec99InternalCommunicationChannels`, usuario `Spec99_Migration`, rama `spec-99-post-ticket-inbound-channels`). En `main` la spec sigue numerada 99. Una cita a "SPEC 99" anterior a esa fecha se lee como SPEC 00.
- **Excepción autorizada (2026-10-24):** por decisión del usuario, la spec pospuesta del módulo Productos ampliado pasó de **85** a **999** (fuera de la secuencia, para que no ocupe un lugar en el orden de implementación) y las tres siguientes subieron un número: 86 → **85** (almacenes), 87 → **86** (estados de factura y crédito) y 88 → **87** (línea de crédito). Las referencias se actualizaron en el mismo cambio. El commit `aa12668` aún muestra la 85 con el contenido de la spec pospuesta.
- El nombre del archivo es `NN-tema-en-ingles.md` y el título empieza con `# SPEC NN — `.
- **Serie `-post`** (2026-10-09): copias de specs de `main` adaptadas a PostgreSQL, `NN-post-tema.md`; no consumen números (ver "Serie `-post`").

### Estados
| Estado | Significado | Quién lo asigna |
|---|---|---|
| `Borrador` | Redactada, pendiente de revisión. Toda spec nueva nace así. | Quien la redacta |
| `Aprobado` | Revisada y lista para implementar. | **Solo el usuario** |
| `Implementado` | El código está en `main` y cumple los criterios de aceptación. | Al cerrar la implementación, con confirmación del usuario |
| `Pospuesto` | Diseño guardado para una etapa posterior. No se implementa hasta que el usuario lo retome; al retomarla se revisa contra el estado actual y vuelve a `Borrador`. | El usuario decide la etapa |

### Cambios sobre una spec existente
- **Cambia el alcance o la lógica** (se agregan o quitan funcionalidades, cambian reglas de negocio): vuelve a `Borrador` y el encabezado lleva una nota **"Historia"** que explica qué cambió y por qué.
- **Ajuste editorial o de consistencia** (nombres, rutas, referencias a otra spec): conserva su estado y el encabezado lleva una nota **"Ajuste por SPEC XX (fecha)"**.
- Una decisión que afecta a specs ya escritas se refleja en **todas** ellas en el mismo cambio, incluidas las del frontend (FE) cuando tocan rutas, permisos o contratos de API.

### Antes de implementar
- Revisar las specs posteriores que la citan (`grep "SPEC NN" specs/`) y sus notas de ajuste, historia o etapa. La spec más reciente prevalece.
- Confirmar que todas sus dependencias están `Implementado` o se implementan en el mismo cambio.

---

## Índice

| Nº | Título | Estado | Depende de (según su encabezado) |
|---|---|---|---|
| 00 | Canales internos `WEB` y `APP` en el catálogo de canales | Borrador | — |
| 01 | TransactionBehavior para Commands en MediatR | Implementado | — |
| 02 | PerformanceBehavior para medición de latencia en MediatR | Implementado | 01 |
| 03 | UnhandledExceptionBehavior y LoggingBehavior | Implementado | 01, 02 |
| 04 | Serilog como motor de logging definitivo | Implementado | 01, 02, 03 |
| 05 | Resiliencia HTTP (Polly v8) y CommandTimeout de EF Core | Implementado | — |
| 06 | Infraestructura base de pruebas de integración | Implementado | 01 |
| 07 | Contenedorización de la API y entorno local mínimo (Docker) | Implementado | — |
| 08 | Versionamiento de API y documentación Scalar | Implementado | 07 |
| 09 | Restaurar el build de CI | Implementado | 01, 06 |
| 10 | Corregir fallas runtime post-SPEC 09 (Person) | Implementado | 01, 06, 09 |
| 11 | Cerrar las fallas restantes de CompanyModules (test-only) | Implementado | 06, 09, 10 |
| 12 | Permisos CanExport y CanExecute en RoleSystemOption | Implementado | — |
| 13 | Corrección de warnings de build y runtime (NuGet + EF Core) | Implementado | — |
| 14 | Saltar el reseed de menú/permisos cuando no es necesario | Implementado | — |
| 15 | Fix de IdentityModel en el collector de HealthChecks.UI | Implementado | 13 |
| 16 | CanExport y CanExecute en SystemOption | Implementado | 12 |
| 17 | `PermissionResource` opcional y override de flags por acción | Implementado | 12, 16 |
| 18 | CRUD de Roles con /detailed y soft delete | Implementado | 17 |
| 19 | Junction `RoleCompany` con CRUD restringido a SuperAdminCompany | Implementado | 17, 18 |
| 20 | Preview de usuarios afectados por rol | Implementado | 17, 18, 19 |
| 21 | SystemOption DTO completeness + ModuleName/ParentName | Aprobado | 16, 18 |
| 22 | RoleSystemOption DTO completeness | Implementado | 12, 16, 17, 21 |
| 23 | RoleSystemOption: tenant desde el JWT en PUT/DELETE | Implementado | 17, 22 |
| 24 | Roles hardening | Implementado | 18, 19, 22 |
| 25 | `RoleSystemOptions` bulk upsert + matrix | Implementado | 18, 22, 23, 24 |
| 26 | `account/*` self-management | Implementado | 18, 22, 23, 25 |
| 27 | Alta administrada de usuarios (invite), activación y reseteo forzado | Aprobado | 17, 23, 25 |
| 28 | Membresía multi-empresa, permisos efectivos y reporte de usuarios | Implementado | 17, 23, 25, 27 |
| 29 | Bitácora de seguridad | Implementado | 17, 23, 25, 27, 28 |
| 30 | Paridad de soft-delete en `UserConnectionLogs` | Implementado | 06, 26 |
| 31 | MFA configuration runbook | Implementado | 26 |
| 32 | Desafío de login MFA (Email OTP + TOTP) | Implementado | 03, 26, 30 |
| 33 | Centralización de `PaginationSettings` | Implementado | — |
| 34 | `TicketUserCompany`: roster de agentes de tickets | Implementado | — |
| 35 | Acciones de ciclo de vida del ticket | Implementado | 34 |
| 36 | Adjuntos de ticket: parámetros, `TicketDocuments` y storage | Implementado | 35 |
| 37 | Transiciones de status parametrizables y SLA | Implementado | 34, 35, 36 |
| 38 | Consolidación de query filters globales | Implementado | 30 |
| 39 | Fork `main_postgresql` | Implementado | — |
| 40 | Índices únicos filtrados por soft-delete | Borrador | — |
| 41 | Visibilidad de registros borrados y restauración para SuperAdmin | Borrador | 38, 40 |
| 42 | Valores iniciales del ticket desde `TicketCompanyDefaults` | Borrador | — |
| 43 | Módulos por empresa: módulos base, menú filtrado y rutas en inglés | Borrador | — |
| 44 | Módulo Calendario: parametrización, calendarios y actividades | Borrador | 00, 43, 48 |
| 45 | Calendario: ingreso por canales (agente) y personas pendientes | **Pospuesto** | 43, 44, 48 |
| 46 | Calendario: estructura para sincronizar con Google Calendar | Borrador | 44 |
| 47 | Ingesta de tickets por WhatsApp y correo | **Pospuesto** | 34, 35, 36, 00, 42, 43 |
| 48 | Bloqueo de APIs y menú por módulo activo de la empresa | Borrador | 43 |
| 49 | Almacenamiento de adjuntos intercambiable: local, Azure Blob, S3 y Cloudflare R2 | Borrador | 36 |
| 50 | `main_postgresql`: pendientes para llevar QA a PostgreSQL (el port de 40–49 pasó a la serie `-post`) | Aprobado | 39 |
| 52 | Tickets: resumen de la bandeja (`GET /Tickets/summary`) y filtro "sin asignar" | Implementado | 37, 38, 41-post, 43-post |
| 53 | Tickets: mis capacidades (`/TicketUserCompanies/me`), usuarios asignables e inactividad en los valores por defecto | Implementado | 34, 35, 37, 43-post |
| 54 | Personas: búsqueda única con marca de cliente (`GET /Persons/lookup`) y datos semilla (40 personas, 30 clientes) | Implementado | 38, 39, 43-post, 55 |
| 55 | Clientes: uno por persona y empresa, solo activo/inactivo (sin borrado), activación idempotente, historial de eventos, catálogo de etapas por empresa (`CustomerStages`, reemplaza el enum `PersonLifecycleStage`) y esquema propio `Customer` (sale de 54) | Implementado | 38, 39, 40-post, 41-post, 43-post, 45-post |
| 56 | Monedas por empresa: moneda base, monedas paralelas y tasas de cambio fechadas (esquema `Finance`) | Implementado | 39, 41-post, 43-post |
| 57 | Catálogo compartido de productos y servicios por empresa (esquema `Catalog`) | Implementado | 41-post, 43-post, 56 |
| 58 | Personas: identificación opcional (única cuando existe; obligatoria para ser cliente) | Implementado | 38, 40-post, 54, 55 |
| 59 | Parametrización comercial: etapas del lead, orígenes, motivos, tipos de vinculación, tipos de contacto y reglas por empresa | Implementado | 41-post, 43-post, 44-post, 00-post |
| 60 | Grupos de trabajo: varios por usuario con grupo principal, vigencia y roles de gestión con alcance de visibilidad | Implementado | 28, 43-post, 59 |
| 61 | Leads: alta, bandeja, detalle, etapas en orden, contactos, comentarios, productos ofrecidos, duplicados, inactivar y reactivar | Implementado | 54, 55, 56, 57, 58, 59, 60 |
| 62 | Documentos de los leads y sus contactos | Implementado | 36, 49-post, 61 |
| 63 | Ganar, perder y reabrir leads; conversión a cliente; etapas de cliente post-venta; migración de clientes en LEAD/PROSPECT | Implementado | 54, 55, 58, 59, 61 |
| 64 | Reportes comerciales: resumen, embudo, ranking de grupos y usuarios, productos, orígenes y contactos | Implementado | 60, 61, 63 |
| 65 | Leads con calendario y personas pendientes: próxima acción agendada y convertir una persona pendiente en lead | Implementado | 44-post, 45-post, 48-post, 59, 61 |
| 66 | Clientes: registrar los cambios de etapa en el historial, con comentario opcional | Aprobado | 55, 63 |
| 67 | Calendario: "Relacionado con" de la actividad unificado en Persona/Cliente | Aprobado | 44-post, 45-post, 54, 55, 65 |
| 68 | Calendario: índice de actividades con filtros y permiso por usuario para ver las de otros | Aprobado | 43-post, 44-post, 48-post, 67 |
| 69 | Calendario: comentarios y documentos de la actividad, con límites de adjuntos por empresa | Aprobado | 36, 44-post, 49-post, 62, 68 |
| 70 | Productos y servicios: imágenes (hasta 5) y documentos, con límites por empresa | Implementado | 36, 41-post, 43-post, 49-post, 57, 62 |
| 71 | Plantillas de correo por empresa, agrupadas por tipo, con variables | Implementado | 41-post, 43-post, 61 |
| 72 | Enviar correos a personas, clientes y leads, y registrarlos en el historial del lead | Implementado | 49-post, 55, 59, 60, 61, 62, 70, 71 |
| 72.1 | Ajuste: tope de adjuntos sin permiso `EmailSettings` y tipos de documento de producto borrados (FE-47.1) | Aprobado | 41-post, 70, 72 |
| 73 | WhatsApp (Meta Cloud API): esquema `Channel_Meta`, conexión por empresa, webhook firmado, perfil del negocio y módulo | Aprobado | 05, 00-post, 43-post, 48-post, 49-post |
| 74 | WhatsApp: conversaciones con código propio, mensajes, estados, marca de identificación, personas pendientes y archivos | Aprobado | 73, 45-post, 49-post, 54, 55, 58, 61, 65 |
| 75 | WhatsApp: agentes y coordinadores, disponibilidad, Cola y En espera, notificaciones, envío de mensajes y tiempo real | Aprobado | 73, 74, 49-post |
| 76 | WhatsApp: plantillas vinculadas con Meta y respuestas rápidas | Aprobado | 73, 74, 75 |
| 77 | WhatsApp: etapas del canal, pipeline por conversación y homologación con Leads y Clientes | Aprobado | 74, 75, 55, 61, 66 |
| 78 | WhatsApp: índice de conversaciones por persona o cliente, con filtros y exportación | Aprobado | 74, 75, 77 |
| 79 | WhatsApp: automatizaciones básicas | Aprobado | 74, 75, 76, 44-post |
| 80 | WhatsApp: asistente IA opcional vía un flujo de n8n | Aprobado | 74, 75, 76, 79, 45-post, 57, 61, 63 |
| 81 | Documentos comerciales del lead: tipos de documento, configuración por tipo y datos del emisor | Borrador | 41-post, 43-post, 49-post, 55, 59, 61, 63, 70, 71 |
| 82 | Impuestos, retenciones y grupos fiscales; grupo fiscal en el catálogo actual de productos | Borrador | 41-post, 43-post, 57, 61, 81 |
| 83 | Documentos comerciales en el lead: crear, emitir, aprobar, convertir; código público, código de barras e histórico del cliente | Borrador | 55, 56, 57, 58, 59, 60, 61, 63, 81, 82 |
| 84 | Documentos comerciales del lead: PDF, descarga, envío al cliente y registro en la bitácora | Borrador | 49-post, 71, 72, 81, 83 |
| 85 | Almacenes y existencias en Leads: entradas por orden de compra, salidas por factura, traslados entre almacenes, kardex, existencia mínima con alertas e infraestructura de alertas | Borrador | 41-post, 43-post, 57, 81, 83, 84 |
| 86 | Estados de la factura, venta a crédito, pagos e historial, con calendario de cobro | Borrador | 44-post, 48-post, 65, 72, 81, 83, 84, 87 |
| 87 | Línea de crédito del cliente: límite, saldo mínimo de advertencia, historial, alertas configurables y panel de crédito | Borrador | 43-post, 55, 56, 72, 81, 83, 85, 86 |
| 88 | Perfil fiscal por país (estándar), primero Nicaragua: impuestos y retenciones base, numeración autorizada, moneda de referencia y punto de conexión para factura electrónica | Borrador | 56, 58, 81, 82, 83, 84, 85, 91 |
| 89 | Punto de venta: venta rápida sin lead, consumidor final, convertir cotizaciones y cobrar facturas, con caja opcional | Borrador | 55, 57, 81, 82, 83, 84, 85, 86, 87, 88, 90 |
| 90 | Costo del producto y margen: costo de última compra, historial de costos, margen por venta e inventario valorizado | Borrador | 56, 57, 81, 83, 85, 89 |
| 91 | Libro de ventas, libro de compras y resumen de impuestos y retenciones por período, con formato por país | Borrador | 82, 83, 85, 88, 89 |
| 92 | Devoluciones con nota de crédito parcial: aplicada a la factura (y a la línea de crédito) o reembolsada en efectivo | Borrador | 81, 83, 84, 85, 86, 87, 88, 89, 90, 91, 93 |
| 93 | Presentación de importes en proformas y facturas: valor de cada línea, IVA cuantificado por tasa, retenciones del cliente y total a pagar | Borrador | 81, 82, 83, 84, 86, 87, 88, 89, 91, 92 |
| 94 | Facturas exentas de impuestos con control: constancia de exoneración adjunta, aprobación, permiso y vigencia | Borrador | 49-post, 55, 81, 82, 83, 84, 85, 88, 89, 91, 92, 93 |
| 95 | Producto: unidad de medida opcional por empresa y precio registrado con o sin IVA | Borrador | 57, 81, 82, 83, 84, 89, 91, 93, 94 |
| 96 | Carga de datos por CSV: guía descargable, plantillas, validación previa y carga confirmada | Borrador | 49-post, 54, 55, 57, 58, 81, 82, 84, 85, 87, 88, 90, 95 |
| 97 | Panel de ventas por período y vendedor, y panel de cuentas por cobrar con antigüedad de saldos, con visibilidad por usuario | Borrador | 43-post, 60, 83, 86, 87, 89, 90, 92, 93 |
| 98 | Límite de descuento por rol, con solicitud de aprobación que otro usuario toma y resuelve | Borrador | 43-post, 81, 83, 85, 89, 90 |
| 100 | Empresa de desarrollo Nicaragua (`JOIN-NI-001`): todo el seed existente, base en córdobas y acceso de los usuarios actuales | Borrador | 19, 28, 43-post, 44-post, 45-post, 48-post, 54, 55, 56, 57, 59–65, 70–72 |
| 101 | Correo por empresa: cuenta de SendGrid propia o de la plataforma, salida única con registro de cada envío, cuotas y reportes de uso por módulo | Borrador | 41-post, 43-post, 72 |
| 999 | Módulo Productos ampliado: catálogo propio del módulo y origen de productos en Leads (**no se implementa**, decisión del usuario 2026-10-24) | Pospuesto | 41-post, 43-post, 48-post, 56, 57, 61, 64, 81–84 |

### Serie `-post` (specs de `main` adaptadas a PostgreSQL, 2026-10-09)

Las filas 00 y 40–49 de la tabla anterior son las copias de `main` del 2026-10-07: se conservan como referencia y no se implementan en el fork. Las que se implementan aquí son estas:

| Nº | Título | Estado | Depende de (según su encabezado) |
|---|---|---|---|
| 00-post | Canales internos `WEB` y `APP` en el catálogo de canales | Implementado | — |
| 40-post | Índices únicos filtrados por soft-delete | Implementado | 39 |
| 41-post | Visibilidad de registros borrados y restauración para SuperAdmin | Implementado | 38, 40-post |
| 42-post | Valores iniciales del ticket desde `TicketCompanyDefaults` | Implementado | 41-post |
| 43-post | Módulos por empresa: módulos base, menú filtrado y rutas en inglés | Implementado | 39 |
| 44-post | Módulo Calendario: parametrización, calendarios y actividades | Implementado | 00-post, 43-post, 48-post |
| 45-post | Calendario: ingreso por canales (agente) y personas pendientes | Implementado | 43-post, 44-post, 48-post |
| 46-post | Calendario: estructura para sincronizar con Google Calendar | Implementado | 44-post |
| 47-post | Ingesta de tickets por WhatsApp y correo | Implementado | 34, 35, 36, 00-post, 42-post, 43-post |
| 48-post | Bloqueo de APIs y menú por módulo activo de la empresa | Implementado | 43-post |
| 49-post | Almacenamiento de adjuntos intercambiable: local, Azure Blob, S3 y Cloudflare R2 | Aprobado | 36 |
| 51-post | Invalidación de la caché de permisos con la clave `permissions:v2` | Implementado | 17, 25 |

---

## Etapa actual: concluir Tickets y Calendar

Orden sugerido de implementación (cada uno requiere `Aprobado`):

1. **00** — canales `WEB`/`APP` (lo necesitan 42 y 44).
2. **43** — módulos base, opciones conectadas a su módulo, rutas en inglés.
3. **48** — bloqueo por módulo e interruptor `Modules:EnforceCompanyModules`.
4. **42** — estado inicial del ticket.
5. **37** — flujo de estados y SLA, ya aprobada.
6. **44** — calendario base.
7. **46** — estructura de Google Calendar.

En paralelo, sin dependencias con lo anterior: **38 → 40 → 41** (query filters, índices filtrados, restauración) y **39** (fork PostgreSQL, `Implementado` en `main_postgresql`).

**En este fork (2026-10-09):** las specs de `main` se implementan como serie `-post` (orden en "Serie `-post`"). La **50** queda solo con los pendientes para llevar QA a PostgreSQL; su antigua sección 0 (un único `git merge main`) se descartó.

**Etapa posterior** (`Pospuesto`): **45** (agente de canales del calendario) y **47** (tickets por WhatsApp y correo), después de concluir Tickets y Calendar.

---

## Serie `-post` (specs de `main` adaptadas a PostgreSQL)

Decisión del usuario (2026-10-09): las specs 40–49, 51 y 99 (hoy 00) de `main` se copian a este fork como **specs `-post`**, idénticas en negocio pero adaptadas a PostgreSQL, y se **implementan desde cero** en `main_postgresql` (no se traen commits de `main`). Reemplaza el "único `git merge main`" que planteaba la sección 0 de SPEC 50.

### Reglas de la serie

- **Archivo:** `NN-post-<nombre-original>.md` (ejemplo: `40-post-filtered-unique-indexes-soft-delete.md`). **Título:** `# SPEC NN-post — …`. **Cita:** "SPEC 40-post" (dentro del fork) o "BE-40-post" (desde `join_frontb`).
- **No consumen números:** el sufijo `-post` marca una copia de la spec `NN` de `main`; el siguiente número libre no cambia.
- **Estado propio:** toda `-post` nace en `Borrador` (las copias de specs `Pospuesto` en `main` conservan `Pospuesto`). Su estado en el fork es independiente del de `main`.
- **Lectura:** dentro de una `-post`, toda referencia a una spec de la serie (40–49, 51, 00) se lee como su versión `-post`. Si el texto copiado de `main` y la sección "Adaptación a PostgreSQL" de la spec difieren, prevalece esta última.
- **Copias anteriores:** los archivos `40-…md` a `49-…md` y `00-…md` sin sufijo son copias de `main` del 2026-10-07; se conservan sin tocar como referencia y **no** se implementan en el fork.
- **Rama de trabajo:** `spec-NN-post-<nombre>`, creada desde `main_postgresql`, PR hacia `main_postgresql` (nunca hacia `main`).

### Convenciones PostgreSQL (aplican a toda `-post`)

1. **Nombres físicos en minúsculas** (`ApplyLowerCaseNaming`, SPEC 39): esquemas, tablas, columnas, PK/FK e índices. En C# y en las specs se usan los nombres lógicos (`Messaging.Tickets`, `UX_Genders_Company_Name`); en la base son `messaging.tickets`, `ux_genders_company_name`. El SQL crudo va sin comillas; solo se citan palabras reservadas, en minúsculas (`sm."order"`).
2. **`HasFilter`:** SQL crudo en minúsculas y entre comillas dobles, booleanos con `TRUE`/`FALSE`: `"gcrecord" = 0`, `"isinitial" = TRUE AND "gcrecord" = 0`, `"code" IS NOT NULL AND "gcrecord" = 0`. Npgsql **no** agrega el filtro automático `IS NOT NULL` que EF pone en SQL Server a los índices únicos sobre columnas nulas: si hace falta, se escribe.
3. **`NULL` en índices únicos:** PostgreSQL trata dos `NULL` como distintos; SQL Server, como iguales. Cuando el comportamiento de `main` depende de que choquen, el índice usa `.AreNullsDistinct(false)` (`NULLS NOT DISTINCT`, PostgreSQL 15+).
4. **Tipos:** `nvarchar(n)` → `varchar(n)`; `nvarchar(max)` → `text`; `bit` → `boolean`; `uniqueidentifier` → `uuid`; `datetime2` → `timestamp with time zone` (lo que Npgsql usa para `DateTime`); `date` → `date` (`DateOnly`); `time` → `time` (`TimeOnly`).
5. **`DateTime` en UTC:** Npgsql rechaza escribir en `timestamp with time zone` un `DateTime` que no sea `Kind=Utc` (columnas y parámetros Dapper). Todo valor que venga de un request o de una librería se normaliza con `ToUniversalTime()` / `DateTime.SpecifyKind(..., DateTimeKind.Utc)` antes de guardarlo o de usarlo como parámetro.
6. **SQL Dapper:** sin corchetes; `COALESCE`; booleanos `= TRUE`/`= FALSE`; `CAST(... AS boolean)` o una expresión booleana directa (no `CASE ... THEN 1 ELSE 0`); `CONCAT(...)` o `||`; `NOW()`; paginación literal `LIMIT @PageSize OFFSET @Offset` (sin branch por proveedor); listas **solo** con `= ANY(@Ids)` y un arreglo (`ids.ToArray()`): con `NpgsqlConnection`, Dapper pasa la lista como arreglo y **no** expande `IN @Ids`, que da error de sintaxis (Ajuste por SPEC 50); filtros opcionales `(@p IS NULL OR col = @p)`: si el valor puede llegar nulo sin tipo (`DynamicParameters.Add(name, null)` o un `DateTime?` nulo), el `IS NULL` lleva `CAST(@p AS <tipo>)` (si no, PostgreSQL responde 42P08); **búsquedas de texto con `ILIKE`** (conserva la búsqueda sin distinción de mayúsculas que da la colación de SQL Server; las búsquedas existentes las convierte SPEC 50 A).
7. **Migraciones:** cada `-post` genera su propia migración incremental contra Npgsql desde `src/4.Services.WebApi` (`dotnet ef migrations add <Nombre> --project ../3.Persistence --startup-project .`) e inspecciona el `Up()` (sin `nvarchar`/`datetime2`/`bit`/corchetes; filtros en minúsculas). Nunca se copian migraciones de `main`. Las migraciones de datos (`migrationBuilder.Sql`) se escriben en SQL de PostgreSQL (`gen_random_uuid()`, `NOW()`, nombres en minúsculas).
8. **Bloqueos:** `pg_advisory_xact_lock(...)` en lugar de `sp_getapplock`; `SELECT ... FOR UPDATE` en lugar de `WITH (UPDLOCK, HOLDLOCK)`.
9. **Violación de índice único:** `PostgresException` con `SqlState = 23505` (y `ConstraintName`); `GlobalExceptionHandler` ya la responde como 409 `DUPLICATE_KEY` (SPEC 39). Si un handler necesita reconocer la violación de un índice concreto, lo hace un servicio de Persistence/Infrastructure (Application no referencia Npgsql).
10. **Validación del SQL:** `EXPLAIN` de cada sentencia nueva contra la base migrada (técnica de SPEC 39).
11. **Tests:** los unitarios que afirman texto SQL afirman el SQL de PostgreSQL. Los de integración usan `CustomWebApplicationFactory` (`postgres:17`, SPEC 50 E); los previos al SPEC 50 E mantienen su sufijo `PostgreSqlTests` y siguen usando esa misma factory. Job de CI: `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj` (sin filtro). Comando: `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj`.
12. **Verificación con Docker** (smoke manual de cada `-post`, decisión del usuario 2026-10-09; `docker-compose.yml` no cambia):

    ```bash
    docker run --name join-pg-spec -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:17
    DatabaseProvider=PostgreSQL \
    ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=join_db;Username=postgres;Password=postgres" \
    dotnet run --project src/4.Services.WebApi
    # smoke de la spec (curl), luego detener la API y:
    docker rm -f join-pg-spec
    ```

    Cuando la spec toca la imagen de la API (`Dockerfile`), el smoke se repite con la imagen: `docker build -t join-api-pg .` y `docker run` con las mismas variables, usando `Host=host.docker.internal`.

### Convención: correo saliente (SPEC 101)

Todo correo que envíe el sistema sale por `IEmailDispatcher` con su clasificación: empresa (o ninguna, si es de sistema), `Module` (enum de SPEC 101) y `Feature` (código con prefijo del módulo). Ninguna spec usa `IEmailService` directamente. Una spec que envíe correo declara su `Module` y sus `Feature`; si necesita un módulo nuevo, lo agrega al enum de SPEC 101 con una nota "Ajuste por SPEC NN".

### Orden de implementación en el fork

1. **40-post** → **41-post** (etapas 1–4) → **00-post** → **42-post** → **43-post** → **48-post** → **44-post** → **46-post**. Es el mismo orden en que se implementaron o se implementarán en `main`; 41-post necesita 40-post, y 42-post usa el bloqueo de borrado de 41-post. **Ajuste (2026-10-09, decisión del usuario):** 42-post se implementó antes que 00-post; no dependen entre sí. **Ajuste (2026-10-12, decisión del usuario):** 44-post se implementa antes que 48-post; hasta que 48-post exista, el módulo Calendario queda sembrado pero el filtro global no bloquea sus APIs por módulo inactivo.
2. **49-post** y **51-post**: sin dependencias con lo anterior, en cualquier momento.
3. **45-post**: `Implementado` (rama `spec-45-post-calendar-channel-intake`, mergeada en `main_postgresql`). **47-post**: `Aprobado`; en `main` sigue `Pospuesto`.
4. **50**: pendientes para llevar QA a PostgreSQL (sin su antigua sección 0), cuando el usuario lo decida.
5. **52**: propia del fork (no es copia de `main`). Resumen de la bandeja y filtro "sin asignar" que pide FE-16 (brechas 2 y 4 de FE-15). Sin dependencias pendientes: 37, 38, 41-post y 43-post ya están implementadas.
6. **53**: propia del fork. Capacidades del usuario, usuarios asignables (`CanResolveTicket`) y `MaxDayTicketInactivity` en los DTO de defaults, que piden FE-17, FE-18 (fase B) y FE-19. Sin dependencias pendientes.
7. **55** → **54**: propias del fork. 55 deja un cliente por persona y empresa (sin borrado, activo/inactivo, activación idempotente con `ICustomerActivationService` e historial `CustomerStatusHistory`, catálogo de etapas por empresa `CustomerStages` con su opción de menú `/customers/customer-stages`); 54 agrega `GET /Persons/lookup`, que pide FE-27 (y cierra la brecha de FE-22), y el seed de 40 personas y 30 clientes registrados por `livingstone23@gmail.com`. 54 necesita 55; pueden ir en el mismo cambio.
8. **56 – 65**: propias del fork. Módulo de clientes al estilo Dynamics 365 (diseño del 2026-10-16, propuesta visual "Clientes JOIN"): el lead es su propia entidad sobre `Admin.Persons` y el cliente se crea al ganar. Orden: **56** (monedas), **57** (productos) y **58** (identificación opcional) son independientes entre sí y van primero; luego **59** (parametrización) → **60** (grupos) → **61** (leads) → **62** (documentos) y **63** (ganar, perder, migración) → **64** (reportes) y **65** (calendario y personas pendientes). 63 cambia el significado de `CustomerStages` (SPEC 55) a etapas post-venta.
9. **73 – 80**: propias del fork. Canal WhatsApp con la Meta Cloud API, una App y un número por empresa, esquema `Channel_Meta` (diseño del 2026-10-18/19, propuesta visual "WhatsApp JOIN" en `join_frontb/SpecPropuestaWhatsApp/`). Orden: **73** → **74** → **75** → **76** → (**77**, **78**, **79** en cualquier orden) → **80** (asistente IA vía n8n). Reemplaza la ingesta por Twilio (47-post) para WhatsApp, que queda para el futuro. 74 ajusta 45-post: personas pendientes con `AssignedUserId` y `ConversationId`.
10. **81 – 84**: propias del fork. Documentos comerciales básicos **dentro de Leads**, parametrizados en Administración (pedido del 2026-10-24): tipos de documento y su configuración (numeración, encabezado, pie, leyendas, efectos al aprobar), impuestos y grupos fiscales sobre `Catalog.Products`, documentos en el lead con código público y código de barras, histórico del cliente, PDF, envío y bitácora. Un modo por empresa (`CompanyCustomers.DocumentsMode`) los activa y, cuando existan los módulos completos, los desconecta. También almacenes y existencias (**85**), estados de factura, crédito, pagos y calendario de cobro (**86**) y línea de crédito con alertas y panel (**87**). Orden: **101** (correo por empresa y salida única, pedido antes de arrancar) → **100** (empresa de desarrollo, para sembrar los ejemplos) → **81** → **82** → **83** → **84** → **85** → **87** + **86** (86 y 87 dependen entre sí y van en el mismo cambio). Perfil fiscal por país (**88**, primero Nicaragua) después de 85. Luego punto de venta (**89**) y costo y margen (**90**) (89 usa el costo de 90 si ya existe; si 89 va antes, el costo de línea se agrega al implementar 90) libros de impuestos (**91**) devoluciones con nota de crédito parcial (**92**) y presentación de importes con IVA, retenciones del cliente y total a pagar (**93**) exenciones controladas con constancia adjunta (**94**), unidad de medida y precio con o sin IVA (**95**), carga por CSV (**96**), paneles de ventas y cuentas por cobrar (**97**) y descuentos con aprobación (**98**). Los procesos automáticos que generan (disparados por acción y programados con Hangfire, pendientes) están en [`PROCESSES.md`](PROCESSES.md). Parametrización por empresa en `Customer.CompanyCustomers` (extensión 1:1 de la empresa). **999** (`Pospuesto`, decisión del usuario): módulo Productos con catálogo propio; **no se implementa**, queda solo como registro del diseño.

---

## Datos de ejemplo de Nicaragua (seed de desarrollo, SPEC 81–98 y 100)

Decisión del usuario (2026-10-24): el seed de desarrollo de las specs 81 en adelante usa **datos simulados de Nicaragua** en la empresa de desarrollo **`JOIN-NI-001`** (SPEC 100: base NIO, todo el seed existente de `JOIN-001` y acceso de los usuarios actuales, incluido `livingstone23@gmail.com`). `JOIN-001` no cambia. Cada spec lista en su sección "Seed de desarrollo (Nicaragua, datos simulados)" lo que siembra. Reglas comunes:

- Solo en `Development` y solo para `JOIN-NI-001`. Idempotente: inserta lo que falte y no pisa cambios del usuario.
- Datos **ficticios**: razones sociales inventadas, RUC con el formato válido pero números inexistentes (`J0310000000xxx`), correos con dominio `.example`, cuentas bancarias y referencias de pago inventadas, autorizaciones de numeración con prefijo `DGI-SIM-`.
- Moneda base y local `NIO` (sembrada en el catálogo global por SPEC 100), referencia `USD` con tasas simuladas alrededor de 36,62 (1 USD = 36,62 NIO). Ciudades: Managua, León, Granada.
- Emisor: JOIN Soluciones Empresariales, S.A. Clientes: Farmacia San Martín, S.A.; Ferretería El Progreso; Clínica Médica Las Colinas, S.A.; Asociación Salud para Todos (exonerada). Proveedor: Distribuidora Tecnológica de Centroamérica, S.A. Almacenes: Bodega Central Managua, Sucursal León, Sucursal Granada.

## Pendientes abiertos

Decisiones o verificaciones que no tienen spec propia todavía:

| Origen | Pendiente |
|---|---|
| 43 (A3) | Método para poblar `RoleSystemOptions` de una empresa nueva. Hoy solo `JOIN-001` tiene permisos sembrados; las demás se configuran a mano. |
| 43 (A6) | Asignación automática de módulos base a empresas nuevas y existentes. |
| ~~45~~ | ~~`DynamicAuthorizationFilter` usa el claim `CompanyId` del JWT, no `X-Company-Id`: definir cómo opera un agente en varias empresas.~~ Resuelto en 45-post (2026-10-12): la empresa es siempre la del token; el agente lista sus empresas con `GET /CalendarChannelIntake/companies` y cambia con `POST /workspaces/switch-company`. |
| 45 | Proceso de limpieza de personas con pendiente `Discarded`. |
| 44 | Recordatorios y tareas automáticas (Hangfire): avisos, cierre de actividades vencidas, barridos de estados. |
| 46 | Acciones de sincronización con Google (OAuth, envío, webhook, worker). |
| 44 | Mejoras diferidas del calendario: invitados, calendarios privados, turnos nocturnos, permisos de medio día. |
| 41 | Al implementarla, incluir las entidades del calendario (44, 45, 46). |
| 55 | Conectar más eventos a `ICustomerActivationService` (alta de ticket, actividad de calendario, ingreso por canal): cada uno en la spec que lo pida, con su `CustomerStatusSource`. |
| 55 | Candidatas a pasar al esquema `Customer`, a analizar al crear el módulo de clientes (decisión del usuario, 2026-10-15: hasta entonces se quedan en `Admin`): `PersonPendingConfirmations` (cola de revisión del menú Clientes) y, si se decide, el agregado `Person` (`Persons` + direcciones, contactos, empleos, perfiles comercial y financiero) con sus catálogos (`Genders`, `IdentificationTypes`, `Industries`, `IncomeRanges`, `TaxRegimes`). Hoy siguen en `Admin`. Revisado al diseñar el módulo de clientes (2026-10-16, SPEC 58 y 65): se quedan en `Admin`; el módulo nuevo usa el esquema `Customer` solo para leads, catálogos comerciales y grupos. |
| 38 (F0) | Ejecutar `docs/migrations/spec-38-data-audit.sql` contra la base de desarrollo y pegar los conteos en el PR; si hay filas afectadas, decidir si se reasignan. |
| 38 (D) | Smoke manual: un `SuperAdminCompany` solo ve su empresa y sus módulos, y no puede borrar empresas ni modificar módulos; validar con `join_frontb`. |
| ~~40-post~~ | ~~`DapperContext.CreateConnection()` siempre crea `SqlConnection`~~ Resuelto (2026-10-09, rama `fix-post-dapper-dateonly-and-dappercontext`): `DapperContext` elige Npgsql o SqlClient según `DatabaseProvider`, igual que `SqlConnectionFactory`, y un handler de Dapper (`DateOnlyCompatibleDateTimeHandler`) lee las columnas `date` (que Npgsql devuelve como `DateOnly`) en propiedades `DateTime`. Caso D.2 de SPEC 40-post ejecutado: cuatro `PUT /Persons` → 200. |
| ~~41-post~~ | ~~`GetPersonByIdQueryHandler` lee direcciones y contactos como `dynamic`~~ Resuelto (2026-10-09, en `main_postgresql`): se leen con filas tipadas (`AddressRow`, `ContactRow`), que Dapper mapea sin distinguir mayúsculas; `GET /Persons/{id}` con direcciones y contactos responde 200. Test de regresión con columnas en minúsculas. |
| 59, 61 | Inactivación automática de leads tras N días sin contacto: una tarea programada por módulo (decisión del usuario, 2026-10-16: por ahora la inactivación es manual). Conviene resolverla junto con la de SPEC 44 (Hangfire). |
| 56, 57, 63 | Módulos futuros (Inventario, Compras, Ventas, Presupuestos): se conectan por `Catalog.Products` (SPEC 57), `Finance` (monedas y tasas, SPEC 56), `Leads` y sus líneas aceptadas (SPEC 61, 63) y `Customers`; "Posible proveedor" (SPEC 59) espera al módulo de Compras para tener destino al ganar. |
| 81–84 | Módulos completos (Ventas, Facturación, Compras) y el modo `External` de `CompanyCustomers.DocumentsMode` para conectarlos; factura electrónica por país (DGI, Hacienda de Costa Rica, SAT de Guatemala, MH de El Salvador, DGI de Panamá…); conciliación bancaria. |
| 81–98 | **Procesos programados por tiempo** (cuotas vencidas o por vencer, numeración y constancias por vencer, solicitudes de descuento vencidas): ver `PROCESSES.md`, sección 2. Se implementan con Hangfire (decisión del usuario, 2026-10-24). |
| 81–98 | **Cuentas por pagar** (deuda y pagos a proveedores): fuera de la serie por decisión del usuario (2026-10-24); llegan con los módulos completos. Las órdenes de compra solo registran ingresos al almacén, costo y datos de la factura del proveedor. |
| 81–98 | **Servicios recurrentes:** facturación automática periódica de un mismo servicio a un cliente (mensualidades, suscripciones, contratos de soporte), con su calendario de emisión. Para después (decisión del usuario, 2026-10-24); depende también de Hangfire. |
| 88 | **Puesta en marcha con contador:** confirmar con un contador o la DGI tasas, retenciones, autorización del sistema de facturación, leyendas obligatorias, formato del libro de ventas y si aplica factura electrónica; luego confirmar la lista de verificación fiscal en el sistema. |
| Proceso | Script de verificación de consistencia de specs (referencias inexistentes, nombres retirados, dependencias de specs pospuestas, specs fuera del índice). |
