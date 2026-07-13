# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## System overview

JOIN is a multi-tenant, omnichannel CRM built on .NET 10 as a Clean Architecture reference implementation. Every entity is scoped to a `CompanyId` tenant.

## Commands

```bash
# Restore / build / run
dotnet restore
dotnet build
dotnet run --project src/4.Services.WebApi/JOIN.Services.WebApi.csproj

# Tests (single unit test project, xUnit)
dotnet test
dotnet test --filter "FullyQualifiedName~CreatePersonCommandHandlerTests"

# EF Core migrations (run from src/4.Services.WebApi so the Design package + DbContext resolve)
dotnet ef migrations add <Name> --project ../3.Persistence --startup-project .
dotnet ef database update --project ../3.Persistence --startup-project .
```

Migrations apply automatically at startup (`Program.cs` calls `context.Database.MigrateAsync()`), and the `DatabaseSeeder` runs after any pending migration or, in Development with no pending migrations, re-runs the idempotent menu/permissions seed.

CI (`.github/workflows/ci.yml`) builds in Release and runs `dotnet test` with Coverlet, **failing the build if line coverage drops below 90%**. Keep new Application-layer code covered by unit tests under `tests/UnitTests/JOIN.Application.UnitTest`, mirroring the `UseCases/<Area>/<Feature>/Commands|Queries/<Name>` folder structure of the source.

No `docker-compose` file exists yet despite the README mentioning one — local dev currently points at a real SQL Server instance via `appsettings.json` connection strings.

## Layer map

```
src/1.Domain            JOIN.Domain            Entities, enums, repository interfaces. No external deps.
src/2.Application.DTO   JOIN.Application.DTO   Immutable DTO records shared across layers.
src/2.Application       JOIN.Application       CQRS handlers (MediatR), FluentValidation, Mapperly mappers, Response<T>.
src/3.Infrastructure    JOIN.Infrastructure    ISqlConnectionFactory impl, JWT, SendGrid, Identity security logic, DI wiring.
src/3.Persistence       JOIN.Persistence       DbContext, EF configurations, migrations, repositories, UnitOfWork, seeder.
src/4.Services.WebApi   JOIN.Services.WebApi   Controllers, Program.cs, middlewares, filters.
tests/UnitTests          JOIN.Application.UnitTest   MSTest-style xUnit tests (Moq, AutoFixture, FluentAssertions) for Application handlers/mappers.
```

Dependency direction is strict: Domain has no project references; Application depends only on Domain + DTO; Persistence/Infrastructure depend on Application+Domain; WebApi depends on Application+Infrastructure+Persistence. Each layer is registered via its own `AddXServices`/`AddInfrastructure`-style extension method (`2.Application/Common/ConfigureServices.cs`, `3.Persistence/Configuration/ConfigureServices.cs`, `3.Infrastructure/DependencyInjection.cs`), all called from `Program.cs`.

## CQRS convention (mandatory for new features)

Every use case lives at `src/2.Application/UseCases/<Area>/<Feature>/{Commands|Queries}/<Name>/` with three files: `<Name>Command|Query.cs`, `<Name>CommandHandler|QueryHandler.cs`, `<Name>CommandValidator.cs` (FluentValidation, commands only).

- **Commands (writes)**: go through `IUnitOfWork` + EF Core repositories (`_unitOfWork.GetRepository<T>()` for generic CRUD, or a named repository like `_unitOfWork.Persons` for custom queries) so multiple aggregates commit atomically via `SaveAsync`.
- **Queries (reads)**: go through `ISqlConnectionFactory` + raw Dapper SQL for performance — never load via EF Core's change tracker for a query handler. Every hand-written query filters `WHERE CompanyId = @TenantId AND GcRecord = 0` (soft-delete + tenant isolation). Cross-DB portability matters: use `CONCAT()` not vendor date functions, and branch pagination clauses on `LIMIT/OFFSET` (Postgres) vs `OFFSET...FETCH NEXT` (SQL Server) — see `GetPersonsPagedQueryHandler` for the pattern.
- Handlers return `Response<T>` (`src/2.Application/Common/Response.cs`) — never throw for expected business failures; set `IsSuccess = false` / use `Response<T>.Error(message, errors)` instead.
- Tenant resolution: every handler that touches tenant data checks `currentUserService.CompanyId == Guid.Empty` up front and short-circuits with an error `Response<T>`. `ICurrentUserService` resolves `CompanyId` from JWT claims or the `X-Company-Id` header.
- IDs are always `Guid`, never `int`.
- Handlers never return Domain entities directly — map to DTOs via the project's Mapperly mappers (`I<Entity>Mapper`, source-generated, in `2.Application/Mappings`).

## Auth & security

ASP.NET Core Identity (`ApplicationUser`/`ApplicationRole`, GUID keys) + JWT bearer auth, with a custom `DynamicAuthorizationFilter` applied globally to all controllers rather than per-endpoint `[Authorize]` policies. Authorization is claims/policy-based, not role-based. Global exception handling goes through `GlobalExceptionHandler` + RFC 7807 `ProblemDetails`.

## API surface

Controllers live under `src/4.Services.WebApi/Controllers/{Admin|Security|Messaging}`. In Development, OpenAPI JSON is at `/openapi/v1.json` and the Scalar UI at `/scalar/v1` (root `/` redirects there). Health checks: `/health/ready` (JSON UI-formatted) and `/health-ui`. See `CURL_REQUESTS.md` / `POSTMAN_CURL.txt` for example requests against each endpoint, including the required `X-Company-Id` header.
