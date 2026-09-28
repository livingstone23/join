# SPEC 41 — Índices únicos filtrados por soft-delete (sacar `GcRecord` de las claves únicas)

> **Status:** Borrador
> **Depends on:** Ninguna. Es prerequisito de SPEC 42 (restauración de registros borrados), que necesita que la unicidad aplique solo a registros activos.
> **Related:** SPEC 39 (query filters), SPEC 40 (fork PostgreSQL — la sintaxis de `HasFilter` es específica del proveedor).
> **Date:** 2026-09-28
> **Objective:** Reemplazar los 14 índices únicos que incluyen `GcRecord` como columna de la clave por índices únicos **filtrados** (`WHERE GcRecord = 0`), el patrón que el sistema ya usa en otros 7 índices, para que la unicidad aplique solo a registros activos y los registros borrados nunca choquen entre sí.

---

## Por qué existe esta spec

`BaseAuditableEntity.MarkAsDeleted()` asigna a `GcRecord` la fecha del borrado en formato `yyyyMMdd` (`GetDeletionGcRecordStamp`, `src/1.Domain/Audit/BaseAuditableEntity.cs:50`). Un registro activo tiene `GcRecord = 0`; uno borrado hoy, `GcRecord = 20260928`.

14 índices únicos incluyen `GcRecord` como columna de la clave. La intención era permitir que un registro borrado y uno activo compartan el mismo valor natural (nombre, código, etc.), pero tiene un efecto no deseado: **dos registros con el mismo valor natural borrados el mismo día tienen la misma clave** y el segundo `SaveChanges` falla con violación de índice único → `DbUpdateException` → HTTP 500.

**Escenario real verificado en el código:** `UpdatePersonCommandHandler.SyncContacts` (`UpdatePersonCommandHandler.cs:298`) marca como borrado todo contacto que no viene en el request y crea como nuevo todo contacto que llega sin `Id`. Tres ediciones de la misma persona en un día — quitar `juan@x.com`, volver a agregarlo, quitarlo otra vez — producen dos filas `(PersonId, Email, juan@x.com, 20260928)`: la tercera edición devuelve 500.

El sistema ya tiene el patrón correcto en 7 índices (`RoleCompany`, `UserCommunicationChannel`, `UserCompany` default, `TicketStatus` x3, `TicketComplexity`, `TimeUnit`): índice único filtrado con `.HasFilter("[GcRecord] = 0")` y sin `GcRecord` en la clave.

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
    .HasFilter("[GcRecord] = 0")
    .HasDatabaseName("UX_Genders_Company_Name");
```

### B. Migración EF

- Una sola migración `FilteredUniqueIndexesForSoftDelete` generada con `dotnet ef migrations add` que hace `DropIndex` de los 14 y `CreateIndex` de los 14 nuevos.
- **Segura por construcción:** hoy no pueden existir dos filas **activas** duplicadas, porque el índice actual ya lo impide con `GcRecord = 0` en la clave. La unicidad entre activos no cambia; solo deja de aplicarse entre borrados.
- `Down()` recrea los índices originales. **Riesgo del rollback:** si después de aplicar la migración se acumularon dos borrados del mismo día con la misma clave, el `Down()` fallará. Documentado, no mitigado (el rollback reintroduciría el bug).

### C. Test de guarda

`tests/IntegrationTests/Persistence/UniqueIndexSoftDeleteGuardTests.cs`: recorre `context.Model.GetEntityTypes()` → `GetIndexes()` y falla si algún índice `IsUnique` incluye la propiedad `GcRecord` en su clave. El mensaje nombra la entidad y el índice y apunta a esta spec. Evita que una entidad nueva (SPEC 34 en adelante) vuelva a usar el patrón viejo.

### D. Tests de comportamiento

`tests/IntegrationTests/Persistence/FilteredUniqueIndexesIntegrationTests.cs` (SQL Server real, `CustomWebApplicationFactory`):

1. `PersonContact`: crear, borrar, recrear y volver a borrar el mismo contacto el mismo día → sin excepción (antes: violación de índice).
2. `PUT /persons/{id}` tres veces en el mismo día quitando/agregando/quitando el mismo email → las tres devuelven 200.
3. `Gender`: dos géneros activos con el mismo nombre en la misma empresa → sigue fallando (la unicidad entre activos se preserva).
4. `Gender`: mismo nombre en empresas distintas → permitido.
5. `Region`: borrar y recrear con el mismo nombre/país dos veces el mismo día → sin excepción.

**Out of scope:**

- **Sintaxis PostgreSQL.** `[GcRecord] = 0` es sintaxis de SQL Server; en PostgreSQL es `"GcRecord" = 0`. Los 7 índices filtrados existentes ya tienen este problema — se resuelve para los 21 en SPEC 40, no acá.
- **Índices no únicos con `GcRecord`** (por ejemplo `TicketLog (CompanyId, TicketId, GcRecord)`, `PersonBusinessProfile (CompanyId, PersonId, GcRecord)`): son índices de rendimiento, no generan colisiones. No se tocan.
- **`CustomerConfiguration.cs:64` con `UserId` nullable.** SQL Server trata los `NULL` como iguales en índices únicos, así que dos clientes activos de la misma persona sin usuario chocan. Es el comportamiento actual y no cambia con esta spec; si no es el deseado, se decide aparte.
- Cambiar el valor de `GcRecord` al borrar (por ejemplo, a un timestamp único). Descartado: el índice filtrado resuelve el problema sin tocar el dominio ni el significado de `GcRecord` que usan las queries Dapper.

---

## Implementation plan

### F1 — Configuraciones

1. Modificar las 14 definiciones de `HasIndex` de la tabla A.
2. `dotnet build -c Release` → 0 errores.

### F2 — Migración

1. `dotnet ef migrations add FilteredUniqueIndexesForSoftDelete --project ../3.Persistence --startup-project .` (desde `src/4.Services.WebApi`).
2. Revisar el migration generado: exactamente 14 `DropIndex` + 14 `CreateIndex` con `filter: "[GcRecord] = 0"`, nada más.
3. Aplicar contra la base de desarrollo; confirmar que arranca sin errores (`MigrateAsync` en `Program.cs`).

### F3 — Tests

1. Test de guarda (C).
2. Tests de comportamiento (D).
3. `grep` de los nombres de índice viejos en `tests/` y `src/` (por ejemplo, tests que capturan `DbUpdateException` y comparan el nombre del índice) → ajustarlos a los nombres nuevos.

### F4 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj /p:CollectCoverage=true /p:Threshold=90 /p:ThresholdType=line` → verde (no debería cambiar: no hay código de Application).
3. `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj` → 0 fallidos.

---

## Acceptance criteria

- [ ] Los 14 índices de la tabla A son únicos filtrados por `GcRecord = 0`, sin `GcRecord` en la clave.
- [ ] Ningún índice único del modelo incluye `GcRecord` en la clave (test de guarda).
- [ ] Borrar dos veces el mismo valor natural el mismo día no produce error.
- [ ] Dos registros activos con el mismo valor natural siguen siendo rechazados.
- [ ] La migración tiene solo los 14 `DropIndex` + 14 `CreateIndex`.
- [ ] Suite completa de integración en verde.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| Algún test o handler compara el nombre del índice viejo al capturar `DbUpdateException`. | F3.3 los busca por nombre antes de mergear. |
| El `Down()` de la migración falla si ya hay borrados duplicados del mismo día. | Documentado. El rollback reintroduciría el bug; si se necesita, limpiar duplicados antes. |
| La sintaxis de `HasFilter` no funciona en PostgreSQL. | Fuera de alcance; SPEC 40 la resuelve para los 21 índices filtrados. |
| SPEC 42 restaura un registro borrado cuando ya existe uno activo con la misma clave → violación del índice filtrado. | SPEC 42 exige validar el duplicado activo antes de restaurar y devolver un error de negocio, no un 500. |
