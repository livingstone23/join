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

RoleDto responses now carry `permissionsCount` — the number of active `RoleSystemOption` rows the role has in the caller's tenant (always tenant-scoped, never cross-tenant). The same field is returned by both the detailed listing and the by-id endpoint (SPEC 24).

```bash
TOKEN="<jwt-from-login>"
COMPANY="00000000-0000-0000-0000-000000000001"

# Legacy: list role names (CanRead).
curl http://localhost:5000/api/v1/Roles \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Detailed: paged + filtered view (CanRead). Returns Response<PagedResult<RoleDto>>.
# Each item carries `permissionsCount` for the caller's tenant.
curl "http://localhost:5000/api/v1/Roles/detailed?page=1&pageSize=20&name=ad&isActive=true" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Get by id (CanRead). 200 with RoleDto (including `permissionsCount`) or 404 with "Rol no encontrado o inactivo.".
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
# Optional `cloneFromRoleId` (SPEC 24): when present, the new role inherits the origin role's
# active RoleSystemOption rows for the caller's tenant (same flags, same OrderMenu, same
# IsVisibleMenu). Atomics via the outer TransactionBehavior — if any clone insert fails the
# role is rolled back. Returns ROLE_NOT_FOUND (400) if the origin is missing, soft-deleted,
# or has permissions only in another tenant.
curl -X POST http://localhost:5000/api/v1/Roles \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"name":"Support","description":"Tier 1 support","isSystemDefault":false}'

# Create by cloning an existing role (cloneFromRoleId).
curl -X POST http://localhost:5000/api/v1/Roles \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"name":"Support v2","description":"Cloned from Support","isSystemDefault":false,"cloneFromRoleId":"<origin-role-guid>"}'

# Update (CanUpdate). 200 with RoleDto / 404 / 409 on rename collision.
# System-default roles reject Name changes and IsSystemDefault demotion.
curl -X PUT http://localhost:5000/api/v1/Roles/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"name":"Support Lead","description":"Tier 1 lead","isSystemDefault":false}'

# Delete (CanDelete). 204 on success / 403 for IsSystemDefault=true / 404 if missing /
# 409 ROLE_HAS_USERS if the role still has active user assignments in the caller's tenant.
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
| `ROLE_HAS_USERS` | `409 Conflict` (Roles — role still has active assignments in caller's tenant) |
| `ROLE_NOT_FOUND` / `ROLE_INACTIVE` | `400 Bad Request` |
| `ROLE_CLONE_FAILED` | `500`-flavored error (Roles — clone inserts persisted 0 rows) |

| Scenario | Status |
|----------|--------|
| Missing/invalid JWT (no `NameIdentifier` claim) | `401 Unauthorized` |
| JWT OK but missing `CompanyId` claim | `401 Unauthorized` |
| JWT OK + flag `false` | `403 Forbidden` |
| Resource name cannot be resolved (fail-closed) | `403 Forbidden` |
| Resource resolved + flag `true` | `200` / `201` / `204` |

## SystemOptions — `/api/v1/SystemOptions`

Administrative endpoints that expose the screen/menu catalog. After SPEC 21 the DTOs and command bodies carry the full set of flags (`CanRead/Create/Update/Delete/Download/Export/Execute`, `IsVisibleMenu`, `OrderMenu`, `Icon`, `ControllerName`, `ModuleName`, `ParentName`). `ModuleName` and `ParentName` are projected via SQL JOINs to `Security.SystemModules` and `Security.SystemOptions` (parent), not hardcoded. Defaults (`true`/`true`/`true`/`true`/`0`) apply when the client omits the new fields.

```bash
TOKEN="<jwt-from-superadmin>"

# Paged listing (CanRead). Returns Response<PagedResult<SystemOptionListItemDto>>.
# Each item carries ModuleName, Icon, ControllerName, all Can* flags, IsVisibleMenu, OrderMenu.
curl "http://localhost:5000/api/v1/SystemOptions?pageNumber=1&pageSize=20" \
  -H "Authorization: Bearer $TOKEN"

# Get by id (CanRead). Returns SystemOptionDto with ModuleName + ParentName populated via JOIN.
curl http://localhost:5000/api/v1/SystemOptions/{id} \
  -H "Authorization: Bearer $TOKEN"

# Create (CanCreate). The five new fields are optional; defaults make the request backwards compatible.
curl -X POST http://localhost:5000/api/v1/SystemOptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "moduleId": "<guid>",
    "name": "Manage Tickets",
    "route": "/tickets/manage",
    "icon": "ticket",
    "parentId": null,
    "controllerName": "Tickets",
    "canRead": true,
    "canCreate": true,
    "canUpdate": true,
    "canDelete": true,
    "canDownload": true,
    "canExport": true,
    "canExecute": true,
    "isVisibleMenu": true,
    "orderMenu": 5
  }'

# Update (CanUpdate). Body shape mirrors Create; Id comes from the URL.
curl -X PUT http://localhost:5000/api/v1/SystemOptions/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Manage Tickets v2",
    "route": "/tickets/v2/manage",
    "icon": "ticket",
    "parentId": null,
    "controllerName": "Tickets",
    "canRead": true,
    "canCreate": false,
    "canUpdate": true,
    "canDelete": false,
    "canDownload": true,
    "canExport": true,
    "canExecute": false,
    "isVisibleMenu": true,
    "orderMenu": 7
  }'

# Delete (CanDelete). Soft delete; no body.
curl -X DELETE http://localhost:5000/api/v1/SystemOptions/{id} \
  -H "Authorization: Bearer $TOKEN"
```

Sample response shape (post-SPEC 21):

```json
{
  "isSuccess": true,
  "message": "System option retrieved successfully.",
  "data": {
    "id": "00000000-0000-0000-0000-000000000000",
    "moduleId": "11111111-1111-1111-1111-111111111111",
    "moduleName": "Security",
    "name": "Manage Tickets",
    "route": "/tickets/manage",
    "icon": "ticket",
    "parentId": null,
    "parentName": null,
    "controllerName": "Tickets",
    "canRead": true,
    "canCreate": true,
    "canUpdate": true,
    "canDelete": true,
    "canDownload": true,
    "canExport": true,
    "canExecute": true,
    "isVisibleMenu": true,
    "orderMenu": 5,
    "created": "2026-08-11T12:00:00Z"
  }
}
```

Validator rejections (FluentValidation, returned as `400 Bad Request` via the standard pipeline):

| Field | Rule |
|-------|------|
| `OrderMenu` | `[0, 10000]` when present |
| `Icon` | ≤ 100 chars when present |
| `ControllerName` | ≤ 250 chars when present |

## RoleSystemOptions — `/api/v1/RoleSystemOptions`

Granular per-role permission rules over system options. After SPEC 22 the DTOs and command bodies carry the full set of flags (`CanRead/Create/Update/Delete/Download/Export/Execute`, `IsVisibleMenu`, `OrderMenu`) plus `CompanyName`, `RoleName`, `SystemOptionName` projected via SQL JOINs. Defaults (`true`/`true`/`true`/`true`/`0`) apply when the client omits the new fields, keeping older clients working.

After **SPEC 23** the tenant for `PUT` and `DELETE` is always derived from the authenticated caller's JWT (`CompanyId` claim or `X-Company-Id` header). The body may still carry `companyId` for backward compatibility — when present it must match the token tenant or the request is rejected with `COMPANY_MISMATCH`. `Create` keeps `companyId` in the body (the caller picks the tenant for new records); `Get*` already used the token; `GetSuperAdminPaged` keeps `companyId` as an optional cross-tenant filter.

```bash
TOKEN="<jwt-with-companyId-claim>"

# Get by id (CanRead). Returns RoleSystemOptionDto with CompanyName/RoleName/SystemOptionName populated via JOIN.
curl http://localhost:5000/api/v1/RoleSystemOptions/{id} \
  -H "Authorization: Bearer $TOKEN"

# Paged listing (CanRead). Returns Response<PagedResult<RoleSystemOptionListItemDto>>.
# The 5 new filters are optional and combine with AND against the existing ones.
curl "http://localhost:5000/api/v1/RoleSystemOptions?pageNumber=1&pageSize=20&canExport=true&orderMenu=0" \
  -H "Authorization: Bearer $TOKEN"

# Paged listing combining all five new filters.
curl "http://localhost:5000/api/v1/RoleSystemOptions?canRead=true&canExport=true&orderMenu=0&isVisibleMenu=true&canDownload=true" \
  -H "Authorization: Bearer $TOKEN"

# Create (CanCreate). Body supplies companyId — the caller decides the tenant for the new record.
curl -X POST http://localhost:5000/api/v1/RoleSystemOptions \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "companyId": "<guid>",
    "roleId": "<guid>",
    "systemOptionId": "<guid>",
    "canRead": true,
    "canCreate": true,
    "canUpdate": true,
    "canDelete": true,
    "canDownload": true,
    "canExport": false,
    "canExecute": true,
    "isVisibleMenu": true,
    "orderMenu": 5
  }'

# Update (CanUpdate). Tenant comes from the JWT; body companyId is optional.
# If supplied and different from the token -> 400 COMPANY_MISMATCH.
curl -X PUT http://localhost:5000/api/v1/RoleSystemOptions/{id} \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "canRead": true,
    "canCreate": false,
    "canUpdate": true,
    "canDelete": false,
    "canDownload": true,
    "canExport": true,
    "canExecute": true,
    "isVisibleMenu": false,
    "orderMenu": 7
  }'

# Delete (CanDelete). Tenant comes from the JWT; no query string required.
# Sending ?companyId=<guid> equal to the token is accepted; mismatch returns 400 COMPANY_MISMATCH.
curl -X DELETE http://localhost:5000/api/v1/RoleSystemOptions/{id} \
  -H "Authorization: Bearer $TOKEN"

# Bulk replace (CanUpdate, SPEC 25). Atomic transaction. Items represents the DESIRED FINAL SET
# for the role in the caller's tenant; rows in the DB absent from Items are soft-deleted,
# rows in Items absent from the DB are inserted, rows present in both have their flags overwritten.
# Capped at 500 items per request (BULK_TOO_LARGE). Duplicates rejected (BULK_DUPLICATE_OPTION).
# Invalidates the permission + sidebar caches for every user currently assigned to the role.
curl -X PUT http://localhost:5000/api/v1/RoleSystemOptions/bulk \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "roleId": "<guid>",
    "items": [
      { "systemOptionId": "<guid-1>", "canRead": true,  "canCreate": false, "canUpdate": false, "canDelete": false, "canDownload": false, "canExport": false, "canExecute": false },
      { "systemOptionId": "<guid-2>", "canRead": true,  "canCreate": true,  "canUpdate": true,  "canDelete": false, "canDownload": true,  "canExport": true,  "canExecute": false }
    ]
  }'

# Permissions matrix (CanRead, SPEC 25). Returns the full grid grouped by SystemModule.
# Every active SystemOption is included even when the role has no RoleSystemOption row for it
# (Granted collapses to all-false). 404 ROLE_NOT_FOUND when the role does not exist or is inactive.
curl "http://localhost:5000/api/v1/RoleSystemOptions/matrix?roleId=<guid>" \
  -H "Authorization: Bearer $TOKEN"

# SuperAdmin cross-tenant paged listing (requires SuperAdmin role).
SA_TOKEN="<jwt-from-superadmin>"
curl "http://localhost:5000/api/v1/RoleSystemOptions/superadmin/all?canDownload=true" \
  -H "Authorization: Bearer $SA_TOKEN"
```

Sample response shape (post-SPEC 22):

```json
{
  "isSuccess": true,
  "message": "Role system option retrieved successfully.",
  "data": {
    "id": "00000000-0000-0000-0000-000000000000",
    "companyId": "11111111-1111-1111-1111-111111111111",
    "companyName": "Acme Corp",
    "roleId": "22222222-2222-2222-2222-222222222222",
    "roleName": "Manager",
    "systemOptionId": "33333333-3333-3333-3333-333333333333",
    "systemOptionName": "Manage Tickets",
    "canRead": true,
    "canCreate": true,
    "canUpdate": true,
    "canDelete": true,
    "canDownload": true,
    "canExport": false,
    "canExecute": true,
    "isVisibleMenu": true,
    "orderMenu": 5,
    "created": "2026-08-11T12:00:00Z"
  }
}
```

Validator rejections (FluentValidation, returned as `400 Bad Request` via the standard pipeline):

| Field | Rule |
|-------|------|
| `OrderMenu` | `[0, 10000]` when present |

`bool` fields (`CanDownload`, `CanExport`, `CanExecute`, `IsVisibleMenu`) accept any value; defaults cover omission.

Bulk endpoint (`PUT /bulk`, SPEC 25) rejections:

| Cause | Status | Error code |
|-------|--------|------------|
| `RoleId` empty | `400` | `RoleId required.` |
| `items` null / empty | `400` | `BULK_EMPTY` |
| `items.Count > 500` | `400` | `BULK_TOO_LARGE` |
| `items` contain duplicate `SystemOptionId` | `400` | `BULK_DUPLICATE_OPTION` |
| Role missing / inactive | `404` | `ROLE_NOT_FOUND` |
| Token missing `CompanyId` claim | `401` | `TENANT_REQUIRED` |

Matrix endpoint (`GET /matrix?roleId=`, SPEC 25) rejections:

| Cause | Status | Error code |
|-------|--------|------------|
| Role missing / inactive | `404` | `ROLE_NOT_FOUND` |
| Token missing `CompanyId` claim | `401` | `TENANT_REQUIRED` |

Auth failures on PUT/DELETE:

| Cause | Status |
|-------|--------|
| JWT missing / `CompanyId` claim empty (`X-Company-Id` not set either) | `400` `INVALID_COMPANY_ID` |
| Body `companyId` (PUT) or query `companyId` (DELETE) differs from token tenant | `400` `COMPANY_MISMATCH` |
| Rule not found for the caller's tenant | `404` `ROLE_SYSTEM_OPTION_NOT_FOUND` |
