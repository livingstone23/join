# SPEC 12 — Permisos CanExport y CanExecute en RoleSystemOption

> **Status:** Implementado
> **Depends on:** Ninguno (extiende el modelo de permisos existente introducido junto con CanRead/CanCreate/CanUpdate/CanDelete/CanDownload/IsVisibleMenu).
> **Date:** 2026-07-25
> **Objective:** Agregar las columnas `CanExport` y `CanExecute` a `RoleSystemOption`, poblarlas junto con `CanDownload` en `true` para los roles SuperAdmin, Admin, Manager y SuperAdminCompany (y en `false` para UsuarioSimple) en el seed del tenant maestro, y aplicar la migración de EF Core correspondiente.

---

## Scope

**In:**

- `src/1.Domain/Security/RoleSystemOption.cs`: agregar las propiedades `bool CanExport` y `bool CanExecute`, con el mismo estilo de comentario XML que las propiedades vecinas (`CanDownload`, `IsVisibleMenu`).
- `src/3.Persistence/Configuration/Security/RoleSystemOptionConfiguration.cs`: agregar `builder.Property(rso => rso.CanExport).HasDefaultValue(false);` y el equivalente para `CanExecute`, junto a las demás propiedades booleanas.
- `src/3.Persistence/Seed/DatabaseSeeder.cs`:
  - `RoleSystemOptionSeed` (record): agregar parámetros opcionales `bool? CanExport = null` y `bool? CanExecute = null`, siguiendo el mismo patrón posicional que `CanDownload`.
  - `SeedRoleSystemOptionsAsync`: calcular `canExport = seed.CanExport ?? seed.CanRead` y `canExecute = seed.CanExecute ?? seed.CanRead` (mismo fallback que `canDownload`), e incluirlos en el insert, en el chequeo de `hasChanges` y en el update.
  - Bloque `privilegedAllOptionSeeds` (roles `Admin` y `SuperAdminCompany`, acceso total a todas las `SystemOptions`): agregar `CanExport: true, CanExecute: true` junto a `CanDownload: true`.
  - `GetAdminFullSystemOptionPermissionSeeds()` (blanket de `Admin` sobre las opciones administrativas): agregar `CanExport: true, CanExecute: true`.
  - `GetRoleSystemOptionSeeds()`: en **todas** las filas de `SuperAdmin`, `Admin` y `Manager` (incluyendo las filas placeholder de menú padre como `"Administracion"`), fijar explícitamente `CanExport: true, CanExecute: true, CanDownload: true`. En **todas** las filas de `UsuarioSimple`, fijar explícitamente `CanExport: false, CanExecute: false, CanDownload: false`. Las filas de `Supervisor` (y cualquier otro rol no mencionado) no se tocan — `CanExport`/`CanExecute` quedan sin especificar y heredan el fallback a `CanRead` fila por fila.
- Nueva migración de EF Core (`dotnet ef migrations add ... --project ../3.Persistence --startup-project .`) que agrega las columnas `CanExport` y `CanExecute` (`bool`, `NOT NULL DEFAULT false`) a `Security.RoleSystemOptions`.
- Aplicar la migración a la base de datos local (`dotnet ef database update --project ../3.Persistence --startup-project .`).

**Out of scope (para specs futuros o decisión ya tomada):**

- Backfill de tenants ya existentes distintos de la company maestra `JOIN-001`: la migración solo crea las columnas con `DEFAULT false`; el seeder (como ya ocurre hoy con `CanDownload`) solo actualiza permisos para `JOIN-001`. Cualquier tenant real ya creado queda con `CanExport`/`CanExecute` en `false` hasta que se reejecute el seed de su company o se pida explícitamente un backfill.
- Enforcement de estos permisos en tiempo de ejecución (p. ej. `DynamicAuthorizationFilter`, endpoints de exportación/ejecución) — este spec solo agrega y puebla las columnas, no cablea lógica de autorización nueva sobre ellas. `CanDownload`/`IsVisibleMenu` tampoco se enforcean hoy fuera de este mismo patrón, así que es consistente con el estado actual.
- Exponer `CanExport`/`CanExecute` en DTOs, mappers o endpoints — hoy `CanDownload` tampoco está expuesto en `2.Application`/`2.Application.DTO`/`4.Services.WebApi` (no existe un caso de uso CQRS para `RoleSystemOption`), así que no hay contrato existente que romper ni extender.
- Roles `Agent`, `Person`, `Coordinador` — no mencionados, no reciben filas nuevas ni valores explícitos.

---

## Data model

Este spec no introduce entidades nuevas — extiende `RoleSystemOption` (entidad ya existente) con dos columnas booleanas y ajusta el record de seed que la alimenta.

**`src/1.Domain/Security/RoleSystemOption.cs`** (nuevas propiedades, mismo estilo que las existentes):

```csharp
/// <summary>
/// Indicates if the role can export data from this screen (e.g. Excel/CSV/PDF report generation).
/// </summary>
public bool CanExport { get; set; }

/// <summary>
/// Indicates if the role can trigger actions/processes on this screen (e.g. run a workflow or batch operation).
/// </summary>
public bool CanExecute { get; set; }
```

**`src/3.Persistence/Configuration/Security/RoleSystemOptionConfiguration.cs`** (junto a las demás `Property(...)`):

```csharp
builder.Property(rso => rso.CanExport).HasDefaultValue(false);
builder.Property(rso => rso.CanExecute).HasDefaultValue(false);
```

**`RoleSystemOptionSeed`** (record privado en `DatabaseSeeder.cs`) — se agregan dos parámetros opcionales, mismo patrón que `CanDownload`:

```csharp
private sealed record RoleSystemOptionSeed(
    string RoleName,
    string SystemOptionName,
    bool CanRead,
    bool CanCreate,
    bool CanUpdate,
    bool CanDelete,
    bool? CanDownload = null,
    bool? CanExport = null,
    bool? CanExecute = null,
    bool? IsVisibleMenu = null,
    int? OrderMenu = null);
```

**`SeedRoleSystemOptionsAsync`** — mismo patrón de fallback que ya existe para `canDownload`:

```csharp
var canDownload = seed.CanDownload ?? seed.CanRead;
var canExport = seed.CanExport ?? seed.CanRead;
var canExecute = seed.CanExecute ?? seed.CanRead;
var isVisibleMenu = seed.IsVisibleMenu ?? seed.CanRead;
```

(se propaga a `CanExport`/`CanExecute` en el insert, el `hasChanges` y el update, igual que `CanDownload` hoy).

**Ejemplo de transformación en `GetRoleSystemOptionSeeds()`** (antes → después):

```csharp
// Antes
new("Manager", "Administracion", false, false, false, false),
new("Manager", "Paises", true, true, true, true),
...
new("UsuarioSimple", "StreetTypes", true, true, true, true),

// Después
new("Manager", "Administracion", false, false, false, false, CanDownload: true, CanExport: true, CanExecute: true),
new("Manager", "Paises", true, true, true, true, CanDownload: true, CanExport: true, CanExecute: true),
...
new("UsuarioSimple", "StreetTypes", true, true, true, true, CanDownload: false, CanExport: false, CanExecute: false),
```

Todas las filas de `SuperAdmin`, `Admin` y `Manager` reciben `CanDownload: true, CanExport: true, CanExecute: true` explícitos; todas las de `UsuarioSimple` reciben `CanDownload: false, CanExport: false, CanExecute: false` explícitos. Las filas de `Supervisor` no cambian (siguen sin especificar esos tres nombrados, heredando el fallback a `CanRead`).

**Bloques blanket** (`privilegedAllOptionSeeds` para `Admin`/`SuperAdminCompany`, y `GetAdminFullSystemOptionPermissionSeeds()` para `Admin`):

```csharp
new RoleSystemOptionSeed(roleName, option.Name, true, true, true, true,
    CanDownload: true, CanExport: true, CanExecute: true, IsVisibleMenu: true, OrderMenu: option.OrderMenu)
```

---

## Implementation plan

1. **Dominio**: agregar `CanExport` y `CanExecute` a `src/1.Domain/Security/RoleSystemOption.cs` (con comentarios XML), siguiendo el estilo de `CanDownload`/`IsVisibleMenu`. El sistema sigue compilando y funcionando igual (propiedades nuevas sin uso todavía).
2. **Persistencia — configuración EF**: agregar `HasDefaultValue(false)` para ambas propiedades en `RoleSystemOptionConfiguration.cs`.
3. **Persistencia — seed**: actualizar el record `RoleSystemOptionSeed` con los dos nuevos parámetros opcionales; actualizar `SeedRoleSystemOptionsAsync` para calcular `canExport`/`canExecute` con fallback a `CanRead` y persistirlos en insert/update/`hasChanges`.
4. **Persistencia — datos de seed**: actualizar `GetRoleSystemOptionSeeds()` fijando `CanDownload/CanExport/CanExecute` explícitos (`true` para SuperAdmin/Admin/Manager, `false` para UsuarioSimple) en cada fila de esos 4 roles; actualizar `privilegedAllOptionSeeds` y `GetAdminFullSystemOptionPermissionSeeds()` agregando `CanExport: true, CanExecute: true`. El proyecto compila y el seeder es idempotente (se puede correr repetidas veces sin duplicar filas).
5. **Migración EF Core**: generar la migración con `dotnet ef migrations add AddCanExportCanExecuteToRoleSystemOption --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi`, revisar el `Up()`/`Down()` generado (debe agregar dos columnas `bool NOT NULL DEFAULT false` a `Security.RoleSystemOptions`, sin tocar otras tablas).
6. **Aplicar la migración**: `dotnet ef database update --project ../3.Persistence --startup-project .` contra la base local, o dejar que `Program.cs` la aplique automáticamente al arrancar la API (`context.Database.MigrateAsync()`), y correr el seeder (`SeedMenuAndPermissionsAsync` ya se re-ejecuta en Development) para verificar que las filas de `JOIN-001` quedan con los valores esperados.
7. **Build final**: `dotnet build` en Release para confirmar que no quedan referencias rotas.

---

## Acceptance criteria

- [x] `RoleSystemOption.cs` expone `CanExport` y `CanExecute` como `bool` públicos.
- [x] `RoleSystemOptionConfiguration.cs` define `HasDefaultValue(false)` para ambas columnas nuevas.
- [x] `dotnet build` en Release compila sin errores ni warnings nuevos.
- [x] Existe una migración de EF Core cuyo `Up()` agrega `CanExport` y `CanExecute` (`bool NOT NULL DEFAULT false`) a `Security.RoleSystemOptions`, y cuyo `Down()` las elimina.
- [x] La migración se aplicó correctamente contra la base local (`dotnet ef database update` sin errores, o arranque exitoso de la API con `MigrateAsync()`).
- [x] Tras correr el seed sobre la company `JOIN-001`: toda fila de `RoleSystemOptions` cuyo rol sea `SuperAdmin`, `Admin` o `Manager` tiene `CanExport = true`, `CanExecute = true`, `CanDownload = true`.
- [x] Tras correr el seed: toda fila de `RoleSystemOptions` cuyo rol sea `UsuarioSimple` tiene `CanExport = false`, `CanExecute = false`, `CanDownload = false`.
- [x] Las filas de `Supervisor` no fueron modificadas más allá del fallback automático (`CanExport`/`CanExecute` igual a su `CanRead` por fila).
- [x] Correr el seeder una segunda vez (idempotencia) no inserta filas duplicadas ni deja `hasChanges` en `true` innecesariamente (no genera updates en el segundo pase).
- [x] `dotnet test` sigue pasando (no se rompen pruebas existentes de `DatabaseSeeder`/`RoleSystemOption` si las hay). *(12 fallos preexistentes de localización FluentValidation, iguales en `main` limpio; ninguno de `DatabaseSeeder`/`RoleSystemOption`.)*

---

## Decisions taken and discarded

- **Blanket true/false por rol, sin importar `CanRead`** (elegido) vs. condicionar a filas con `CanRead = true`: se eligió el blanket porque el pedido original es explícito ("CanExport, CanExecute, CanDownload en true") sin condicionarlo a otra columna, y porque simplifica el razonamiento sobre qué rol puede exportar/ejecutar sin depender de la semántica de cada fila individual.
- **Fallback a `CanRead` para roles no mencionados (Supervisor, etc.)** (elegido) vs. dejarlos explícitamente en `false` o en `true`: se eligió reutilizar el mismo mecanismo de fallback que ya existe para `CanDownload`/`IsVisibleMenu`, para no introducir un tercer comportamiento distinto y para no tomar una decisión de negocio sobre Supervisor que el usuario no pidió.
- **`SuperAdminCompany` también recibe `true`** (elegido) vs. dejarlo sin tocar: `SuperAdminCompany` ya comparte el mismo bloque de acceso total (`privilegedAllOptionSeeds`) que `Admin`, incluyendo `CanDownload: true` hoy; dejarlo fuera habría creado una inconsistencia dentro del mismo bloque de código.
- **Sin backfill SQL para tenants ya existentes** (elegido) vs. incluir un `UPDATE` en la migración: se eligió no backfillear porque es el comportamiento actual del sistema para `CanDownload`/`IsVisibleMenu` (el seeder solo cubre `JOIN-001`), y porque escribir a datos de tenants reales dentro de una migración es una operación de mayor riesgo que no fue pedida explícitamente.
- **No se expone `CanExport`/`CanExecute` vía DTO/API** (elegido) vs. agregarlo a un mapper: no existe hoy ningún caso de uso CQRS que exponga `RoleSystemOption` (ni siquiera `CanDownload`), así que agregarlo habría sido una ampliación de alcance no solicitada.
- **No se implementa enforcement de estos permisos** (elegido) vs. cablearlos en `DynamicAuthorizationFilter`: el pedido es poblar datos, no agregar lógica de autorización nueva; se deja fuera de alcance explícitamente.

---

## Identified risks

- **Tenants existentes fuera de `JOIN-001`**: cualquier company real ya creada en producción/staging quedará con `CanExport = false` y `CanExecute = false` para todos sus roles hasta que se reejecute manualmente el seed de esa company (`SeedDefaultCatalogsForCompanyAsync`/reseed) — decisión explícita tomada arriba, pero vale dejarlo documentado como impacto operativo.
- **Migración con `DEFAULT false` sobre tabla ya poblada**: si `Security.RoleSystemOptions` ya tiene filas, EF debe generar el `ALTER TABLE ... ADD COLUMN ... DEFAULT false` correctamente para no fallar por `NOT NULL` en filas existentes; se debe revisar el script generado antes de aplicarlo (paso 5 del plan).
