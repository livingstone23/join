# SPEC 14 — Saltar el reseed de menú/permisos en startup cuando no es necesario

> **Status:** Draft
> **Depends on:** Ninguno.
> **Date:** 2026-07-25
> **Objective:** Evitar que `SeedMenuAndPermissionsAsync()` se ejecute en cada arranque de Development cuando los datos de seed no cambiaron, comparando un checksum de las definiciones de seed contra uno guardado en una tabla nueva (`Security.SeedState`), con un flag de configuración para forzar el reseed manualmente cuando haga falta.

---

## Scope

**In:**

- `src/1.Domain/Security/SeedState.cs` (nueva entidad): registra el último checksum del seed de menú/permisos aplicado por company — `Id`, `CompanyId`, `Checksum` (string), `LastAppliedAt` (DateTime).
- `src/3.Persistence/Configuration/Security/SeedStateConfiguration.cs` (nuevo): mapea `SeedState` a `Security.SeedState`, con índice único en `CompanyId` (una fila por company).
- `src/3.Persistence/Contexts/ApplicationDbContext.cs`: agregar `DbSet<SeedState> SeedStates`.
- `src/3.Persistence/Seed/DatabaseSeeder.cs`:
  - Nuevo método privado `ComputeMenuPermissionsSeedChecksum()` que serializa de forma determinística las definiciones estáticas que alimentan `SeedMenuAndPermissionsAsync` (el array de roles de `SeedRolesAsync`, `GetDefaultUserSeeds()`, `GetAdministrativeSystemOptionSeeds()`, `GetRoleSystemOptionSeeds()`, `GetAdminFullSystemOptionPermissionSeeds()` y el array `privilegedRoleNames`) y calcula un hash (SHA-256) sobre esa serialización.
  - `SeedMenuAndPermissionsAsync` recibe un parámetro `bool forceReseed = false`. Al entrar: calcula el checksum actual, busca la fila de `SeedState` para `joinCompanyId`; si existe, coincide con el checksum actual y `forceReseed` es `false`, loguea que se salta el reseed y retorna sin tocar `SeedRolesAsync`/`SeedDefaultUsersAsync`/`SeedUserAccessAsync`/`SeedSystemOptionsAsync`/`SeedRoleSystemOptionsAsync`. En caso contrario, ejecuta el seed completo como hoy y al final hace upsert de la fila de `SeedState` con el checksum nuevo y `LastAppliedAt = DateTime.UtcNow`.
- `src/4.Services.WebApi/Program.cs`: en el bloque `else if (app.Environment.IsDevelopment())` (línea ~224-227), leer `Seeding:ForceMenuPermissionsReseed` desde `IConfiguration` y pasarlo como `forceReseed` a `seeder.SeedMenuAndPermissionsAsync(forceReseed)`.
- `src/4.Services.WebApi/appsettings.Development.json`: documentar la clave `"Seeding": { "ForceMenuPermissionsReseed": false }` con su valor por defecto.
- Nueva migración de EF Core que crea la tabla `Security.SeedState`.
- Aplicar la migración a la base de datos local.

**Out of scope (para specs futuros o decisión ya tomada):**

- El branch `SeedAsync()` completo (cuando `pendingMigrations.Count > 0`, para bases nuevas o con migraciones nuevas) no cambia — sigue ejecutándose siempre que haya migraciones pendientes, sin checksum.
- Reducir la verbosidad del logging de comandos SQL de EF Core en general — eso ya se cubre en SPEC 13 (sensitive data logging); este spec no toca `appsettings.json`'s `Logging` section.
- Cambiar cualquier valor de los datos de seed (roles, permisos, usuarios) — este spec solo agrega el mecanismo de skip, no modifica qué se siembra.
- Aplicar el mismo mecanismo de checksum a los demás métodos de seed (`SeedCountriesAsync`, `SeedAreasByCompanyAsync`, etc.) que corren dentro de `SeedAsync()` — esos solo se ejecutan una vez (base nueva o migración nueva) y no tienen el problema de re-ejecución en cada arranque.
- Soporte multi-tenant para el reseed automático de menú/permisos en companies distintas de `JOIN-001` — `SeedMenuAndPermissionsAsync` ya está limitado a la company maestra hoy; este spec no amplía ese alcance.

---

## Data model

**`src/1.Domain/Security/SeedState.cs`** (nueva entidad):

```csharp
/// <summary>
/// Tracks the last checksum of the menu/permissions seed applied for a company,
/// so the idempotent Development reseed can be skipped when nothing changed.
/// </summary>
public class SeedState : BaseEntity
{
    public Guid CompanyId { get; set; }

    /// <summary>
    /// SHA-256 hash of the current in-code seed definitions (roles, users, system options,
    /// role/system-option permissions) used by SeedMenuAndPermissionsAsync.
    /// </summary>
    public string Checksum { get; set; } = string.Empty;

    public DateTime LastAppliedAt { get; set; }
}
```

**`src/3.Persistence/Configuration/Security/SeedStateConfiguration.cs`** (nuevo):

```csharp
public class SeedStateConfiguration : IEntityTypeConfiguration<SeedState>
{
    public void Configure(EntityTypeBuilder<SeedState> builder)
    {
        builder.ToTable("SeedState", "Security");
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => s.CompanyId).IsUnique();
        builder.Property(s => s.Checksum).IsRequired().HasMaxLength(64); // SHA-256 hex string
    }
}
```

**`src/3.Persistence/Seed/DatabaseSeeder.cs`** (forma del cambio, no el código completo):

```csharp
public async Task SeedMenuAndPermissionsAsync(bool forceReseed = false, CancellationToken cancellationToken = default)
{
    var joinCompanyId = await _context.Companies
        .IgnoreQueryFilters()
        .Where(c => c.TaxId == "JOIN-001")
        .Select(c => c.Id)
        .FirstOrDefaultAsync(cancellationToken);

    if (joinCompanyId == Guid.Empty)
    {
        _logger.LogWarning("Menu and permissions seed skipped. Master company JOIN-001 was not found.");
        return;
    }

    var currentChecksum = ComputeMenuPermissionsSeedChecksum();

    if (!forceReseed)
    {
        var existingState = await _context.SeedStates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.CompanyId == joinCompanyId, cancellationToken);

        if (existingState is not null && existingState.Checksum == currentChecksum)
        {
            _logger.LogInformation("Menu and permissions seed skipped for company {CompanyId}: no changes detected.", joinCompanyId);
            return;
        }
    }

    await SeedRolesAsync();
    await SeedDefaultUsersAsync();
    await SeedUserAccessAsync(joinCompanyId);
    await SeedSystemOptionsAsync();
    await SeedRoleSystemOptionsAsync(joinCompanyId);

    await UpsertSeedStateAsync(joinCompanyId, currentChecksum, cancellationToken);

    _logger.LogInformation("Menu and permissions seed completed for company {CompanyId}.", joinCompanyId);
}
```

**`Program.cs`** (bloque de arranque, antes → después):

```csharp
// Antes
else if (app.Environment.IsDevelopment())
{
    logger.LogInformation("Running idempotent menu and permissions seed (Development).");
    await seeder.SeedMenuAndPermissionsAsync();
}

// Después
else if (app.Environment.IsDevelopment())
{
    var forceReseed = builder.Configuration.GetValue<bool>("Seeding:ForceMenuPermissionsReseed");
    await seeder.SeedMenuAndPermissionsAsync(forceReseed);
}
```

**`appsettings.Development.json`** (nueva clave):

```json
{
  "Seeding": {
    "ForceMenuPermissionsReseed": false
  }
}
```

---

## Implementation plan

1. **Dominio**: crear `src/1.Domain/Security/SeedState.cs` con `CompanyId`, `Checksum`, `LastAppliedAt`.
2. **Persistencia — configuración**: crear `SeedStateConfiguration.cs` mapeando la entidad a `Security.SeedState` con índice único en `CompanyId`.
3. **Persistencia — DbContext**: agregar `DbSet<SeedState> SeedStates` a `ApplicationDbContext.cs`.
4. **Seeder — checksum**: agregar `ComputeMenuPermissionsSeedChecksum()` a `DatabaseSeeder.cs`, serializando de forma determinística (mismo orden siempre) el array de roles, `GetDefaultUserSeeds()`, `GetAdministrativeSystemOptionSeeds()`, `GetRoleSystemOptionSeeds()`, `GetAdminFullSystemOptionPermissionSeeds()` y `privilegedRoleNames`, y aplicando SHA-256 sobre esa serialización.
5. **Seeder — skip logic**: modificar la firma de `SeedMenuAndPermissionsAsync` a `(bool forceReseed = false, CancellationToken cancellationToken = default)`; agregar la comparación contra `Security.SeedState` y el `return` temprano cuando el checksum coincide y `forceReseed` es `false`; agregar `UpsertSeedStateAsync` al final del camino que sí siembra.
6. **Program.cs**: leer `Seeding:ForceMenuPermissionsReseed` de la configuración y pasarlo a `seeder.SeedMenuAndPermissionsAsync(forceReseed)` en el bloque `else if (app.Environment.IsDevelopment())`.
7. **appsettings.Development.json**: agregar la clave `"Seeding": { "ForceMenuPermissionsReseed": false }`.
8. **Migración EF Core**: `dotnet ef migrations add AddSeedStateTable --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi`. Revisar que el `Up()` solo cree la tabla `Security.SeedState`, sin tocar otras tablas.
9. **Aplicar la migración**: `dotnet ef database update --project ../3.Persistence --startup-project .` contra la base local.
10. **Verificación manual**: arrancar la API dos veces seguidas en Development sin cambiar `DatabaseSeeder.cs` entre ambas — la segunda vez el log debe mostrar el mensaje de "skipped: no changes detected" y no debe emitir ninguna de las consultas SQL de roles/usuarios/permisos. Luego modificar cualquier valor en `GetRoleSystemOptionSeeds()`, reiniciar, y confirmar que esta vez sí corre el seed completo y actualiza `Security.SeedState`. Finalmente probar `Seeding:ForceMenuPermissionsReseed=true` y confirmar que fuerza el reseed aunque el checksum no haya cambiado.

---

## Acceptance criteria

- [ ] Existe la entidad `SeedState` (`CompanyId`, `Checksum`, `LastAppliedAt`) y su configuración EF con índice único en `CompanyId`.
- [ ] `ApplicationDbContext` expone `DbSet<SeedState> SeedStates`.
- [ ] Existe una migración de EF Core que crea `Security.SeedState` y no modifica ninguna otra tabla; aplicada correctamente contra la base local.
- [ ] `SeedMenuAndPermissionsAsync` acepta `forceReseed` (default `false`).
- [ ] Con la base ya seedeada y sin cambios en las definiciones de seed: un segundo arranque en Development loguea el mensaje de "skipped: no changes detected" y **no** ejecuta ninguna de las consultas SQL de `SeedRolesAsync`/`SeedDefaultUsersAsync`/`SeedUserAccessAsync`/`SeedSystemOptionsAsync`/`SeedRoleSystemOptionsAsync`.
- [ ] Modificando cualquier valor de `GetRoleSystemOptionSeeds()` (o de cualquiera de los otros métodos hasheados) y reiniciando: el checksum calculado difiere del guardado, el seed completo corre de nuevo, y `Security.SeedState.Checksum`/`LastAppliedAt` se actualizan.
- [ ] Con `Seeding:ForceMenuPermissionsReseed=true`, el seed completo corre aunque el checksum no haya cambiado.
- [ ] El branch `SeedAsync()` (cuando hay migraciones pendientes) sigue funcionando sin cambios — no depende de `SeedState`.
- [ ] `dotnet build` en Release compila sin errores.
- [ ] `dotnet test` sigue pasando.

---

## Decisions taken and discarded

- **Checksum de las definiciones de seed en vez de saltar el reseed solo cuando hay migración pendiente** (elegido): la opción de "solo reseed con migración pendiente" es más simple pero elimina la conveniencia actual de que un desarrollador vea reflejados sus cambios en `DatabaseSeeder.cs` sin tener que crear una migración solo para forzar el reseed. El checksum detecta automáticamente cambios reales en los datos de seed sin requerir intervención manual.
- **Tabla SQL (`Security.SeedState`) en vez de archivo local en disco** (elegido): decisión explícita del usuario. Persiste igual que el resto de la base de datos y sobrevive a redeploys/contenedores efímeros, a diferencia de un archivo en el content root.
- **Flag de configuración `Seeding:ForceMenuPermissionsReseed` como escape hatch** (elegido) vs. requerir borrar la fila de `SeedState` a mano: decisión explícita del usuario. Da una forma explícita y documentada de forzar el reseed sin necesitar acceso directo a la base de datos.
- **Checksum calculado sobre las definiciones estáticas en código, no sobre el estado actual de la base de datos**: comparar contra la base de datos (por ejemplo, releyendo todas las filas de `RoleSystemOptions` y comparándolas) reproduciría exactamente el problema que se quiere evitar (cientos de `SELECT`s en cada arranque). Calcular el hash solo sobre los arrays/records estáticos en `DatabaseSeeder.cs` es una operación en memoria, sin acceso a la base, y agrega una única consulta extra (leer la fila de `SeedState`) en el camino feliz.
- **Una sola fila de `SeedState` por company (sin distinguir "tipo de seed") en vez de una clave compuesta `(CompanyId, SeedKey)`**: hoy solo `SeedMenuAndPermissionsAsync` usa este mecanismo, y agregar una clave `SeedKey` genérica sería diseñar para un caso hipotético futuro no pedido. Si se necesita en otro spec, se puede extender el esquema entonces.

---

## Identified risks

- **Determinismo del checksum**: si la serialización usada para el hash no es 100% determinística (p. ej. por el orden de un `Dictionary` o de una colección sin orden garantizado), el checksum podría variar entre arranques sin que los datos hayan cambiado realmente, forzando reseeds innecesarios — o, peor, si el bug va al revés, no detectar un cambio real. Mitigación: usar únicamente listas ordenadas (`List<T>`/arrays, ya en el orden fijo en que están escritas en el código) para la serialización, nunca `Dictionary`/`HashSet`.
- **Condición de carrera en despliegues multi-instancia**: si dos instancias de la API arrancan al mismo tiempo, ambas pueden leer el mismo checksum guardado y decidir reseedear en paralelo (mismo riesgo que ya existe hoy sin este mecanismo, no lo empeora, pero tampoco lo soluciona — el lock `__EFMigrationsLock` solo protege las migraciones, no el seed).
- **Falso "no necesario" si se edita el checksum manualmente en la base**: si alguien corrompe o modifica la fila de `Security.SeedState` a mano, el seed se saltaría incorrectamente hasta que se use `Seeding:ForceMenuPermissionsReseed=true` o se borre la fila.
