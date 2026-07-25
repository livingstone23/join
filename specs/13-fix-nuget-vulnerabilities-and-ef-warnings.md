# SPEC 13 — Corrección de warnings de build y runtime (NuGet + EF Core)

> **Status:** Draft
> **Depends on:** Ninguno.
> **Date:** 2026-07-25
> **Objective:** Fijar versiones parchadas para las 2 vulnerabilidades NuGet transitivas (KubernetesClient → 18.0.13, Microsoft.OpenApi → 2.10.0), hacer opcional la relación `ApplicationUser`→`UserConnectionLog` para eliminar el warning de filtro global no coincidente, y restringir `EnableSensitiveDataLogging()` a Development.

---

## Scope

**In:**

- `src/4.Services.WebApi/JOIN.Services.WebApi.csproj`: agregar overrides directos `<PackageReference Include="KubernetesClient" Version="18.0.13" />` y `<PackageReference Include="Microsoft.OpenApi" Version="2.10.0" />` para forzar esas versiones parchadas por encima de las transitivas que trae `AspNetCore.HealthChecks.UI 9.0.0` (KubernetesClient 15.0.1) y `Microsoft.AspNetCore.OpenApi 10.0.2` (Microsoft.OpenApi 2.0.0).
- `src/1.Domain/Security/UserConnectionLog.cs`: `UserId` pasa de `Guid` a `Guid?`; `User` pasa de `= null!` a `ApplicationUser?`.
- `src/3.Persistence/Configuration/Security/UserConnectionLogConfiguration.cs`: quitar `.IsRequired()` implícito de la relación (ya no aplica al ser FK nullable) y cambiar `.OnDelete(DeleteBehavior.Cascade)` a `.OnDelete(DeleteBehavior.SetNull)` — si el usuario se borra físicamente, el log de conexión sobrevive con `UserId = null` en lugar de desaparecer.
- `src/3.Persistence/Configuration/ConfigureServices.cs`: dentro del `AddDbContext<ApplicationDbContext>((sp, options) => {...})` ya existente, resolver `IHostEnvironment` desde `sp` y llamar `options.EnableSensitiveDataLogging()` solo si `env.IsDevelopment()`.
- `src/3.Persistence/Contexts/ApplicationDbContext.cs`: quitar la llamada incondicional a `optionsBuilder.EnableSensitiveDataLogging()` en `OnConfiguring` (líneas 299-300), ya que se centraliza en `ConfigureServices.cs` junto a la resolución de la cadena de conexión (mismo patrón que ya usa ese archivo).
- Nueva migración de EF Core que hace nullable la columna `UserId` de `Security.UserConnectionLogs` y actualiza la FK a `ON DELETE SET NULL`.
- Aplicar la migración a la base de datos local.

**Out of scope (para specs futuros o decisión ya tomada):**

- Cualquier otro paquete NuGet con warnings no listados en el pedido original (solo se tocan `KubernetesClient` y `Microsoft.OpenApi`).
- Saltar `Microsoft.OpenApi` a la línea mayor 3.x — descartado explícitamente por el riesgo de incompatibilidad con `Microsoft.AspNetCore.OpenApi 10.0.2`.
- Actualizar `AspNetCore.HealthChecks.UI` o `Microsoft.AspNetCore.OpenApi` en sí — no hay versiones más nuevas compatibles con `net10.0` estable disponibles hoy; el fix es solo el override de la dependencia transitiva.
- Cualquier otra relación con filtro global no coincidente que EF Core no reportó en este log (no se auditan otras entidades por fuera de `ApplicationUser`/`UserConnectionLog`).
- Rehacer el pipeline de logging de Serilog o su configuración — el fix es puntual a `EnableSensitiveDataLogging()` en `ApplicationDbContext`.

---

## Data model

No se introducen entidades nuevas. Se modifica `UserConnectionLog` (existente) para que su FK a `ApplicationUser` sea opcional.

**`src/1.Domain/Security/UserConnectionLog.cs`** (antes → después):

```csharp
// Antes
public Guid UserId { get; set; }
...
public virtual ApplicationUser User { get; set; } = null!;

// Después
public Guid? UserId { get; set; }
...
public virtual ApplicationUser? User { get; set; }
```

**`src/3.Persistence/Configuration/Security/UserConnectionLogConfiguration.cs`** (antes → después, bloque de relación):

```csharp
// Antes
builder.HasOne(log => log.User)
    .WithMany()
    .HasForeignKey(log => log.UserId)
    .OnDelete(DeleteBehavior.Cascade);

// Después
builder.HasOne(log => log.User)
    .WithMany()
    .HasForeignKey(log => log.UserId)
    .OnDelete(DeleteBehavior.SetNull);
```

**`src/3.Persistence/Configuration/ConfigureServices.cs`** (dentro del `AddDbContext` existente, antes → después):

```csharp
// Antes
services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
    options.AddInterceptors(interceptor);

    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");

    options.UseSqlServer(connectionString,
        builder => builder
            .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)
            .CommandTimeout(30));
});

// Después
services.AddDbContext<ApplicationDbContext>((sp, options) =>
{
    var interceptor = sp.GetRequiredService<AuditableEntitySaveChangesInterceptor>();
    options.AddInterceptors(interceptor);

    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");

    options.UseSqlServer(connectionString,
        builder => builder
            .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)
            .CommandTimeout(30));

    // Sensitive data logging must never run outside Development (leaks parameter values into logs).
    var env = sp.GetRequiredService<IHostEnvironment>();
    if (env.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }
});
```

**`src/3.Persistence/Contexts/ApplicationDbContext.cs`** (`OnConfiguring`, antes → después):

```csharp
// Antes
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder.AddInterceptors(_auditableInterceptor);
    optionsBuilder.EnableSensitiveDataLogging();
    base.OnConfiguring(optionsBuilder);
}

// Después
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder.AddInterceptors(_auditableInterceptor);
    base.OnConfiguring(optionsBuilder);
}
```

**`src/4.Services.WebApi/JOIN.Services.WebApi.csproj`** (nuevas líneas en el `ItemGroup` de `PackageReference`):

```xml
<PackageReference Include="KubernetesClient" Version="18.0.13" />
<PackageReference Include="Microsoft.OpenApi" Version="2.10.0" />
```

---

## Implementation plan

1. **NuGet — KubernetesClient**: agregar `<PackageReference Include="KubernetesClient" Version="18.0.13" />` a `JOIN.Services.WebApi.csproj`. Correr `dotnet restore` y confirmar que `NU1902` ya no aparece.
2. **NuGet — Microsoft.OpenApi**: agregar `<PackageReference Include="Microsoft.OpenApi" Version="2.10.0" />` al mismo `.csproj`. Correr `dotnet restore` y confirmar que `NU1903` ya no aparece. Verificar que `/openapi/v1.json` y `/scalar/v1` siguen sirviendo correctamente (la generación de OpenAPI usa esta librería).
3. **Dominio — UserConnectionLog**: cambiar `UserId` a `Guid?` y `User` a `ApplicationUser?` en `src/1.Domain/Security/UserConnectionLog.cs`.
4. **Persistencia — configuración**: en `UserConnectionLogConfiguration.cs`, cambiar `OnDelete(DeleteBehavior.Cascade)` a `OnDelete(DeleteBehavior.SetNull)`. El proyecto sigue compilando.
5. **Persistencia — sensitive data logging**: mover `EnableSensitiveDataLogging()` de `ApplicationDbContext.OnConfiguring` al `AddDbContext` factory en `ConfigureServices.cs`, gateado por `IHostEnvironment.IsDevelopment()`.
6. **Migración EF Core**: `dotnet ef migrations add MakeUserConnectionLogUserIdOptional --project ../3.Persistence --startup-project .` desde `src/4.Services.WebApi`. Revisar que el `Up()` haga `ALTER COLUMN UserId` nullable y actualice la FK a `ON DELETE SET NULL`, sin tocar otras tablas.
7. **Aplicar la migración**: `dotnet ef database update --project ../3.Persistence --startup-project .` contra la base local (o dejar que `Program.cs` la aplique al arrancar con `MigrateAsync()`).
8. **Verificación de warnings**: correr `dotnet build` en Release y confirmar que ya no aparecen `NU1902`, `NU1903`, ni el warning de EF Core sobre `ApplicationUser`/`UserConnectionLog`. Confirmar que `EnableSensitiveDataLogging` ya no aparece en el log al arrancar con `ASPNETCORE_ENVIRONMENT=Production` (o cualquier no-Development), y que sigue apareciendo en Development.

---

## Acceptance criteria

- [ ] `dotnet restore` / `dotnet build` ya no emiten `NU1902` (KubernetesClient) ni `NU1903` (Microsoft.OpenApi).
- [ ] `dotnet list src/4.Services.WebApi/JOIN.Services.WebApi.csproj package --vulnerable --include-transitive` no reporta paquetes vulnerables.
- [ ] `KubernetesClient` resuelve a `18.0.13` y `Microsoft.OpenApi` a `2.10.0` (verificable con `dotnet list package --include-transitive`).
- [ ] `UserConnectionLog.UserId` es `Guid?` y `UserConnectionLog.User` es `ApplicationUser?`.
- [ ] Al arrancar la API en Development, la consola de EF Core ya no muestra el warning "Entity 'ApplicationUser' has a global query filter defined and is the required end of a relationship with the entity 'UserConnectionLog'".
- [ ] Existe una migración de EF Core cuyo `Up()` hace `UserId` nullable en `Security.UserConnectionLogs` y cambia el `ON DELETE` de la FK a `SET NULL`; el `Down()` revierte ambos cambios.
- [ ] La migración se aplicó correctamente contra la base local sin errores.
- [ ] Con `ASPNETCORE_ENVIRONMENT=Development`, el log de arranque muestra "Sensitive data logging is enabled" (comportamiento esperado, sin cambios).
- [ ] Con `ASPNETCORE_ENVIRONMENT` distinto de `Development` (p. ej. `Production` o `Staging`), el log de arranque **no** muestra el warning de sensitive data logging.
- [ ] `/openapi/v1.json` y `/scalar/v1` siguen respondiendo correctamente tras el bump de `Microsoft.OpenApi`.
- [ ] `dotnet build` en Release compila sin errores.
- [ ] `dotnet test` sigue pasando (no se rompen pruebas existentes relacionadas con `UserConnectionLog` o `ApplicationDbContext`, si las hay).

---

## Decisions taken and discarded

- **Un solo spec para los 3 problemas** (elegido) vs. separar en 2 specs: se optó por agruparlos porque son cambios pequeños, cada uno acotado a 1-3 archivos, y revisarlos juntos como "limpieza de warnings de build/runtime" evita fragmentar el trabajo en PRs triviales. Decisión explícita del usuario.
- **Override directo de versión en el `.csproj` en vez de esperar upgrade de `AspNetCore.HealthChecks.UI`/`Microsoft.AspNetCore.OpenApi`** (elegido): ninguno de los dos paquetes padre tiene hoy una versión estable más nueva para `net10.0` que arrastre la dependencia ya parchada, así que la única forma de cerrar el CVE sin esperar a upstream es pinnear la transitiva directamente. Es una técnica estándar de NuGet (nearest-wins) y de bajo riesgo aquí porque ninguna de las dos librerías (K8s client para health checks, OpenAPI parser) se usa activamente en rutas core de la app (no hay `docker-compose`/K8s en este repo, y OpenAPI solo se sirve en Development).
- **`KubernetesClient` a 18.0.13 y `Microsoft.OpenApi` a 2.10.0 (penúltimas versiones) en vez de las últimas (19.0.2 / 3.9.0)** (elegido): decisión explícita del usuario. Mismo razonamiento adicional: mantenerse en la misma línea mayor que el paquete padre espera (`Microsoft.OpenApi` 2.x, ya que `Microsoft.AspNetCore.OpenApi 10.0.2` lo fija en `2.0.0`) reduce el riesgo de romper la generación de OpenAPI.
- **`UserConnectionLog.UserId` opcional (nullable FK) en vez de agregar un filtro global coincidente** (elegido): un log de auditoría de conexiones debe seguir siendo consultable aunque el usuario asociado se desactive o se borre lógicamente; agregar el mismo filtro que `ApplicationUser` ocultaría esos logs, contradiciendo el propósito de auditoría de la entidad.
- **`DeleteBehavior.SetNull` en vez de mantener `Cascade`** (elegido, consecuencia de la decisión anterior): con FK nullable, `Cascade` ya no tiene sentido semántico (implicaría borrar logs al borrar físicamente el usuario); `SetNull` preserva el historial.
- **Mover `EnableSensitiveDataLogging()` al `AddDbContext` factory de `ConfigureServices.cs` en vez de inyectar `IHostEnvironment` en el propio `ApplicationDbContext`** (elegido): sigue el mismo patrón ya establecido en ese archivo (resolver dependencias desde `sp` en vez de capturarlas eagerly, según el commit reciente `fix: resolve DefaultConnection from DI instead of capturing it eagerly`), y evita agregar una dependencia nueva al constructor del `DbContext`.

---

## Identified risks

- **Compatibilidad binaria de `KubernetesClient` 18.0.13**: `AspNetCore.HealthChecks.UI 9.0.0` fue compilado contra `15.0.1`; un salto de 3 versiones mayores podría introducir breaking changes en la superficie de API que usa internamente. Mitigación: el health check UI (`/health-ui`) debe probarse manualmente tras el cambio.
- **Compatibilidad de `Microsoft.OpenApi` 2.10.0 con `Microsoft.AspNetCore.OpenApi` 10.0.2**: el paquete padre fija la versión exacta `2.0.0`; aunque 2.10.0 es la misma línea mayor, el overload/override podría exponer comportamiento distinto en la generación de schemas. Mitigación: verificar `/openapi/v1.json` y `/scalar/v1` tras el cambio (ya incluido en el plan y en acceptance criteria).
- **Migración sobre tabla con datos existentes**: si `Security.UserConnectionLogs` ya tiene filas, el `ALTER COLUMN UserId` a nullable es una operación segura (no requiere backfill), pero el cambio de `ON DELETE CASCADE` a `SET NULL` en la FK debe verificarse en el script generado antes de aplicar, para confirmar que SQL Server lo traduce correctamente.
