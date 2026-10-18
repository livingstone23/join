# SPEC 50 — `main_postgresql`: pendientes para llevar QA a PostgreSQL

> **Status:** Aprobado
> **Historia (2026-10-09):** vuelve a Borrador por cambio de alcance. El usuario decidió traer las specs de `main` al fork como **serie `-post`** (copias adaptadas a PostgreSQL de 40–49, 51 y 00, implementadas desde cero en el fork; ver `specs/README.md` → "Serie `-post`"). Se elimina la sección 0 (único `git merge main` + migración `Port_Specs_40_49`) con su fase F0 y sus criterios; la sección F pasa a verificar la convención de la serie. El resto (A–E) no cambia.
> **Ajuste por SPEC 50 (implementación, 2026-10-13):** se aplicó la Decisión 1A (`SpecifyKind(..., Unspecified)` en crear y actualizar; sin migración) y la 2A. La revisión de F1 encontró dos defectos del fork que ningún test cubría y que se corrigen aquí: (1) `IN @lista` no funciona con `NpgsqlConnection` (Dapper pasa la lista como arreglo y no la expande → error de sintaxis). Se cambiaron los 7 usos a `= ANY(@lista)`, incluido el `UPPER(r.Name) IN @RoleNames` que la sección A daba por correcto: `UserManagementReportQueryHelper`, `UserAdminRepository` (×3), `ReplaceUserRolesCommandHandler` y `BulkUpdateUserRolesCommandHandler`. Afectaba a los reportes de usuarios, a reemplazar roles y a los roles en lote. (2) Los filtros opcionales `@p IS NULL` con un valor nulo sin tipo daban 42P08: el reporte de usuarios y `GET /Audit/security` respondían 500 siempre. Ahora llevan `CAST(@p AS <tipo>)`. Ambos quedan cubiertos por `UserAdministrationPostgreSqlTests`, y la convención 6 del README se corrigió.

> **Depends on:** SPEC 39 (fork `main_postgresql`, `Implementado`). No depende de las `-post`; si alguna ya está implementada al arrancar, sus tests `*PostgreSqlTests` entran en la paridad de E. Rama de trabajo: `spec-50-postgresql-qa-readiness`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql` — nunca hacia `main`.
> **Date:** 2026-10-07
> **Objective:** Cerrar los pendientes que SPEC 39 dejó documentados, para que `main_postgresql` pueda reemplazar a SQL Server en el entorno de QA: búsquedas sin distinción de mayúsculas, el tipo de `DeclaredDate`, la configuración y el aprovisionamiento de la base de QA, y la paridad de la suite de integración contra PostgreSQL. Al terminar esta spec, QA pasa a PostgreSQL.

---

## Por qué existe esta spec

SPEC 39 dejó `main_postgresql` funcionando de punta a punta (migración `InitialPostgres`, seed, 27 smoke tests y CI en verde), pero su verificación fue local y con alcance *smoke*. Durante la implementación quedaron identificadas — y anotadas en la propia SPEC 39 como "Diferencias de comportamiento conocidas" y "Out of scope" — varias brechas que no rompen el arranque pero sí cambian el comportamiento que hoy ven los usuarios de QA sobre SQL Server:

| Pendiente | Evidencia | Efecto en QA si no se resuelve |
|---|---|---|
| `LIKE` distingue mayúsculas en PostgreSQL; en SQL Server (colación por defecto) no | 66 usos en 34 archivos Dapper, ninguno con `UPPER`/`LOWER` | Buscar "juan" deja de encontrar "Juan" en todos los listados paginados |
| `PersonFinancialProfile.DeclaredDate` quedó como `timestamp` (sin zona) | `Admin/PersonFinancialProfileConfiguration.cs:43`; el valor llega del request (`CreatePersonFinancialProfileCommand.DeclaredDate`, `UpdatePersonFinancialProfileCommand.DeclaredDate`) | Npgsql rechaza escribir un `DateTime` con `Kind=Utc` en `timestamp without time zone`: un JSON con fecha terminada en `Z` produce 500 al crear/editar el perfil financiero |
| Configuración de conexión | `appsettings.json` apunta a un Postgres local provisional (`localhost:5432`, `postgres/postgres`); `.env.example` sigue con el formato de SQL Server y no declara `DatabaseProvider` | No hay forma documentada de apuntar el despliegue de QA a su Postgres |
| Base de QA | QA corre hoy sobre SQL Server (`join_db_qa`) | No existe la base Postgres de QA ni una decisión sobre sus datos |
| Suite de integración existente (26 tests) | Corre contra `Testcontainers.MsSql`; en el fork no puede pasar y el paso de CI se quitó (SPEC 39, Scope H) | Solo los 27 smoke tests protegen el fork: login, MFA, sesiones, filtros globales y registro quedan sin cobertura de integración |
| Specs implementadas en `main` después del fork | ~~Plan del 2026-10-07: traerlas una sola vez en esta spec con un `git merge main`.~~ Reemplazado el 2026-10-09 por la serie `-post` (README) | Fuera de esta spec: cada `-post` se implementa en su propia rama desde `main_postgresql` |

---

## Scope

**In:**

### ~~0. Traer `main` al fork y convertir las specs 40–49~~

Eliminada el 2026-10-09 (ver Historia): lo reemplaza la serie `-post`.

### A. Búsquedas sin distinción de mayúsculas (`LIKE` → `ILIKE`)

- Reemplazar cada `LIKE` por `ILIKE` en el SQL Dapper de los 34 archivos (66 usos), listados exhaustivamente en el Implementation plan (F1). Reemplazo mecánico: los patrones (`'%' || ...`, parámetros `%valor%` armados en C#) no cambian.
- No se toca `UPPER(r.Name) IN @RoleNames` (`UserAdminRepository`, `UserManagementReportQueryHelper`): ya compara sin distinción de mayúsculas.
- No se toca `t.Code.StartsWith(prefix)` en `TicketCodeGenerator` (EF): los códigos de ticket los genera el sistema con un prefijo fijo, no los escribe el usuario.

### B. `DeclaredDate` y la zona horaria

- Corregir la escritura de `PersonFinancialProfile.DeclaredDate` para que no falle con fechas UTC. Ver Decisión pendiente 1 — la recomendación es normalizar el `Kind` en los handlers, sin migración.

### C. Configuración de conexión para QA

- `appsettings.json`: la cadena de conexión deja de traer credenciales y queda como valor de ejemplo vacío o local sin contraseña real; el despliegue de QA la recibe por variables de entorno (`ConnectionStrings__DefaultConnection`, `DatabaseProvider=PostgreSQL`), igual que hoy documenta `CLAUDE.md` para Docker.
- `.env.example`: formato Npgsql (`Host=...;Port=5432;Database=join_db_qa;Username=...;Password=...`) y nueva línea `DatabaseProvider=PostgreSQL`.
- `docker-compose.yml`: sin cambios — carga todas las variables desde `.env` (`env_file`).
- Ninguna credencial real de QA se versiona.

### D. Aprovisionamiento de la base de QA

- Base `join_db_qa` en PostgreSQL 17 (misma versión usada en desarrollo y en CI por SPEC 39), con un usuario de aplicación propietario de los esquemas `admin`, `common`, `messaging`, `security`, `support` y `public` (este último para `__EFMigrationsHistory`).
- Primer arranque de la API contra esa base: aplica `InitialPostgres` y corre `DatabaseSeeder` (comportamiento de `Program.cs`). Ver Decisión pendiente 2 sobre los datos actuales de QA.
- Runbook breve en esta spec (sección Data model) con los pasos de creación, variables y verificación.

### E. Paridad de la suite de integración contra PostgreSQL

- `CustomWebApplicationFactory` pasa a levantar PostgreSQL (`Testcontainers.PostgreSql`) en lugar de SQL Server; se elimina `Testcontainers.MsSql` y la espera de estabilidad específica de la imagen de SQL Server (`WaitUntilStablyQueryableAsync`).
- `PostgreSqlWebApplicationFactory` (SPEC 39) se fusiona en `CustomWebApplicationFactory` para no mantener dos factories equivalentes; `PostgreSqlSmokeTests` pasa a usar la factory unificada y mantiene el nombre (el filtro de CI `~PostgreSql` deja de ser necesario).
- `GlobalQueryFiltersGuardTests` cambia su `UseSqlServer(...)` por `UseNpgsql(...)` (solo construye el modelo, no abre conexión).
- Los 26 tests existentes (`RegisterEndpointTests`, `AccountSessionsRevokeTests`, `RoleUserSessionRepositoryIntegrationTests`, `MfaLoginChallengeTests`, `GlobalQueryFiltersIntegrationTests`, `GlobalQueryFiltersGuardTests`) deben pasar contra PostgreSQL; cualquier ajuste se limita a los tests salvo que revele un defecto real del fork, que se corrige y se anota.
- CI: el job `integration-tests-postgres` corre la suite completa (sin `--filter`).
- Los tests `*PostgreSqlTests` de las specs `-post` ya implementadas pasan a usar la factory unificada (mantienen el nombre).

### F. Procedimiento para traer specs de `main` al fork

- **Resuelto por la serie `-post` (2026-10-09):** `specs/README.md` del fork tiene la sección "Serie `-post`" con las reglas de la serie, las convenciones PostgreSQL (incluida la lista de verificación que esta sección pedía: `HasFilter`, tipos, SQL Dapper, migraciones, `EXPLAIN`, tests) y el orden de implementación, y el índice lista el estado de cada `-post`.
- Al cerrar esta spec solo se verifica que esa sección siga vigente y que el índice refleje el estado real de cada `-post`.

**Out of scope:**

- Traer specs de `main` al fork: lo hace la serie `-post` (README), no esta spec.
- El permiso `pull-requests: write` que falta en el `ci.yml` de **`main`** (mismo problema que corrigió SPEC 39 en el fork): pertenece a `main`, se resuelve allí.
- Índices únicos por nombre sin distinción de mayúsculas a nivel de base (ver Decisiones).
- Migración de datos desde la base SQL Server de QA, salvo que la Decisión pendiente 2 la incluya.

---

## Data model

Sin cambios de dominio.

- **Con la recomendación de la Decisión pendiente 1:** sin migración; `DeclaredDate` sigue como `timestamp`.
- **Si se elige `timestamp with time zone`:** migración incremental `DeclaredDateTimestampTz` en el fork (`ALTER COLUMN ... TYPE timestamp with time zone USING declareddate AT TIME ZONE 'UTC'`).

### Runbook de la base de QA

> **Aplicado:** Decisión 2A — base nueva sembrada por `DatabaseSeeder`; no se migran los datos actuales de QA sobre SQL Server.
>
> **Ensayo (2026-10-13):** se ejecutó paso a paso contra un PostgreSQL 17.11 desechable, con `join_app` sin privilegios de superusuario y la API en `ASPNETCORE_ENVIRONMENT=Staging`, sin `appsettings.Development.json` y con la conexión solo por variables de entorno:
> - Aplicó las 9 migraciones y sembró, y `join_app` quedó dueño de todos los esquemas.
> - `/health/ready` → 200, login → 200, `search=portal` → 1 ticket y el perfil financiero con `...Z` → 201.
> - Barrido de los 81 `GET` sin parámetros de ruta: ningún 500 tras las correcciones.
>
> Falta ejecutarlo en el servidor real de QA (F4.1); lo hace quien tenga acceso.

#### Valores base (no secretos)

| Concepto | Valor |
|---|---|
| Motor | PostgreSQL 17 (mismo que dev y CI, SPEC 39) |
| Base | `join_db_qa` |
| Rol de aplicación | `join_app`, sin superusuario. Como dueño de la base crea los esquemas `admin`, `calendar`, `common`, `messaging`, `security` y `support`, que quedan suyos. `public` (con `__EFMigrationsHistory`) es de `pg_database_owner`, es decir del dueño de la base |
| Cadena de conexión | `Host=<host-qa>;Port=5432;Database=join_db_qa;Username=join_app;Password=<secreto>;Timeout=60;` (vía variable de entorno `ConnectionStrings__DefaultConnection`) |
| Proveedor EF | `DatabaseProvider=PostgreSQL` |

#### Pasos (ejecutar contra el servidor de QA)

1. Crear el rol y la base:

   ```sql
   CREATE ROLE join_app LOGIN PASSWORD '<secreto>';
   CREATE DATABASE join_db_qa OWNER join_app;
   ```

2. Configurar el despliegue de QA con las variables de entorno anteriores (nunca commitear el secreto).
3. Arrancar la API contra `join_db_qa` (`ASPNETCORE_ENVIRONMENT` distinto de `Development`). Aplica `InitialPostgres` + las incrementales del fork (9 migraciones al 2026-10-13) y corre `DatabaseSeeder` (programado en `Program.cs`). Usuario sembrado para el login: `lcano@join.com` (SuperAdmin; cambiar su contraseña tras el primer ingreso).
4. Verificar:
   - `GET /health/ready` → `200`.
   - Login con un usuario sembrado (la identidad sembrada por `DatabaseSeeder`).
   - `GET /api/v1/tickets?search=portal` (header `X-Company-Id` de JOIN-001) devuelve el ticket sembrado "Portal login intermittent failure" (búsqueda sin distinción de mayúsculas, F1).
   - `POST /api/v1/personFinancialProfile` con `DeclaredDate` ISO terminado en `Z` → `201` (F2).
   - `GET /api/v1/Users/reports/system` y `GET /api/v1/Audit/security` → `200` (defectos corregidos por esta spec).
5. Retirar la configuración de SQL Server del despliegue de QA.

---

## Implementation plan

### F1 — `LIKE` → `ILIKE`

1. Reemplazar `LIKE` por `ILIKE` en el SQL de estos 34 archivos (conteo de usos entre paréntesis):
   - `Messaging/TicketCompanyDefaults/Queries/GetSystemWideTicketCompanyDefaults/GetSystemWideTicketCompanyDefaultsQueryHandler.cs` (8)
   - `Security/Queries/GetSystemWideUserReport/UserManagementReportQueryHelper.cs` (4)
   - `Security/RoleSystemOptions/Queries/RoleSystemOptionQuerySql.cs` (3), `Admin/Customers/Queries/GetCustomersPaged/GetCustomersPagedQueryHandler.cs` (3)
   - Con 2 usos: `GetSystemWideTimeUnits`, `GetSystemWideTicketUserCompanies`, `GetSystemWideTicketStatuses`, `GetSystemWideTickets`, `GetSystemWideTicketComplexities`, `GetTaxRegimes`, `GetIndustries`, `GetGenders`, `GetEntityStatus`, `GetCompanyModules`
   - Con 1 uso: `3.Persistence/Repositories/Security/RoleRepository.cs`, `GetSystemOptionsPaged`, `GetTimeUnits`, `GetTicketStatuses`, `GetTickets`, `GetTicketComplexities`, `GetSystemWideTicketAttachmentSettings`, `GetStreetTypesPaged`, `GetRegions`, `GetProvinces`, `GetMunicipalities`, `GetCountriesPaged`, `GetCompaniesPaged`, `GetCommunicationChannelsPaged`, `GetSystemModules`, `GetProjects`, `GetPersonsPaged`, `GetIncomeRanges`, `GetIdentificationTypes`, `GetAreas`
2. Actualizar los tests unitarios que afirman `LIKE` en el texto SQL.
3. `EXPLAIN` de las sentencias modificadas contra la base migrada → 0 errores.

### F2 — `DeclaredDate`

1. Aplicar la opción elegida en la Decisión pendiente 1.
2. Test unitario de los dos handlers (crear/actualizar) con `DeclaredDate` en `Kind=Utc`, `Local` y `Unspecified`.
3. Test de integración: crear un perfil financiero vía HTTP con una fecha ISO terminada en `Z` → 201.

### F3 — Configuración

1. `appsettings.json` y `.env.example` según Scope C.
2. Verificar que la API arranca leyendo la conexión solo de variables de entorno.

### F4 — Base de QA

1. Ejecutar el runbook contra el servidor de QA (lo ejecuta quien tenga acceso; no se versionan credenciales).
2. Completar el runbook con los valores reales no secretos (host, versión, nombre de rol).
3. Aplicar la Decisión pendiente 2 sobre los datos.

### F5 — Paridad de integración

1. Unificar factories (Scope E) y migrar `GlobalQueryFiltersGuardTests`.
2. `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj` (sin filtro) → 0 fallidos.
3. CI: quitar el `--filter` del job `integration-tests-postgres`; quitar `Testcontainers.MsSql` del `.csproj`.

### F6 — Procedimiento de port

1. Verificar la sección "Serie `-post`" de `specs/README.md` y el estado de cada `-post` en el índice (Scope F).

### F7 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. Tests unitarios (cultura `en-US`) con gate de cobertura ≥ 90 %.
3. Suite de integración completa contra PostgreSQL → 0 fallidos.
4. CI verde en `main_postgresql`.
5. QA: runbook ejecutado y smoke manual (login, búsqueda en minúsculas que encuentra un nombre con mayúscula, crear un perfil financiero con fecha UTC).

---

## Acceptance criteria

### F1 — Búsquedas
- [x] Ningún archivo bajo `src/2.Application/UseCases` ni `src/3.Persistence/Repositories` contiene `LIKE` (solo `ILIKE`).
- [x] Un listado filtrado por un texto en minúsculas devuelve registros cuyo nombre tiene mayúsculas (test de integración).

### F2 — `DeclaredDate`
- [x] Crear y actualizar un perfil financiero con `DeclaredDate` en `Kind=Utc`, `Local` y `Unspecified` no lanza excepción.
- [x] El test de integración con fecha ISO `...Z` responde 201.

### F3 — Configuración
- [x] `appsettings.json` no contiene credenciales de ningún entorno.
- [x] `.env.example` usa el formato de conexión de Npgsql e incluye `DatabaseProvider=PostgreSQL`.

### F4 — QA
- [ ] La API de QA arranca contra `join_db_qa` en PostgreSQL, migra y siembra (o restaura datos, según la Decisión 2).
- [ ] El runbook de esta spec refleja los pasos realmente ejecutados.

### F5 — Integración
- [x] La suite de integración completa (26 + 27 + los nuevos de esta spec) pasa contra PostgreSQL, sin `Testcontainers.MsSql` en el proyecto.
- [x] El job de CI corre la suite completa sin filtro.

### F6 — Port
- [x] `specs/README.md` del fork mantiene la sección "Serie `-post`" y el índice refleja el estado de cada `-post`.

### General
- [x] `dotnet build -c Release` → 0 errores, 0 warnings nuevos; gate de cobertura ≥ 90 %.
- [ ] CI verde en `main_postgresql` (se confirma al hacer push/PR).

---

## Decisiones (resueltas: 1A y 2A)

1. **`DeclaredDate`:**
   - **A (recomendada).** Normalizar en los handlers de crear/actualizar: `DateTime.SpecifyKind(request.DeclaredDate, DateTimeKind.Unspecified)`. Sin migración; conserva la semántica de `datetime2` de SQL Server (fecha y hora sin zona).
   - **B.** Cambiar la columna a `timestamp with time zone`, como el resto de las fechas del modelo, con migración incremental; los handlers convierten a UTC (`ToUniversalTime()` para `Local`, `SpecifyKind(..., Utc)` para `Unspecified`).
   - **C.** Cambiar la columna a `date` (como `PersonEmployment`/`PersonBusinessProfile`): pierde la hora.
2. **Datos de QA:**
   - **A (recomendada).** Base nueva sembrada por `DatabaseSeeder`; los datos actuales de QA en SQL Server no se migran (coherente con SPEC 39: "Esta spec parte de una base vacía").
   - **B.** Migrar los datos actuales de `join_db_qa` (SQL Server) a PostgreSQL. Requiere herramienta (p. ej. `pgloader`) y su propio plan de validación; si se elige, conviene sacarlo a una spec aparte.

## Decisions taken and discarded

- **`ILIKE` en vez de `UPPER(col) LIKE UPPER(@p)` o colación `citext`** (elegido). Es el reemplazo 1:1 más simple en un fork que solo habla PostgreSQL; `citext` obligaría a cambiar tipos de columna y regenerar migraciones. Contrapartida aceptada: `ILIKE` con `%valor%` no usa índices B-tree — igual que el `LIKE '%valor%'` actual.
- **Unicidad por nombre sin distinción de mayúsculas a nivel de base: no se agrega** (descartado por ahora). Los índices `ux_ticketcomplexities_company_name` y `ux_timeunits_company_name` distinguen mayúsculas en PostgreSQL, pero los handlers ya comparan con `StringComparison.OrdinalIgnoreCase` antes de insertar (p. ej. `CreateTicketStatusCommandHandler`), así que el comportamiento visible no cambia. Un índice por expresión `lower(name)` exigiría SQL crudo en la migración; queda para cuando haga falta.
- **Una sola factory de integración** (elegido). Mantener `CustomWebApplicationFactory` (SQL Server) y `PostgreSqlWebApplicationFactory` en un fork que nunca corre SQL Server es duplicación sin beneficio.

## Identified risks

| Riesgo | Mitigación |
|---|---|
| Algún test de integración existente revele un defecto real del fork no cubierto por los smoke tests de SPEC 39 | Es el objetivo de F5: se corrige en esta spec y se anota como "Ajuste por SPEC 50". |
| `ILIKE` cambia resultados que algún usuario de QA esperaba sensibles a mayúsculas | Ningún filtro actual lo es en SQL Server; `ILIKE` restaura el comportamiento previo. |
| Credenciales de QA filtradas al repo durante F3/F4 | Solo variables de entorno; revisión del diff antes de cada commit. |
| Una `-post` implementada mientras esta spec está en curso cambia SQL que F1 convierte a `ILIKE` | Las `-post` ya escriben sus búsquedas con `ILIKE` (convención 6 del README); F1 se aplica sobre el estado de la rama al momento de implementarla. |
| Una PR de esta rama dirigida a `main` llevaría todo PostgreSQL a `main` | La rama sale de `main_postgresql` y su PR apunta a `main_postgresql`; verificar la base de la PR antes de crearla. |

## What is **not** in this spec

- Traer specs de `main` al fork (serie `-post`).
- Cambios en `main` (incluido su `ci.yml`).
- Migración de datos de SQL Server, salvo que la Decisión 2 la incluya.
- Cambios de dominio o de negocio.
