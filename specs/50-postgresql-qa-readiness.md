# SPEC 50 — `main_postgresql`: pendientes para llevar la base de QA a PostgreSQL

> **Status:** Borrador
> **Depends on:** SPEC 39 (fork `main_postgresql`, `Implementado`). Rama de trabajo: `main_postgresql` (fork permanente, sin reintegración a `main`).
> **Date:** 2026-10-07
> **Objective:** Cerrar los pendientes que SPEC 39 dejó documentados para que `main_postgresql` pueda reemplazar a SQL Server en el entorno de QA: búsquedas sin distinción de mayúsculas, el tipo de `DeclaredDate`, la configuración y el aprovisionamiento de la base de QA, la paridad de la suite de integración contra PostgreSQL, y el procedimiento para portar al fork las specs que se implementen en `main`. Al terminar esta spec, QA pasa a PostgreSQL.

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
| Specs implementadas en `main` después del fork | SPEC 39 decidió "fork permanente, sin sincronización automática" | Cada spec que llegue a `main` (40–49 están en `Borrador`) necesita un port manual al fork; sin un procedimiento, se pierden `HasFilter`/SQL nuevos |

---

## Scope

**In:**

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

### F. Procedimiento para portar specs de `main` al fork

- Agregar a `specs/README.md` de `main_postgresql` una sección "Port al fork PostgreSQL" con la lista de verificación que se aplica cada vez que una spec se implementa en `main` y se trae a `main_postgresql`:
  1. `HasFilter` → comillas dobles, nombres en minúsculas y `TRUE`/`FALSE` (convención `ApplyLowerCaseNaming`, SPEC 39).
  2. `HasColumnType` → sin tipos exclusivos de SQL Server (`nvarchar(max)`, `datetime2`, `bit`, `uniqueidentifier`).
  3. SQL Dapper → sin corchetes, `ISNULL`, `TOP`, `+` entre strings, `OFFSET...FETCH`, `SYSUTCDATETIME`, `STRING_SPLIT`, `TRY_CAST`, hints `WITH (...)`; booleanos con `TRUE`/`FALSE`; búsquedas con `ILIKE`.
  4. Migraciones: no se copian las de `main`; se genera una migración incremental nueva en el fork contra Npgsql.
  5. Validación: `EXPLAIN` de cada sentencia nueva contra la base migrada (técnica de SPEC 39) + `dotnet test` de la suite de integración.
- Marcar en el índice qué specs de `main` ya están portadas (columna o nota).

**Out of scope:**

- Implementar en el fork las specs 40–49: cada una se porta cuando se implemente en `main`, con el procedimiento de F.
- El permiso `pull-requests: write` que falta en el `ci.yml` de **`main`** (mismo problema que corrigió SPEC 39 en el fork): pertenece a `main`, se resuelve allí.
- Índices únicos por nombre sin distinción de mayúsculas a nivel de base (ver Decisiones).
- Migración de datos desde la base SQL Server de QA, salvo que la Decisión pendiente 2 la incluya.

---

## Data model

Sin cambios de dominio.

- **Con la recomendación de la Decisión pendiente 1:** sin migración; `DeclaredDate` sigue como `timestamp`.
- **Si se elige `timestamp with time zone`:** migración incremental `DeclaredDateTimestampTz` en el fork (`ALTER COLUMN ... TYPE timestamp with time zone USING declareddate AT TIME ZONE 'UTC'`).

### Runbook de la base de QA (borrador, se completa en F4)

1. Crear el rol de aplicación y la base: `CREATE ROLE join_app LOGIN PASSWORD '<secreto>'; CREATE DATABASE join_db_qa OWNER join_app;`
2. Configurar el despliegue de QA: `DatabaseProvider=PostgreSQL`, `ConnectionStrings__DefaultConnection=Host=<host-qa>;Port=5432;Database=join_db_qa;Username=join_app;Password=<secreto>`.
3. Arrancar la API: migra (`InitialPostgres` + incrementales del fork) y siembra.
4. Verificar: `/health/ready` → 200; login con un usuario sembrado; `GET /api/v1/tickets` paginado.
5. Retirar del despliegue de QA la configuración de SQL Server.

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

1. Sección "Port al fork PostgreSQL" en `specs/README.md` (Scope F).
2. Registrar el estado de port de las specs 40–49 (todas: "pendiente — no implementada en `main`").

### F7 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. Tests unitarios (cultura `en-US`) con gate de cobertura ≥ 90 %.
3. Suite de integración completa contra PostgreSQL → 0 fallidos.
4. CI verde en `main_postgresql`.
5. QA: runbook ejecutado y smoke manual (login, búsqueda en minúsculas que encuentra un nombre con mayúscula, crear un perfil financiero con fecha UTC).

---

## Acceptance criteria

### F1 — Búsquedas
- [ ] Ningún archivo bajo `src/2.Application/UseCases` ni `src/3.Persistence/Repositories` contiene `LIKE` (solo `ILIKE`).
- [ ] Un listado filtrado por un texto en minúsculas devuelve registros cuyo nombre tiene mayúsculas (test de integración).

### F2 — `DeclaredDate`
- [ ] Crear y actualizar un perfil financiero con `DeclaredDate` en `Kind=Utc`, `Local` y `Unspecified` no lanza excepción.
- [ ] El test de integración con fecha ISO `...Z` responde 201.

### F3 — Configuración
- [ ] `appsettings.json` no contiene credenciales de ningún entorno.
- [ ] `.env.example` usa el formato de conexión de Npgsql e incluye `DatabaseProvider=PostgreSQL`.

### F4 — QA
- [ ] La API de QA arranca contra `join_db_qa` en PostgreSQL, migra y siembra (o restaura datos, según la Decisión 2).
- [ ] El runbook de esta spec refleja los pasos realmente ejecutados.

### F5 — Integración
- [ ] La suite de integración completa (26 + 27 + los nuevos de esta spec) pasa contra PostgreSQL, sin `Testcontainers.MsSql` en el proyecto.
- [ ] El job de CI corre la suite completa sin filtro.

### F6 — Port
- [ ] `specs/README.md` del fork contiene el procedimiento de port y el estado de port de las specs 40–49.

### General
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos; gate de cobertura ≥ 90 %; CI verde en `main_postgresql`.

---

## Decisiones pendientes (las resuelve el usuario antes de `Aprobado`)

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
| Specs de `main` implementadas mientras esta spec está en curso | Se portan con el procedimiento de F6 antes de ejecutar F4. |

## What is **not** in this spec

- El port de las specs 40–49 (se hace al implementarse cada una en `main`).
- Cambios en `main` (incluido su `ci.yml`).
- Migración de datos de SQL Server, salvo que la Decisión 2 la incluya.
- Cambios de dominio o de negocio.
