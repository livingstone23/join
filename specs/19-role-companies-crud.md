# SPEC 19 — Junction `RoleCompany` con CRUD restringido a SuperAdminCompany

> **Status:** Draft
> **Depends on:** SPEC 18 (Roles CRUD), SPEC 17 (PermissionFlags), `CompaniesController` (precedente de `[Authorize(Roles = "SuperAdminCompany")]`)
> **Date:** 2026-08-08
> **Objective:** Crear la entidad `RoleCompany` (vínculo `Role` ↔ `Company` con `BaseTenantEntity`) y un controlador `RoleCompaniesController` con endpoints `GET /{id}`, `GET /` (paged), `POST /`, `PUT /{id}`, `DELETE /{id}` (soft delete), donde `CompanyId` se resuelve exclusivamente desde el token del usuario con rol `SuperAdminCompany`, persistido con índice unique `(RoleId, CompanyId)` para evitar duplicados activos.

---

## Scope

**In:**

- `src/1.Domain/Security/RoleCompany.cs` (nuevo): entidad `BaseTenantEntity` (`Id` Guid, `CompanyId` Guid, audit fields) + `RoleId` Guid (FK a `Security.Roles`) + navigation `Role : ApplicationRole`.
- `src/3.Persistence/Configuration/Security/RoleCompanyConfiguration.cs` (nuevo): tabla `Security.RoleCompanies`, PK `Id`, FKs con `OnDelete(DeleteBehavior.Restrict)` (mismo patrón que `UserRoleCompanyConfiguration`), índice unique `(RoleId, CompanyId)` con `HasFilter("[GcRecord] = 0")` para impedir duplicados solo entre registros activos, query filter global `GcRecord == 0`, navegación bidireccional `Role -> RoleCompanies`.
- `src/2.Application.DTO/Security/RoleCompany/RoleCompanyDto.cs` (nuevo): record `{ Guid Id, Guid RoleId, string RoleName, bool IsSystemDefault, string? CreatedBy, DateTime Created }`. `CompanyId` se omite del DTO (siempre viene del token; exponerlo en el contrato es ruido).
- `src/2.Application.DTO/Security/RoleCompany/RoleCompanyListItemDto.cs` (nuevo): variante ligera `{ Guid Id, Guid RoleId, string RoleName, bool IsSystemDefault, DateTime Created }` para grilla.
- `src/2.Application.DTO/Security/RoleCompany/CreateRoleCompanyRequestDto.cs` (nuevo): `{ Guid RoleId }` (único input; `CompanyId` viene del token).
- `src/2.Application.DTO/Security/RoleCompany/UpdateRoleCompanyRequestDto.cs` (nuevo): `{ Guid RoleId }` (re-asignar el rol, manteniendo la `CompanyId` del token).
- `src/2.Application/Interface/Persistence/Security/IRoleCompanyRepository.cs` (nuevo): `Task<RoleCompanyDto?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct)`, `Task<(IReadOnlyList<RoleCompanyListItemDto> Items, int Total)> GetPagedAsync(Guid tenantId, Guid? roleIdFilter, bool? isActive, int page, int pageSize, CancellationToken ct)`, `Task AddAsync(RoleCompany entity, CancellationToken ct)`, `Task UpdateAsync(RoleCompany entity, CancellationToken ct)`, `Task SoftDeleteAsync(Guid id, Guid tenantId, string modifiedBy, CancellationToken ct)`, `Task<bool> ExistsActiveLinkAsync(Guid roleId, Guid companyId, CancellationToken ct)` (excluye `GcRecord > 0`).
- `src/3.Persistence/Repositories/Security/RoleCompanyRepository.cs` (nuevo): Dapper para reads (con `JOIN Security.Roles r ON rc.RoleId = r.Id AND r.GcRecord = 0` para proyectar `RoleName`), EF para writes. `SoftDeleteAsync` = `UPDATE Security.RoleCompanies SET GcRecord = GcRecord + 1, LastModified = SYSUTCDATETIME(), LastModifiedBy = @modifiedBy WHERE Id = @id AND CompanyId = @tenantId AND GcRecord = 0`.
- `src/2.Application/UseCases/Security/RoleCompanies/Queries/GetRoleCompanyById/GetRoleCompanyByIdQuery.cs` + `Handler`.
- `src/2.Application/UseCases/Security/RoleCompanies/Queries/GetRoleCompaniesPaged/GetRoleCompaniesPagedQuery.cs` + `Handler` (clamp `pageSize [1, 100]`, `page >= 1`, filtros `roleId`, `isActive`).
- `src/2.Application/UseCases/Security/RoleCompanies/Commands/CreateRoleCompany/CreateRoleCompanyCommand.cs` + `Handler` + `Validator` (FluentValidation: `RoleId` `NotEqual(Guid.Empty)`).
- `src/2.Application/UseCases/Security/RoleCompanies/Commands/UpdateRoleCompany/UpdateRoleCompanyCommand.cs` + `Handler` + `Validator`.
- `src/2.Application/UseCases/Security/RoleCompanies/Commands/DeleteRoleCompany/DeleteRoleCompanyCommand.cs` + `Handler`.
- `src/2.Application/Mappings/Security/RoleCompanyMapper.cs` (nuevo): `IRoleCompanyMapper` Mapperly con `RoleCompanyDto FromEntity(RoleCompany)`, `RoleCompanyListItemDto ToListItem(RoleCompany)`.
- `src/4.Services.WebApi/Controllers/Security/RoleCompaniesController.cs` (nuevo):
  - `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/[controller]")]`, `[Produces("application/json")]`, `[Authorize]`, `[PermissionResource("RoleCompanies")]`.
  - `GET /api/v1/RoleCompanies/{id:guid}`: `[Authorize(Roles = "SuperAdminCompany")]`. Devuelve 200 con `RoleCompanyDto` o 404.
  - `GET /api/v1/RoleCompanies`: `[Authorize(Roles = "SuperAdminCompany")]`. Query params: `page = 1`, `pageSize = 20`, `roleId?`, `isActive?`. Devuelve `Response<PagedResult<RoleCompanyListItemDto>>`.
  - `POST /api/v1/RoleCompanies`: `[Authorize(Roles = "SuperAdminCompany")]`. Body `CreateRoleCompanyRequestDto`. 201 + `Location`, 409 si link duplicado, 400 si `RoleId` inválido.
  - `PUT /api/v1/RoleCompanies/{id:guid}`: `[Authorize(Roles = "SuperAdminCompany")]`. Body `UpdateRoleCompanyRequestDto`. 200 con `RoleCompanyDto`, 404 si no encontrado, 409 si nuevo `RoleId` ya está vinculado.
  - `DELETE /api/v1/RoleCompanies/{id:guid}`: `[Authorize(Roles = "SuperAdminCompany")]`. 204 si ok, 404 si no encontrado.
- Reglas transversales en handlers:
  - `ICurrentUserService.CompanyId == Guid.Empty` → 401 (`Response<T>.Error("INVALID_COMPANY_ID", "El token no contiene un CompanyId válido.")`).
  - En todas las queries/mutations, `CompanyId` se toma **siempre** de `ICurrentUserService.CompanyId`; nunca del body ni de query.
  - En `GetById`, si el `Id` pertenece a otra `CompanyId` (cross-tenant) → 404 (no 403, no exponer existencia).
  - En `Update`, si `Id` no pertenece a la `CompanyId` del token → 404.
  - En `Delete`, mismo contrato (404 si no es del tenant).
  - Crear/Update con `RoleId` cuyo `ApplicationRole` está soft-deleted (`GcRecord != 0`) → 400 con mensaje descriptivo.
  - `RoleId` que no existe → 400 con mensaje descriptivo.
- Tests unitarios en `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleCompanies/`:
  - `GetRoleCompanyByIdQueryHandlerTests`, `GetRoleCompaniesPagedQueryHandlerTests`, `CreateRoleCompanyCommandHandlerTests`, `CreateRoleCompanyCommandValidatorTests`, `UpdateRoleCompanyCommandHandlerTests`, `UpdateRoleCompanyCommandValidatorTests`, `DeleteRoleCompanyCommandHandlerTests`.
  - Cobertura ≥ 90% en clases nuevas.
- Migración EF Core: `dotnet ef migrations add AddRoleCompanyJunction --project ../3.Persistence --startup-project .` (debe crear `Security.RoleCompanies` con FKs, índice unique parcial, columnas de auditoría).
- Registro de `IRoleCompanyRepository` en `src/3.Persistence/Configuration/ConfigureServices.cs`.

**Out of scope (para specs futuros):**

- Validación "rol activo + link `RoleCompany` activo" en el flujo de `UserRoleCompany` (assign role to user): queda como spec aparte. El `RoleCompany` provee la entidad, pero el consumer debe usarla.
- Bulk assign / bulk unassign de roles a múltiples companies.
- Endpoint para "qué roles tiene esta company" en formato `List<RoleDto>` (derivado de `RoleCompany.LeftJoin(Role)`): si negocio lo pide, spec aparte.
- Auditoría de quién asignó/desasignó el rol: ya cubierto por `CreatedBy`/`LastModifiedBy` que hereda `BaseTenantEntity`.
- Multi-tenancy de la entidad `RoleCompany` ya viene por `BaseTenantEntity`; no se rediseña.
- Migración de `RolesController` para usar `RoleCompany` en vez de `RoleManager` (SPEC 18 lo cubre).
- `SystemOption` con `ControllerName = "RoleCompanies"` para activar el filtro de SPEC 17: se asume que existe o se agrega en migración de datos; este spec no toca `SystemOption`.

---

## Data model

Nueva entidad `RoleCompany` y su mapper. Sin cambios a entidades existentes.

### `src/1.Domain/Security/RoleCompany.cs` (nuevo)

```csharp
using JOIN.Domain.Audit;

namespace JOIN.Domain.Security;

/// <summary>
/// Defines which Roles are available within a specific Company (tenant).
/// A role can only be assigned to a user of Company X if there is an active
/// <see cref="RoleCompany"/> link for (Role, Company X). This is the catalog
/// a SuperAdminCompany uses to decide which roles their company may assign.
/// </summary>
public class RoleCompany : BaseTenantEntity
{
    /// <summary>
    /// Foreign key to the ApplicationRole.
    /// </summary>
    public Guid RoleId { get; set; }

    // --- Navigation Properties ---
    public virtual ApplicationRole Role { get; set; } = null!;
}
```

`Id`, `CompanyId`, `Created`, `CreatedBy`, `LastModified`, `LastModifiedBy`, `GcRecord` los hereda de `BaseTenantEntity` → `BaseAuditableEntity`.

### `src/3.Persistence/Configuration/Security/RoleCompanyConfiguration.cs` (nuevo)

```csharp
using JOIN.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JOIN.Persistence.Configuration.Security;

public class RoleCompanyConfiguration : IEntityTypeConfiguration<RoleCompany>
{
    public void Configure(EntityTypeBuilder<RoleCompany> builder)
    {
        builder.ToTable("RoleCompanies", "Security");

        builder.HasKey(rc => rc.Id);

        // Unique index: prevents two active links for the same (Role, Company).
        // Filtered to GcRecord = 0 so soft-deleted rows do not block resurrection.
        builder.HasIndex(rc => new { rc.RoleId, rc.CompanyId })
            .IsUnique()
            .HasFilter("[GcRecord] = 0");

        builder.HasOne(rc => rc.Role)
            .WithMany() // si se necesita navegación inversa, agregar `ICollection<RoleCompany> RoleCompanies` a ApplicationRole
            .HasForeignKey(rc => rc.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(rc => rc.Company)
            .WithMany()
            .HasForeignKey(rc => rc.CompanyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasQueryFilter(rc => rc.GcRecord == 0);
    }
}
```

### Cambios en `src/1.Domain/Security/ApplicationRole.cs`

Agregar navigation `public virtual ICollection<RoleCompany> RoleCompanies { get; set; } = new List<RoleCompany>();` para soportar `WithMany(r => r.RoleCompanies)` en EF y para diagnóstico desde el dominio.

### `src/2.Application.DTO/Security/RoleCompany/RoleCompanyDto.cs` (nuevo)

```csharp
namespace JOIN.Application.DTO.Security.RoleCompany;

public sealed record RoleCompanyDto(
    Guid Id,
    Guid RoleId,
    string RoleName,
    bool IsSystemDefault,
    string? CreatedBy,
    DateTime Created);
```

### `src/2.Application.DTO/Security/RoleCompany/RoleCompanyListItemDto.cs` (nuevo)

```csharp
namespace JOIN.Application.DTO.Security.RoleCompany;

public sealed record RoleCompanyListItemDto(
    Guid Id,
    Guid RoleId,
    string RoleName,
    bool IsSystemDefault,
    DateTime Created);
```

### `src/2.Application.DTO/Security/RoleCompany/CreateRoleCompanyRequestDto.cs` (nuevo)

```csharp
namespace JOIN.Application.DTO.Security.RoleCompany;

public sealed record CreateRoleCompanyRequestDto(Guid RoleId);
```

### `src/2.Application.DTO/Security/RoleCompany/UpdateRoleCompanyRequestDto.cs` (nuevo)

```csharp
namespace JOIN.Application.DTO.Security.RoleCompany;

public sealed record UpdateRoleCompanyRequestDto(Guid RoleId);
```

### `src/2.Application/Mappings/Security/RoleCompanyMapper.cs` (nuevo)

```csharp
using JOIN.Application.DTO.Security.RoleCompany;
using Riok.Mapperly.Abstractions;

namespace JOIN.Application.Mappings.Security;

[Mapper]
public partial interface IRoleCompanyMapper
{
    RoleCompanyDto FromEntity(Domain.Security.RoleCompany entity);
    RoleCompanyListItemDto ToListItem(Domain.Security.RoleCompany entity);
}
```

### Migración EF Core

Genera `Security.RoleCompanies` con columnas:
- `Id UNIQUEIDENTIFIER NOT NULL` (PK)
- `RoleId UNIQUEIDENTIFIER NOT NULL` (FK `Security.Roles(Id)`)
- `CompanyId UNIQUEIDENTIFIER NOT NULL` (FK `Common.Companies(Id)`)
- `Created DATETIME2 NOT NULL`
- `CreatedBy NVARCHAR(MAX) NULL`
- `LastModified DATETIME2 NULL`
- `LastModifiedBy NVARCHAR(MAX) NULL`
- `GcRecord INT NOT NULL DEFAULT 0`
- Índice unique filtrado `IX_RoleCompanies_RoleId_CompanyId ON Security.RoleCompanies (RoleId, CompanyId) WHERE GcRecord = 0`
- FKs con `ON DELETE NO ACTION` (Restrict)

---

## Implementation plan

### F1 — Dominio + EF config + migración

1. Crear `src/1.Domain/Security/RoleCompany.cs` con la firma del scope.
2. Agregar `public virtual ICollection<RoleCompany> RoleCompanies { get; set; } = new List<RoleCompany>();` a `ApplicationRole.cs`.
3. Crear `src/3.Persistence/Configuration/Security/RoleCompanyConfiguration.cs` con la config del scope (tabla, índice unique filtrado, FKs, query filter).
4. `dotnet build -c Release` → 0 errores. Sin migración todavía, pero el mapeo debe compilar.
5. Generar migración: `dotnet ef migrations add AddRoleCompanyJunction --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi`. Revisar `Up()`/`Down()` generado (índice unique filtrado presente, FKs con `NO ACTION`, query filter compatible).
6. `dotnet ef database update --project ../3.Persistence --startup-project .` (o dejar que `Program.cs -> MigrateAsync` aplique). Compila y arranca.

### F2 — DTOs, mapper, repo interface

1. Crear carpeta `src/2.Application.DTO/Security/RoleCompany/` y los 4 archivos (`RoleCompanyDto`, `RoleCompanyListItemDto`, `CreateRoleCompanyRequestDto`, `UpdateRoleCompanyRequestDto`).
2. Crear `src/2.Application/Mappings/Security/RoleCompanyMapper.cs` con `IRoleCompanyMapper`.
3. Crear `src/2.Application/Interface/Persistence/Security/IRoleCompanyRepository.cs` con la firma del scope.
4. Registrar `IRoleCompanyRepository` en `src/3.Persistence/Configuration/ConfigureServices.cs`: `services.AddScoped<IRoleCompanyRepository, RoleCompanyRepository>();`. Verificar que `Program.cs` ya invoca la extensión.
5. `dotnet build -c Release` → 0 errores.

### F3 — `RoleCompanyRepository` (Dapper + EF)

1. Crear `src/3.Persistence/Repositories/Security/RoleCompanyRepository.cs`. Inyecta `IApplicationDbContext` (writes) + `ISqlConnectionFactory` (reads).
2. `GetByIdAsync`:
   - Query Dapper: `SELECT rc.Id, rc.RoleId, r.Name AS RoleName, r.IsSystemDefault, rc.CreatedBy, rc.Created FROM Security.RoleCompanies rc INNER JOIN Security.Roles r ON rc.RoleId = r.Id AND r.GcRecord = 0 WHERE rc.Id = @Id AND rc.CompanyId = @tenantId AND rc.GcRecord = 0`.
   - Si no hay row → `null`.
3. `GetPagedAsync`:
   - Count: `SELECT COUNT(*) FROM Security.RoleCompanies rc WHERE rc.CompanyId = @tenantId AND rc.GcRecord = 0 AND (@roleIdFilter IS NULL OR rc.RoleId = @roleIdFilter) AND (@isActive IS NULL OR ((@isActive = 1 AND rc.GcRecord = 0) OR (@isActive = 0 AND rc.GcRecord <> 0)))`.
   - Page: `SELECT ... ORDER BY rc.Created DESC, rc.Id OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY` (SQL Server) o `LIMIT @pageSize OFFSET @offset` (rama Postgres).
   - JOIN con `Security.Roles` para proyectar `RoleName` e `IsSystemDefault`.
4. `AddAsync`: `_dbContext.RoleCompanies.AddAsync(entity); SaveChangesAsync`. Auditoría seteada en handler.
5. `UpdateAsync`: localizar con `FindAsync(id)`, setear `RoleId`, `LastModified = UtcNow`, `LastModifiedBy`, `SaveChangesAsync`. Validar `CompanyId == tenantId` server-side.
6. `SoftDeleteAsync`: `UPDATE Security.RoleCompanies SET GcRecord = GcRecord + 1, LastModified = SYSUTCDATETIME(), LastModifiedBy = @modifiedBy WHERE Id = @id AND CompanyId = @tenantId AND GcRecord = 0`. Retorna rows affected.
7. `ExistsActiveLinkAsync`: `SELECT 1 FROM Security.RoleCompanies WHERE RoleId = @roleId AND CompanyId = @companyId AND GcRecord = 0`.
8. `dotnet build` → 0 errores.

### F4 — Queries (handlers Dapper)

1. `src/2.Application/UseCases/Security/RoleCompanies/Queries/GetRoleCompanyById/GetRoleCompanyByIdQuery.cs`:
   ```csharp
   public sealed record GetRoleCompanyByIdQuery(Guid Id) : IRequest<Response<RoleCompanyDto>>;
   ```
2. `GetRoleCompanyByIdQueryHandler.cs`: valida `ICurrentUserService.CompanyId != Guid.Empty`; llama `_repo.GetByIdAsync(id, tenantId)`; si null → `Response<RoleCompanyDto>.Error("ROLE_COMPANY_NOT_FOUND", "No se encontró el vínculo rol-empresa para la compañía del token.", 404)`; si OK → `Response<RoleCompanyDto>.Ok(...)`.
3. `src/2.Application/UseCases/Security/RoleCompanies/Queries/GetRoleCompaniesPaged/GetRoleCompaniesPagedQuery.cs`:
   ```csharp
   public sealed record GetRoleCompaniesPagedQuery(
       Guid? RoleId,
       bool? IsActive,
       int Page = 1,
       int PageSize = 20) : IRequest<Response<PagedResult<RoleCompanyListItemDto>>>;
   ```
4. `GetRoleCompaniesPagedQueryHandler.cs`: clamp `pageSize [1, 100]`, `page >= 1`; valida `CompanyId`; llama repo; retorna `Response<PagedResult<RoleCompanyListItemDto>>.Ok(...)`.
5. `dotnet build` → 0 errores.

### F5 — Commands (handlers con UnitOfWork)

1. `src/2.Application/UseCases/Security/RoleCompanies/Commands/CreateRoleCompany/CreateRoleCompanyCommand.cs`:
   ```csharp
   public sealed record CreateRoleCompanyCommand(Guid RoleId) : IRequest<Response<RoleCompanyDto>>;
   ```
2. `CreateRoleCompanyCommandValidator.cs`: `RuleFor(c => c.RoleId).NotEqual(Guid.Empty).WithMessage("RoleId es requerido.");`.
3. `CreateRoleCompanyCommandHandler.cs`:
   - Valida `CompanyId != Guid.Empty` → 401 descriptivo si vacío.
   - Valida `RoleId` existe y `ApplicationRole.GcRecord == 0` → 400 descriptivo si no.
   - Valida `ExistsActiveLinkAsync(RoleId, CompanyId)` → 409 con código `ROLE_COMPANY_DUPLICATE` si true.
   - Construye `RoleCompany { Id = Guid.NewGuid(), RoleId = RoleId, CompanyId = tenantId, Created = UtcNow, CreatedBy = userId, GcRecord = 0 }`. Persiste. Mapea DTO.
4. `UpdateRoleCompanyCommand.cs`:
   ```csharp
   public sealed record UpdateRoleCompanyCommand(Guid Id, Guid RoleId) : IRequest<Response<RoleCompanyDto>>;
   ```
5. `UpdateRoleCompanyCommandValidator.cs`: `RuleFor(c => c.Id).NotEqual(Guid.Empty); RuleFor(c => c.RoleId).NotEqual(Guid.Empty);`.
6. `UpdateRoleCompanyCommandHandler.cs`:
   - Carga `RoleCompany` por Id con `CompanyId = tenantId`. Si null → 404 `ROLE_COMPANY_NOT_FOUND`.
   - Valida nuevo `RoleId` existe y activo → 400 si no.
   - Si `RoleId` cambia → valida `ExistsActiveLinkAsync(newRoleId, tenantId)` (excluyendo el actual) → 409 `ROLE_COMPANY_DUPLICATE` si colisiona.
   - Actualiza `RoleId`, `LastModified`, `LastModifiedBy`. Save. Mapea DTO.
7. `DeleteRoleCompanyCommand.cs`:
   ```csharp
   public sealed record DeleteRoleCompanyCommand(Guid Id) : IRequest<Response<bool>>;
   ```
8. `DeleteRoleCompanyCommandHandler.cs`:
   - Valida `CompanyId`.
   - `SoftDeleteAsync(id, tenantId, userId)`. Si `affected == 0` → 404 `ROLE_COMPANY_NOT_FOUND` (puede ser cross-tenant o inexistente).
   - Loggear warning si hay `UserRoleCompanies` activos que referencien este `RoleCompany` indirectamente (no se bloquea, pero el warning visibiliza el impacto).
9. `dotnet build` → 0 errores.

### F6 — Controller

1. `src/4.Services.WebApi/Controllers/Security/RoleCompaniesController.cs`:
   - Mismo scaffolding que `CompaniesController`: `[ApiController]`, `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/[controller]")]`, `[Produces("application/json")]`, `[Authorize]`, `[PermissionResource("RoleCompanies")]`.
   - Inyecta `IMediator` + `ICurrentUserService`.
   - `GET /{id:guid}`: `[HttpGet("{id:guid}")]`, `[Authorize(Roles = "SuperAdminCompany")]`, `[ProducesResponseType(typeof(Response<RoleCompanyDto>), 200)]` + 404.
   - `GET /`: `[HttpGet]`, `[Authorize(Roles = "SuperAdminCompany")]`, query params, `[ProducesResponseType(typeof(Response<PagedResult<RoleCompanyListItemDto>>), 200)]`.
   - `POST /`: `[HttpPost]`, body `CreateRoleCompanyRequestDto`, `[ProducesResponseType(typeof(Response<RoleCompanyDto>), 201)]` + 400 + 409. `CreatedAtAction(nameof(GetById), new { id = dto.Id }, response)`.
   - `PUT /{id:guid}`: `[HttpPut("{id:guid}")]`, body `UpdateRoleCompanyRequestDto`, `[ProducesResponseType(typeof(Response<RoleCompanyDto>), 200)]` + 404 + 409.
   - `DELETE /{id:guid}`: `[HttpDelete("{id:guid}")]`, `[ProducesResponseType(typeof(Response<bool>), 204)]` + 404.
   - Switch de códigos por `response.Message`:
     - `INVALID_COMPANY_ID` → 401.
     - `ROLE_COMPANY_NOT_FOUND` → 404.
     - `ROLE_COMPANY_DUPLICATE` → 409.
     - `ROLE_NOT_FOUND` / `ROLE_INACTIVE` → 400.
2. `dotnet build` → 0 errores. Smoke: `dotnet run` y `curl` con token de SuperAdminCompany → 200; sin token → 401; con rol `Admin` → 403.

### F7 — Tests unitarios

1. Carpeta `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleCompanies/`.
2. `GetRoleCompanyByIdQueryHandlerTests`:
   - `CompanyId` vacío → 401 `INVALID_COMPANY_ID`.
   - Repo retorna DTO → 200 OK.
   - Repo retorna null → 404 `ROLE_COMPANY_NOT_FOUND`.
3. `GetRoleCompaniesPagedQueryHandlerTests`:
   - Valida `CompanyId` vacío.
   - Sin filtros → repo recibe `tenantId, null, null, 1, 20`.
   - `pageSize > 100` → clamp a 100.
   - `page < 1` → clamp a 1.
4. `CreateRoleCompanyCommandHandlerTests`:
   - `CompanyId` vacío → 401 antes de tocar repo.
   - `RoleId` no existente → 400 `ROLE_NOT_FOUND`.
   - `RoleId` soft-deleted → 400 `ROLE_INACTIVE`.
   - Link duplicado → 409 `ROLE_COMPANY_DUPLICATE`, no llama `AddAsync`.
   - Happy path → crea, llama `AddAsync`, retorna 201 con DTO.
5. `CreateRoleCompanyCommandValidatorTests`:
   - `RoleId` `Guid.Empty` → `ValidationFailure`.
   - `RoleId` válido → pasa.
6. `UpdateRoleCompanyCommandHandlerTests`:
   - `Id` no encontrado o de otra company → 404.
   - `RoleId` no existente → 400.
   - `RoleId` inactivo → 400.
   - Cambiar `RoleId` y colisionar → 409.
   - Cambiar `RoleId` sin colisión → 200.
   - Mismo `RoleId` (sin cambio) → 200 sin validar duplicado.
7. `UpdateRoleCompanyCommandValidatorTests`:
   - `Id` `Guid.Empty` → `ValidationFailure`.
   - `RoleId` `Guid.Empty` → `ValidationFailure`.
8. `DeleteRoleCompanyCommandHandlerTests`:
   - `CompanyId` vacío → 401.
   - `affected == 0` → 404.
   - Happy path → 204, `SoftDeleteAsync` llamado con `(id, tenantId, userId)`.
   - Log warning si hay `UserRoleCompanies` activos.
9. `dotnet test --filter "FullyQualifiedName~RoleCompanies"` → 0 fallidos. Cobertura ≥ 90% en clases nuevas.

### F8 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test` con coverage → cobertura total `JOIN.Application` ≥ 90%.
3. Migración aplicada: `dotnet ef database update` sin errores, o arranque de la API con `MigrateAsync`.
4. Smoke: `curl` con token `SuperAdminCompany` → `GET /RoleCompanies` (lista vacía), `POST /RoleCompanies { RoleId: ... }` (201), `GET /RoleCompanies/{id}` (200), `PUT /RoleCompanies/{id} { RoleId: ... }` (200), `DELETE /RoleCompanies/{id}` (204). Repetir `POST` con mismo `RoleId` → 409. Repetir `GET /RoleCompanies/{id}` con token de otra company → 404.
5. `CURL_REQUESTS.md`: agregar bloque con los 5 endpoints (estilo `CompaniesController`).
6. Confirmar que `SystemOption` con `ControllerName = "RoleCompanies"` existe en seed/DbContext; si no, agregarlo al `DatabaseSeeder` (decisión: extender `SeedMenuAndPermissionsAsync`).

---

## Acceptance criteria

- [ ] Existe `src/1.Domain/Security/RoleCompany.cs` que hereda `BaseTenantEntity` con `RoleId` (Guid) + navigation `Role`.
- [ ] `ApplicationRole.cs` expone `ICollection<RoleCompany> RoleCompanies`.
- [ ] Existe `src/3.Persistence/Configuration/Security/RoleCompanyConfiguration.cs` con tabla `Security.RoleCompanies`, FKs `Restrict`, índice unique filtrado `(RoleId, CompanyId) WHERE GcRecord = 0`, query filter `GcRecord == 0`.
- [ ] Migración EF Core generada y aplicada: crea `Security.RoleCompanies` con todas las columnas de `BaseTenantEntity` + `RoleId`, índice unique filtrado presente, FKs `NO ACTION`.
- [ ] Existe `src/2.Application.DTO/Security/RoleCompany/RoleCompanyDto.cs` con `{ Guid Id, Guid RoleId, string RoleName, bool IsSystemDefault, string? CreatedBy, DateTime Created }`.
- [ ] Existe `src/2.Application.DTO/Security/RoleCompany/RoleCompanyListItemDto.cs` con la firma del scope.
- [ ] Existen `CreateRoleCompanyRequestDto` y `UpdateRoleCompanyRequestDto`, ambos con `Guid RoleId` como único campo de payload.
- [ ] Existe `src/2.Application/Interface/Persistence/Security/IRoleCompanyRepository.cs` con los 6 métodos del scope.
- [ ] Existe `src/3.Persistence/Repositories/Security/RoleCompanyRepository.cs` con implementación Dapper (reads + JOIN a `Security.Roles`) + EF (writes + `SoftDeleteAsync`).
- [ ] `IRoleCompanyRepository` registrado en `ConfigureServices.cs` de Persistence.
- [ ] Existe `src/2.Application/Mappings/Security/RoleCompanyMapper.cs` con `IRoleCompanyMapper` Mapperly.
- [ ] `GetRoleCompanyByIdQueryHandler` retorna 401 `INVALID_COMPANY_ID` si `ICurrentUserService.CompanyId == Guid.Empty`.
- [ ] `GetRoleCompanyByIdQueryHandler` retorna 404 `ROLE_COMPANY_NOT_FOUND` si el `Id` no existe o pertenece a otra `CompanyId`.
- [ ] `GetRoleCompanyByIdQueryHandler` filtra por `CompanyId == ICurrentUserService.CompanyId` antes de retornar DTO.
- [ ] `GetRoleCompaniesPagedQueryHandler` clamp `pageSize [1, 100]` y `page >= 1`.
- [ ] `GetRoleCompaniesPagedQueryHandler` siempre filtra `CompanyId = @tenantId` (no acepta `CompanyId` del cliente).
- [ ] `CreateRoleCompanyCommandValidator` rechaza `RoleId == Guid.Empty` con `ValidationFailure`.
- [ ] `CreateRoleCompanyCommandHandler` retorna 401 si `CompanyId == Guid.Empty`.
- [ ] `CreateRoleCompanyCommandHandler` retorna 400 `ROLE_NOT_FOUND` si `RoleId` no existe.
- [ ] `CreateRoleCompanyCommandHandler` retorna 400 `ROLE_INACTIVE` si `RoleId` existe pero `ApplicationRole.GcRecord != 0`.
- [ ] `CreateRoleCompanyCommandHandler` retorna 409 `ROLE_COMPANY_DUPLICATE` si `ExistsActiveLinkAsync` es true.
- [ ] `CreateRoleCompanyCommandHandler` setea `CompanyId` desde `ICurrentUserService.CompanyId` (nunca del body).
- [ ] `UpdateRoleCompanyCommandHandler` retorna 404 si el `Id` no pertenece a la `CompanyId` del token.
- [ ] `UpdateRoleCompanyCommandHandler` retorna 409 `ROLE_COMPANY_DUPLICATE` si el nuevo `RoleId` ya está vinculado a la misma company.
- [ ] `UpdateRoleCompanyCommandHandler` no cambia `CompanyId` (siempre la del token).
- [ ] `DeleteRoleCompanyCommandHandler` ejecuta `UPDATE Security.RoleCompanies SET GcRecord = GcRecord + 1, LastModified = SYSUTCDATETIME(), LastModifiedBy = @userId WHERE Id = @id AND CompanyId = @tenantId AND GcRecord = 0`.
- [ ] `DeleteRoleCompanyCommandHandler` retorna 404 si `affected == 0` (id no existe o es de otra company).
- [ ] `RoleCompaniesController`:
  - `GET /api/v1/RoleCompanies/{id}` → 200 / 404, decorado con `[Authorize(Roles = "SuperAdminCompany")]`.
  - `GET /api/v1/RoleCompanies` → 200, decorado con `[Authorize(Roles = "SuperAdminCompany")]`, query params `page`, `pageSize`, `roleId`, `isActive`.
  - `POST /api/v1/RoleCompanies` → 201 / 400 / 409, decorado con `[Authorize(Roles = "SuperAdminCompany")]`, retorna `Location: /api/v1/RoleCompanies/{id}`.
  - `PUT /api/v1/RoleCompanies/{id}` → 200 / 404 / 409, decorado con `[Authorize(Roles = "SuperAdminCompany")]`.
  - `DELETE /api/v1/RoleCompanies/{id}` → 204 / 404, decorado con `[Authorize(Roles = "SuperAdminCompany")]`.
- [ ] Ningún endpoint acepta `CompanyId` en body o query; el `CompanyId` siempre se resuelve del token.
- [ ] Existe `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleCompanies/` con 8 archivos de tests cubriendo handlers y validators.
- [ ] Tests pasan: `dotnet test --filter "FullyQualifiedName~RoleCompanies"` → 0 fallidos.
- [ ] Cobertura total `JOIN.Application` ≥ 90% (gate de CI).
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` documenta los 5 nuevos endpoints.
- [ ] `SystemOption` con `ControllerName = "RoleCompanies"` existe en seed (verificado en F8); si no, agregarlo a `DatabaseSeeder`.
- [ ] Smoke test: cross-tenant access retorna 404 (no 403, no 200).
- [ ] Smoke test: doble POST con mismo `RoleId` desde misma `CompanyId` retorna 409.
- [ ] Smoke test: rol `Admin` (no `SuperAdminCompany`) recibe 403 sobre los 5 endpoints.

---

## Decisions taken and discarded

- **`RoleCompany` hereda `BaseTenantEntity`** (elegido) vs. `BaseAuditableEntity` con `CompanyId` propio (como `UserRoleCompany`). `BaseTenantEntity` es el patrón moderno (CLAUDE.md: "Every entity is scoped to a `CompanyId` tenant") y ya aporta `CompanyId` + navegación `Company`. `UserRoleCompany` quedó en el patrón viejo y queda fuera de alcance de este spec.
- **`[Authorize(Roles = "SuperAdminCompany")]` per-action** (elegido) vs. atributo nuevo o chequeo en handler. Precedente directo en `CompaniesController` (mismo archivo, mismo patrón). Mantiene consistencia con el resto de controllers administrativos de la solución.
- **`CompanyId` omitido del `RoleCompanyDto`** (elegido) vs. exponerlo. El cliente ya conoce su `CompanyId` (viene de su token); exponerlo en el response es ruido y abre vectores de discrepancia visibles (cliente vs. servidor). Solo `RoleId` + `RoleName` + auditoría.
- **`PagedResult<T>` reusable del SPEC 18** (elegido) vs. nuevo `RoleCompanyPagedResult`. Reutilizar evita proliferación de tipos paginados.
- **Índice unique filtrado `(RoleId, CompanyId) WHERE GcRecord = 0`** (elegido) vs. índice unique simple. Permite soft-deleted rows coexistir con reactivaciones futuras del mismo `(RoleId, CompanyId)` sin violar la uniqueness constraint. Coherente con `[PermissionResource]` style de SPEC 17.
- **`SoftDeleteAsync` requiere `CompanyId` en el WHERE** (elegido) vs. soft delete sin filtrar tenant. Refuerza que un `SuperAdminCompany` no pueda borrar links de otra company desde SQL injection o path manipulation. Defense-in-depth.
- **`Update` permite cambiar `RoleId` con `CompanyId` fija** (elegido) vs. bloquear `Update` (solo `Delete` + `Create`). Cambio de `RoleId` es la única mutación semánticamente útil para un junction: garbage in, garbage out. Reasignar equivaldría a `Delete` + `Create` con la ventaja de mantener `Id` estable.
- **`Update` no valida duplicado si `RoleId` no cambia** (elegido) vs. validar siempre. Skip si `RoleId == current` evita false-positive en updates que solo modifican `LastModified`.
- **`Create`/`Update` rechazan `RoleId` con `ApplicationRole.GcRecord != 0`** (elegido) vs. permitir links a roles soft-deleted. Roles soft-deleted no son asignables a usuarios; tener un link activo a un rol soft-deleted es estado inconsistente. Validación defensiva.
- **Cross-tenant access retorna 404, no 403** (elegido) vs. 403. 404 evita leak de existencia del recurso en otra company. Patrón estándar de seguridad.
- **`CompanyId` siempre del `ICurrentUserService.CompanyId`** (elegido) vs. permitir override desde controller/header. El handler nunca lee `CompanyId` del body ni del query. Imposible que un usuario de company A cree/lea/escriba links de company B.
- **Dapper para reads + EF para writes** (elegido, manda de CLAUDE.md). Reads paginados con JOIN a `Roles` se materializan rápido con Dapper. Writes (insert/update/soft delete) requieren change tracking + SaveChanges.
- **Mapperly para `IRoleCompanyMapper`** (elegido, convención del proyecto).
- **Sin endpoint "roles disponibles para esta company"** (elegido, out of scope). Sería `GET /RoleCompanies/available-roles` que devuelve `RoleDto[]` filtrados por `RoleCompany` activo. Útil para UI de asignación pero no está en el pedido. Spec aparte si se pide.
- **Validación "rol activo + link activo" en asignación de rol a usuario queda out of scope** (elegido, explícito). El consumer (`UserRoleCompany` mutation) es responsable de consultar `RoleCompany` antes de asignar. Si negocio lo pide, spec aparte lo agrega.
- **Migración EF Core manual** (elegido) vs. `dotnet ef database update` automático en cada arranque. El `MigrateAsync` del `Program.cs` la aplica al arrancar; el dev puede disparar `dotnet ef database update` para verificar manualmente.
- **`SystemOption` con `ControllerName = "RoleCompanies"` se asume/agrega en seed** (elegido) vs. desactivar `PermissionResource` filter. Sin esa fila, `DynamicAuthorizationFilter` de SPEC 17 no puede resolver permisos y retornaría 403 a usuarios sin flag. Se agrega al seed como parte de F8.
- **No se invalida cache de `RoleManager` al crear/eliminar `RoleCompany`** (elegido) vs. reset explícito. `RoleCompany` no se usa en el path de `RoleManager` (este último solo lee `Security.Roles`). Cache no se afecta.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **`UserRoleCompany` ya tiene un junction user-rol-company** y agregar `RoleCompany` introduce una capa extra a validar. Usuarios podrían asignarse roles sin que el `RoleCompany` link exista (vía `UserRoleCompany` directo). | Documentar en decisiones/spec del consumer: todo flujo de asignación de rol a usuario debe consultar `RoleCompany` y validar `(Role activo) AND (RoleCompany link activo)`. Spec aparte cubre el enforcement. Este spec solo provee la entidad. |
| **`SystemOption` con `ControllerName = "RoleCompanies"` quizás no exista en seed** → `DynamicAuthorizationFilter` retorna 403 a usuarios sin flag. | F8.6 lo verifica y lo agrega al `DatabaseSeeder` si falta. Sin esta fila, los endpoints son inaccesibles. |
| **Índice unique filtrado `(RoleId, CompanyId) WHERE GcRecord = 0` se traduce distinto en SQL Server vs. Postgres** si no se usa `HasFilter("[GcRecord] = 0")` literal. | Usar sintaxis compatible cross-DB (sin `IS` ni `LOWER`). El filtro parcial funciona en ambos motores. Verificar en `Up()` generado. |
| **Cross-tenant attack via response manipulation** — un `SuperAdminCompany` de company A lee `Id` de un `RoleCompany` de company B y construye `GET /RoleCompanies/{id}`. | Handler valida `entity.CompanyId == ICurrentUserService.CompanyId` antes de retornar DTO. Si no coincide → 404. Defense-in-depth además del filtro SQL. |
| **TOCTOU en `Create`/`Update`**: validación de duplicado y `INSERT` no son atómicos. Dos requests concurrentes podrían ambos pasar `ExistsActiveLinkAsync` y solo uno fallaría en el índice unique. | Aceptar la race condition benigna: el segundo request recibe `DbUpdateException` por violation del índice unique, que el handler traduce a 409 `ROLE_COMPANY_DUPLICATE`. El usuario ve el mismo error que si hubiera ganado el race. |
| **`RoleCompany` no expone `LastModified`/`LastModifiedBy`** en el DTO → ocultar auditoría de cambios. | Decisión consciente: el handler setea `LastModified` internamente (defensa contra actualización silenciosa). Si negocio pide, extender DTO. |
| **Soft delete de `RoleCompany` deja `UserRoleCompany` huérfanos** (asignaciones user-role que referencian un role que sigue activo pero el link de la company ya no). | Warning loggeado en `DeleteRoleCompanyCommandHandler`. Cleanup transaccional de `UserRoleCompany` queda en spec aparte. |
| **Migración `AddRoleCompanyJunction` con `Restrict` FK** puede fallar si existen `AspNetRoles` o `Companies` referenciados en otra migración que no se aplicó. | Aplicar migraciones en orden, `dotnet ef migrations list` para verificar. Raro en práctica porque `Security.Roles` y `Common.Companies` ya existen. |
| **`PermissionResource("RoleCompanies")` vs resource name real**: si se opta por nombre singular (`RoleCompany`), SPEC 17 fallback a `descriptor.ControllerName` ya strip-ea "Controller" → "RoleCompanies" (plural). Match OK. | El controller se llama `RoleCompaniesController` (plural). Coincide con intención del seed. |
| **El validador de `RoleId` no chequea que el rol existe** (solo `NotEqual(Guid.Empty)`); la validación de existencia vive en el handler. | Tests cubren los caminos `ROLE_NOT_FOUND` / `ROLE_INACTIVE`. Si FluentValidation se ejecuta en pipeline antes del handler, ambas capas aportan (defense-in-depth). |

---

## What is **not** in this spec

- Validación "rol activo + link `RoleCompany` activo" en el flujo de `UserRoleCompany`.
- Bulk assign / bulk unassign de roles a múltiples companies.
- Endpoint "qué roles tiene esta company" en formato `List<RoleDto>`.
- Hard delete físico de `RoleCompany`.
- Multi-tenancy de la entidad (ya viene por `BaseTenantEntity`).
- Migración de `RolesController` para usar `RoleCompany` en vez de `RoleManager` (SPEC 18).
- `SystemOption` con `ControllerName = "RoleCompanies"` no se modela en este spec (se agrega en seed).

Cada uno, si llega, va en su propio spec.
