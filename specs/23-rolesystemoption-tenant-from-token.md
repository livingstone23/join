# SPEC 23 — RoleSystemOption tenant derivation from JWT for PUT/DELETE

> **Status:** Implementado
> **Depends on:** SPEC 17 (PermissionResource / `ICurrentUserService`), SPEC 22 (DTO completeness)
> **Date:** 2026-08-12
> **Objective:** Eliminar la dependencia explícita de `CompanyId` en `PUT /api/v1/RoleSystemOptions/{id}` (body) y `DELETE /api/v1/RoleSystemOptions/{id}` (query) para `RoleSystemOptionsController`. Derivar el tenant siempre desde `ICurrentUserService.CompanyId` (claim del JWT o header `X-Company-Id`), al igual que ya hacen `GET` y `GET (paged)`. Reducir la fricción del cliente y eliminar el bug frecuente donde DELETE recibe el `companyId` en body (ignorado por `[FromQuery]`) y devuelve `ROLE_SYSTEM_OPTION_NOT_FOUND` falsamente. No tocar `Create` (el body sigue siendo la fuente legítima del tenant — el caller decide dónde crea) ni `GetSuperAdminPaged` (cross-tenant explícito).

---

## Scope

**In:**

- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/UpdateRoleSystemOption/UpdateRoleSystemOptionCommand.cs`: hacer `CompanyId` opcional con `Guid? CompanyId = null`. El handler lo ignora si llega y siempre usa `currentUserService.CompanyId`.
- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/UpdateRoleSystemOption/UpdateRoleSystemOptionCommandHandler.cs`: inyectar `ICurrentUserService`. Si `currentUserService.CompanyId == Guid.Empty` → `INVALID_COMPANY_ID` (mismo mensaje que Create/Get). Usar ese valor en `GetTrackedActiveByIdAndCompanyAsync` y `GetWithNamesAsync`. Si llega `request.CompanyId` con valor distinto al del token, rechazar con `COMPANY_MISMATCH` (defense-in-depth — el tenant del caller debe coincidir con el tenant del registro).
- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/DeleteRoleSystemOption/DeleteRoleSystemOptionCommand.cs`: añadir `Guid? CompanyId = null` (opcional, para validación opcional de mismatch).
- `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/DeleteRoleSystemOption/DeleteRoleSystemOptionCommandHandler.cs`: inyectar `ICurrentUserService`. Si `CompanyId == Guid.Empty` → `INVALID_COMPANY_ID`. Usar el tenant del token para el delete. Si el body/query trae `CompanyId` y difiere del token → `COMPANY_MISMATCH`.
- `src/4.Services.WebApi/Controllers/Security/RoleSystemOptionsController.cs`:
  - `Update(Guid id, [FromBody] UpdateRoleSystemOptionCommand command, ...)`: el body ya no exige `companyId`. La línea `command with { Id = id }` se mantiene. Doc XML actualizado.
  - `Delete(Guid id, CancellationToken cancellationToken)`: quitar `[FromQuery] Guid companyId`. El handler deriva el tenant del token. Doc XML actualizado.
  - `GetById`, `GetPaged`, `Create`, `GetSuperAdminPaged`: sin cambios.
- Tests unitarios nuevos / actualizados en `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleSystemOptions/`:
  - `UpdateRoleSystemOptionCommandHandlerTests.cs`: añadir caso `CompanyId == Guid.Empty` retorna `INVALID_COMPANY_ID`; caso `request.CompanyId` distinto del token retorna `COMPANY_MISMATCH`; caso happy path donde el handler ignora `request.CompanyId` y usa el del token.
  - `DeleteRoleSystemOptionCommandHandlerTests.cs` (nuevo): mock de `ICurrentUserService`, mock del repo. Cubrir: `CompanyId == Guid.Empty` → `INVALID_COMPANY_ID`; mismatch → `COMPANY_MISMATCH`; happy path sin enviar `CompanyId` en el command.
- `CURL_REQUESTS.md`: actualizar el bloque `RoleSystemOptions` — eliminar `companyId` del body en PUT y de la query en DELETE; documentar el header `X-Company-Id` (alternativa cuando el JWT no trae el claim).

**Out of scope:**

- Cambios en `Create` (sigue recibiendo `CompanyId` por body — es el contrato legítimo).
- Cambios en `GetSuperAdminPaged` (el `companyId` opcional ahí es un filtro explícito, no auth).
- Cambios en `RoleSystemOptionDto` / `RoleSystemOptionListItemDto` (sin nuevos campos).
- Cambios en queries paged / by-id (ya derivan tenant del token).
- Cambios en `DynamicAuthorizationFilter` (enforcement de flags sigue diferido).
- Cambios en otros controllers (Roles, SystemOptions, RoleCompanies siguen con su contrato actual).
- Migración EF / seeder (no aplica).

---

## Data model

Sin entidades nuevas. Cambios viven en `UpdateRoleSystemOptionCommand`, `DeleteRoleSystemOptionCommand` (param opcional nuevo), handlers (inyección `ICurrentUserService`).

### `UpdateRoleSystemOptionCommand` (extendido)

```csharp
public sealed record UpdateRoleSystemOptionCommand(
    [property: JsonIgnore] Guid Id,
    Guid? CompanyId = null,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool CanDownload = true,
    bool CanExport = true,
    bool CanExecute = true,
    bool IsVisibleMenu = true,
    int? OrderMenu = 0)
    : ITransactionalCommand<Response<RoleSystemOptionDto>>;
```

`CompanyId` pasa a `Guid?` opcional con default `null`. Mantiene compatibilidad con clientes que aún lo envíen; si lo envían y difiere del token → `COMPANY_MISMATCH`.

### `DeleteRoleSystemOptionCommand` (extendido)

```csharp
public sealed record DeleteRoleSystemOptionCommand(
    [property: JsonIgnore] Guid Id,
    Guid? CompanyId = null)
    : ITransactionalCommand<Response<Guid>>;
```

Mismo patrón: `CompanyId` opcional. Si lo pasan, se valida contra el token.

---

## Implementation plan

### F1 — Commands

1. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/UpdateRoleSystemOption/UpdateRoleSystemOptionCommand.cs`: cambiar `Guid CompanyId` → `Guid? CompanyId = null`. Mantener el resto intacto.
2. Editar `src/2.Application/UseCases/Security/RoleSystemOptions/Commands/DeleteRoleSystemOption/DeleteRoleSystemOptionCommand.cs`: añadir `Guid? CompanyId = null` como segundo param posicional (después de `Id`).
3. `dotnet build` → 0 errores. Clientes existentes que envían `companyId` en body siguen funcionando — la firma sigue aceptando ese param.

### F2 — Handlers

1. Editar `UpdateRoleSystemOptionCommandHandler.cs`:
   - Inyectar `ICurrentUserService` (tercer ctor param, después de `IUnitOfWork` y `IRoleSystemOptionMapper`).
   - Validar `currentUserService.CompanyId == Guid.Empty` → `INVALID_COMPANY_ID`.
   - Si `request.CompanyId.HasValue && request.CompanyId.Value != currentUserService.CompanyId` → `COMPANY_MISMATCH` con errores `["CompanyId in the body does not match the authenticated tenant."]`.
   - Pasar `currentUserService.CompanyId` a `GetTrackedActiveByIdAndCompanyAsync` y `GetWithNamesAsync` (reemplaza el uso de `request.CompanyId`).
2. Editar `DeleteRoleSystemOptionCommandHandler.cs`:
   - Inyectar `ICurrentUserService`.
   - Mismas validaciones (`INVALID_COMPANY_ID` + `COMPANY_MISMATCH`).
   - Usar `currentUserService.CompanyId` en `GetTrackedActiveByIdAndCompanyAsync` / equivalente del delete repo.
3. `dotnet build` → 0 errores.

### F3 — DI / Handler resolution

1. Verificar que `ICurrentUserService` está registrado en `Program.cs` / `ConfigureServices` (ya está — se usa en GetById y GetPaged). No requiere cambios.
2. Confirmar que MediatR resuelve los nuevos ctors vía DI. Si no, registrar manualmente.

### F4 — Controller

1. Editar `RoleSystemOptionsController.cs`:
   - `Update(Guid id, [FromBody] UpdateRoleSystemOptionCommand command, ...)`: doc XML actualizado — eliminar mención de "companyId in the body". El handler ya no lo usa.
   - `Delete(Guid id, CancellationToken cancellationToken)`: eliminar el param `[FromQuery] Guid companyId`. El `new DeleteRoleSystemOptionCommand(id)` se queda sin `companyId` (default null). Doc XML actualizado.
   - `GetById`, `GetPaged`, `Create`, `GetSuperAdminPaged`: sin cambios.
2. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.

### F5 — Tests unitarios

1. Editar `UpdateRoleSystemOptionCommandHandlerTests.cs`:
   - Caso nuevo: `currentUserService.CompanyId == Guid.Empty` → `INVALID_COMPANY_ID`.
   - Caso nuevo: `request.CompanyId = X` y `currentUserService.CompanyId = Y` con `X != Y` → `COMPANY_MISMATCH`.
   - Caso nuevo: `request.CompanyId = null` → handler usa `currentUserService.CompanyId` y persiste.
   - Actualizar el caso happy path existente para mockear `ICurrentUserService`.
2. Crear `DeleteRoleSystemOptionCommandHandlerTests.cs`:
   - Mock `IUnitOfWork`, `IRoleSystemOptionsRepository`, `ICurrentUserService`.
   - Caso `CompanyId == Guid.Empty` → `INVALID_COMPANY_ID`, sin tocar el repo.
   - Caso mismatch → `COMPANY_MISMATCH`.
   - Caso happy path: el comando no trae `CompanyId`, el handler usa el del token, repo marca `GcRecord` y persiste.
3. `dotnet test --filter "FullyQualifiedName~RoleSystemOptions"` → 0 fallidos. Cobertura ≥ 90% en handlers modificados.

### F6 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test` con `--collect:"XPlat Code Coverage"` → gate 90% OK.
3. Smoke test manual con `dotnet run`:
   - `PUT /api/v1/RoleSystemOptions/{id}` con body **sin** `companyId` → 200 con DTO actualizado.
   - `PUT` con `companyId` en body igual al del token → 200 (compat).
   - `PUT` con `companyId` en body distinto del token → 400 con `COMPANY_MISMATCH`.
   - `DELETE /api/v1/RoleSystemOptions/{id}` sin query → 200 (deriva del token).
   - `DELETE` con `?companyId=X` donde `X == token` → 200 (compat).
   - `DELETE` con `?companyId=X` donde `X != token` → 400 con `COMPANY_MISMATCH`.
4. `CURL_REQUESTS.md`: actualizar bloque `RoleSystemOptions` con los nuevos ejemplos (sin `companyId` en PUT body, sin query en DELETE).

---

## Acceptance criteria

- [ ] `UpdateRoleSystemOptionCommand.CompanyId` cambia de `Guid` (requerido) a `Guid? CompanyId = null` (opcional).
- [ ] `DeleteRoleSystemOptionCommand.CompanyId` es `Guid?` con default `null`.
- [ ] `UpdateRoleSystemOptionCommandHandler` inyecta `ICurrentUserService`. Si `currentUserService.CompanyId == Guid.Empty` → `INVALID_COMPANY_ID`. Si `request.CompanyId.HasValue && request.CompanyId.Value != currentUserService.CompanyId` → `COMPANY_MISMATCH`. Si todo OK, persiste con el `CompanyId` del token.
- [ ] `DeleteRoleSystemOptionCommandHandler` mismo patrón.
- [ ] `RoleSystemOptionsController.Update` doc XML ya no menciona `companyId` obligatorio en body.
- [ ] `RoleSystemOptionsController.Delete` ya no tiene `[FromQuery] Guid companyId`. El endpoint pasa a ser `DELETE /api/v1/RoleSystemOptions/{id}` sin query params obligatorios.
- [ ] `Create`, `GetById`, `GetPaged`, `GetSuperAdminPaged` sin cambios estructurales.
- [ ] Smoke: `PUT` sin `companyId` en body funciona y persiste con el tenant del token.
- [ ] Smoke: `PUT` con `companyId` en body igual al token funciona (compat con clientes viejos).
- [ ] Smoke: `PUT` con `companyId` distinto al token → 400 con `COMPANY_MISMATCH`.
- [ ] Smoke: `DELETE` sin query funciona (deriva del token).
- [ ] Smoke: `DELETE` con `?companyId=X` (X == token) funciona (compat).
- [ ] Smoke: `DELETE` con `?companyId=X` (X != token) → 400 con `COMPANY_MISMATCH`.
- [ ] Tests en `tests/UnitTests/JOIN.Application.UnitTest/Security/RoleSystemOptions/` cubren los 3 caminos del handler Update + los 3 del handler Delete.
- [ ] `dotnet test --filter "FullyQualifiedName~RoleSystemOptions"` → 0 fallidos.
- [ ] Cobertura total `JOIN.Application` ≥ 90% (gate CI).
- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `CURL_REQUESTS.md` documenta los nuevos curls (sin `companyId`) y el comportamiento de `COMPANY_MISMATCH`.
- [ ] `DynamicAuthorizationFilter` no fue tocado (enforcement de flags sigue diferido).
- [ ] Migración EF no requerida (sin cambios de schema).

---

## Decisions taken and discarded

- **PUT/DELETE derivan del token, Create mantiene body** (elegido) vs. los 3 derivan del token. Create es creación — el caller decide el tenant del nuevo registro (caso de uso administrativo donde un SuperAdmin o seed crea reglas para otra empresa). PUT/DELETE son mutaciones sobre registros existentes — el tenant debe ser el del token (no hay razón legítima para que difiera). Mantener Create intacto respeta el contrato actual sin sobre-restringir.
- **`CompanyId` en command opcional + validación `COMPANY_MISMATCH`** (elegido) vs. eliminar el campo completamente. Mantener el campo opcional cubre a clientes legacy que aún lo envían; el mismatch detection es defense-in-depth contra un cliente que manda un tenant distinto (probable bug o intento de cross-tenant attack).
- **Códigos de error: `INVALID_COMPANY_ID` + `COMPANY_MISMATCH`** (elegido) vs. un único `TENANT_ERROR`. Mensajes separados facilitan debugging del lado del cliente y mantienen consistencia con el patrón ya existente (`INVALID_COMPANY_ID` ya se usa en Create, GetById, GetPaged).
- **`COMPANY_MISMATCH` retorna 400** (elegido) vs. 403. Es un error del cliente (envió data inválida), no una violación de permisos per se. El `403` ya lo maneja `DynamicAuthorizationFilter` cuando el flag no aplica.
- **DELETE sin query params** (elegido) vs. mantener `companyId` opcional en query. Si el token trae el tenant, el query es ruido. Eliminarlo simplifica el contrato. Clientes que aún lo manden no rompen — el handler lo lee si existe y valida mismatch.
- **No tocar GetSuperAdminPaged** (elegido) vs. derivar también ese endpoint del token. `GetSuperAdmin` es cross-tenant explícito; el `companyId` ahí es un filtro de narrowing, no auth. Su contrato actual es correcto.
- **No tocar otros controllers (Roles, SystemOptions, RoleCompanies)** (elegido) vs. aplicar el mismo patrón globalmente. Cada uno tiene su propio contrato; este spec se enfoca en `RoleSystemOption`. Specs aparte si se quiere propagar.
- **Tests nuevos solo para Update/Delete handlers** (elegido) vs. también tests de integración via `Testcontainers`. La cobertura del SQL via mock `ISqlConnectionFactory` ya está cubierta en SPEC 22. El cambio es de auth (DI de `ICurrentUserService`), no de SQL.

---

## Identified risks

- **Cross-tenant leak**: el handler Update/Delete antes aceptaba `companyId` desde body/query, lo que en teoría permitía mutar registros de otro tenant si el caller conocía un `id` ajeno. **Este spec lo elimina** — el tenant siempre es el del token. Mitigación adicional: la validación `COMPANY_MISMATCH` detecta clientes que envían un `companyId` distinto (probable bug). Defense-in-depth.
- **Clientes legacy que envían `companyId` en body PUT o query DELETE**: el cambio mantiene el param opcional con validación de mismatch. Si el `companyId` que mandan coincide con el token → funciona idéntico. Si difiere → `400`. Riesgo de breaking change mínimo.
- **`ICurrentUserService` no resuelve `CompanyId` (claim faltante)**: el handler corta con `INVALID_COMPANY_ID` (mismo path que Create/Get). `DynamicAuthorizationFilter` ya retorna 401 antes si el token no es válido. Sin nuevos vectores.
- **`COMPANY_MISMATCH` puede ser ruidoso en logs**: clientes que copian ciegamente un `companyId` hardcodeado van a empezar a recibir 400. Aceptable — es el comportamiento correcto.
- **Cache de permisos / cache de tenant**: `ICurrentUserService` se invoca por request; sin caché. Sin riesgo nuevo.
- **Tests existentes de Update/Delete handler**: asumen contrato anterior (`request.CompanyId` como fuente). Hay que actualizarlos para mockear `ICurrentUserService` y validar los 3 nuevos caminos. Incluido en F5.
- **`Create` y `Update` con distinto contrato de tenant**: Create sigue con `CompanyId` en body, Update desde token. Puede confundir a clientes nuevos. Documentado en `CURL_REQUESTS.md` y doc XML del controller.