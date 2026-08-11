# CURL examples — JOIN API

Base URL (Development): `http://localhost:5000` (or whatever `launchSettings.json` binds).
All tenant-scoped endpoints require the `X-Company-Id` header in addition to the bearer token.

## Auth

```bash
# Login
curl -X POST http://localhost:5000/api/v1/Auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@join.local","password":"ChangeMe!1"}'

# Refresh
curl -X POST http://localhost:5000/api/v1/Auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refresh-token>"}'
```

Both endpoints are decorated with `[AllowAnonymous]`; they bypass the dynamic permission filter.

## Standard CRUD (HTTP-verb → flag default)

```bash
TOKEN="<jwt-from-login>"
COMPANY="00000000-0000-0000-0000-000000000001"

# Read (CanRead)
curl http://localhost:5000/api/v1/Persons \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Create (CanCreate)
curl -X POST http://localhost:5000/api/v1/Persons \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"firstName":"Ada","lastName":"Lovelace","identificationNumber":"12345"}'

# Update (CanUpdate)
curl -X PUT http://localhost:5000/api/v1/Persons/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"firstName":"Ada","lastName":"Byron"}'

# Delete (CanDelete)
curl -X DELETE http://localhost:5000/api/v1/Persons/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"
```

## Override per-action — `[RequirePermission]`

When the endpoint's semantic flag diverges from the HTTP-verb default, decorate the action with `[RequirePermission(PermissionFlags.*)]`. The filter honors the override instead of the verb mapping.

### `[RequirePermission(CanExport)]` on a `GET` endpoint

```csharp
[HttpGet("export")]
[RequirePermission(PermissionFlags.CanExport)]
public async Task<IActionResult> Export()
{
    // ... build Excel/CSV/PDF ...
}
```

```bash
# A role with CanRead=true but CanExport=false → 403
# A role with CanExport=true → 200 + file stream
curl -o persons.xlsx \
  http://localhost:5000/api/v1/Persons/export \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"
```

### `[RequirePermission(CanExecute)]` on a `POST` endpoint

```csharp
[HttpPost("run-profile")]
[RequirePermission(PermissionFlags.CanExecute)]
public async Task<IActionResult> RunProfile([FromBody] RunProfileRequest request)
{
    // ... kick off background job / workflow ...
}
```

```bash
curl -X POST http://localhost:5000/api/v1/Persons/run-profile \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"profileId":"<guid>"}'
```

### `[RequirePermission(CanDownload)]` on a `GET` endpoint

```csharp
[HttpGet("{id}/download")]
[RequirePermission(PermissionFlags.CanDownload)]
public async Task<IActionResult> DownloadDocument(Guid id)
{
    // ... return FileStreamResult ...
}
```

## `[PermissionResource]` without arguments

```csharp
[ApiController]
[Route("api/v1/[controller]")]
// No [PermissionResource] literal: filter infers "Persons" from the class name.
public class PersonsController(IMediator mediator) : ControllerBase { ... }
```

For controllers whose class name does not match the seed `SystemOption.ControllerName` (sub-resources like `PersonContactController` → `"Persons"`), keep the explicit literal:

```csharp
[ApiController]
[Route("api/v1/[controller]")]
[PermissionResource("Persons")]  // alias to the Persons SystemOption row
public class PersonContactController(IMediator mediator) : ControllerBase { ... }
```

## Bypass attributes

```csharp
[ApiController]
[Route("api/v1/[controller]")]
[AllowAnonymous]  // no auth, no permission check (e.g. login)
public class AuthController { ... }

[ApiController]
[Route("api/v1/[controller]")]
[SkipDynamicAuthorization]  // auth required, but the resource-based filter is skipped
                           // (e.g. GetSidebarMenu — the menu itself is the source of truth)
public class UsersController { ... }
```

## Roles — `/api/v1/Roles`

The Roles controller keeps the legacy `GET /` endpoint (`IEnumerable<string>`) for selectors and adds a detailed CRUD surface. The detailed endpoints are gated by the `Roles` permission resource and the HTTP-verb default flag. The preview endpoint `GET /{id}/users` is gated by the `Roles` resource and the `SuperAdminCompany` role; it powers the "usuarios afectados" preview before saving role changes (SPEC 20).

```bash
TOKEN="<jwt-from-login>"
COMPANY="00000000-0000-0000-0000-000000000001"

# Legacy: list role names (CanRead).
curl http://localhost:5000/api/v1/Roles \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Detailed: paged + filtered view (CanRead). Returns Response<PagedResult<RoleDto>>.
curl "http://localhost:5000/api/v1/Roles/detailed?page=1&pageSize=20&name=ad&isActive=true" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Get by id (CanRead). 200 with RoleDto or 404 with "Rol no encontrado o inactivo.".
curl http://localhost:5000/api/v1/Roles/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Users affected by role (CanRead). Paged; pageSize clamped to [1,100]. Requires SuperAdminCompany.
# 200 with Response<PagedResult<RoleAffectedUserDto>> / 404 if role missing or soft-deleted
# ("Rol no encontrado o inactivo.") / 401 INVALID_COMPANY_ID if X-Company-Id absent.
curl "http://localhost:5000/api/v1/Roles/{id}/users?page=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Create (CanCreate). 201 + Location header / 409 on duplicate name.
curl -X POST http://localhost:5000/api/v1/Roles \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"name":"Support","description":"Tier 1 support","isSystemDefault":false}'

# Update (CanUpdate). 200 with RoleDto / 404 / 409 on rename collision.
# System-default roles reject Name changes and IsSystemDefault demotion.
curl -X PUT http://localhost:5000/api/v1/Roles/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"name":"Support Lead","description":"Tier 1 lead","isSystemDefault":false}'

# Delete (CanDelete). 204 on success / 403 for IsSystemDefault=true / 404 if missing.
curl -X DELETE http://localhost:5000/api/v1/Roles/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"
```

## RoleCompanies — `/api/v1/RoleCompanies`

The RoleCompany junction endpoints decide which Roles are available within a tenant. All five endpoints require the `SuperAdminCompany` role and the `RoleCompanies` permission resource; `CompanyId` is resolved exclusively from the caller's JWT (never from the body or query string).

```bash
TOKEN="<jwt-from-superadmin-company>"
COMPANY="00000000-0000-0000-0000-000000000001"

# Paged listing (CanRead). roleId/isActive optional. Returns Response<PagedResult<RoleCompanyListItemDto>>.
# pageSize clamps to [1, 100]; page clamps to >= 1.
curl "http://localhost:5000/api/v1/RoleCompanies?page=1&pageSize=20&roleId=<guid>&isActive=true" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Get by id (CanRead). 200 with RoleCompanyDto / 404 if missing, soft-deleted, or cross-tenant.
curl http://localhost:5000/api/v1/RoleCompanies/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Create (CanCreate). 201 + Location header / 400 if RoleId missing/inactive / 409 if active link exists.
curl -X POST http://localhost:5000/api/v1/RoleCompanies \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"roleId":"<guid>"}'

# Update (CanUpdate). 200 with RoleCompanyDto / 404 if missing/cross-tenant / 409 if new RoleId collides.
# CompanyId is preserved from the token; only RoleId changes.
curl -X PUT http://localhost:5000/api/v1/RoleCompanies/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"roleId":"<new-guid>"}'

# Delete (CanDelete). 204 on success / 404 if missing, soft-deleted, or cross-tenant.
curl -X DELETE http://localhost:5000/api/v1/RoleCompanies/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"
```

Error message → status mapping (handlers encode outcomes as codes in `Response.Message`):

| Message | Status |
|---------|--------|
| `INVALID_COMPANY_ID` | `401 Unauthorized` |
| `ROLE_COMPANY_NOT_FOUND` | `404 Not Found` |
| `ROLE_COMPANY_DUPLICATE` | `409 Conflict` |
| `ROLE_NOT_FOUND` / `ROLE_INACTIVE` | `400 Bad Request` |

| Scenario | Status |
|----------|--------|
| Missing/invalid JWT (no `NameIdentifier` claim) | `401 Unauthorized` |
| JWT OK but missing `CompanyId` claim | `401 Unauthorized` |
| JWT OK + flag `false` | `403 Forbidden` |
| Resource name cannot be resolved (fail-closed) | `403 Forbidden` |
| Resource resolved + flag `true` | `200` / `201` / `204` |
