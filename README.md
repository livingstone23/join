# JOIN — Enterprise .NET 10 Reference Architecture

![CI](https://github.com/livingstone23/join/actions/workflows/ci.yml/badge.svg)
![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![License](https://img.shields.io/badge/license-MIT-green)

Hi bud! 👋 I'm Livingstone. After 15+ years of building software, I've realized that the difference between a project that scales and one that becomes a nightmare is **Architecture**.

I built **JOIN** as a personal "Gold Standard". It's not a toy sample — it's a working multi-tenant CRM backend that I use to demonstrate enterprise-grade patterns end-to-end: real CQRS, real multi-tenancy, real resilience, real CI with an enforced coverage gate.

## 🎯 The Mission: Why SOLID & Clean Architecture?

The core goal of this project is to demonstrate that **SOLID principles** aren't just interview questions — they are the foundation of maintainable software.

By using **Clean Architecture**, business logic stays decoupled from delivery mechanisms and infrastructure choices. Whether the app talks to SQL Server or PostgreSQL, the **Domain** layer never knows or cares.

## 🏗️ Layers & Dependency Direction

```
JOIN.Domain            →  no project references (pure C#, entities, enums, repository interfaces)
JOIN.Application.DTO   →  immutable DTO records shared across layers
JOIN.Application       →  depends on Domain + DTO only (CQRS handlers, validation, mapping)
JOIN.Infrastructure    →  depends on Application + Domain (SQL connections, JWT, SendGrid, Identity)
JOIN.Persistence       →  depends on Application + Domain (DbContext, EF configs, repositories, UoW)
JOIN.Services.WebApi   →  depends on Application + Infrastructure + Persistence (controllers, Program.cs)
```

Dependency direction is enforced by the project references themselves, not by convention: `Domain` cannot see EF Core, ASP.NET Core, or any driver — it has zero package references beyond ASP.NET Core Identity's abstractions. Each outer layer is wired up through its own `AddXServices` extension method, all composed from `Program.cs`.

## ✅ What This Project Actually Implements

Every item below exists in the codebase today — not on a roadmap.

### CQRS & the MediatR Pipeline

- **Commands vs. Queries**, physically separated per feature: `UseCases/<Area>/<Feature>/{Commands|Queries}/<Name>/`.
- **Commands** go through `IUnitOfWork` + EF Core repositories so multiple aggregates commit atomically.
- **Queries** go through `Dapper` + raw, hand-tuned SQL — deliberately bypassing EF Core's change tracker for read performance, with pagination clauses branched between `LIMIT/OFFSET` (Postgres) and `OFFSET…FETCH NEXT` (SQL Server) for cross-database portability.
- Five composable **MediatR pipeline behaviors** wrap every request, in order: `UnhandledExceptionBehavior` → `LoggingBehavior` → `PerformanceBehavior` → `ValidationBehavior` (FluentValidation) → `TransactionBehavior` (opens/commits/rolls back an explicit EF Core transaction for any command implementing `ITransactionalCommand<T>`).
- **`Response<T>` result pattern** — handlers never throw for expected business failures; they return `Response<T>.Error(...)`, keeping control flow predictable and exception stacks reserved for genuinely unexpected errors.

### Multi-Tenancy & Data Access

- Every domain entity is scoped to a `CompanyId`. Tenant resolution happens once, in `ICurrentUserService`, from the JWT claims (or the `X-Company-Id` header for anonymous/bootstrap flows) — handlers short-circuit with an error `Response<T>` if the tenant can't be resolved.
- **Soft delete** everywhere via `GcRecord`; every hand-written query filters `WHERE CompanyId = @TenantId AND GcRecord = 0`.
- **Dual persistence** by design, not by accident: EF Core for transactional writes, Dapper for high-performance reads.
- **Multi-database support**: SQL Server and PostgreSQL are both first-class, switchable at runtime via the `DatabaseProvider` setting — health checks, migrations, and query pagination all branch on it.
- **Domain encapsulation**: entities expose `private set` properties and mutate only through factory methods and invariant-enforcing coordinators (`PersonContactPrimaryCoordinator`, `PersonAddressDefaultCoordinator`) that guarantee things like "only one primary contact per person" at the domain layer, not in application code.
- **Riok.Mapperly** source-generated mappers between entities and DTOs — no runtime reflection.

### Authentication & Authorization

- **ASP.NET Core Identity** (`ApplicationUser` / `ApplicationRole`, GUID keys) backing **JWT Bearer** authentication.
- Authorization is **claims/policy-based**, enforced globally by a custom `DynamicAuthorizationFilter` applied to every controller — not scattered `[Authorize]` attributes.
- Custom password validator (`CustomPasswordValidator`) enforcing a configurable policy: minimum length, no repetitive characters, no common sequences, no username substrings.
- **Global exception handling** via `GlobalExceptionHandler` + native **RFC 7807 ProblemDetails**, including trace IDs and timestamps on every error response.

### Resilience & Observability

- **Serilog** dual-sink logging: human-readable console output in Development, `CompactJsonFormatter` (structured JSON) in Production.
- **Polly v8** (`Microsoft.Extensions.Http.Resilience`'s `AddStandardResilienceHandler`) wraps outbound SendGrid HTTP calls with retry/circuit-breaker/timeout policies.
- EF Core commands use an explicit `CommandTimeout(30)` — deliberately **not** `EnableRetryOnFailure`, because EF Core's retrying execution strategy is incompatible with the explicit, user-initiated transactions `TransactionBehavior` opens for `ITransactionalCommand` handlers.
- **Health Checks** for the active database provider, surfaced at `/health/ready` and a live dashboard at `/health-ui`, with configurable **email alerting** (via SendGrid) when checks start failing.
- **Rate limiting** with two tiers (`Global` and `Strict`, both configurable — limit, window, queue behavior) plus a `DynamicStrictRateLimitingMiddleware` for sensitive endpoints, returning RFC 7807-shaped `429` responses.
- **Automatic database migrations** at startup (`context.Database.MigrateAsync()`), with a connection-retry loop that opens a fresh `DbContext`/connection on every attempt, followed by an idempotent seeder (`DatabaseSeeder`) for menus, permissions, and catalog data.

### API Surface

- **API versioning** via `Asp.Versioning` (route pattern `api/v{version:apiVersion}/...`).
- Interactive docs via **Scalar** (not Swagger UI) at `/scalar/v1`, backed by .NET 10's native OpenAPI generation at `/openapi/v1.json`. The root `/` redirects straight to the Scalar UI in Development.
- Modules exposed today, grouped by area:

  | Area | Controllers |
  |---|---|
  | **Admin** | Persons, Person Address / Contact / Employment / Business Profile / Financial Profile, Company Modules, System Modules, Areas, Projects, Customers, Genders, Identification Types, Income Ranges, Industries, Entity Statuses, Tax Regimes |
  | **Security** | Auth, Account, Users, Roles, Role System Options, System Options, Workspaces |
  | **Messaging** | Tickets, Ticket Statuses, Ticket Complexities, Ticket Company Defaults, Time Units |
  | **Common** | Companies, Countries, Provinces, Municipalities, Regions, Street Types, Communication Channels |

  See `CURL_REQUESTS.md` / `POSTMAN_CURL.txt` for runnable request examples against each of these, including the required `X-Company-Id` header.

### Testing & CI

- **Unit tests**: xUnit + Moq + AutoFixture + FluentAssertions, covering `Application`-layer handlers, validators, and mappers, mirroring the `UseCases/<Area>/<Feature>/Commands|Queries/<Name>` source structure.
- **Integration tests**: `Testcontainers.MsSql` + `WebApplicationFactory<Program>` spin up a real, ephemeral SQL Server per run and drive the API in-process end-to-end — no mocks below the HTTP boundary except outbound email.
- **CI gate**: GitHub Actions builds in Release, runs the unit suite with Coverlet, and **fails the build if line coverage on the Application layer drops below 90%** — then runs the full integration suite separately (excluded from the coverage gate on purpose). PRs get an automatic coverage report comment.

### Containerization

- Multi-stage **Alpine**-based `Dockerfile`: SDK image for restore/publish, minimal `aspnet:10.0-alpine` for the final runtime. Project files are copied and restored before the rest of the source, so dependency layers stay cached across rebuilds.
- Ships with `icu-libs` and `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` explicitly — Alpine's aspnet image disables globalization by default, which breaks `Microsoft.Data.SqlClient`'s culture-aware connection string parsing.
- `docker-compose.yml` builds and runs the API container on port `8080`, reading secrets/connection strings from a local `.env` file. It intentionally does **not** bundle a database — point `ConnectionStrings:DefaultConnection` at your own SQL Server or PostgreSQL instance.

## 📂 Project Structure

```plaintext
src/
 ├── 1.Domain            # Entities, enums, repository interfaces — zero external deps
 ├── 2.Application.DTO   # Immutable DTO records shared across layers
 ├── 2.Application       # CQRS handlers (MediatR), FluentValidation, Mapperly mappers, Response<T>
 ├── 3.Infrastructure    # ISqlConnectionFactory impl, JWT, SendGrid, Identity security logic, DI wiring
 ├── 3.Persistence       # DbContext, EF configurations, migrations, repositories, UnitOfWork, seeder
 └── 4.Services.WebApi   # Controllers, Program.cs, middlewares, filters
tests/
 ├── UnitTests           # xUnit + Moq + AutoFixture + FluentAssertions — Application layer, 90% gate
 └── IntegrationTests    # Testcontainers.MsSql + WebApplicationFactory — full API flows
```

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A reachable **SQL Server** or **PostgreSQL** instance (local dev currently points at SQL Server via `appsettings.json`)
- Docker, if you want to run the integration test suite (uses Testcontainers)

### Run locally

```bash
dotnet restore
dotnet build
dotnet run --project src/4.Services.WebApi/JOIN.Services.WebApi.csproj
```

Migrations apply automatically at startup, and the seeder populates menus/permissions/catalogs on first run. Once it's up, open `/scalar/v1` for interactive API docs.

### Run in Docker

```bash
docker-compose up -d --build
```

Set `ConnectionStrings__DefaultConnection` (and any other secrets — JWT key, SendGrid API key) in a `.env` file next to `docker-compose.yml` before starting; the container doesn't bundle a database.

### EF Core migrations

Run from `src/4.Services.WebApi` so the Design package and `DbContext` resolve correctly:

```bash
dotnet ef migrations add <Name> --project ../3.Persistence --startup-project .
dotnet ef database update --project ../3.Persistence --startup-project .
```

## 🧪 Running Tests

```bash
# Everything
dotnet test

# Just one handler
dotnet test --filter "FullyQualifiedName~CreatePersonCommandHandlerTests"

# Unit tests with the same coverage gate CI enforces
dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj \
  /p:CollectCoverage=true /p:CoverletOutputFormat=cobertura /p:Threshold=90 /p:ThresholdType=line

# Integration tests (needs Docker)
dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj
```

## ⚙️ Configuration Reference

Key sections in `appsettings.json` (see `appsettings.Development.json` for environment overrides):

| Section | Purpose |
|---|---|
| `DatabaseProvider` / `ConnectionStrings` | `"SqlServer"` or `"PostgreSQL"`, plus the connection string EF Core, Dapper, and health checks all read from |
| `Jwt` | Signing key, issuer, audience, access/refresh token lifetimes |
| `PasswordPolicy` | Minimum length and the repetitive/common-sequence/username restrictions `CustomPasswordValidator` enforces |
| `AreaPagination` | Default/max page size for paginated list queries |
| `Performance` | Millisecond threshold `PerformanceBehavior` uses to flag slow requests |
| `SendGrid` | Outbound email provider credentials, wrapped in a Polly resilience handler |
| `HealthCheckAlerts` | Recipients and subject prefix for email alerts fired when health checks fail |
| `HealthChecksUI` | Dashboard configuration for `/health-ui` |
| `RateLimiting` | `Global` and `Strict` tiers — permit limits, window, queue behavior, and the RFC 7807 rejection payload |
| `Serilog` | Sinks and minimum log levels, per environment |

## 📖 API Documentation

- Interactive docs (Development): `/scalar/v1`
- Raw OpenAPI document: `/openapi/v1.json`
- Health status (JSON, UI-formatted): `/health/ready`
- Health dashboard: `/health-ui`
- Ready-to-run request examples: [`CURL_REQUESTS.md`](CURL_REQUESTS.md), [`POSTMAN_CURL.txt`](POSTMAN_CURL.txt)

## 📜 License

MIT — see [`LICENSE`](LICENSE). Use it, fork it, ship it.

---

💡 **Why JOIN?**
In a Senior/Lead role, consistency is key. This project demonstrates how to decouple infrastructure from domain logic, making the system easy to test and evolve. Whether you are building a small service or a complex microsystem, these patterns will save you months of refactoring.

Note to my fellow devs: this is a living project based on real-world experience. I'm constantly updating it with the latest .NET features and industry best practices. If you find it useful, give it a ⭐️ and let's build better software together!

Feel free to explore, open an issue, or use it as a base for your next big project. Let's grow together!
