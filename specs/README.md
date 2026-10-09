# Specs del backend (BE)

Índice de todas las specs de este repositorio. Se actualiza **en el mismo cambio** que crea una spec o le cambia el estado, la etapa o las dependencias.

Las specs del frontend viven en `join_frontb/specs` y tienen su propia numeración. Para evitar ambigüedad, entre repositorios se citan con prefijo: **BE-43** (esta carpeta) y **FE-15** (frontend). Dentro de una misma carpeta basta "SPEC 43".

---

## Reglas

### Numeración
- Una spec nueva toma el **siguiente número libre** (hoy: **51**).
- Los números **no se reutilizan ni se reordenan**. Si una spec se divide, la parte que sale toma un número nuevo y ambas lo dicen en su encabezado (ejemplo: SPEC 99 → SPEC 47).
- **Excepción autorizada (2026-10-05):** por decisión del usuario se renumeró una sola vez: la 37 (canales `WEB`/`APP`) pasó a **99** y las 38–49 bajaron un número (38→37 … 49→48). Las referencias se actualizaron en ambos repositorios. Una spec citada con su número anterior en un commit o conversación previa a esa fecha debe traducirse con esta tabla.
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
| 44 | Módulo Calendario: parametrización, calendarios y actividades | Borrador | 99, 43, 48 |
| 45 | Calendario: ingreso por canales (agente) y personas pendientes | **Pospuesto** | 43, 44, 48 |
| 46 | Calendario: estructura para sincronizar con Google Calendar | Borrador | 44 |
| 47 | Ingesta de tickets por WhatsApp y correo | **Pospuesto** | 34, 35, 36, 99, 42, 43 |
| 48 | Bloqueo de APIs y menú por módulo activo de la empresa | Borrador | 43 |
| 49 | Almacenamiento de adjuntos intercambiable: local, Azure Blob, S3 y Cloudflare R2 | Borrador | 36 |
| 50 | `main_postgresql`: pendientes para llevar QA a PostgreSQL (el port de 40–49 pasó a la serie `-post`) | Borrador | 39 |
| 99 | Canales internos `WEB` y `APP` en el catálogo de canales | Borrador | — |

### Serie `-post` (specs de `main` adaptadas a PostgreSQL, 2026-10-09)

Las filas 40–49 y 99 de la tabla anterior son las copias de `main` del 2026-10-07: se conservan como referencia y no se implementan en el fork. Las que se implementan aquí son estas:

| Nº | Título | Estado | Depende de (según su encabezado) |
|---|---|---|---|
| 40-post | Índices únicos filtrados por soft-delete | Borrador | 39 |
| 41-post | Visibilidad de registros borrados y restauración para SuperAdmin | Borrador | 38, 40-post |
| 42-post | Valores iniciales del ticket desde `TicketCompanyDefaults` | Borrador | 41-post |
| 43-post | Módulos por empresa: módulos base, menú filtrado y rutas en inglés | Borrador | 39 |
| 44-post | Módulo Calendario: parametrización, calendarios y actividades | Borrador | 99-post, 43-post, 48-post |
| 45-post | Calendario: ingreso por canales (agente) y personas pendientes | **Pospuesto** | 43-post, 44-post, 48-post |
| 46-post | Calendario: estructura para sincronizar con Google Calendar | Borrador | 44-post |
| 47-post | Ingesta de tickets por WhatsApp y correo | **Pospuesto** | 34, 35, 36, 99-post, 42-post, 43-post |
| 48-post | Bloqueo de APIs y menú por módulo activo de la empresa | Borrador | 43-post |
| 49-post | Almacenamiento de adjuntos intercambiable: local, Azure Blob, S3 y Cloudflare R2 | Borrador | 36 |
| 51-post | Invalidación de la caché de permisos con la clave `permissions:v2` | Borrador | 17, 25 |
| 99-post | Canales internos `WEB` y `APP` en el catálogo de canales | Borrador | — |

---

## Etapa actual: concluir Tickets y Calendar

Orden sugerido de implementación (cada uno requiere `Aprobado`):

1. **99** — canales `WEB`/`APP` (lo necesitan 42 y 44).
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

Decisión del usuario (2026-10-09): las specs 40–49, 51 y 99 de `main` se copian a este fork como **specs `-post`**, idénticas en negocio pero adaptadas a PostgreSQL, y se **implementan desde cero** en `main_postgresql` (no se traen commits de `main`). Reemplaza el "único `git merge main`" que planteaba la sección 0 de SPEC 50.

### Reglas de la serie

- **Archivo:** `NN-post-<nombre-original>.md` (ejemplo: `40-post-filtered-unique-indexes-soft-delete.md`). **Título:** `# SPEC NN-post — …`. **Cita:** "SPEC 40-post" (dentro del fork) o "BE-40-post" (desde `join_frontb`).
- **No consumen números:** el sufijo `-post` marca una copia de la spec `NN` de `main`; el siguiente número libre no cambia.
- **Estado propio:** toda `-post` nace en `Borrador` (las copias de specs `Pospuesto` en `main` conservan `Pospuesto`). Su estado en el fork es independiente del de `main`.
- **Lectura:** dentro de una `-post`, toda referencia a una spec de la serie (40–49, 51, 99) se lee como su versión `-post`. Si el texto copiado de `main` y la sección "Adaptación a PostgreSQL" de la spec difieren, prevalece esta última.
- **Copias anteriores:** los archivos `40-…md` a `49-…md` y `99-…md` sin sufijo son copias de `main` del 2026-10-07; se conservan sin tocar como referencia y **no** se implementan en el fork.
- **Rama de trabajo:** `spec-NN-post-<nombre>`, creada desde `main_postgresql`, PR hacia `main_postgresql` (nunca hacia `main`).

### Convenciones PostgreSQL (aplican a toda `-post`)

1. **Nombres físicos en minúsculas** (`ApplyLowerCaseNaming`, SPEC 39): esquemas, tablas, columnas, PK/FK e índices. En C# y en las specs se usan los nombres lógicos (`Messaging.Tickets`, `UX_Genders_Company_Name`); en la base son `messaging.tickets`, `ux_genders_company_name`. El SQL crudo va sin comillas; solo se citan palabras reservadas, en minúsculas (`sm."order"`).
2. **`HasFilter`:** SQL crudo en minúsculas y entre comillas dobles, booleanos con `TRUE`/`FALSE`: `"gcrecord" = 0`, `"isinitial" = TRUE AND "gcrecord" = 0`, `"code" IS NOT NULL AND "gcrecord" = 0`. Npgsql **no** agrega el filtro automático `IS NOT NULL` que EF pone en SQL Server a los índices únicos sobre columnas nulas: si hace falta, se escribe.
3. **`NULL` en índices únicos:** PostgreSQL trata dos `NULL` como distintos; SQL Server, como iguales. Cuando el comportamiento de `main` depende de que choquen, el índice usa `.AreNullsDistinct(false)` (`NULLS NOT DISTINCT`, PostgreSQL 15+).
4. **Tipos:** `nvarchar(n)` → `varchar(n)`; `nvarchar(max)` → `text`; `bit` → `boolean`; `uniqueidentifier` → `uuid`; `datetime2` → `timestamp with time zone` (lo que Npgsql usa para `DateTime`); `date` → `date` (`DateOnly`); `time` → `time` (`TimeOnly`).
5. **`DateTime` en UTC:** Npgsql rechaza escribir en `timestamp with time zone` un `DateTime` que no sea `Kind=Utc` (columnas y parámetros Dapper). Todo valor que venga de un request o de una librería se normaliza con `ToUniversalTime()` / `DateTime.SpecifyKind(..., DateTimeKind.Utc)` antes de guardarlo o de usarlo como parámetro.
6. **SQL Dapper:** sin corchetes; `COALESCE`; booleanos `= TRUE`/`= FALSE`; `CAST(... AS boolean)` o una expresión booleana directa (no `CASE ... THEN 1 ELSE 0`); `CONCAT(...)` o `||`; `NOW()`; paginación literal `LIMIT @PageSize OFFSET @Offset` (sin branch por proveedor); listas con `= ANY(@Ids)` (arreglo) o la expansión `IN @Ids` de Dapper; **búsquedas de texto con `ILIKE`** (conserva la búsqueda sin distinción de mayúsculas que da la colación de SQL Server; las búsquedas existentes las convierte SPEC 50 A).
7. **Migraciones:** cada `-post` genera su propia migración incremental contra Npgsql desde `src/4.Services.WebApi` (`dotnet ef migrations add <Nombre> --project ../3.Persistence --startup-project .`) e inspecciona el `Up()` (sin `nvarchar`/`datetime2`/`bit`/corchetes; filtros en minúsculas). Nunca se copian migraciones de `main`. Las migraciones de datos (`migrationBuilder.Sql`) se escriben en SQL de PostgreSQL (`gen_random_uuid()`, `NOW()`, nombres en minúsculas).
8. **Bloqueos:** `pg_advisory_xact_lock(...)` en lugar de `sp_getapplock`; `SELECT ... FOR UPDATE` en lugar de `WITH (UPDLOCK, HOLDLOCK)`.
9. **Violación de índice único:** `PostgresException` con `SqlState = 23505` (y `ConstraintName`); `GlobalExceptionHandler` ya la responde como 409 `DUPLICATE_KEY` (SPEC 39). Si un handler necesita reconocer la violación de un índice concreto, lo hace un servicio de Persistence/Infrastructure (Application no referencia Npgsql).
10. **Validación del SQL:** `EXPLAIN` de cada sentencia nueva contra la base migrada (técnica de SPEC 39).
11. **Tests:** los unitarios que afirman texto SQL afirman el SQL de PostgreSQL. Los de integración usan `PostgreSqlWebApplicationFactory` (`postgres:17`) y su clase termina en `PostgreSqlTests`, para entrar al filtro `FullyQualifiedName~PostgreSql` del job `integration-tests-postgres` (hasta que SPEC 50 E unifique las factories). Comando: `LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8 dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj --filter "FullyQualifiedName~PostgreSql"`.
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

### Orden de implementación en el fork

1. **40-post** → **41-post** (etapas 1–4) → **99-post** → **42-post** → **43-post** → **48-post** → **44-post** → **46-post**. Es el mismo orden en que se implementaron o se implementarán en `main`; 41-post necesita 40-post, y 42-post usa el bloqueo de borrado de 41-post.
2. **49-post** y **51-post**: sin dependencias con lo anterior, en cualquier momento.
3. **45-post** y **47-post**: `Pospuesto`, igual que en `main`.
4. **50**: pendientes para llevar QA a PostgreSQL (sin su antigua sección 0), cuando el usuario lo decida.

---

## Pendientes abiertos

Decisiones o verificaciones que no tienen spec propia todavía:

| Origen | Pendiente |
|---|---|
| 43 (A3) | Método para poblar `RoleSystemOptions` de una empresa nueva. Hoy solo `JOIN-001` tiene permisos sembrados; las demás se configuran a mano. |
| 43 (A6) | Asignación automática de módulos base a empresas nuevas y existentes. |
| 45 | `DynamicAuthorizationFilter` usa el claim `CompanyId` del JWT, no `X-Company-Id`: definir cómo opera un agente en varias empresas. |
| 45 | Proceso de limpieza de personas con pendiente `Discarded`. |
| 44 | Recordatorios y tareas automáticas (Hangfire): avisos, cierre de actividades vencidas, barridos de estados. |
| 46 | Acciones de sincronización con Google (OAuth, envío, webhook, worker). |
| 44 | Mejoras diferidas del calendario: invitados, calendarios privados, turnos nocturnos, permisos de medio día. |
| 41 | Al implementarla, incluir las entidades del calendario (44, 45, 46). |
| 38 (F0) | Ejecutar `docs/migrations/spec-38-data-audit.sql` contra la base de desarrollo y pegar los conteos en el PR; si hay filas afectadas, decidir si se reasignan. |
| 38 (D) | Smoke manual: un `SuperAdminCompany` solo ve su empresa y sus módulos, y no puede borrar empresas ni modificar módulos; validar con `join_frontb`. |
| ~~40-post~~ | ~~`DapperContext.CreateConnection()` siempre crea `SqlConnection`~~ Resuelto (2026-10-09, rama `fix-post-dapper-dateonly-and-dappercontext`): `DapperContext` elige Npgsql o SqlClient según `DatabaseProvider`, igual que `SqlConnectionFactory`, y un handler de Dapper (`DateOnlyCompatibleDateTimeHandler`) lee las columnas `date` (que Npgsql devuelve como `DateOnly`) en propiedades `DateTime`. Caso D.2 de SPEC 40-post ejecutado: cuatro `PUT /Persons` → 200. |
| 41-post | `GetPersonByIdQueryHandler` lee direcciones y contactos como `dynamic` y accede a `a.Id`, `a.AddressLine1`…; en PostgreSQL las columnas llegan en minúsculas y `GET /Persons/{id}` responde 500 si la persona tiene direcciones o contactos. Arreglo aparte (alias entre comillas o DTO tipado). |
| Proceso | Script de verificación de consistencia de specs (referencias inexistentes, nombres retirados, dependencias de specs pospuestas, specs fuera del índice). |
