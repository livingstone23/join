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

## Users — `/api/v1/Users` (SPEC 28)

SPEC 28 reshapes the user-management surface of `UsersController`: roles are now written
to `Security.UserRoleCompanies` (the table that actually governs authorization), the
`/companies` endpoints are restricted to `SuperAdmin`, and a bulk role endpoint plus
a paginated `reports/my-company` endpoint are added. Tenant comes from the JWT (SPEC 23).

```bash
TOKEN="<jwt-from-login>"
COMPANY="00000000-0000-0000-0000-000000000001"
USER_ID="<target-user-guid>"

# 1) Replace the full role set of a user (PUT /{userId}/roles).
#    Body is the role NAMES (unchanged contract); resolution is tenant-scoped.
#    Writes Security.UserRoleCompanies (NOT Security.UserRoles) and invalidates
#    the permission cache for the user/tenant pair.
curl -X PUT "http://localhost:5000/api/v1/Users/${USER_ID}/roles" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{"roles":["Admin","Contador"]}'

# 2) Bulk add/remove roles across up to 200 users in one transaction (PUT /roles/bulk).
#    Delta semantics: addRoleIds and removeRoleIds are applied per user; rows not
#    in either list are untouched. Declared BEFORE PUT /{userId}/roles so the
#    literal "roles/bulk" segment wins the routing match.
curl -X PUT "http://localhost:5000/api/v1/Users/roles/bulk" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{
    "userIds":["11111111-1111-1111-1111-111111111111","22222222-2222-2222-2222-222222222222"],
    "addRoleIds":["33333333-3333-3333-3333-333333333333"],
    "removeRoleIds":["44444444-4444-4444-4444-444444444444"]
  }'

# 3) Effective permissions of a user inside the caller's tenant (GET /{userId}/effective-permissions).
#    No ?companyId= — the tenant comes from the JWT. The handler runs a fresh
#    Dapper query (no PermissionService cache) so the panel reflects live DB state.
curl "http://localhost:5000/api/v1/Users/${USER_ID}/effective-permissions" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# 4) List every active company linked to a user (GET /{userId}/companies).
#    SPEC 28: restricted to SuperAdmin (was leaking cross-tenant before).
curl "http://localhost:5000/api/v1/Users/${USER_ID}/companies" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# 5) Add or refresh a user's membership in a company (POST /{userId}/companies).
#    SuperAdmin only. Body's companyId is the tenant of the new membership, not
#    the caller's tenant. Idempotent; reactivates soft-deleted rows in place
#    so the (UserId, CompanyId) unique index never collides.
curl -X POST "http://localhost:5000/api/v1/Users/${USER_ID}/companies" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY" \
  -H "Content-Type: application/json" \
  -d '{
    "companyId":"55555555-5555-5555-5555-555555555555",
    "roleIds":["33333333-3333-3333-3333-333333333333"]
  }'

# 6) Remove a user's membership in a company (DELETE /{userId}/companies/{companyId}).
#    SuperAdmin only. Guard order: 409 CANNOT_REMOVE_LAST_COMPANY beats
#    409 CANNOT_REMOVE_DEFAULT_COMPANY when a user has only one company.
curl -X DELETE "http://localhost:5000/api/v1/Users/${USER_ID}/companies/55555555-5555-5555-5555-555555555555" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# 7) Paged user-management report for the caller's company (GET /reports/my-company).
#    Shape changed in SPEC 28 from a flat collection to PagedResult<T>.
#    pageNumber >= 1 (clamped), pageSize in [1, 50] (default 10, clamped).
#    search matches Email and "FirstName ' ' LastName" case-insensitive.
#    isActive absent → both; isActive=false → inactive users (Dapper bypasses
#    the EF global filter that would otherwise hide them).
curl "http://localhost:5000/api/v1/Users/reports/my-company?pageNumber=1&pageSize=10&search=juan&isActive=false" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"
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

## Audit / Security bitácora (SPEC 29)

The endpoint requires `CanRead` on the `Audit` resource (resolved via the
`SystemOption` of the same `ControllerName`). A SuperAdmin can pass
`allTenants=true` to span every tenant; everyone else sees only their own.

```bash
TOKEN="<jwt-from-login>"
COMPANY="00000000-0000-0000-0000-000000000001"

# Default page (pageNumber=1, pageSize=20) — only the caller's tenant
curl "http://localhost:5000/api/v1/Audit/security" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Filter by entity (case-insensitive), with paging
curl "http://localhost:5000/api/v1/Audit/security?entity=RoleSystemOption&pageNumber=1&pageSize=50" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Filter by date range and actor
curl "http://localhost:5000/api/v1/Audit/security?fromDate=2026-08-01&toDate=2026-08-31&changedBy=<userId>" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Filter by a specific row of one of the audited entities
curl "http://localhost:5000/api/v1/Audit/security?entity=UserRoleCompany&entityId=<urc-id>" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# SuperAdmin only — every tenant's events
curl "http://localhost:5000/api/v1/Audit/security?allTenants=true&pageSize=100" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"

# Filter by action (Created | Updated | Deleted)
curl "http://localhost:5000/api/v1/Audit/security?action=Deleted&entity=Role" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $COMPANY"
```

Available query parameters:

| Param | Type | Notes |
|-------|------|-------|
| `entity` | `Role` \| `User` \| `RoleSystemOption` \| `UserRoleCompany` \| `UserCompany` \| `RoleCompany` | Case-insensitive; invalid value → `400 INVALID_ENTITY` |
| `entityId` | Guid | Filters to one row of the chosen entity |
| `changedBy` | user-id string | Exact match |
| `action` | `Created` \| `Updated` \| `Deleted` | Case-insensitive; invalid value → `400 INVALID_ACTION` |
| `fromDate` / `toDate` | ISO date | Inclusive `fromDate`, exclusive `toDate` (`toDate + 1 day`); `fromDate > toDate` → `400` |
| `pageNumber` | int | Clamped to `>= 1`, default `1` |
| `pageSize` | int | Clamped to `[1, 100]`, default `20` |
| `allTenants` | bool | Honored only for SuperAdmin; ignored silently otherwise |

Endpoint errors:

| Cause | Status | Code |
|-------|--------|------|
| Token missing `CompanyId` | `400` | `TENANT_REQUIRED` |
| `entity` outside the enum | `400` | `INVALID_ENTITY` |
| `action` outside the enum | `400` | `INVALID_ACTION` |
| Missing / invalid token | `401` | — |
| Caller lacks `CanRead` on `Audit` | `403` | — |
| `fromDate > toDate` | `400` | FluentValidation |

Response shape (one item per bitácora row):

```json
{
  "isSuccess": true,
  "message": "Security audit log retrieved.",
  "data": {
    "items": [
      {
        "id": "…",
        "entityName": "RoleSystemOption",
        "entityId": "…",
        "entityLabel": "Contador → Personas",
        "action": "Updated",
        "changedBy": "…",
        "changedByName": "Ana Pérez",
        "changedAtUtc": "2026-08-15T14:22:07Z",
        "ipAddress": "190.12.4.8",
        "changes": [
          { "field": "CanDelete", "oldValue": "True", "newValue": "False" }
        ],
        "metadata": "{\"bulkOperationId\":\"…\"}"
      }
    ],
    "pageNumber": 1,
    "pageSize": 20,
    "totalCount": 42,
    "totalPages": 3
  }
}
```

`changes[]` is the union of keys from `OldValuesJson` / `NewValuesJson`; rows
with corrupt JSON surface with an empty `changes[]` instead of failing the page.
`metadata` carries `bulkOperationId` for grouped bulk writes (RoleSystemOptions
matrix upsert and bulk role updates).

## TicketUserCompanies — `/api/v1/TicketUserCompanies` (SPEC 34)

Tenant-scoped CRUD over the ticket-management roster (the `User ↔ Company` link
with `IsSuperAdminTicket`, `CanFinishTicket` and `CanResolveTicket` flags).
Tenant-scoped endpoints are gated by the flags attached to the `TicketUserCompanies`
resource; the cross-tenant `system-wide` endpoint is gated by the `SuperAdmin` role.

```bash
# 1. List (paginated, tenant-scoped, optional filters)
curl -s "$BASE_URL/api/v1/TicketUserCompanies?pageNumber=1&pageSize=10" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID"

# Filter by capability flag (nullable bools)
curl -s "$BASE_URL/api/v1/TicketUserCompanies?isSuperAdminTicket=true" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID"
```

```bash
# 2. Cross-tenant list (SuperAdmin only)
curl -s "$BASE_URL/api/v1/TicketUserCompanies/system-wide?companyName=JOIN" \
  -H "Authorization: Bearer $TOKEN"
# Responds 403 for any role other than SuperAdmin.
```

```bash
# 3. Get by id (tenant-scoped; returns 404 for cross-tenant ids)
curl -s "$BASE_URL/api/v1/TicketUserCompanies/$ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID"
```

```bash
# 4. Create — UserId comes from the body; CompanyId always comes from the token.
curl -s -X POST "$BASE_URL/api/v1/TicketUserCompanies" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "11111111-1111-1111-1111-111111111111",
    "isSuperAdminTicket": true,
    "canFinishTicket": true,
    "canResolveTicket": true
  }'
# Returns 201 with Location header pointing to /api/v1/TicketUserCompanies/{id}.
# Returns 409 TICKET_USER_COMPANY_DUPLICATE when an active row already exists
# for the (UserId, CompanyId) pair.
# Returns 400 USER_NOT_FOUND / USER_NOT_IN_TENANT when the user is not a
# member of the tenant.
```

```bash
# 5. Update — payload cannot reassign UserId; delete + create to change user.
curl -s -X PUT "$BASE_URL/api/v1/TicketUserCompanies/$ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{
    "isSuperAdminTicket": false,
    "canFinishTicket": true,
    "canResolveTicket": true
  }'
# Returns 409 LAST_SUPERADMIN_TICKET when the only active row with
# IsSuperAdminTicket = true for the tenant is being turned off.
```

```bash
# 6. Soft delete
curl -s -X DELETE "$BASE_URL/api/v1/TicketUserCompanies/$ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID"
# Returns 409 LAST_SUPERADMIN_TICKET when the row is the only active
# IsSuperAdminTicket for the tenant.
```

All endpoints require the `X-Company-Id` header except `system-wide`. The
filtered unique index `(UserId, CompanyId) WHERE GcRecord = 0` enforces
"one active row per user-company pair" at the database level even if a
concurrent request slips past the in-memory check.

## Tickets — `/api/v1/Tickets` (SPEC 35)

CRUD over support tickets, plus three lifecycle actions introduced by SPEC 35
(`reassign`, `finish`, `notes`). All endpoints are tenant-scoped and share the
`Tickets` permission resource (HTTP-verb default flags: GET → CanRead, POST →
CanCreate, PUT → CanUpdate, DELETE → CanDelete). A token without `CompanyId`
returns **401** on every endpoint below — including the pre-existing five, which
previously surfaced it as 400. This is the documented behavior change that landed
with SPEC 35 (see also `CLAUDE.md`).

Error codes returned by the ticket endpoints map to HTTP status as follows:

| Code | HTTP |
|------|------|
| `COMPANY_REQUIRED` / `USER_REQUIRED` | 401 |
| `TICKET_NOT_FOUND` | 404 |
| `TICKET_REASSIGN_FORBIDDEN` / `TICKET_FINISH_FORBIDDEN` | 403 |
| `TARGET_NOT_ELIGIBLE_RESOLVER` / `INVALID_ASSIGNED_USER` / `INVALID_ASSIGNED_USER_TENANT` / `INVALID_ASSIGNED_USER_NOT_RESOLVER` / `INVALID_TICKET_STATUS` / `TICKET_STATUS_NOT_FINAL` / `INVALID_LOG_TYPE` | 400 |
| `TICKET_ALREADY_FINISHED` / `TICKET_CODE_IN_USE` | 409 |

```bash
# 1. List (paginated, tenant-scoped, optional filters)
curl -s "$BASE_URL/api/v1/Tickets?pageNumber=1&pageSize=10" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID"

# Filter by status, assignee, customer, project, etc.
curl -s "$BASE_URL/api/v1/Tickets?assignedToUserId=$USER_ID&isVisibleToExternals=true" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID"
```

```bash
# 2. Get by id — returns the flattened ticket projection plus a `logs` collection
#    (filtered by visibility: see SPEC 35 F9).
curl -s "$BASE_URL/api/v1/Tickets/$ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID"
# Returns 404 TICKET_NOT_FOUND for cross-tenant ids or missing tickets.
# Logs marked with IsOnlyForCreatedAndAssigned = 1 are hidden from viewers
# who are not the creator, not the current assignee, and not a tenant
# IsSuperAdminTicket.
```

```bash
# 3. Create — optional initial assignment; if AssignedToUserId is supplied,
#    the target must hold CanResolveTicket = 1 in the tenant's TicketUserCompany
#    roster (otherwise 400 INVALID_ASSIGNED_USER_NOT_RESOLVER).
curl -s -X POST "$BASE_URL/api/v1/Tickets" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Portal returns 500 on checkout",
    "description": "Customer cannot complete purchase flow.",
    "estimatedTime": 4,
    "consumedTime": 0,
    "ticketStatusId": "22222222-2222-2222-2222-222222222222",
    "ticketComplexityId": "33333333-3333-3333-3333-333333333333",
    "timeUnitId": "44444444-4444-4444-4444-444444444444",
    "channelId": "55555555-5555-5555-5555-555555555555",
    "personId": "66666666-6666-6666-6666-666666666666",
    "assignedToUserId": "77777777-7777-7777-7777-777777777777"
  }'
# Returns 201 with Location header pointing to /api/v1/Tickets/{id}.
# Returns 409 TICKET_CODE_IN_USE when the generated TICK-YYYYMM-XXXX code
# collides with an existing ticket (rare; retry).
# Returns 400 INVALID_ASSIGNED_USER / INVALID_ASSIGNED_USER_TENANT /
#   INVALID_ASSIGNED_USER_NOT_RESOLVER when the destination is not a roster
#   resolver of the current tenant.
```

```bash
# 4. Update — IMPORTANT contract change in SPEC 35: the payload NO LONGER
#    accepts `assignedToUserId`. Reassignment is the exclusive responsibility
#    of PUT /Tickets/{id}/reassign. A client that still sends the field
#    silently has it ignored by deserialization; the ticket's assignee is
#    preserved unchanged. This is deliberate — see SPEC 35 "Identified risks"
#    for the migration guidance.
curl -s -X PUT "$BASE_URL/api/v1/Tickets/$ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Portal returns 500 on checkout (updated)",
    "description": "Customer cannot complete purchase flow. Triaging.",
    "estimatedTime": 6,
    "consumedTime": 1,
    "ticketStatusId": "22222222-2222-2222-2222-222222222222",
    "ticketComplexityId": "33333333-3333-3333-3333-333333333333",
    "timeUnitId": "44444444-4444-4444-4444-444444444444",
    "channelId": "55555555-5555-5555-5555-555555555555"
  }'
# Returns 200 with the updated TicketDto.
# Returns 404 TICKET_NOT_FOUND for cross-tenant ids.
```

```bash
# 5. Soft delete
curl -s -X DELETE "$BASE_URL/api/v1/Tickets/$ID" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID"
```

```bash
# 6. Reassign — actor must hold IsSuperAdminTicket = 1 for the tenant;
#    the target must hold CanResolveTicket = 1. Covers both the first
#    assignment of a previously-unassigned ticket and the reassignment of
#    one already assigned. Reassigning to the same user is a successful
#    no-op (no new Reassignment log entry, but the target still has to
#    pass the same validation gates).
curl -s -X PUT "$BASE_URL/api/v1/Tickets/$ID/reassign" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{
    "newAssignedToUserId": "77777777-7777-7777-7777-777777777777"
  }'
# Returns 200 with the updated TicketDto.
# Returns 403 TICKET_REASSIGN_FORBIDDEN when the actor lacks IsSuperAdminTicket.
# Returns 400 INVALID_ASSIGNED_USER / INVALID_ASSIGNED_USER_TENANT /
#   TARGET_NOT_ELIGIBLE_RESOLVER when the target is not a roster resolver.
# Returns 404 TICKET_NOT_FOUND for cross-tenant ids.
```

```bash
# 7. Finish — transition the ticket to a status with IsFinal = true. The
#    actor must hold CanFinishTicket = 1 OR IsSuperAdminTicket = 1; the
#    second flag is the documented bypass for ticket super-admins. A
#    ticket already in a final status returns 409 (re-finalization is
#    refused — see SPEC 35 decisions).
curl -s -X PUT "$BASE_URL/api/v1/Tickets/$ID/finish" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{
    "ticketStatusId": "88888888-8888-8888-8888-888888888888",
    "resolutionSummary": "Customer confirmed fix by phone."
  }'
# Returns 200 with the updated TicketDto.
# Returns 403 TICKET_FINISH_FORBIDDEN when the actor lacks both flags.
# Returns 400 INVALID_TICKET_STATUS / TICKET_STATUS_NOT_FINAL when the
#   target status does not exist or is not marked IsFinal.
# Returns 409 TICKET_ALREADY_FINISHED when the ticket is already in a
#   final status.
# `resolutionSummary` is optional (max 500 chars); when blank the audit log
# entry is recorded with the default text "Ticket finalizado".
```

```bash
# 8. Add note — appends a TicketLog row of LogType.InternalNote (2) or
#    ExternalNote (3). Visibility is derived server-side from the LogType:
#    internal notes are hidden from third viewers; external notes are
#    public to every reader of the ticket.
curl -s -X POST "$BASE_URL/api/v1/Tickets/$ID/notes" \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-Company-Id: $TENANT_ID" \
  -H "Content-Type: application/json" \
  -d '{
    "logType": 2,
    "summary": "Customer escalated by phone at 14:30."
  }'
# Returns 201 with Response<TicketLogDto> (the freshly-created log entry).
# Returns 400 INVALID_LOG_TYPE when logType is not 2 or 3 (validator
#   catches it; handler has a defensive duplicate that emits the same code).
# Returns 404 TICKET_NOT_FOUND for cross-tenant ids.
# Allowed logType values:
#   2 = InternalNote  → IsOnlyForCreatedAndAssigned = true  (hidden from third viewers)
#   3 = ExternalNote  → IsOnlyForCreatedAndAssigned = false (visible to every reader)
```
