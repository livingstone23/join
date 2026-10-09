# SPEC 40-post — Índices únicos filtrados por soft-delete (sacar `GcRecord` de las claves únicas)

> **Status:** Aprobado
> **Origen:** copia de SPEC 40 de `main` (estado allí al copiarla: Implementado), adaptada a PostgreSQL para el fork `main_postgresql`. Se implementa **desde cero** en el fork, sin traer commits de `main` (decisión del usuario, 2026-10-09).
> **Rama de trabajo:** `spec-40-post-filtered-unique-indexes-soft-delete`, creada desde `main_postgresql`; PR hacia `main_postgresql`, nunca hacia `main`.
> **Lectura en el fork:** dentro de esta spec, toda referencia a una spec de la serie copiada (40–49, 51, 99) se lee como su versión `-post` ("SPEC 41" = SPEC 41-post), y aplican las convenciones de `specs/README.md` → "Serie `-post`". Donde el texto copiado de `main` y la sección "Adaptación a PostgreSQL" difieren, prevalece esta última.
> **Depends on:** SPEC 39 (fork `main_postgresql`, convenciones de nombres en minúsculas). Es prerequisito de SPEC 41-post.
> **Related:** SPEC 38 (query filters), SPEC 39 (fork PostgreSQL — la sintaxis de `HasFilter` es específica del proveedor).
> **Date:** 2026-10-09 (copia `-post`; original: 2026-09-28)
> **Ajuste por SPEC 40 (implementación, 2026-10-07):** (1) Nombres definitivos siguiendo el ejemplo `UX_<Tabla>_Company_<…>` (se normaliza `CompanyId` → `Company`): `UX_PersonContacts_Person_Type_Value`, `UX_Persons_Company_IdType_IdNumber`, `UX_Customers_Company_CustomerCode`, `UX_Customers_Company_Person_User`, `UX_Genders_Company_Code|Name`, `UX_Industries_Company_Code|Name`, `UX_TaxRegimes_Company_Code|Name`, `UX_IncomeRanges_Company_DisplayName|DisplayOrder`, `UX_Regions_Company_Country_Name|Code`. (2) `Region.Code` es nullable y en `main` el índice viejo tenía el filtro automático que EF agrega en SQL Server (`[Code] IS NOT NULL`); para no cambiar el comportamiento con códigos nulos, `UX_Regions_Company_Country_Code` usa `HasFilter("\"code\" IS NOT NULL AND \"gcrecord\" = 0")` (los otros 13 usan `"gcrecord" = 0`). En el fork el filtro de `Code` es obligatorio, porque Npgsql no agrega ese filtro automático.
> **Ajuste por SPEC 40-post (implementación, 2026-10-09):** (1) `Customer.UserId` es obligatorio (`Guid`, columna `NOT NULL`) en el fork y en `main`; la premisa de "`UserId` nullable" de la SPEC 40 original era incorrecta. `UX_Customers_Company_Person_User` conserva `.AreNullsDistinct(false)` como estaba acordado, pero hoy no tiene efecto; se quitan el caso D.6 y su criterio de aceptación. (2) El caso D.2 (`PUT /persons` tres veces) y el smoke HTTP no se pueden ejecutar: `DapperContext.CreateConnection()` del fork siempre crea `SqlConnection` (rama PostgreSQL comentada desde SPEC 39), y `PUT /Persons` responde 500 antes de llegar a los índices. Se corrige aparte (decisión del usuario; pendiente en `specs/README.md`); el comportamiento que D.2 validaba lo cubre D.1 a nivel de base.
> **Objective:** Reemplazar los 14 índices únicos que incluyen `GcRecord` como columna de la clave por índices únicos **filtrados** (`WHERE GcRecord = 0`), el patrón que el sistema ya usa en otros 7 índices, para que la unicidad aplique solo a registros activos y los registros borrados nunca choquen entre sí.

---

## Adaptación a PostgreSQL (fork `main_postgresql`)

| Tema | En `main` (SQL Server) | En esta versión |
|---|---|---|
| Filtro de los 14 índices | `.HasFilter("[GcRecord] = 0")` | `.HasFilter("\"gcrecord\" = 0")` |
| `UX_Regions_Company_Country_Code` | `[Code] IS NOT NULL AND [GcRecord] = 0` | `"code" IS NOT NULL AND "gcrecord" = 0`. El filtro de `Code` es obligatorio: Npgsql no agrega el `IS NOT NULL` automático que EF pone en SQL Server a un índice único sobre columna nula. |
| `UX_Customers_Company_Person_User` | SQL Server trata dos `NULL` como iguales | `.AreNullsDistinct(false)` → `NULLS NOT DISTINCT` (PostgreSQL 15+), decisión del usuario 2026-10-09. Hoy sin efecto: `UserId` es obligatorio (ver Ajuste). |
| Nombres físicos | `UX_Genders_Company_Name` | `HasDatabaseName("UX_Genders_Company_Name")` se conserva; la convención `ApplyLowerCaseNaming` lo deja como `ux_genders_company_name` en la base. |
| Migración | `FilteredUniqueIndexesForSoftDelete` contra SQL Server | Misma, generada contra Npgsql: 14 `DropIndex` + 14 `CreateIndex` con `filter: "\"gcrecord\" = 0"` (y `"code" IS NOT NULL AND ...` en Regions) y la anotación `NullsDistinct = false` en el de clientes. |
| Violación del índice | `SqlException` 2601/2627 | `PostgresException` `23505` con `ConstraintName` = el índice; `GlobalExceptionHandler` ya responde 409 `DUPLICATE_KEY` (SPEC 39). |
| Tests | `UniqueIndexSoftDeleteGuardTests` y `FilteredUniqueIndexesIntegrationTests` sobre `CustomWebApplicationFactory` (MsSql) | `UniqueIndexSoftDeleteGuardPostgreSqlTests` (solo modelo) y `FilteredUniqueIndexesPostgreSqlTests` sobre `PostgreSqlWebApplicationFactory`. Además, la teoría de índices filtrados de `PostgreSqlSmokeTests` (caso 3) suma los 14 índices nuevos. |

Casos de prueba que cambian respecto de `main`:
- **D.7 (nuevo):** dos `Region` activas del mismo país con `Code` nulo → permitido (prueba el filtro `"code" IS NOT NULL`).

Verificación con Docker (convención 12 del README): arrancar la API contra `postgres:17`, comprobar en `pg_indexes` que los 14 índices existen con su `WHERE` y repetir el escenario de `UpdatePersonCommandHandler.SyncContacts` (quitar/agregar/quitar el mismo email el mismo día → tres 200). El escenario HTTP queda pendiente del arreglo de `DapperContext` (ver Ajuste).

---

## Por qué existe esta spec

`BaseAuditableEntity.MarkAsDeleted()` asigna a `GcRecord` la fecha del borrado en formato `yyyyMMdd` (`GetDeletionGcRecordStamp`, `src/1.Domain/Audit/BaseAuditableEntity.cs:50`). Un registro activo tiene `GcRecord = 0`; uno borrado hoy, `GcRecord = 20260928`.

14 índices únicos incluyen `GcRecord` como columna de la clave. La intención era permitir que un registro borrado y uno activo compartan el mismo valor natural (nombre, código, etc.), pero tiene un efecto no deseado: **dos registros con el mismo valor natural borrados el mismo día tienen la misma clave** y el segundo `SaveChanges` falla con violación de índice único → `DbUpdateException` → HTTP 500.

**Escenario real verificado en el código:** `UpdatePersonCommandHandler.SyncContacts` (`UpdatePersonCommandHandler.cs:298`) marca como borrado todo contacto que no viene en el request y crea como nuevo todo contacto que llega sin `Id`. Tres ediciones de la misma persona en un día — quitar `juan@x.com`, volver a agregarlo, quitarlo otra vez — producen dos filas `(PersonId, Email, juan@x.com, 20260928)`: la tercera edición devuelve 500.

El sistema ya tiene el patrón correcto en 7 índices (`RoleCompany`, `UserCommunicationChannel`, `UserCompany` default, `TicketStatus` x3, `TicketComplexity`, `TimeUnit`): índice único filtrado con `.HasFilter("\"gcrecord\" = 0")` (forma PostgreSQL, SPEC 39) y sin `GcRecord` en la clave.

---

## Scope

**In:**

### A. Los 14 índices a migrar

| Configuración | Índice actual | Clave nueva (filtrada `GcRecord = 0`) |
|---|---|---|
| `PersonContactConfiguration.cs:74` | `IX_PersonContacts_Unique_ValuePerPerson` | `(PersonId, ContactType, ContactValue)` |
| `PersonConfiguration.cs:62` | `IX_Persons_Company_IdType_IdNumber_GcRecord` | `(CompanyId, IdentificationTypeId, IdentificationNumber)` |
| `CustomerConfiguration.cs:60` | `IX_Customers_Company_CustomerCode_GcRecord` | `(CompanyId, CustomerCode)` |
| `CustomerConfiguration.cs:64` | `IX_Customers_Company_Person_User_GcRecord` | `(CompanyId, PersonId, UserId)` |
| `GenderConfiguration.cs:59` | `IX_Genders_CompanyId_Code_GcRecord` | `(CompanyId, Code)` |
| `GenderConfiguration.cs:63` | `IX_Genders_CompanyId_Name_GcRecord` | `(CompanyId, Name)` |
| `IndustryConfiguration.cs:60` | `IX_Industries_CompanyId_Code_GcRecord` | `(CompanyId, Code)` |
| `IndustryConfiguration.cs:65` | `IX_Industries_CompanyId_Name_GcRecord` | `(CompanyId, Name)` |
| `TaxRegimeConfiguration.cs:58` | `IX_TaxRegimes_CompanyId_Code_GcRecord` | `(CompanyId, Code)` |
| `TaxRegimeConfiguration.cs:63` | `IX_TaxRegimes_CompanyId_Name_GcRecord` | `(CompanyId, Name)` |
| `IncomeRangeConfiguration.cs:72` | `IX_IncomeRanges_CompanyId_DisplayName_GcRecord` | `(CompanyId, DisplayName)` |
| `IncomeRangeConfiguration.cs:76` | `IX_IncomeRanges_CompanyId_DisplayOrder_GcRecord` | `(CompanyId, DisplayOrder)` |
| `RegionConfiguration.cs:56` | `IX_Regions_Company_Country_Name_GcRecord` | `(CompanyId, CountryId, Name)` |
| `RegionConfiguration.cs:61` | `IX_Regions_Company_Country_Code_GcRecord` | `(CompanyId, CountryId, Code)` |

Nomenclatura nueva: prefijo `UX_` (el que ya usan los índices filtrados existentes), sin el sufijo `_GcRecord`. Ejemplo: `UX_Genders_Company_Name`.

Patrón:

```csharp
builder.HasIndex(g => new { g.CompanyId, g.Name })
    .IsUnique()
    .HasFilter("\"gcrecord\" = 0")
    .HasDatabaseName("UX_Genders_Company_Name");
```

### B. Migración EF

- Una sola migración `FilteredUniqueIndexesForSoftDelete` generada con `dotnet ef migrations add` que hace `DropIndex` de los 14 y `CreateIndex` de los 14 nuevos.
- **Segura por construcción:** hoy no pueden existir dos filas **activas** duplicadas, porque el índice actual ya lo impide con `GcRecord = 0` en la clave. La unicidad entre activos no cambia; solo deja de aplicarse entre borrados.
- `Down()` recrea los índices originales. **Riesgo del rollback:** si después de aplicar la migración se acumularon dos borrados del mismo día con la misma clave, el `Down()` fallará. Documentado, no mitigado (el rollback reintroduciría el bug).

### C. Test de guarda

`tests/IntegrationTests/Persistence/UniqueIndexSoftDeleteGuardPostgreSqlTests.cs`: recorre `context.Model.GetEntityTypes()` → `GetIndexes()` y falla si algún índice `IsUnique` incluye la propiedad `GcRecord` en su clave. El mensaje nombra la entidad y el índice y apunta a esta spec. Evita que una entidad nueva (SPEC 34 en adelante) vuelva a usar el patrón viejo.

### D. Tests de comportamiento

`tests/IntegrationTests/Persistence/FilteredUniqueIndexesPostgreSqlTests.cs` (PostgreSQL real, `PostgreSqlWebApplicationFactory`, `postgres:17`):

1. `PersonContact`: crear, borrar, recrear y volver a borrar el mismo contacto el mismo día → sin excepción (antes: violación de índice).
2. `PUT /persons/{id}` tres veces en el mismo día quitando/agregando/quitando el mismo email → las tres devuelven 200. **Pendiente** del arreglo de `DapperContext` (ver Ajuste).
3. `Gender`: dos géneros activos con el mismo nombre en la misma empresa → sigue fallando (la unicidad entre activos se preserva).
4. `Gender`: mismo nombre en empresas distintas → permitido.
5. `Region`: borrar y recrear con el mismo nombre/país dos veces el mismo día → sin excepción.
7. `Region`: dos regiones activas del mismo país con `Code` nulo → permitido (filtro `"code" IS NOT NULL`).

**Out of scope:**

- ~~**Sintaxis PostgreSQL.**~~ Resuelta en esta versión: los 14 filtros usan `"gcrecord" = 0` (convención de SPEC 39, que ya convirtió los índices filtrados existentes).
- **Índices no únicos con `GcRecord`** (por ejemplo `TicketLog (CompanyId, TicketId, GcRecord)`, `PersonBusinessProfile (CompanyId, PersonId, GcRecord)`): son índices de rendimiento, no generan colisiones. No se tocan.
- **Cambiar la regla de `CustomerConfiguration.cs:64` con `UserId` nullable.** En `main` (SQL Server) dos clientes activos de la misma persona sin usuario chocan, porque los `NULL` se consideran iguales. PostgreSQL los considera distintos; esta versión conserva la regla de `main` con `NULLS NOT DISTINCT` (decisión del usuario, 2026-10-09). Cambiar la regla en sí se decide aparte.
- Cambiar el valor de `GcRecord` al borrar (por ejemplo, a un timestamp único). Descartado: el índice filtrado resuelve el problema sin tocar el dominio ni el significado de `GcRecord` que usan las queries Dapper.

---

## Implementation plan

### F1 — Configuraciones

1. Modificar las 14 definiciones de `HasIndex` de la tabla A.
2. `dotnet build -c Release` → 0 errores.

### F2 — Migración

1. `dotnet ef migrations add FilteredUniqueIndexesForSoftDelete --project ../3.Persistence --startup-project .` (desde `src/4.Services.WebApi`).
2. Revisar el migration generado (Npgsql): exactamente 14 `DropIndex` + 14 `CreateIndex` con `filter: "\"gcrecord\" = 0"` (Regions Code: `"code" IS NOT NULL AND "gcrecord" = 0`) y `NullsDistinct = false` en `ux_customers_company_person_user`, nada más.
3. Aplicar contra un contenedor `postgres:17` (convención 12 del README); confirmar que arranca sin errores (`MigrateAsync` en `Program.cs`).

### F3 — Tests

1. Test de guarda (C).
2. Tests de comportamiento (D).
3. `grep` de los nombres de índice viejos en `tests/` y `src/` (por ejemplo, tests que capturan `DbUpdateException` y comparan el nombre del índice) → ajustarlos a los nombres nuevos.

### F4 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj /p:CollectCoverage=true /p:Threshold=90 /p:ThresholdType=line` → verde (no debería cambiar: no hay código de Application).
3. `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj --filter "FullyQualifiedName~PostgreSql"` → 0 fallidos.
4. Smoke con Docker (sección "Adaptación a PostgreSQL").

---

## Acceptance criteria

- [x] Los 14 índices de la tabla A son únicos filtrados por `GcRecord = 0`, sin `GcRecord` en la clave.
- [x] Ningún índice único del modelo incluye `GcRecord` en la clave (test de guarda).
- [x] Borrar dos veces el mismo valor natural el mismo día no produce error.
- [x] Dos registros activos con el mismo valor natural siguen siendo rechazados.
- [x] La migración tiene solo los 14 `DropIndex` + 14 `CreateIndex`.
- [x] Suite de integración PostgreSQL (`~PostgreSql`) en verde.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| Algún test o handler compara el nombre del índice viejo al capturar `DbUpdateException`. | F3.3 los busca por nombre antes de mergear. |
| El `Down()` de la migración falla si ya hay borrados duplicados del mismo día. | Documentado. El rollback reintroduciría el bug; si se necesita, limpiar duplicados antes. |
| PostgreSQL trata los `NULL` como distintos en índices únicos y no agrega filtros `IS NOT NULL` automáticos. | `NULLS NOT DISTINCT` en el índice de clientes y filtro explícito de `Code` en regiones; caso D.7. |
| SPEC 41 restaura un registro borrado cuando ya existe uno activo con la misma clave → violación del índice filtrado. | SPEC 41 exige validar el duplicado activo antes de restaurar y devolver un error de negocio, no un 500. |
