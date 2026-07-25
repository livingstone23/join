# SPEC 16 — Agregar CanExport y CanExecute a SystemOption

> **Status:** Implementado
> **Depends on:** [[12-role-system-option-canexport-canexecute]] (introdujo los flags en `RoleSystemOption`; este spec los replica en `SystemOption`).
> **Date:** 2026-07-25
> **Objective:** Agregar las propiedades `CanExport` y `CanExecute` a `JOIN.Domain.Security.SystemOption` con default `true`, replicarlas en `SystemOptionConfiguration`, en una nueva migración EF Core y en los registros seed de `DatabaseSeeder`, sin tocar el filtro de autorización (enforcement queda para un spec futuro).

---

## Scope

**In:**

- `src/1.Domain/Security/SystemOption.cs`: agregar `public bool CanExport { get; set; } = true;` y `public bool CanExecute { get; set; } = true;` con XML doc idéntico al de `RoleSystemOption` (SPEC 12) — "CanExport: Excel/CSV/PDF report generation"; "CanExecute: run a workflow or batch operation".
- `src/3.Persistence/Configuration/Security/SystemOptionConfiguration.cs`: mapear `CanExport` y `CanExecute` con `HasDefaultValue(true)` (mismo patrón que `CanRead`/`CanCreate`/`CanUpdate`/`CanDelete`/`CanDownload`).
- `src/3.Persistence/Migrations/<timestamp>_AddCanExportCanExecuteToSystemOption.cs`: nueva migración EF Core con `AddColumn<bool>` para `Security.SystemOptions.CanExport` y `Security.SystemOptions.CanExecute`, ambas con `defaultValue: true` y `nullable: false` (default `bit NOT NULL`).
- `src/3.Persistence/Seed/DatabaseSeeder.cs`:
  - Extender el record `SystemOptionSeed` (línea 2721) con `bool? CanExport = null` y `bool? CanExecute = null` (nullable, mismo patrón que `CanDownload`).
  - En `SeedAdministrativeSystemOptionsAsync` (línea ~2000): derivar `canExport = seed.CanExport ?? true` y `canExecute = seed.CanExecute ?? true` (default `true` en seed, no `CanRead` como `RoleSystemOption`).
  - Setear `CanExport`/`CanExecute` en el `new SystemOption {...}` (insert path), en `hasChanges` (update diff) y en el bloque de asignación final.
  - Reservar el orden de los flags en los seed records existentes (no es necesario reescribirlos, los nulls caen al default `true`).
- Verificación: `dotnet build` + `dotnet ef database update` + `dotnet test`.

**Out of scope:**

- Cualquier cambio en `DynamicAuthorizationFilter` / `IPermissionService` / `PermissionService` — el enforcement queda para un spec futuro (puede requerir nuevo attribute tipo `[RequireExportPermission]` o extender `PermissionResourceAttribute` con un enum `RequiredAction`).
- No se replican los flags en `RoleSystemOption` (ya existen desde SPEC 12).
- No se cambia el `DEFAULT` constraint en `RoleSystemOption.CanExport`/`CanExecute` (siguen en `false` por diseño: SPEC 12).
- No se cambia `DatabaseSeeder.GetRoleSystemOptionSeeds()` ni la siembra de `RoleSystemOption` (ya setean `CanExport: true`/`CanExecute: true` para roles privilegiados).
- No se actualizan los `*.Designer.cs` ni `ApplicationDbContextModelSnapshot.cs` (EF Core los regenera automáticamente al correr `dotnet ef migrations add`).
- No se cambian pruebas unitarias — la lógica de filters/servicios no se toca.

---

## Data model

Dos columnas nuevas en `Security.SystemOptions` (misma tabla que `CanRead`/`CanCreate`/`CanUpdate`/`CanDelete`/`CanDownload`):

| Column | Type | Nullable | Default | Notes |
|---|---|---|---|---|
| `CanExport` | `bit` | NOT NULL | `((1))` | Replica del flag en `Security.RoleSystemOptions` (SPEC 12). Permiso por-opción (no por-rol). |
| `CanExecute` | `bit` | NOT NULL | `((1))` | Replica del flag en `Security.RoleSystemOptions` (SPEC 12). Permiso por-opción (no por-rol). |

Sin nuevas tablas, sin nuevas FK, sin cambios en `RoleSystemOption`. El record `SystemOptionSeed` (en `DatabaseSeeder.cs`) gana dos parámetros nullable: `bool? CanExport = null`, `bool? CanExecute = null`.

---

## Implementation plan

1. **Domain — agregar propiedades**: editar `src/1.Domain/Security/SystemOption.cs`. Después de `CanDownload` (línea 61), agregar `CanExport` y `CanExecute` con `= true` y XML doc idéntico al de `RoleSystemOption` (líneas 55-63).
2. **Configuration — mapear defaults**: en `src/3.Persistence/Configuration/Security/SystemOptionConfiguration.cs`, después de `builder.Property(o => o.CanDownload).HasDefaultValue(true);` (línea 44), agregar las dos líneas equivalentes para `CanExport` y `CanExecute`.
3. **Migración EF Core**: correr `dotnet ef migrations add AddCanExportCanExecuteToSystemOption --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi`. Verificar que el archivo generado crea las dos columnas en `Security.SystemOptions` con `defaultValue: true`. Editar si EF nombró la migración distinto.
4. **Seeder — extender record**: en `src/3.Persistence/Seed/DatabaseSeeder.cs`, agregar `bool? CanExport = null` y `bool? CanExecute = null` al record `SystemOptionSeed` (línea 2721) — mismo patrón que `CanDownload`.
5. **Seeder — insert path**: en `SeedAdministrativeSystemOptionsAsync` (línea ~2056), calcular `var canExport = seed.CanExport ?? true;` y `var canExecute = seed.CanExecute ?? true;` junto a `canDownload` (línea 2043). Setear ambos en el `new SystemOption {...}` antes de `_context.SystemOptions.Add(option)`.
6. **Seeder — update diff y asignación**: en la misma función, agregar al `hasChanges` (línea 2098) las comparaciones `option.CanExport != canExport` y `option.CanExecute != canExecute`. En el bloque de asignación final (línea 2117), agregar `option.CanExport = canExport;` y `option.CanExecute = canExecute;` junto a `option.CanDownload = canDownload;`.
7. **Compilar**: `dotnet build -c Release`. Confirmar 0 errores.
8. **Aplicar migración**: arrancar la API — `MigrateAsync()` corre automático en `Program.cs`. Verificar en la tabla `Security.SystemOptions` que las dos columnas existen con `DEFAULT 1`.
9. **Verificar seed**: en Development, `DatabaseSeeder` re-corre los seeds (no-op si no hay cambios). Confirmar que las opciones existentes en `Security.SystemOptions` ahora tienen `CanExport=1` y `CanExecute=1` (gracias al `DEFAULT` constraint, no requiere acción manual).
10. **dotnet test**: suite debe pasar en verde (los 12 fallos pre-existentes de locale FluentValidation son parte del baseline, no introducen cambios).

---

## Acceptance criteria

- [ ] `SystemOption.cs` expone `bool CanExport { get; set; } = true;` y `bool CanExecute { get; set; } = true;` con XML doc idéntico al de `RoleSystemOption`.
- [ ] `SystemOptionConfiguration.cs` mapea `CanExport` y `CanExecute` con `HasDefaultValue(true)`.
- [ ] Existe migración `<timestamp>_AddCanExportCanExecuteToSystemOption.cs` con dos `AddColumn<bool>` en `Security.SystemOptions`, ambas con `defaultValue: true` y `nullable: false`.
- [ ] El `Down()` de la migración dropea ambas columnas.
- [ ] `SystemOptionSeed` record tiene `bool? CanExport = null` y `bool? CanExecute = null` después de `CanDownload`.
- [ ] `SeedAdministrativeSystemOptionsAsync` setea `CanExport`/`CanExecute` en el path de insert (línea ~2056), en el `hasChanges` (línea ~2098) y en la asignación final (línea ~2117).
- [ ] `canExport`/`canExecute` se calculan como `seed.CanExport ?? true` / `seed.CanExecute ?? true` (default `true`, no `CanRead`).
- [ ] `dotnet build -c Release` compila con 0 errores.
- [ ] Tras arrancar la API en Development, la tabla `Security.SystemOptions` tiene columnas `CanExport` y `CanExecute` con `DEFAULT 1`.
- [ ] Registros existentes en `Security.SystemOptions` (pre-existentes a la migración) muestran `CanExport = 1` y `CanExecute = 1` post-migración.
- [ ] `RoleSystemOption` no fue modificado (las columnas `CanExport`/`CanExecute` ya existían desde SPEC 12).
- [ ] `DynamicAuthorizationFilter` / `IPermissionService` / `PermissionService` no fueron tocados — enforcement queda diferido a spec futuro.
- [ ] `dotnet test` no introduce nuevos fallos (los 12 pre-existentes de locale FluentValidation siguen siendo el baseline).

---

## Decisions taken and discarded

- **Default `true` (SystemOption) vs. default `false` (RoleSystemOption)**: inconsistente en superficie pero correcto en semántica. `SystemOption` representa "qué soporta la pantalla por defecto"; `RoleSystemOption` representa "qué le concedes al rol específico" (default conservador = opt-in). Mismo razonamiento que `CanRead`/`CanCreate`/etc. en `SystemOption` (true) vs. `RoleSystemOption` (false en seed, pero nullable). Sin colisión.
- **Replicar `CanExport`/`CanExecute` en `SystemOptionConfiguration` con `HasDefaultValue(true)`** vs. dejar el seeder forzar el valor en cada insert: el `HasDefaultValue` + `DEFAULT` constraint en SQL garantiza que cualquier insert futuro (no solo desde el seeder) reciba `true`. Defense in depth.
- **Calcular `canExport = seed.CanExport ?? true`** vs. `seed.CanExport ?? seed.CanRead` (como hace `RoleSystemOption`): el spec del usuario (sección 1 de la conversación) pide explícitamente `true` por defecto. `CanRead` puede ser `false` para pantallas "Padre" (agrupadores) que no tienen controller/action — usar `true` directo evita que un agrupador herede `CanExport=false`意外的.
- **No tocar el filtro de autorización** (decisión confirmada por el usuario): enforcement de `CanExport`/`CanExecute` queda para spec dedicado. Razón: el `DynamicAuthorizationFilter` actual mapea HTTP method → CRUD flag; `Export`/`Execute` no se infieren por verbo HTTP, y el mecanismo de señalización (nuevo attribute vs. extender `PermissionResourceAttribute`) merece spec aparte.
- **Reutilizar `SystemOptionSeed` record** vs. crear un `SystemOptionExtendedSeed` separado: el record sólo se usa en `SeedAdministrativeSystemOptionsAsync`; agregar dos parámetros nullable es menos invasivo que dividir el tipo.
- **No tocar `GetRoleSystemOptionSeeds()`** (decisión por scope): ya setea `CanExport: true`/`CanExecute: true` para roles privilegiados (líneas 2177-2179, 2521-2523). El spec se limita a `SystemOption`, no a `RoleSystemOption`.

---

## Identified risks

- **Migración contra base ya deployada en producción**: `AddColumn<bool>` con `defaultValue: true` reescribe todas las filas existentes en `Security.SystemOptions` (algunas pueden ser millones de filas en un CRM real). Para una tabla de configuración como `SystemOptions` (decenas/centenares de filas, no millones) el impacto es despreciable. Si el volumen fuera otro, considerar batched update con `WHERE CanExport IS NULL`.
- **Convención `CanExport` vs `CanDownload`**: `CanDownload` ya existe desde SPEC 06. `CanExport` es semánticamente distinto (export = generar archivo nuevo, download = descargar archivo existente). Si en UI/backend las pantallas exponen botones "Export" vs "Download", los permisos deberían ser independientes — este spec mantiene esa independencia. Riesgo bajo de confusión para el usuario final si los permisos no se documentan en el frontend.
- **DEFAULT `true` podría sobre-permisar usuarios**: si el seeder pre-existente tenía `CanRead=false` (agrupadores), ahora `CanExport=true`/`CanExecute=true` se activan por default. Mitigación: si una opción es agrupador (`Parent != null` o `ControllerName` null), el seeder puede pasar `CanExport=false`/`CanExecute=false` explícito en el `SystemOptionSeed`. Por ahora los seed records existentes tienen `CanRead=true` (línea 2557 confirma), no hay agrupadores con `CanRead=false` — el riesgo es teórico hasta nuevo seed.
- **El filtro no enforza estos flags aún**: un rol con `CanExport=false` en `RoleSystemOption` pero `CanExport=true` en `SystemOption` no será bloqueado de un endpoint export. El filtro actual sólo chequea CRUD flags. Spec futuro debe (a) definir mecanismo de señalización (nuevo attribute?), (b) actualizar `PermissionService.HasPermissionAsync` para incluir `CanExport`/`CanExecute` en `PermissionFlags`, (c) propagar al `DynamicAuthorizationFilter`. Riesgo fuera de scope del presente spec; documentado para seguimiento.
