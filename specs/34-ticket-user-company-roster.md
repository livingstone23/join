# SPEC 34 — `TicketUserCompany`: roster de agentes de tickets por empresa

> **Status:** Aprobado
> **Depends on:** Nada (primera spec del módulo de tickets extendido). Precede a SPEC 35 (acciones de ciclo de vida gateadas por esta entidad), SPEC 36 (adjuntos), SPEC 37 (ingesta multicanal) y SPEC 38 (workflow/SLA parametrizable).
> **Date:** 2026-09-25
> **Objective:** Crear la entidad `TicketUserCompany` (vínculo `User` ↔ `Company` con los flags `IsSuperAdminTicket`, `CanFinishTicket`, `CanResolveTicket`) que define, por empresa, qué usuarios están habilitados para gestionar tickets y con qué nivel de autoridad, junto con un `TicketUserCompaniesController` (CRUD tenant-scoped) y la invariante "al menos un `IsSuperAdminTicket` activo por empresa una vez que el roster está poblado".

---

## Por qué existe esta spec

El módulo de tickets (`Ticket`, `TicketStatus`, `TicketComplexity`, `TimeUnit`, `TicketCompanyDefault`, `TicketLog`, `TicketNotification`) ya está construido de punta a punta —dominio, EF, CQRS y controllers— desde la migración `InitialReset`, sin haber pasado por el proceso de specs. Al auditarlo contra la meta de negocio (centralización por empresa, gestión parametrizable, múltiples canales de registro, log de vida del ticket, adjuntos con cuota, y reasignación gobernada por un admin de tickets por empresa) aparecen brechas puntuales. Esta spec cierra la primera y más fundamental: **hoy nada limita quién puede recibir, resolver, finalizar o redistribuir un ticket más allá del permiso CRUD genérico del recurso `Tickets`**.

Prueba concreta en el código actual: `CreateTicketCommandHandler` y `UpdateTicketCommandHandler` (`src/2.Application/UseCases/Messaging/Tickets/Commands/`) validan que `AssignedToUserId` tenga un `UserCompany` activo con la empresa del token —es decir, que sea *miembro* de la empresa— pero no validan ninguna capacidad *específica de tickets*. Cualquier usuario con `CanUpdate` sobre el recurso `Tickets` puede reasignar un ticket a cualquier otro miembro de la empresa, sin importar si ese usuario participa o no en la resolución de tickets.

El brief de negocio pide explícitamente esta entidad:

> "Debe existir una entidad que permita vincular qué usuarios estarán implicados en la solución de tickets y quién será el o los coordinadores de esto... **TicketUserCompany**: Id, UserId, CompanyId, IsSuperAdminTicket (al menos debe existir un usuario una vez que se pueble, solo él puede reasignar ticket), CanFinishTicket (permite a usuarios operativos finalizar ticket), CanResolveTicket (permite indicar entre quiénes se pueden asignar ticket... permite recibir un ticket y gestionarlo)."

El campo `CampoControl` que cerraba la lista del brief no es un campo de negocio nuevo: es la referencia a que la entidad debe usar el mismo bloque de campos de control/auditoría que ya usa cada entidad tenant-scoped del proyecto vía `BaseTenantEntity` (`Id`, `CompanyId`, `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `GcRecord`) — confirmado por el usuario durante la revisión de esta spec. No se modela como columna propia.

Esta spec entrega **solo la entidad, su invariante y su CRUD**. No toca `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` para gatear la reasignación con estos flags — eso es SPEC 35, que depende de que esta tabla ya exista y tenga datos poblados.

---

## Scope

**In:**

### A. Dominio

- `src/1.Domain/Messaging/TicketUserCompany.cs` (nuevo): entidad `BaseTenantEntity` (aporta `Id`, `CompanyId`, `Company`, y los campos de auditoría) + `UserId` (Guid, FK a `Security.Users`) + `IsSuperAdminTicket` (bool) + `CanFinishTicket` (bool) + `CanResolveTicket` (bool) + navegación `User : ApplicationUser`. Vive en `Messaging` (no en `Security`) porque es configuración propia del módulo de tickets, igual que `TicketCompanyDefault`.

### B. Persistencia

- `src/3.Persistence/Configuration/Messaging/TicketUserCompanyConfiguration.cs` (nuevo): tabla `Messaging.TicketUserCompanies`, PK `Id`, FK `UserId → Security.Users` con `OnDelete(DeleteBehavior.Restrict)`, FK `CompanyId → Common.Companies` con `OnDelete(DeleteBehavior.Restrict)` (mismo patrón que `TicketComplexityConfiguration`), índice único filtrado `(UserId, CompanyId) WHERE GcRecord = 0` (una sola fila activa por usuario y empresa; permite resucitar la combinación tras un soft delete, igual que SPEC 19), query filter global `GcRecord == 0`.
- `src/3.Persistence/Contexts/ApplicationDbContext.cs`: `DbSet<TicketUserCompany> TicketUserCompanies`.
- Migración EF Core: `AddTicketUserCompanies`.

### C. Application — Comandos (EF vía `IUnitOfWork`, patrón nativo del módulo)

- `src/2.Application/UseCases/Messaging/TicketUserCompanies/Commands/CreateTicketUserCompany/CreateTicketUserCompanyCommand.cs` + `CommandHandler` + `CommandValidator`.
- `src/2.Application/UseCases/Messaging/TicketUserCompanies/Commands/UpdateTicketUserCompany/UpdateTicketUserCompanyCommand.cs` + `CommandHandler` + `CommandValidator`.
- `src/2.Application/UseCases/Messaging/TicketUserCompanies/Commands/DeleteTicketUserCompany/DeleteTicketUserCompanyCommand.cs` + `CommandHandler`.
- Los tres comandos implementan `ITransactionalCommand<Response<T>>` (no `IRequest<T>` plano), igual que `CreateTicketComplexityCommand`/`DeleteTicketComplexityCommand`, para pasar por `TransactionBehavior`.
- Los handlers usan `_unitOfWork.GetRepository<TicketUserCompany>()` (repo genérico) — **sin** repositorio dedicado. Es el patrón real del módulo: ni `Ticket`, ni `TicketComplexity`, ni `TicketStatus`, ni `TicketCompanyDefault` tienen un `I<Entity>Repository` propio; todos pasan por `IGenericRepository<T>` + `GetAllAsync()` filtrado en memoria para chequeos de unicidad/existencia dentro del handler. Se replica exactamente ese estilo, no el de `IRoleCompanyRepository` (SPEC 19, módulo Security).
- Sin Mapperly: los DTOs se construyen a mano en cada handler con inicializadores de objeto, igual que `CreateTicketComplexityCommandHandler`. `ITicketMapper` existe solo para `Ticket` porque tiene ~15 campos relacionados; `TicketUserCompany` no lo justifica.

### D. Application — Queries (Dapper inline vía `ISqlConnectionFactory`, patrón nativo del módulo)

- `src/2.Application/UseCases/Messaging/TicketUserCompanies/Queries/GetTicketUserCompanyById/GetTicketUserCompanyByIdQuery.cs` + `QueryHandler`.
- `src/2.Application/UseCases/Messaging/TicketUserCompanies/Queries/GetTicketUserCompanies/GetTicketUserCompaniesQuery.cs` + `QueryHandler` (paginado, tenant-scoped).
- `src/2.Application/UseCases/Messaging/TicketUserCompanies/Queries/GetSystemWideTicketUserCompanies/GetSystemWideTicketUserCompaniesQuery.cs` + `QueryHandler` (paginado, cross-tenant, solo `SuperAdmin`).
- Los tres inyectan `ISqlConnectionFactory` + `ICurrentUserService` (+ `IOptions<PaginationSettings>` en los paginados) y arman SQL crudo con `Dapper`/`DynamicParameters`, igual que `GetTicketByIdQueryHandler`/`GetTicketsQueryHandler`. Paginación centralizada vía `PaginationSettings.Sanitize(...)` (SPEC 33) — **no** clamps manuales `[1, 100]` como en specs previas a SPEC 33.

### E. Application — Coordinador de invariante

- `src/2.Application/UseCases/Messaging/TicketUserCompanies/TicketUserCompanySuperAdminCoordinator.cs` (nuevo): clase DI-injected (`IUnitOfWork`), sin herencia de MediatR ni de Domain, siguiendo el patrón `<Thing><Invariant>Coordinator` de `PersonAddressDefaultCoordinator`/`PersonContactPrimaryCoordinator`. Expone `Task<bool> AnotherActiveSuperAdminExistsAsync(Guid companyId, Guid excludeId, CancellationToken ct)`. `Update` y `Delete` la invocan antes de quitarle `IsSuperAdminTicket = true` a la última fila activa que lo tiene.
- Registro en `src/2.Application/Common/ConfigureServices.cs`: `services.AddScoped<TicketUserCompanySuperAdminCoordinator>();` (misma línea de bloque que los coordinadores de `Person*`).

### F. DTOs

- `src/2.Application.DTO/Messaging/TicketUserCompanies/TicketUserCompanyDto.cs` (nuevo): un único DTO reutilizado por `GetById`, `GetPaged` y `GetSystemWide` — igual que `TicketComplexityDto`/`TicketStatusDto`, que no tienen variante "ListItem" separada (a diferencia de `Ticket`, que sí la tiene porque el detalle trae `Logs` anidados; acá no hay nada anidado).
- `src/2.Application.DTO/Messaging/TicketUserCompanies/CreateTicketUserCompanyDto.cs` (nuevo, webapi-facing): `{ Guid UserId, bool IsSuperAdminTicket, bool CanFinishTicket, bool CanResolveTicket }`.
- `src/2.Application.DTO/Messaging/TicketUserCompanies/UpdateTicketUserCompanyDto.cs` (nuevo, webapi-facing): `{ bool IsSuperAdminTicket, bool CanFinishTicket, bool CanResolveTicket }` — **sin** `UserId`: el vínculo usuario↔fila no se reasigna, se borra y se crea de nuevo (ver Decisiones).

### G. Controller

- `src/4.Services.WebApi/Controllers/Messaging/TicketUserCompaniesController.cs` (nuevo): `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/[controller]")]`, `[Produces("application/json")]`, `[PermissionResource("TicketUserCompanies")]`. Sin `[Authorize(Roles = ...)]` hardcodeado en los endpoints tenant-scoped —igual que `TicketCompanyDefaultsController`, `TicketComplexitiesController`, `TicketStatusesController`: la autorización vive enteramente en los flags `CanRead/CanCreate/CanUpdate/CanDelete` del recurso `TicketUserCompanies` resueltos por `DynamicAuthorizationFilter`, no en un rol fijo.
  - `GET /api/v1/TicketUserCompanies` — paginado, tenant-scoped. Query: `pageNumber`, `pageSize`, `userId?`, `isSuperAdminTicket?`, `canFinishTicket?`, `canResolveTicket?`.
  - `GET /api/v1/TicketUserCompanies/system-wide` — `[Authorize(Roles = "SuperAdmin")]`, paginado, cross-tenant. Query: igual + `companyName?`.
  - `GET /api/v1/TicketUserCompanies/{id:guid}`.
  - `POST /api/v1/TicketUserCompanies`.
  - `PUT /api/v1/TicketUserCompanies/{id:guid}`.
  - `DELETE /api/v1/TicketUserCompanies/{id:guid}`.

### H. Seed

- `src/3.Persistence/Seed/DatabaseSeeder.cs`: agregar la entrada de menú/permiso `SystemOptionSeed("TicketUserCompanies", "/ManejoTickets/ticket-user-companies", "@Icons.Material.Filled.SupervisorAccount", "ManejoTickets", "TicketUserCompanies", true, true, true, true)` en `GetAdministrativeSystemOptionSeeds()` junto a `TimeUnits`/`TicketComplexities`/`TicketStatuses`/`TicketCompanyDefaults`. Sin esto el endpoint devuelve 403 a todos salvo `SuperAdmin` (mismo motivo documentado en SPEC 29 para `Audit`).
- **Y además** las filas de `RoleSystemOptionSeed` correspondientes en `GetRoleSystemOptionSeeds()`. El `SystemOptionSeed` solo declara que el recurso existe; **no** otorga permiso a ningún rol. `SeedRoleSystemOptionsAsync` auto-otorga los 7 flags únicamente a `PrivilegedRoleNames` (`Admin`, `SuperAdminCompany`, vía `privilegedAllOptionSeeds`) y a `Admin` otra vez vía `GetAdminFullSystemOptionPermissionSeeds()`. `Manager`, `Supervisor` y `UsuarioSimple` **solo** reciben lo que esté escrito explícitamente en `GetRoleSystemOptionSeeds()` — exactamente como ya ocurre con `Tickets`/`TimeUnits`/`TicketComplexities`/`TicketStatuses`/`TicketCompanyDefaults`. Filas a agregar:
  ```csharp
  new("Manager", "TicketUserCompanies", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
  new("Supervisor", "TicketUserCompanies", true, false, false, false),
  new("UsuarioSimple", "TicketUserCompanies", false, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
  ```
  Sin la fila de `Manager`, el propio `manager@join.com` que esta spec siembra como `IsSuperAdminTicket` recibe 403 al llamar cualquier endpoint de `/TicketUserCompanies` y el smoke test F9.4 falla.
- Nuevo método `SeedTicketUserCompaniesAsync(Guid companyId)`, idempotente, invocado junto a `SeedTicketCompanyDefaultsAsync` en el flujo de seed de JOIN: da de alta `manager@join.com` como `IsSuperAdminTicket = true, CanFinishTicket = true, CanResolveTicket = true` y `simpleuser@join.com` como `CanResolveTicket = true` (agente operativo sin autoridad de redistribución/cierre) para la empresa sembrada. Satisface la invariante desde el arranque en el entorno de desarrollo/demo y deja un ejemplo realista del roster.

### I. Tests

- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Messaging/TicketUserCompanies/` con handlers de las 3 queries + 3 commands + el coordinador. Cobertura ≥ 90% en las clases nuevas (gate de CI).

**Out of scope (para specs futuras):**

- **Gatear `AssignedToUserId` en `CreateTicket`/`UpdateTicket` contra `CanResolveTicket`**, y **restringir la reasignación a `IsSuperAdminTicket`**: SPEC 35. Esta spec solo crea la tabla y la puebla; ningún handler de `Ticket` se modifica acá.
- **Comandos `ResolveTicket`/`FinishTicket` dedicados** gateados por `CanResolveTicket`/`CanFinishTicket`: SPEC 35.
- **Bulk assign/unassign** de flags a múltiples usuarios en una sola operación.
- **Auto-promoción** de un segundo usuario a `IsSuperAdminTicket` cuando el único activo se desactiva: la operación simplemente se bloquea (409) hasta que alguien con permiso designe manualmente un reemplazo antes de quitarle el flag al actual. Sin swap atómico.
- **Trigger que fuerce la creación del primer `IsSuperAdminTicket`** apenas una empresa se crea: el seed cubre las empresas sembradas de desarrollo; una empresa nueva en producción arranca sin roster (comportamiento válido — "una vez que se pueble" es condicional) hasta que alguien con `CanCreate` sobre `TicketUserCompanies` dé de alta la primera fila.
- **Endpoint "mis capacidades de ticket"** (`GET /TicketUserCompanies/me`) para que el frontend consulte los flags del usuario autenticado sin buscar por `Id`: útil para SPEC 35, se agrega si esa spec lo necesita.
- **Auditoría vía `IAuditLogger`** (SPEC 29): esa bitácora cubre 6 entidades de seguridad explícitamente listadas; `TicketUserCompany` es una entidad de negocio, fuera de ese alcance por diseño de SPEC 29. Si se quiere trazabilidad de "quién le dio permisos de ticket a quién", es una spec de extensión de SPEC 29 o un `TicketLog` genérico — no se resuelve acá.
- **Notificar al usuario** cuando se le otorga/quita un flag.

---

## Data model

### `TicketUserCompany` (nuevo, schema `Messaging`)

```csharp
// src/1.Domain/Messaging/TicketUserCompany.cs
using JOIN.Domain.Audit;
using JOIN.Domain.Security;

namespace JOIN.Domain.Messaging;

/// <summary>
/// Links a User to a Company as a ticket-management agent, with the specific
/// capabilities that user holds within that tenant's ticket workflow.
/// </summary>
public class TicketUserCompany : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the ApplicationUser.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Grants authority to redistribute (reassign) any ticket within the company.
    /// Exactly the mechanism that decides who may reassign — see the class-level
    /// invariant enforced by <c>TicketUserCompanySuperAdminCoordinator</c>.
    /// </summary>
    public bool IsSuperAdminTicket { get; set; }

    /// <summary>
    /// Allows an operational user to mark a ticket as finished.
    /// </summary>
    public bool CanFinishTicket { get; set; }

    /// <summary>
    /// Marks the user as eligible to receive and manage ticket assignments.
    /// </summary>
    public bool CanResolveTicket { get; set; }

    // --- Navigation ---
    public virtual ApplicationUser User { get; set; } = null!;
}
```

`Id`, `CompanyId`, `Company`, `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `GcRecord` los hereda de `BaseTenantEntity` → `BaseAuditableEntity`. No hay columna `CampoControl`.

### `src/3.Persistence/Configuration/Messaging/TicketUserCompanyConfiguration.cs` (nuevo)

```csharp
using JOIN.Domain.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Messaging;

public class TicketUserCompanyConfiguration : IEntityTypeConfiguration<TicketUserCompany>
{
    public void Configure(EntityTypeBuilder<TicketUserCompany> builder)
    {
        builder.ToTable("TicketUserCompanies", "Messaging");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.IsSuperAdminTicket).IsRequired();
        builder.Property(x => x.CanFinishTicket).IsRequired();
        builder.Property(x => x.CanResolveTicket).IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Company)
            .WithMany()
            .HasForeignKey(x => x.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        // One active row per (User, Company). Filtered so a soft-deleted row
        // does not block re-adding the same user to the roster later.
        builder.HasIndex(x => new { x.UserId, x.CompanyId })
            .HasDatabaseName("UX_TicketUserCompanies_User_Company")
            .IsUnique()
            .HasFilter("[GcRecord] = 0");

        builder.HasQueryFilter(x => x.GcRecord == 0);
    }
}
```

### DTOs

```csharp
// src/2.Application.DTO/Messaging/TicketUserCompanies/TicketUserCompanyDto.cs
namespace JOIN.Application.DTO.Messaging;

public record TicketUserCompanyDto
{
    public Guid Id { get; init; }
    public Guid CompanyId { get; init; }
    public string? CompanyName { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = string.Empty;
    public string? UserEmail { get; init; }
    public bool IsSuperAdminTicket { get; init; }
    public bool CanFinishTicket { get; init; }
    public bool CanResolveTicket { get; init; }
    public DateTime CreatedAt { get; init; }
}

// src/2.Application.DTO/Messaging/TicketUserCompanies/CreateTicketUserCompanyDto.cs
public record CreateTicketUserCompanyDto
{
    public Guid UserId { get; init; }
    public bool IsSuperAdminTicket { get; init; }
    public bool CanFinishTicket { get; init; }
    public bool CanResolveTicket { get; init; }
}

// src/2.Application.DTO/Messaging/TicketUserCompanies/UpdateTicketUserCompanyDto.cs
public record UpdateTicketUserCompanyDto
{
    public bool IsSuperAdminTicket { get; init; }
    public bool CanFinishTicket { get; init; }
    public bool CanResolveTicket { get; init; }
}
```

### Commands

```csharp
// CreateTicketUserCompanyCommand.cs
public record CreateTicketUserCompanyCommand : ITransactionalCommand<Response<TicketUserCompanyDto>>
{
    public Guid UserId { get; init; }
    public bool IsSuperAdminTicket { get; init; }
    public bool CanFinishTicket { get; init; }
    public bool CanResolveTicket { get; init; }
}

// UpdateTicketUserCompanyCommand.cs
public record UpdateTicketUserCompanyCommand : ITransactionalCommand<Response<TicketUserCompanyDto>>
{
    public Guid Id { get; init; }
    public bool IsSuperAdminTicket { get; init; }
    public bool CanFinishTicket { get; init; }
    public bool CanResolveTicket { get; init; }
}

// DeleteTicketUserCompanyCommand.cs
public sealed record DeleteTicketUserCompanyCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
```

### Coordinador de invariante

```csharp
// src/2.Application/UseCases/Messaging/TicketUserCompanies/TicketUserCompanySuperAdminCoordinator.cs
namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies;

public sealed class TicketUserCompanySuperAdminCoordinator(IUnitOfWork unitOfWork)
{
    public async Task<bool> AnotherActiveSuperAdminExistsAsync(
        Guid companyId,
        Guid excludeId,
        CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<TicketUserCompany>();
        var all = await repository.GetAllAsync();

        return all.Any(x =>
            x.GcRecord == 0
            && x.CompanyId == companyId
            && x.Id != excludeId
            && x.IsSuperAdminTicket);
    }
}
```

Se invoca desde `UpdateTicketUserCompanyCommandHandler` cuando `entity.IsSuperAdminTicket == true && request.IsSuperAdminTicket == false`, y desde `DeleteTicketUserCompanyCommandHandler` cuando `entity.IsSuperAdminTicket == true`. En ambos casos, si devuelve `false`, el handler corta con `LAST_SUPERADMIN_TICKET` (409) antes de persistir.

### Query de detalle (Dapper)

```sql
SELECT
    tuc.Id, tuc.CompanyId, co.Name AS CompanyName,
    tuc.UserId, CONCAT(u.FirstName, ' ', u.LastName) AS UserName, u.Email AS UserEmail,
    tuc.IsSuperAdminTicket, tuc.CanFinishTicket, tuc.CanResolveTicket,
    tuc.Created AS CreatedAt
FROM Messaging.TicketUserCompanies tuc
INNER JOIN Security.Users u ON tuc.UserId = u.Id
LEFT JOIN Common.Companies co ON tuc.CompanyId = co.Id
WHERE tuc.Id = @Id AND tuc.CompanyId = @TenantId AND tuc.GcRecord = 0;
```

El paginado (`GetTicketUserCompaniesQueryHandler`) agrega `WHERE tuc.CompanyId = @TenantId AND tuc.GcRecord = 0` más los filtros opcionales (`userId`, `isSuperAdminTicket`, `canFinishTicket`, `canResolveTicket`) con el patrón `(@Param IS NULL OR Columna = @Param)`, `ORDER BY tuc.Created DESC`, y branching `OFFSET/FETCH NEXT` (SQL Server) vs `LIMIT/OFFSET` (Postgres) — igual que `GetTicketsQueryHandler`. El system-wide (`GetSystemWideTicketUserCompaniesQueryHandler`) es igual pero sin el filtro `CompanyId` y con `companyName` opcional vía `LIKE`.

### Migración EF Core

Genera `Messaging.TicketUserCompanies` con:
- `Id UNIQUEIDENTIFIER NOT NULL` (PK)
- `UserId UNIQUEIDENTIFIER NOT NULL` (FK `Security.Users(Id)`, `NO ACTION`)
- `CompanyId UNIQUEIDENTIFIER NOT NULL` (FK `Common.Companies(Id)`, `NO ACTION`)
- `IsSuperAdminTicket BIT NOT NULL`
- `CanFinishTicket BIT NOT NULL`
- `CanResolveTicket BIT NOT NULL`
- Columnas de auditoría estándar (`Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `GcRecord INT NOT NULL DEFAULT 0`)
- Índice único filtrado `UX_TicketUserCompanies_User_Company ON (UserId, CompanyId) WHERE GcRecord = 0`

---

## Implementation plan

### F1 — Dominio + EF config + migración

1. Crear `src/1.Domain/Messaging/TicketUserCompany.cs`.
2. Crear `src/3.Persistence/Configuration/Messaging/TicketUserCompanyConfiguration.cs`.
3. Agregar `DbSet<TicketUserCompany> TicketUserCompanies` a `ApplicationDbContext`.
4. `dotnet build -c Release` → 0 errores.
5. `dotnet ef migrations add AddTicketUserCompanies --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi`. Verificar en el `Up()` generado: índice único filtrado presente, ambas FKs `NO ACTION`, columnas `BIT NOT NULL`.
6. `dotnet ef database update` (o dejar que `Program.cs → MigrateAsync` la aplique al arrancar).

### F2 — DTOs

1. Crear `src/2.Application.DTO/Messaging/TicketUserCompanies/` con `TicketUserCompanyDto.cs`, `CreateTicketUserCompanyDto.cs`, `UpdateTicketUserCompanyDto.cs`.
2. `dotnet build` → 0 errores.

### F3 — Coordinador

1. Crear `src/2.Application/UseCases/Messaging/TicketUserCompanies/TicketUserCompanySuperAdminCoordinator.cs` con la firma de la sección Data model.
2. Registrar en `src/2.Application/Common/ConfigureServices.cs`: `services.AddScoped<TicketUserCompanySuperAdminCoordinator>();`.
3. `dotnet build` → 0 errores.

### F4 — Commands

1. `CreateTicketUserCompanyCommand` + `CreateTicketUserCompanyCommandValidator` (`RuleFor(x => x.UserId).NotEqual(Guid.Empty)`).
2. `CreateTicketUserCompanyCommandHandler`:
   - `CompanyId == Guid.Empty` → `Response.Error("COMPANY_REQUIRED", ...)`.
   - `_unitOfWork.GetRepository<ApplicationUser>().GetAsync(request.UserId)` null → `USER_NOT_FOUND` (400).
   - Verificar que el usuario tiene un `UserCompany` activo con la `CompanyId` del token (mismo chequeo `assignedUserHasTenant` que ya existe en `CreateTicketCommandHandler`) → si no, `USER_NOT_IN_TENANT` (400).
   - `GetRepository<TicketUserCompany>().GetAllAsync()` filtrado por `GcRecord == 0 && CompanyId == tenantId && UserId == request.UserId` → si existe, `TICKET_USER_COMPANY_DUPLICATE` (409).
   - Construir entidad, `InsertAsync`, `SaveChangesAsync`. Mapear DTO a mano (el `ApplicationUser` ya cargado da `UserName`/`UserEmail`; `CompanyName` desde `_unitOfWork.GetRepository<Company>().GetAsync(tenantId)`).
3. `UpdateTicketUserCompanyCommand` + `UpdateTicketUserCompanyCommandValidator` (`RuleFor(x => x.Id).NotEqual(Guid.Empty)`).
4. `UpdateTicketUserCompanyCommandHandler`:
   - `CompanyId == Guid.Empty` → `COMPANY_REQUIRED`.
   - Cargar entidad por `Id`; `null` o `CompanyId != tenantId` → `TICKET_USER_COMPANY_NOT_FOUND` (404).
   - Si `entity.IsSuperAdminTicket && !request.IsSuperAdminTicket`: llamar al coordinador; si `false` → `LAST_SUPERADMIN_TICKET` (409), no persistir.
   - Aplicar los tres flags, `LastModified = UtcNow`, `LastModifiedBy = userId`. `UpdateAsync` + `SaveChangesAsync`. Mapear DTO.
5. `DeleteTicketUserCompanyCommand`.
6. `DeleteTicketUserCompanyCommandHandler`:
   - `CompanyId == Guid.Empty` → `COMPANY_REQUIRED`.
   - Cargar entidad por `Id` filtrada por `tenantId`; `null` → `TICKET_USER_COMPANY_NOT_FOUND` (404).
   - Si `entity.IsSuperAdminTicket`: llamar al coordinador; si `false` → `LAST_SUPERADMIN_TICKET` (409).
   - `entity.MarkAsDeleted()`, `UpdateAsync` + `SaveChangesAsync`. Si `affected == 0` → `TICKET_USER_COMPANY_NOT_FOUND`.
7. `dotnet build` → 0 errores.

### F5 — Queries

1. `GetTicketUserCompanyByIdQuery` + `GetTicketUserCompanyByIdQueryHandler` (Dapper, SQL de la sección Data model). `CompanyId == Guid.Empty` → `COMPANY_REQUIRED`. Sin fila → `TICKET_USER_COMPANY_NOT_FOUND`.
2. `GetTicketUserCompaniesQuery` (`PageNumber?`, `PageSize?`, `UserId?`, `IsSuperAdminTicket?`, `CanFinishTicket?`, `CanResolveTicket?`) + `GetTicketUserCompaniesQueryHandler`: inyecta `IOptions<PaginationSettings>`, usa `.Sanitize(...)`, arma `WHERE` dinámico con `StringBuilder`/`DynamicParameters` igual que `GetTicketsQueryHandler`, branching de paginación por proveedor.
3. `GetSystemWideTicketUserCompaniesQuery` (igual + `CompanyName?`, sin `CompanyId` fijo) + `GetSystemWideTicketUserCompaniesQueryHandler`: mismo patrón sin el filtro de tenant.
4. `dotnet build` → 0 errores.

### F6 — Controller

1. `src/4.Services.WebApi/Controllers/Messaging/TicketUserCompaniesController.cs` con los 6 endpoints del scope. Mapeo de errores:

   | Código | HTTP |
   |---|---|
   | `COMPANY_REQUIRED` | 401 |
   | `TICKET_USER_COMPANY_NOT_FOUND` | 404 |
   | `TICKET_USER_COMPANY_DUPLICATE` | 409 |
   | `LAST_SUPERADMIN_TICKET` | 409 |
   | `USER_NOT_FOUND` / `USER_NOT_IN_TENANT` | 400 |

2. `POST` responde `201` con `CreatedAtAction(nameof(GetById), new { id = response.Data!.Id }, response)`, igual que `TicketsController.Create`.
3. `dotnet build` → 0 errores.

### F7 — Seed

1. Agregar el `SystemOptionSeed` de `TicketUserCompanies` a `GetAdministrativeSystemOptionSeeds()`, junto a `TicketCompanyDefaults`.
1b. Agregar las tres filas de `RoleSystemOptionSeed` (`Manager`/`Supervisor`/`UsuarioSimple`) a `GetRoleSystemOptionSeeds()`, junto a las de `TicketCompanyDefaults`. **No es opcional**: sin ellas ningún rol no privilegiado puede usar el recurso (ver sección Scope H).
2. Crear `SeedTicketUserCompaniesAsync(Guid companyId)`, idempotente (`AnyAsync` antes de insertar por `UserId + CompanyId`), invocada junto al resto del flujo de seed de mensajería para la empresa JOIN: `manager@join.com` → los tres flags en `true`; `simpleuser@join.com` → solo `CanResolveTicket = true`.
3. Verificar que correr el seeder dos veces no duplica filas.
4. `dotnet build` → 0 errores. Arrancar la API contra una base limpia y confirmar que el seed corre sin error.

### F8 — Tests (~26 casos)

- `GetTicketUserCompanyByIdQueryHandlerTests` (4): `CompanyId` vacío → 401; encontrado → 200; no encontrado → 404; cross-tenant → 404.
- `GetTicketUserCompaniesQueryHandlerTests` (5): `CompanyId` vacío; sin filtros; cada filtro opcional aplica; `pageSize` fuera de rango se sanitiza vía `PaginationSettings`; orden `Created DESC`.
- `GetSystemWideTicketUserCompaniesQueryHandlerTests` (2): sin filtro de tenant devuelve filas de varias empresas; `companyName` filtra por `LIKE`.
- `CreateTicketUserCompanyCommandHandlerTests` (6): `CompanyId` vacío → 401; `UserId` inexistente → 400 `USER_NOT_FOUND`; usuario sin `UserCompany` activo en el tenant → 400 `USER_NOT_IN_TENANT`; link duplicado → 409; happy path crea y devuelve 201; los tres flags se persisten tal cual vienen en el comando (incluyendo `false, false, false`).
- `CreateTicketUserCompanyCommandValidatorTests` (2): `UserId == Guid.Empty` falla; válido pasa.
- `UpdateTicketUserCompanyCommandHandlerTests` (6): `Id` no encontrado o cross-tenant → 404; apagar `IsSuperAdminTicket` siendo el único activo → 409 `LAST_SUPERADMIN_TICKET`, no persiste; apagarlo habiendo otro activo → 200; prenderlo → 200 sin llamar al coordinador; cambiar solo `CanFinishTicket`/`CanResolveTicket` → 200 sin llamar al coordinador; `LastModified`/`LastModifiedBy` se actualizan.
- `UpdateTicketUserCompanyCommandValidatorTests` (1): `Id == Guid.Empty` falla.
- `DeleteTicketUserCompanyCommandHandlerTests` (4): `CompanyId` vacío → 401; no encontrado → 404; único `IsSuperAdminTicket` activo → 409, no soft-deletea; no es super admin o hay otro activo → soft-delete OK.
- `TicketUserCompanySuperAdminCoordinatorTests` (3): ninguna fila → `false`; solo la excluida es super admin → `false`; otra fila activa con el flag → `true`; una fila soft-deleted con el flag no cuenta.
- `dotnet test --filter "FullyQualifiedName~TicketUserCompan"` → 0 fallidos.

### F9 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → gate de 90% en `JOIN.Application` sigue en verde.
3. `dotnet ef database update` sobre base limpia → migración aplica y el seeder corre con el roster de ejemplo.
4. Smoke: usuario con `CanCreate` sobre `TicketUserCompanies` da de alta un segundo `IsSuperAdminTicket` para la empresa JOIN → 201. Intenta apagar el flag del primero (`manager@join.com`) → 200 (hay otro activo). Intenta apagar el del segundo también → 409 `LAST_SUPERADMIN_TICKET`.
5. Smoke: `POST` duplicado (mismo `UserId`, misma empresa) → 409.
6. Smoke: usuario de otra empresa intenta `GET /TicketUserCompanies/{id}` de una fila ajena → 404.
7. Smoke: `GET /TicketUserCompanies/system-wide` sin rol `SuperAdmin` → 403; con `SuperAdmin` → 200 con filas de ambas empresas sembradas.
8. `CURL_REQUESTS.md` — agregar los 6 endpoints.

---

## Acceptance criteria

### F1 — Dominio, persistencia y migración

- [ ] `TicketUserCompany` hereda `BaseTenantEntity` y expone `UserId`, `IsSuperAdminTicket`, `CanFinishTicket`, `CanResolveTicket` + navegación `User`. Ninguna columna `CampoControl`.
- [ ] Tabla `Messaging.TicketUserCompanies` con FKs `UserId → Security.Users` y `CompanyId → Common.Companies`, ambas `NO ACTION`/`Restrict`.
- [ ] Índice único filtrado `(UserId, CompanyId) WHERE GcRecord = 0`.
- [ ] Query filter global `GcRecord == 0` configurado.
- [ ] Una sola migración EF Core nueva (`AddTicketUserCompanies`) aplica limpio sobre base vacía.

### F3 — Coordinador

- [ ] `TicketUserCompanySuperAdminCoordinator.AnotherActiveSuperAdminExistsAsync` ignora filas con `GcRecord != 0`, ignora la fila excluida por `excludeId`, y filtra por `CompanyId`.
- [ ] Registrado como `Scoped` en `ConfigureServices.cs`.

### F4 — Commands

- [ ] `CreateTicketUserCompanyCommandHandler` retorna 401 `COMPANY_REQUIRED` si el token no trae `CompanyId`.
- [ ] Retorna 400 `USER_NOT_FOUND` si `UserId` no existe.
- [ ] Retorna 400 `USER_NOT_IN_TENANT` si el usuario existe pero no tiene `UserCompany` activo con la empresa del token.
- [ ] Retorna 409 `TICKET_USER_COMPANY_DUPLICATE` si ya hay una fila activa para `(UserId, CompanyId)`.
- [ ] `CompanyId` de la fila creada siempre sale de `ICurrentUserService.CompanyId`, nunca del body.
- [ ] `UpdateTicketUserCompanyCommandHandler` retorna 404 si el `Id` no existe o pertenece a otra empresa.
- [ ] Retorna 409 `LAST_SUPERADMIN_TICKET` al intentar apagar `IsSuperAdminTicket` en la única fila activa que lo tiene para esa empresa, **sin persistir el cambio**.
- [ ] Permite apagar `IsSuperAdminTicket` sin error cuando existe otra fila activa con el flag en `true` para la misma empresa.
- [ ] No permite cambiar `UserId` (el DTO/comando de update no lo incluye).
- [ ] `DeleteTicketUserCompanyCommandHandler` retorna 409 `LAST_SUPERADMIN_TICKET` y **no** soft-deletea si la fila es la única con `IsSuperAdminTicket = true` activa en la empresa.
- [ ] Permite el soft delete cuando la fila no tiene `IsSuperAdminTicket` o cuando hay otra fila activa con el flag.
- [ ] El soft delete usa `entity.MarkAsDeleted()` + `UpdateAsync` + `SaveChangesAsync` (no SQL crudo de `GcRecord`).

### F5 — Queries

- [ ] `GetTicketUserCompanyByIdQueryHandler` y `GetTicketUserCompaniesQueryHandler` filtran siempre por `CompanyId = @TenantId` (nunca aceptan `CompanyId` del cliente) y por `GcRecord = 0`.
- [ ] `GetTicketUserCompaniesQueryHandler` usa `PaginationSettings.Sanitize(...)` — no hay clamps manuales `[1, 100]` hardcodeados en el handler.
- [ ] `GetSystemWideTicketUserCompaniesQueryHandler` no filtra por `CompanyId` salvo por el `companyName` opcional, y solo es alcanzable con rol `SuperAdmin`.

### F6 — Controller

- [ ] `TicketUserCompaniesController` decorado con `[PermissionResource("TicketUserCompanies")]` a nivel de clase, sin `[Authorize(Roles=...)]` hardcodeado en los 5 endpoints tenant-scoped.
- [ ] `GET .../system-wide` decorado con `[Authorize(Roles = "SuperAdmin")]`.
- [ ] `POST` retorna 201 con header `Location` apuntando a `GetById`.
- [ ] Ningún endpoint acepta `CompanyId` en body o query.

### F7 — Seed

- [ ] `SystemOption` con `ControllerName = "TicketUserCompanies"` existe en el seed idempotente, con `CanRead/CanCreate/CanUpdate/CanDelete = true`.
- [ ] `GetRoleSystemOptionSeeds()` incluye las filas de `Manager`, `Supervisor` y `UsuarioSimple` para `TicketUserCompanies`.
- [ ] `manager@join.com` (rol `Manager`, no privilegiado) obtiene 200 —no 403— en `GET /api/v1/TicketUserCompanies` tras correr el seeder contra base limpia.
- [ ] Correr el seeder dos veces no duplica ni la `SystemOption`, ni los `RoleSystemOption`, ni las filas de `TicketUserCompanies`.
- [ ] Tras el seed, la empresa JOIN sembrada tiene al menos una fila con `IsSuperAdminTicket = true`.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test --filter "FullyQualifiedName~TicketUserCompan"` → 0 fallidos, ≥ 26 tests nuevos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate de CI).
- [ ] Ningún handler de `Ticket`/`UpdateTicket`/`CreateTicket` fue modificado por esta spec.
- [ ] `CURL_REQUESTS.md` documenta los 6 endpoints nuevos.

---

## Decisions taken and discarded

- **Sin repositorio dedicado (`ITicketUserCompanyRepository`)** (elegido) vs. repo Dapper+EF dedicado al estilo SPEC 19. El módulo de tickets entero (`Ticket`, `TicketComplexity`, `TicketStatus`, `TicketCompanyDefault`) usa `IGenericRepository<T>` genérico en los commands y Dapper inline en las queries, sin excepción. Introducir un repo dedicado acá rompería la consistencia del módulo sin aportar nada que el genérico no resuelva (la tabla es chica, sin joins complejos en escritura).
- **Sin Mapperly** (elegido) vs. `ITicketUserCompanyMapper`. Mismo criterio: `TicketComplexity`/`TicketStatus`/`TicketCompanyDefault` construyen sus DTOs a mano; solo `Ticket` (agregado grande, ~15 campos) justificó un mapper generado. Siete campos no lo justifican.
- **Un único DTO (`TicketUserCompanyDto`) sin variante "ListItem"** (elegido) vs. par Detail/ListItem como en SPEC 19. No hay campos anidados que recortar para la grilla (a diferencia de `Ticket`, que sí tiene `Logs`); los catálogos hermanos (`TicketComplexityDto`, `TicketStatusDto`) tampoco tienen esa variante.
- **`TicketUserCompanySuperAdminCoordinator` bloquea (409) en vez de auto-promover un reemplazo** (elegido) vs. swap atómico automático. Auto-promoción exige una regla de negocio no especificada (¿quién es "el siguiente"? ¿por antigüedad, por rol, alfabético?). Bloquear y exigir una acción explícita ("primero dale el flag a otro, después quitáselo a este") es la opción sin ambigüedad y sin inventar política de negocio.
- **Invariante enforced solo sobre `IsSuperAdminTicket`, no sobre `CanFinishTicket`/`CanResolveTicket`** (elegido) vs. exigir también al menos un `CanResolveTicket` activo. El brief solo pide la garantía "al menos un `IsSuperAdminTicket`" explícitamente ("al menos debe existir un usuario"); una empresa sin ningún `CanResolveTicket` simplemente no puede recibir tickets nuevos — eso es un problema operativo visible de inmediato, no un estado inconsistente de datos que haya que impedir a nivel de dominio.
- **`Update` no permite cambiar `UserId`** (elegido) vs. permitir reasignar el vínculo a otro usuario (como SPEC 19 permite reasignar `RoleId` en `RoleCompany`). La diferencia semántica: en `RoleCompany`, el `RoleId` es *el dato* del vínculo (qué rol está disponible), reasignarlo es la única mutación útil. En `TicketUserCompany`, el vínculo *es* la identidad del usuario en el roster — "cambiar de usuario" no es una edición, es dar de baja a uno y alta a otro. Se modela como `Delete` + `Create`.
- **`CompanyId` omitido de `CreateTicketUserCompanyDto`/`UpdateTicketUserCompanyDto`** (elegido, mismo criterio que SPEC 19): siempre sale del token, nunca del body.
- **Namespace `Messaging`, no `Security`** (elegido) vs. modelarlo junto a `RoleCompany`/`UserCompany`. Es configuración propia del dominio de tickets (qué usuarios gestionan tickets), no de autenticación/roles de plataforma. Vive junto a `TicketCompanyDefault`, que ya sienta el precedente de "configuración de empresa para tickets" en `Messaging`.
- **Sin `[Authorize(Roles = "SuperAdminCompany")]` hardcodeado en los endpoints tenant-scoped** (elegido) vs. el patrón de SPEC 19 (`RoleCompaniesController`). Los controllers hermanos del propio módulo de tickets (`TicketCompanyDefaultsController`, `TicketComplexitiesController`, `TicketStatusesController`) no hardcodean rol alguno salvo en su endpoint `system-wide` — la autorización tenant-scoped vive enteramente en `[PermissionResource]` + los flags de `RoleSystemOption`. Se sigue ese precedente, más reciente y más consistente con el resto del módulo, en vez del de Security.
- **Bootstrap del primer `IsSuperAdminTicket` vía seed de desarrollo, sin trigger de producción** (elegido) vs. crear automáticamente una fila `IsSuperAdminTicket = true` para el creador de cada empresa nueva. No hay spec de "alta de empresa" en el repo que sea el lugar natural para enganchar ese trigger, y el brief dice "una vez que se pueble" — condicional, no obligatorio desde el día uno. Se deja como responsabilidad operativa (alguien con `CanCreate` sobre este recurso da de alta al primero) y se documenta el riesgo abajo.
- **`USER_NOT_IN_TENANT` valida contra `UserCompany`, no contra `TicketUserCompany`** (elegido, obvio pero explícito): un usuario debe ser miembro de la empresa (`UserCompany` activo) *antes* de poder entrar al roster de tickets de esa empresa. Mismo chequeo que ya hace `CreateTicketCommandHandler` para `AssignedToUserId`, reutilizado acá para `UserId`.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **Empresas ya sembradas o creadas antes de esta spec quedan con el roster vacío** (sin ningún `IsSuperAdminTicket`), y nada en producción fuerza a poblarlo. | F7 siembra el roster de las empresas de desarrollo. Para producción, queda como tarea operativa documentada; si negocio lo requiere, una spec de "alta de empresa" puede enganchar la creación automática de la primera fila. |
| **`SystemOption` con `ControllerName = "TicketUserCompanies"` ausente del seed** → `DynamicAuthorizationFilter` devuelve 403 a todos salvo `SuperAdmin`. | F7.1 la agrega explícitamente; F9.4-F9.7 la verifican con smoke tests reales, no solo con el build. |
| **`RoleSystemOptionSeed` ausente aunque la `SystemOption` exista** → el recurso aparece declarado pero ningún rol no privilegiado (`Manager`, `Supervisor`, `UsuarioSimple`) tiene flags sobre él, y `DynamicAuthorizationFilter` devuelve 403 igual. Es un fallo silencioso distinto del anterior: el menú se ve, el endpoint no responde. | F7.1b las agrega. El criterio de aceptación exige verificar con `manager@join.com` concreto, no con `Admin`/`SuperAdmin` (que pasan siempre por `privilegedAllOptionSeeds` y por lo tanto **no** detectan este fallo). |
| **TOCTOU entre el chequeo de duplicado (`ExistsActiveLinkAsync` in-memory) y el `INSERT`**: dos requests concurrentes para el mismo `(UserId, CompanyId)` podrían ambos pasar la validación en memoria. | El índice único filtrado en la base es la garantía real; el segundo `INSERT` falla con `DbUpdateException`, que el handler debe traducir a 409 `TICKET_USER_COMPANY_DUPLICATE` (agregar `try/catch` específico si el genérico no lo hace ya). |
| **`GetAllAsync()` en memoria para chequear duplicados/invariante** no escala si una empresa tiene miles de agentes de tickets. | Aceptable para el tamaño esperado de un roster de agentes (decenas, no miles) y coherente con el patrón que ya usa todo el módulo (`CreateTicketCommandHandler` hace lo mismo sobre `UserCompany` y sobre `Tickets` completos). Si el volumen crece, es una optimización de query, no un cambio de contrato. |
| **Un `SuperAdmin` de plataforma no forma parte de ningún `TicketUserCompany`** y por ende no puede recibir tickets ni redistribuirlos vía este mecanismo. | Correcto y esperado: `SuperAdmin` es un rol de plataforma, no un agente de tickets de una empresa específica. Si negocio necesita que un `SuperAdmin` opere tickets de una empresa, debe tener también su fila `TicketUserCompany` como cualquier otro agente. |
| **Soft delete de la única fila con `CanResolveTicket = true`** deja a la empresa sin nadie elegible para recibir tickets nuevos, sin que esta spec lo bloquee (a diferencia de `IsSuperAdminTicket`). | Decisión consciente (ver Decisiones). Es un estado operativo visible — los tickets nuevos quedarían sin poder asignarse — no un estado de datos inconsistente. SPEC 35, al gatear la asignación con `CanResolveTicket`, es el lugar natural para decidir si además quiere una alerta o un bloqueo preventivo. |

---

## What is **not** in this spec

- Modificar `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` para gatear la reasignación o la asignación inicial con estos flags.
- Comandos `AssignTicket`/`ReassignTicket`/`ResolveTicket`/`FinishTicket` dedicados.
- Filtrar la visibilidad de `TicketLog` (`IsOnlyForCreatedAndAssigned`) según estos flags.
- Bulk assign/unassign de flags.
- Auto-promoción de un reemplazo al desactivar el último `IsSuperAdminTicket`.
- Trigger de alta automática de la primera fila al crear una empresa nueva.
- Endpoint `GET /TicketUserCompanies/me`.
- Auditoría vía `IAuditLogger` (SPEC 29) sobre esta entidad.
- Notificaciones al otorgar/quitar un flag.
- Tabla de parámetros de adjuntos, `TicketDocuments`, ingesta multicanal, workflow/SLA parametrizable — SPECs 36, 37, 38 respectivamente.

Cada uno, si llega, va en su propia spec.
