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

## Status codes

| Scenario | Status |
|----------|--------|
| Missing/invalid JWT (no `NameIdentifier` claim) | `401 Unauthorized` |
| JWT OK but missing `CompanyId` claim | `401 Unauthorized` |
| JWT OK + flag `false` | `403 Forbidden` |
| Resource name cannot be resolved (fail-closed) | `403 Forbidden` |
| Resource resolved + flag `true` | `200` / `201` / `204` |
