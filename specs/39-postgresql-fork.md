# SPEC 39 — Fork `main_postgresql`: portar persistencia, queries Dapper y seed a PostgreSQL

> **Status:** Borrador
> **Depends on:** Ninguna dependencia dura de código. SPEC 38 (consolidación de query filters) no necesita portarse — su mecanismo (`Expression`/reflexión sobre `IAuditableEntity`/`BaseTenantEntity`) es 100% agnóstico de proveedor y funciona igual una vez fusionado a `main`; solo las declaraciones puntuales de `HasFilter`/`HasColumnType` en las clases `IEntityTypeConfiguration<T>` (fuera del alcance de SPEC 38) necesitan ajuste, y son objeto de esta spec.
> **Date:** 2026-09-28
> **Objective:** Crear la rama `main_postgresql` como **fork permanente** (sin plan de reintegración a `main`) donde el sistema corre íntegramente contra PostgreSQL: registro del `DbContext` (`UseNpgsql` en vez de `UseSqlServer`), un historial de migraciones EF Core propio y generado desde cero para esa rama, las 9 declaraciones `HasFilter`/4 `HasColumnType` con sintaxis exclusiva de SQL Server corregidas, los ~29 archivos de SQL crudo (Dapper) con sintaxis no portable corregidos, el seed verificado end-to-end, una suite de tests de integración *smoke* (no paridad completa) contra `Testcontainers.PostgreSql`, y un job de CI que corre exclusivamente en esa rama.

---

## Por qué existe esta spec

Surgió al confirmar, durante la revisión de SPEC 38, una asimetría real en el proyecto: `CLAUDE.md` describe el sistema como cross-DB-portable ("usar `CONCAT()` no funciones de fecha del vendor... branch pagination clauses on `LIMIT/OFFSET` (Postgres) vs `OFFSET...FETCH NEXT` (SQL Server)"), y el lado **Dapper** (Queries) efectivamente lo es — `SqlConnectionFactory` (`src/3.Infrastructure/Persistence/SqlConnectionFactory.cs`) ya elige entre `SqlConnection`/`NpgsqlConnection` según `DatabaseProvider`, y 28 de 29 handlers paginados ya branchean `LIMIT/OFFSET` vs `OFFSET...FETCH`. Pero el lado **EF Core** (Commands + migraciones + configuración de entidades) nunca lo hizo: `AddPersistenceServices` (`src/3.Persistence/Configuration/ConfigureServices.cs:77`) llama `options.UseSqlServer(...)` sin ninguna rama — `DatabaseProvider` solo se lee para health checks, un registro completamente aparte. El propio código deja una pista de que esto ya se había pensado y nunca se terminó: `UserCompanyConfiguration.cs` tiene, comentado, exactamente el filtro que Postgres necesitaría:

```csharp
// PostgreSQL note:
// Use the following filter instead when generating provider-specific migrations:
// .HasFilter("\"IsDefault\" = TRUE AND \"GcRecord\" = 0");
```

Esta spec parte de una decisión explícita del usuario: **no** se persigue soporte dual-proveedor mantenido indefinidamente en `main`. `main_postgresql` es un **fork permanente** — diverge de `main` para servir un despliegue que solo habla PostgreSQL, sin intención de volver a fusionarse. Esto simplifica el trabajo real de forma importante: en vez de mantener ramas condicionales (`if (Database.IsNpgsql()) ... else ...`) que sobrevivan para siempre en el código, cada punto de fricción se **reemplaza** directamente por su equivalente PostgreSQL, sin dejar la rama SQL Server como alternativa viva dentro del mismo archivo.

### Auditoría exhaustiva (no muestreo) — el tamaño real del trabajo

Antes de diseñar nada se grepeó `src/` completo contra los patrones no portables conocidos. Resultado, con archivos exactos:

| Categoría | Alcance | Cantidad | Complejidad del fix |
|---|---|---|---|
| `ApplicationDbContext` — registro del proveedor | `ConfigureServices.cs` | 1 línea | Reemplazar `UseSqlServer` por `UseNpgsql` |
| Migraciones EF Core | `src/3.Persistence/Migrations/` | Todo el historial actual (SQL Server) | Generar un historial nuevo desde cero para esta rama (ver Data model) |
| `HasFilter(...)` con sintaxis `[Columna] = 1` | 6 archivos de configuración, 9 llamadas | 9 | Sintaxis Postgres ya insinuada en el código (`"Columna" = TRUE`) |
| `HasColumnType(...)` con nombres de tipo exclusivos de SQL Server (`nvarchar(max)`, `datetime2`) | 2 archivos (`SecurityEventLogConfiguration`, `AuditLogConfiguration`, `PersonFinancialProfileConfiguration`) | 5 llamadas | Reemplazo directo (`text`, `timestamp`) |
| Paginación Dapper sin branch `LIMIT/OFFSET` | `RoleSystemOptionQuerySql.cs` (helper compartido, usado por varios handlers) | 1 archivo | Aplicar el patrón ya establecido (`GetTicketsQueryHandler.GetPaginationClause`) |
| Identificadores entre corchetes `[Schema].[Tabla]` (inválido en Postgres) | 18 archivos, mayormente módulo Security + `AuditLogRepository` | 18 | Quitar los corchetes (sintaxis sin comillas, válida en ambos motores) |
| `ISNULL(` (exclusivo T-SQL) | 5 archivos | 5 | Reemplazo directo por `COALESCE(` — funciona en ambos motores, cero branching |
| `TOP (n)` (exclusivo T-SQL, sin equivalente por posición) | 3 archivos, todos en `3.Persistence/Repositories/Security` | 3 | Reescribir a `LIMIT n` (Postgres) — estos 3 repos ya son proveedor-fijo (SPEC 30 los diseñó solo para SQL Server), pasan a ser Postgres-only en esta rama |
| Concatenación con `+` entre strings (exclusivo T-SQL) | `RoleRepository.cs`, `AuditLogRepository.cs` | 2 | Reemplazo por `CONCAT(...)` — funciona en ambos motores, mismo criterio que ya exige `CLAUDE.md` |
| `CAST(... AS NVARCHAR(n))` | `GetMySessionsQueryHandler.cs` | 1 | Reemplazo directo por `VARCHAR(n)` |
| `ExecuteSqlRaw`/`FromSqlRaw` en el seed | `DatabaseSeeder.cs` | 0 | Sin cambios de código — el seed es 100% `DbSet.Add()`/LINQ; **sí** requiere verificación end-to-end (ver Scope E) |
| `ROW_NUMBER()`/`OUTER APPLY`/`OUTPUT INSERTED` | Todo `src/` | 0 | N/A |

**Total: ~35 archivos con un cambio puntual y verificable, más el registro del `DbContext` y la regeneración de migraciones.** No es una reescritura del sistema — es una lista de fixes acotados, cada uno con su archivo y su línea ya identificados.

---

## Scope

**In:**

### A. Rama y flujo de trabajo

- Crear `main_postgresql` desde el `main` actual. Fork permanente — sin PRs de vuelta a `main`. Todo el trabajo de esta spec ocurre en esa rama (commits directos o PRs internos hacia `main_postgresql`, a criterio del equipo).
- `appsettings.json`/`appsettings.Development.json` en esta rama: `"DatabaseProvider": "PostgreSQL"` como único valor válido (no hace falta preservar la rama `"SqlServer"` del switch de `AddPersistenceHealthChecks`/`SqlConnectionFactory` — ver Decisiones sobre si se colapsa o se deja).

### B. `ApplicationDbContext` — registro del proveedor

- `src/3.Persistence/Configuration/ConfigureServices.cs`: `options.UseSqlServer(connectionString, builder => builder.MigrationsAssembly(...).CommandTimeout(30))` → `options.UseNpgsql(connectionString, builder => builder.MigrationsAssembly(...).CommandTimeout(30))`.
- `MigrationsAssembly` sigue apuntando al mismo ensamblado (`3.Persistence`) — lo que cambia es el **contenido** de `Migrations/`, no el proyecto que las contiene.

### C. Migraciones EF Core — historial nuevo desde cero

- El historial actual (`src/3.Persistence/Migrations/*.cs`) fue generado íntegramente contra el proveedor SQL Server — cada migración usa `MigrationBuilder` con el mapeo de tipos de ese proveedor (`nvarchar`, `uniqueidentifier`, `bit`, etc.) y no es reproducible tal cual contra Postgres.
- En `main_postgresql`: eliminar `src/3.Persistence/Migrations/*.cs` (incluidos `*.Designer.cs` y el `ApplicationDbContextModelSnapshot.cs`), y generar una única migración inicial (`InitialPostgres`) desde el modelo ya corregido (ver Scope D), con `UseNpgsql` activo.
- Ningún dato productivo depende de esta rama todavía — es seguro partir de cero. Si en el futuro `main_postgresql` necesita levantar una base con datos ya migrados desde SQL Server, eso es un proyecto de migración de datos aparte (fuera de esta spec).

### D. Configuración EF — `HasFilter`/`HasColumnType`

Reemplazo directo, archivo por archivo (no hace falta branching por proveedor porque la rama es Postgres-only):

| Archivo | Antes (SQL Server) | Después (PostgreSQL) |
|---|---|---|
| `Security/UserCompanyConfiguration.cs:44` | `.HasFilter("[IsDefault] = 1 AND [GcRecord] = 0")` | `.HasFilter("\"IsDefault\" = TRUE AND \"GcRecord\" = 0")` *(ya insinuado en el comentario existente del archivo)* |
| `Security/RoleCompanyConfiguration.cs:32` | `.HasFilter("[GcRecord] = 0")` | `.HasFilter("\"GcRecord\" = 0")` |
| `Admin/UserCommunicationChannelConfiguration.cs:72` | `.HasFilter("[GcRecord] = 0")` | `.HasFilter("\"GcRecord\" = 0")` |
| `Messaging/TicketStatusConfiguration.cs:71` | `.HasFilter("[IsInitial] = 1 AND [GcRecord] = 0")` | `.HasFilter("\"IsInitial\" = TRUE AND \"GcRecord\" = 0")` |
| `Messaging/TicketStatusConfiguration.cs:76` | `.HasFilter("[IsPaused] = 1 AND [GcRecord] = 0")` | `.HasFilter("\"IsPaused\" = TRUE AND \"GcRecord\" = 0")` |
| `Messaging/TicketStatusConfiguration.cs:81` | `.HasFilter("[IsFinal] = 1 AND [GcRecord] = 0")` | `.HasFilter("\"IsFinal\" = TRUE AND \"GcRecord\" = 0")` |
| `Messaging/TicketComplexityConfiguration.cs:64` | `.HasFilter("[GcRecord] = 0")` | `.HasFilter("\"GcRecord\" = 0")` |
| `Messaging/TimeUnitConfiguration.cs:65` | `.HasFilter("[GcRecord] = 0")` | `.HasFilter("\"GcRecord\" = 0")` |

`RoleCompanyConfiguration`/`UserCommunicationChannelConfiguration`/`TicketComplexityConfiguration`/`TimeUnitConfiguration` comparten el mismo patrón simple (`[GcRecord] = 0` → `"GcRecord" = 0`); `UserCompanyConfiguration`/`TicketStatusConfiguration` combinan una columna booleana con `GcRecord`, y requieren `TRUE` en vez de `1` porque Postgres no compara implícitamente un `boolean` con un entero.

`HasColumnType`:

| Archivo | Antes | Después | Nota |
|---|---|---|---|
| `Security/SecurityEventLogConfiguration.cs:42` | `"nvarchar(max)"` | `"text"` | Equivalente directo, sin límite de longitud en ambos |
| `Audit/AuditLogConfiguration.cs:52,55,58` | `"nvarchar(max)"` (×3) | `"text"` (×3) | Idem |
| `Admin/PersonFinancialProfileConfiguration.cs:43` | `"datetime2"` | `"timestamp"` | `datetime2` no existe en Postgres |
| `Admin/PersonEmploymentConfiguration.cs:50,54` / `Admin/PersonBusinessProfileConfiguration.cs:43` | `"date"` | `"date"` (sin cambios) | Nombre de tipo válido en ambos motores |
| `Messaging/TicketConfiguration.cs:40` | `"decimal(5,1)"` | `"decimal(5,1)"` (sin cambios) | Sintaxis válida en ambos motores |

### E. Queries Dapper y repositorios

Cinco categorías de fix, cada una mecánica y sin ambigüedad:

1. **Paginación** (`RoleSystemOptionQuerySql.cs`): reemplazar el `OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY` fijo por `LIMIT @PageSize OFFSET @Offset` — como esta rama es Postgres-only, **no** hace falta el branch `connection.GetType().Name.Contains("Npgsql")` que sí necesita `main` (que sirve ambos); se reemplaza directo, sin condicional. Mismo criterio en los 28 handlers que sí ya branchean: la rama SQL Server de cada `if`/`switch` se elimina, dejando solo la cláusula Postgres — simplifica cada uno de esos 28 archivos en vez de mantener el branching muerto.
2. **Identificadores entre corchetes** (18 archivos, ver lista completa en Implementation plan F4): quitar `[` `]` de cada referencia `[Schema].[Tabla]`/`[Columna]`. Sin comillas, un identificador como `Security.Users` es válido tal cual en ambos motores (Postgres pliega a minúsculas de forma consistente si nadie lo cita entre comillas, que es exactamente esta situación).
3. **`ISNULL(` → `COALESCE(`** (5 archivos): reemplazo de texto directo, funciona igual en ambos motores — dejar `COALESCE` aunque `main` (SQL Server) también lo soporte no cuesta nada y de hecho *mejora* la portabilidad de `main` sin que esta spec necesite tocarlo (es un cambio que, si se quisiera, podría cherry-pickearse a `main` aparte).
4. **`TOP (n)` → `LIMIT n`** (3 archivos en `3.Persistence/Repositories/Security`, ver F4): a diferencia de `ISNULL`, esto sí requiere reescribir la forma de la query (`LIMIT` va al final, no después de `SELECT`), no un simple find-replace.
5. **Concatenación `+` → `CONCAT(...)`** (`RoleRepository.cs`, `AuditLogRepository.cs`): `'%' + @nameFilter + '%'` → `CONCAT('%', @nameFilter, '%')`; `u.FirstName + ' ' + u.LastName` → `CONCAT(u.FirstName, ' ', u.LastName)` — mismo patrón que ya usa el resto del código citado en `CLAUDE.md`.
6. **`CAST(... AS NVARCHAR(n))` → `CAST(... AS VARCHAR(n))`** (`GetMySessionsQueryHandler.cs`): reemplazo directo.

### F. Seed

- `DatabaseSeeder.cs` no tiene SQL crudo (`ExecuteSqlRaw`/`FromSqlRaw`) — confirmado por grep exhaustivo. Es 100% `DbContext`/`DbSet.Add()`/LINQ, por lo tanto agnóstico de proveedor por construcción.
- **No requiere cambios de código.** Sí requiere una corrida completa end-to-end contra Postgres real como parte de la verificación (F7) — valores por defecto de `Guid.NewGuid()`, precisión `decimal`, y el comportamiento de `IDENTITY`/autogeneración no deberían diferir, pero no se asume sin probarlo.

### G. Tests

- Agregar `Testcontainers.PostgreSql` a `tests/IntegrationTests/JOIN.IntegrationTests.csproj` (junto a `Testcontainers.MsSql`, que en esta rama queda en desuso pero no estorba dejarlo — ver Decisiones).
- `CustomWebApplicationFactory` gana una variante (o un flag) que levanta un contenedor Postgres en vez de SQL Server.
- **Alcance de tests: suite *smoke*, no paridad completa** (decisión explícita del usuario — la paridad completa queda para una spec de seguimiento si esta rama se vuelve prioritaria). `tests/IntegrationTests/Persistence/PostgreSqlSmokeTests.cs` (nuevo), casos mínimos:
  1. La migración `InitialPostgres` aplica limpio sobre un Postgres vacío.
  2. El seed completo (`DatabaseSeeder`) corre sin error contra esa base.
  3. Cada uno de los 9 índices filtrados corregidos en Scope D se comporta como se espera: insertar dos filas que violarían la unicidad si el filtro no aplicara correctamente (ej. dos `TicketStatus.IsInitial = true` para la misma empresa) debe fallar; una fila soft-deleted no bloquea la resurrección de la combinación.
  4. Un flujo CRUD + paginado end-to-end sobre `Tickets` (crear, listar paginado, filtrar) — reutiliza el módulo ya auditado en profundidad en esta conversación como caso representativo.
  5. Un flujo de sesión/login básico (`RoleUserSessionRepository`, ya que sus 3 archivos con `TOP (n)` quedan reescritos en esta rama) — confirma que el `LIMIT` reemplazando `TOP` funciona.

### H. CI

- `.github/workflows/ci.yml`: agregar `main_postgresql` a `on.push.branches`/`on.pull_request.branches`.
- Nuevo job (no matrix — esta rama es de un solo proveedor, no tiene sentido una matriz `[SqlServer, PostgreSql]` cuando `main_postgresql` nunca corre contra SQL Server): `integration-tests-postgres`, corre `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj --filter "FullyQualifiedName~PostgreSql"` contra el `PostgreSqlSmokeTests` de Scope G, usando Testcontainers (no necesita un servicio Postgres declarado aparte en el workflow — Testcontainers lo levanta por sí solo, igual que ya hace con SQL Server hoy).
- El job de cobertura de `JOIN.Application.UnitTest` (90%) se mantiene sin cambios — no depende de ningún proveedor de base de datos real.

**Out of scope (para specs futuras, si esta rama se vuelve prioritaria):**

- **Paridad completa de tests de integración** (re-ejecutar toda la suite existente contra Postgres, parametrizada por proveedor). Decisión explícita: empezar con el *smoke* de Scope G.
- **Migración de datos** de una base SQL Server productiva a Postgres. Esta spec parte de una base vacía.
- **Mantener `main` y `main_postgresql` sincronizadas** con algún proceso automatizado (cherry-pick, rebase periódico). Al ser un fork permanente, la divergencia es aceptada; si en el futuro se necesita traer cambios de `main`, es un proceso manual caso por caso.
- **Colapsar el switch `DatabaseProvider` de `SqlConnectionFactory`/health checks a un único valor fijo.** Se deja como está (soporta ambos valores, esta rama solo usa `"PostgreSQL"`) — ver Decisiones.

---

## Data model

Sin cambios de dominio. El "modelo" de esta spec es el conjunto de archivos de configuración/queries listados en Scope C-E, más la migración `InitialPostgres` generada por herramienta (no escrita a mano).

### `IDesignTimeDbContextFactory` — necesario para `dotnet ef` en esta rama

Como `main_postgresql` fija el proveedor directamente en `UseNpgsql(...)` dentro de `AddPersistenceServices`, `dotnet ef migrations add`/`database update` (ejecutados desde `src/4.Services.WebApi`, según `CLAUDE.md`) ya resuelven el proveedor correcto sin necesidad de una fábrica de diseño adicional — a diferencia de un escenario dual-proveedor (que sí la necesitaría para elegir en tiempo de diseño). No se agrega `IDesignTimeDbContextFactory` en esta spec; si `dotnet ef` no logra resolver el `DbContext` vía DI estándar del `WebApi` (poco probable, es el patrón que ya usa `main` hoy), se agrega como ajuste puntual durante F3.

---

## Implementation plan

### F1 — Crear la rama

1. `git checkout main && git pull && git checkout -b main_postgresql`.
2. `appsettings.json`/`appsettings.Development.json`: `"DatabaseProvider": "PostgreSQL"`, `ConnectionStrings:DefaultConnection` apuntando a una instancia Postgres local/dev.

### F2 — `ApplicationDbContext` y migraciones

1. `ConfigureServices.cs`: `UseSqlServer` → `UseNpgsql` (Scope B).
2. Eliminar `src/3.Persistence/Migrations/*.cs` (todo el historial SQL Server) y `ApplicationDbContextModelSnapshot.cs`.
3. Aplicar todos los fixes de Scope D (`HasFilter`/`HasColumnType`) **antes** de generar la migración — si se genera primero y se corrige después, hay que regenerar.
4. Desde `src/4.Services.WebApi`: `dotnet ef migrations add InitialPostgres --project ../3.Persistence --startup-project .`.
5. Inspeccionar el `Up()` generado: confirmar que los 9 índices filtrados usan comillas dobles/`TRUE` (no corchetes/`1`), que no aparece `nvarchar`/`datetime2` en ninguna columna, y que los tipos `uniqueidentifier`/`bit` de SQL Server se tradujeron a `uuid`/`boolean` (comportamiento automático del proveotor Npgsql, no requiere intervención).
6. `dotnet ef database update --project ../3.Persistence --startup-project .` contra una instancia Postgres local → aplica limpio.

### F3 — Configuración EF (`HasFilter`/`HasColumnType`)

1. Aplicar los 9 reemplazos de `HasFilter` y los 5 de `HasColumnType` de la tabla en Scope D.
2. `dotnet build -c Release` → 0 errores.
3. Si F2.4 ya se corrió antes de este paso, repetir F2.4-F2.6 con los fixes aplicados (ver nota de orden en F2.3).

### F4 — Queries Dapper y repositorios

Lista exhaustiva de archivos a tocar, agrupada por fix (todos confirmados por grep, no estimados):

**Corchetes `[Schema].[Tabla]` → sin corchetes** (18 archivos):
`Security/UserCompanies/Commands/RemoveUserCompany/RemoveUserCompanyCommandHandler.cs`, `Security/UserCompanies/Commands/AddUserCompany/AddUserCompanyCommandHandler.cs`, `Security/Users/Commands/InviteUser/InviteUserCommandHandler.cs`, `Security/Users/Queries/GetUsersWithRoles/GetUsersWithRolesQueryHandler.cs`, `Security/Users/Commands/ReplaceUserRoles/ReplaceUserRolesCommandHandler.cs`, `Security/Users/Commands/BulkUpdateUserRoles/BulkUpdateUserRolesCommandHandler.cs`, `Security/SystemOptions/Queries/GetSystemOptionsPaged/GetSystemOptionsPagedQueryHandler.cs`, `Security/SystemOptions/Queries/GetSystemOptionById/GetSystemOptionByIdQueryHandler.cs`, y en `3.Persistence/Repositories/Security/`: `SecurityEventRepository.cs`, `RoleCompanyRepository.cs`, `UserAdminRepository.cs`, `RoleRepository.cs`, `EmailOtpEnableCodeRepository.cs`, `PhoneVerificationCodeRepository.cs`, `RoleUserSessionRepository.cs`, `UserMfaRecoveryCodeRepository.cs`, `MfaLoginChallengeRepository.cs`, `RoleSystemOptionsRepository.cs`, más `3.Persistence/Repositories/Audit/AuditLogRepository.cs`.

**`ISNULL(` → `COALESCE(`** (5 archivos): `Admin/Customers/Queries/CustomerQuerySql.cs`, `Admin/Customers/Queries/GetCustomersPaged/GetCustomersPagedQueryHandler.cs`, `3.Persistence/Repositories/Security/UserAdminRepository.cs`, `3.Persistence/Repositories/Security/RoleUserSessionRepository.cs`, `3.Persistence/Repositories/Security/RoleSystemOptionsRepository.cs`.

**`TOP (n)` → `LIMIT n`** (3 archivos, requiere reescribir la forma de la query, no solo texto): `3.Persistence/Repositories/Security/PhoneVerificationCodeRepository.cs`, `3.Persistence/Repositories/Security/EmailOtpEnableCodeRepository.cs`, `3.Persistence/Repositories/Security/RoleUserSessionRepository.cs`.

**Concatenación `+` → `CONCAT(...)`** (2 archivos): `3.Persistence/Repositories/Security/RoleRepository.cs:65` (`'%' + @nameFilter + '%'`), `3.Persistence/Repositories/Audit/AuditLogRepository.cs:130` (`u.FirstName + ' ' + u.LastName`).

**`CAST(... AS NVARCHAR(n))` → `CAST(... AS VARCHAR(n))`** (1 archivo): `Security/Account/Queries/GetMySessions/GetMySessionsQueryHandler.cs:41`.

**Paginación sin branch** (1 archivo): `Security/RoleSystemOptions/Queries/RoleSystemOptionQuerySql.cs`.

**Los 28 handlers que ya branchean `LIMIT/OFFSET` vs `OFFSET...FETCH`**: eliminar la rama SQL Server de cada uno (dejar solo `LIMIT @PageSize OFFSET @Offset`), ya que esta rama nunca evalúa esa condición. Localizarlos con `grep -rl "GetPaginationClause\|Npgsql" src/2.Application/UseCases --include="*.cs"`.

Tras cada grupo: `dotnet build -c Release` → 0 errores (el build no detecta errores de SQL crudo, así que esto solo confirma que no se rompió C#; la validación real de que el SQL es correcto ocurre en F7).

### F5 — Seed

1. Sin cambios de código (Scope F).
2. Levantar la API contra la base Postgres migrada (F2) y confirmar que `DatabaseSeeder` corre sin excepciones — usuarios, roles, catálogos, empresas de desarrollo, y el roster de tickets si SPEC 34 ya está implementada para cuando se llegue a este punto.

### F6 — Tests

1. Agregar `Testcontainers.PostgreSql` a `tests/IntegrationTests/JOIN.IntegrationTests.csproj`.
2. Adaptar `CustomWebApplicationFactory` (o crear una variante) para levantar el contenedor Postgres.
3. Crear `tests/IntegrationTests/Persistence/PostgreSqlSmokeTests.cs` con los 5 casos de Scope G.
4. `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj --filter "FullyQualifiedName~PostgreSql"` → 0 fallidos.
5. Confirmar que la suite de integración **existente** (la que hoy corre contra SQL Server vía `Testcontainers.MsSql`) sigue compilando en esta rama aunque no se ejecute — si algún test existente referencia sintaxis SQL Server directamente (poco probable, ya auditado en SPEC 38 que ninguno lo hace para las entidades relevantes), ajustar o marcar `[Trait("Provider", "SqlServer")]`/excluir del filtro por ahora.

### F7 — CI

1. Agregar `main_postgresql` a `on.push.branches`/`on.pull_request.branches` en `.github/workflows/ci.yml`.
2. Nuevo job `integration-tests-postgres` (Scope H).
3. Push de prueba a `main_postgresql` → confirmar que el job corre y pasa.

### F8 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos, en toda la solución.
2. `dotnet ef database update` sobre una Postgres vacía → aplica limpio.
3. Levantar la API completa (`dotnet run --project src/4.Services.WebApi`) contra Postgres, con el seed corriendo automáticamente (per `CLAUDE.md`: migra y siembra al arrancar).
4. Smoke manual: login, listar tickets paginado, crear un `TicketStatus` duplicado con `IsInitial = true` para la misma empresa → debe rechazarse por el índice filtrado (confirma que el filtro Postgres corregido funciona igual que el de SQL Server).
5. `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj --filter "FullyQualifiedName~PostgreSql"` → 0 fallidos.
6. CI verde en un push real a `main_postgresql`.

---

## Acceptance criteria

### F2/F3 — DbContext y migraciones

- [ ] `ConfigureServices.cs` usa `UseNpgsql`, no `UseSqlServer`.
- [ ] Existe una única migración `InitialPostgres`, generada contra Npgsql, sin restos del historial SQL Server.
- [ ] `dotnet ef database update` aplica limpio sobre una instancia Postgres vacía.
- [ ] Los 9 índices filtrados usan comillas dobles y `TRUE`/valores booleanos, no corchetes ni `1`.
- [ ] Ninguna columna usa `nvarchar`/`datetime2` — se verificó `text`/`timestamp` en su lugar.

### F4 — Dapper

- [ ] Ningún archivo bajo `src/2.Application/UseCases` o `src/3.Persistence/Repositories` contiene `[Schema].[Tabla]`, `ISNULL(`, `TOP (`, concatenación con `+` entre strings, o `NVARCHAR` en un `CAST`.
- [ ] Los 28 handlers que antes brancheaban `LIMIT/OFFSET` vs `OFFSET...FETCH` ahora usan solo `LIMIT/OFFSET` sin condicional.
- [ ] `RoleSystemOptionQuerySql.cs` pagina con `LIMIT/OFFSET`.

### F5 — Seed

- [ ] `DatabaseSeeder` corre sin excepciones contra Postgres, verificado con la API real arrancando contra una base limpia.

### F6 — Tests

- [ ] Los 5 casos de `PostgreSqlSmokeTests.cs` pasan contra `Testcontainers.PostgreSql`.
- [ ] El caso de índice filtrado (#3) prueba explícitamente que la unicidad condicionada por `GcRecord`/columna booleana se respeta — no solo que la migración aplicó, sino que el comportamiento en runtime es el esperado.
- [ ] La suite existente de `tests/IntegrationTests` (SQL Server) sigue compilando en esta rama.

### F7 — CI

- [ ] `main_postgresql` dispara el workflow en push/PR.
- [ ] El job `integration-tests-postgres` corre y pasa contra un push real.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] La API arranca contra Postgres, migra y siembra automáticamente al inicio (mismo comportamiento que `main` describe para SQL Server en `CLAUDE.md`).

---

## Decisions taken and discarded

- **Fork permanente, sin plan de reintegración a `main`** (elegido por el usuario). Simplifica cada fix: en vez de mantener condicionales de proveedor para siempre, cada punto de fricción se reemplaza directamente. Si en el futuro se decide converger, es un trabajo de fusión manual, no algo que esta spec deba prever.
- **Historial de migraciones regenerado desde cero, no traducido migración por migración** (elegido, necesidad técnica más que preferencia). Cada migración SQL Server ya escaneada usa `MigrationBuilder` con el mapeo de tipos de ese proveedor — no hay una forma automática de "traducir" ~40 migraciones incrementales a Postgres. Partir de una única migración inicial contra el modelo ya corregido es más simple, más auditable, y coherente con que esta rama no necesita preservar el historial incremental de `main` (no hay datos productivos que migrar).
- **Suite de tests *smoke*, no paridad completa** (elegido por el usuario). Cubre los puntos de mayor riesgo real (los 9 índices filtrados, migración, seed, un flujo end-to-end) sin duplicar/parametrizar toda la suite existente todavía. Si esta rama se vuelve prioritaria, la paridad completa es la spec de seguimiento natural.
- **CI con un job single-provider, no una matriz `[SqlServer, PostgreSql]`** (elegido). `main_postgresql` nunca necesita correr contra SQL Server — una matriz sería trabajo sin beneficio. `main` sigue probando solo SQL Server, sin cambios.
- **Se deja el switch `DatabaseProvider` de `SqlConnectionFactory`/health checks tal cual, sin colapsarlo a un único valor fijo** (elegido). Ya soporta ambos proveedores correctamente (es la parte del sistema que sí era portable desde el principio); quitar la rama SQL Server ahorra unas pocas líneas pero agranda el diff respecto de `main` sin beneficio funcional — esta rama simplemente nunca ejerce esa rama del switch.
- **`ISNULL` → `COALESCE` sin branching, aunque `COALESCE` también funciona en SQL Server** (elegido, la única categoría de fix de Scope E que no es exclusiva de esta rama). Se aplica igual en `main_postgresql` por consistencia con el resto de los cambios de Scope E, y queda disponible como un cherry-pick trivial hacia `main` si alguna vez se quiere, sin que esta spec dependa de eso.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **Los 3 repositorios con `TOP (n)` (`PhoneVerificationCodeRepository`, `EmailOtpEnableCodeRepository`, `RoleUserSessionRepository`) son flujos de seguridad (MFA/OTP/sesiones)** — un error al reescribir a `LIMIT` podría romper silenciosamente la lógica de "el código/sesión más reciente". | F6 caso #5 ejercita explícitamente el flujo de sesión. Se recomienda, al implementar, comparar el resultado de cada query reescrita contra su versión SQL Server con datos de prueba idénticos antes de dar el fix por bueno. |
| **La migración `InitialPostgres` no fue revisada por un DBA de Postgres** — el mapeo automático de tipos de Npgsql (`uniqueidentifier→uuid`, `bit→boolean`, etc.) es confiable pero no infalible para casos de borde (precisión decimal, colación de texto). | F2.5 exige inspección manual del `Up()` generado antes de aplicarlo. Si aparece algo inesperado, es un ajuste puntual de esa migración, no un problema de diseño de esta spec. |
| **La suite *smoke* no cubre todos los ~35 archivos tocados uno por uno** — un fix mecánico mal aplicado en un archivo no ejercitado por los 5 casos de `PostgreSqlSmokeTests` podría pasar desapercibido. | Aceptado como parte de la decisión de alcance reducido (ver Decisiones). Cada fix es mecánico y de bajo riesgo individual (reemplazos de texto casi 1:1); el riesgo residual es proporcional al alcance que se decidió no cubrir todavía. |
| **`main_postgresql` diverge de `main` desde el día de su creación** — cualquier cambio futuro en `main` (incluidas SPEC 34-38 y 99 si se implementan después del fork) no llega automáticamente a esta rama. | Aceptado explícitamente por la decisión de "fork permanente". Si se necesita traer una spec de `main` a esta rama, es un cherry-pick/port manual, revisando en particular cualquier `HasFilter`/`HasColumnType`/SQL crudo que la spec nueva introduzca. |
| **Los 28 handlers que hoy branchean `LIMIT/OFFSET` vs `OFFSET...FETCH` quedan simplificados (sin la rama SQL Server) en esta rama** — si alguna vez se decide reintegrar a `main`, hay que revertir esa simplificación, no solo re-agregar lo que falta. | Coherente con "fork permanente, sin plan de reintegración" — si esa decisión cambia en el futuro, es trabajo adicional conocido y documentado acá, no una sorpresa. |

---

## What is **not** in this spec

- Paridad completa de tests de integración contra Postgres (solo *smoke*).
- Migración de datos desde una base SQL Server productiva.
- Sincronización automática entre `main` y `main_postgresql`.
- Colapsar el switch de proveedor en `SqlConnectionFactory`/health checks.
- Reintegración a `main` (esta rama es un fork permanente por decisión explícita).
- Cualquier cambio de dominio o de negocio — esta spec es puramente de portabilidad de infraestructura.

Cada uno, si llega, va en su propia spec.
