# SPEC 06 — Infraestructura base de pruebas de integración

> **Status:** Draft
> **Depends on:** SPEC 01 (referencia informativa — el piloto ejercita indirectamente TransactionBehavior, ya que RegisterCommand implementa ITransactionalCommand desde SPEC 01)
> **Date:** 2026-07-14
> **Objective:** Establecer la infraestructura base de pruebas de integración (xUnit + WebApplicationFactory + Testcontainers.MsSql) con una prueba piloto sobre RegisterCommand que demuestre el pipeline completo contra una base de datos SQL Server efímera, ejecutada en un job de CI separado del gate de cobertura de unitarias.

---

## Scope

**In:**

- Nuevo proyecto `tests/IntegrationTests/JOIN.IntegrationTests.csproj` (`net10.0`), con referencia a `src/4.Services.WebApi/JOIN.Services.WebApi.csproj` (necesaria para `WebApplicationFactory<Program>`).
- Paquetes NuGet: `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `AutoFixture`, `FluentAssertions`, `Moq` (mismos que el proyecto de unitarias, por coherencia), más `Microsoft.AspNetCore.Mvc.Testing` y `Testcontainers.MsSql`, nuevos para este spec.
- Clase `CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime` en `tests/IntegrationTests/CustomWebApplicationFactory.cs`:
  - `InitializeAsync()`: construye y arranca un `MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build()` de Testcontainers.
  - `ConfigureWebHost(...)`: fuerza `UseEnvironment("Development")` (reutiliza `appsettings.Development.json` existente); sobreescribe `ConnectionStrings:DefaultConnection` con la cadena de conexión del contenedor efímero vía `ConfigureAppConfiguration`; reemplaza el registro real de `IEmailService` por un mock (`Moq`) vía `ConfigureTestServices`, para que ningún test dependa de red externa ni envíe correos reales.
  - `DisposeAsync()`: detiene y destruye el contenedor Testcontainers por completo.
  - Las migraciones de EF Core se aplican automáticamente al arrancar el host — reutiliza el `context.Database.MigrateAsync()` que `Program.cs` ya ejecuta en el startup normal; no se agrega lógica de migración adicional en la factory.
- Prueba piloto `RegisterEndpointTests` (`tests/IntegrationTests/Auth/RegisterEndpointTests.cs`): `IClassFixture<CustomWebApplicationFactory>`, hace `POST /api/auth/register` con un payload válido (password que cumpla `PasswordPolicySettings` de `appsettings.Development.json`) y verifica `HttpStatusCode.OK`/`Created` + `Response<RegisterResponseDto>.IsSuccess == true`.
- Agregar `tests/IntegrationTests/JOIN.IntegrationTests.csproj` a `JOIN.slnx` bajo una carpeta `/tests/IntegrationTests/`.
- Modificar `.github/workflows/ci.yml`: agregar un step nuevo, después del step de unitarias, que corra `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj` **sin** las flags de Coverlet/Threshold — completamente separado del gate de cobertura del 90%.
- Agregar `public partial class Program { }` al final de `src/4.Services.WebApi/Program.cs` — único cambio fuera de `tests/IntegrationTests/`, requerido porque `WebApplicationFactory<Program>` necesita que la clase de entrada sea accesible desde el ensamblado de test.

**Out of scope (para specs futuros):**

- Pruebas de integración adicionales más allá del piloto de `RegisterCommand` (Login, Queries autenticadas, etc.) — cada una se agrega en su propio spec/PR conforme se necesite.
- Mocks/Fakes de I/O externo distintos a `IEmailService` — no existe otra integración externa hoy (confirmado en SPEC 05); si aparece una nueva, se mockea en el spec que la introduzca.
- Crear un ambiente `"Testing"` dedicado o `appsettings.Testing.json` — decisión explícita de reutilizar `"Development"`.
- Testcontainers para PostgreSQL — solo SQL Server, único provider EF Core activo hoy.
- Optimización de tiempo de ejecución (ej. compartir un único contenedor Docker entre múltiples clases de test en vez de uno por fixture) — el enfoque de este spec es un contenedor por instancia de `CustomWebApplicationFactory`; paralelización/reuso queda para un spec futuro si el tiempo de CI se vuelve un problema real.
- Modificar `RegisterCommandHandler.cs` o cualquier lógica de dominio/aplicación — este spec es puramente infraestructura de testing.
- Modificar `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, la resiliencia HTTP/EF Core, o los documentos `specs/01-*.md` a `specs/05-*.md`.

---

## Data model

Este spec no introduce entidades de dominio ni DTOs de negocio — los artefactos son infraestructura de testing:

```csharp
// tests/IntegrationTests/CustomWebApplicationFactory.cs (forma, no implementación completa)
public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _dbContainer = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _dbContainer.GetConnectionString()
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddScoped(_ => Mock.Of<IEmailService>(
                e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()) == Task.FromResult(true)));
        });
    }

    public Task InitializeAsync() => _dbContainer.StartAsync();
    public new Task DisposeAsync() => _dbContainer.DisposeAsync().AsTask();
}
```

```csharp
// tests/IntegrationTests/Auth/RegisterEndpointTests.cs (forma)
public class RegisterEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    [Fact]
    public async Task Register_WithValidPayload_ReturnsSuccessAndPersistsUser()
    {
        var client = factory.CreateClient();
        var command = new RegisterCommand { Email = ..., Password = "Str0ng!Pass2026", FirstName = ..., LastName = ... };

        var response = await client.PostAsJsonAsync("/api/auth/register", command);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<Response<RegisterResponseDto>>();
        body!.IsSuccess.Should().BeTrue();
    }
}
```

```yaml
# .github/workflows/ci.yml (step nuevo, sin cobertura)
- name: 🐳 Run Integration Tests (Testcontainers)
  run: dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj --no-build --configuration Release --verbosity normal
```

Conventions:

- El `Password` del payload de prueba se construye a mano (no vía `AutoFixture.Create<string>()` puro), porque `PasswordPolicySettings` (`MinimumLength`, `RestrictRepetitiveChars`, `RestrictCommonSequences`, `RestrictUsernameInPassword`) rechazaría una cadena aleatoria genérica — AutoFixture se usa para el resto de campos (`Email`, `FirstName`, `LastName`) donde no hay reglas de complejidad.
- `IEmailService` se reemplaza en el contenedor de DI de test, no en el código de producción — `SendGridEmailAdapter` permanece intacto (SPEC 05).

---

## Implementation plan

1. Agregar `public partial class Program { }` al final de `src/4.Services.WebApi/Program.cs`. Build de `4.Services.WebApi` sin errores; cambio no funcional, la app sigue comportándose igual en runtime.
2. Crear `tests/IntegrationTests/JOIN.IntegrationTests.csproj` con los paquetes NuGet (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `AutoFixture`, `FluentAssertions`, `Moq`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`) y referencia a `JOIN.Services.WebApi.csproj`; agregarlo a `JOIN.slnx`. Build sin errores (proyecto vacío, sin clases todavía).
3. Crear `CustomWebApplicationFactory.cs`: contenedor `MsSqlContainer` (imagen `mcr.microsoft.com/mssql/server:2022-latest`), `ConfigureWebHost` con override de `ConnectionStrings:DefaultConnection` y mock de `IEmailService`, `InitializeAsync`/`DisposeAsync` para el ciclo de vida del contenedor. Build de `JOIN.IntegrationTests` sin errores.
4. Crear `RegisterEndpointTests.cs` con la prueba piloto sobre `POST /api/auth/register`. Build sin errores.
5. Verificación funcional manual local (requiere Docker corriendo): ejecutar `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj` y confirmar que: el contenedor SQL Server arranca, las migraciones de EF Core se aplican automáticamente, el `POST` a `/api/auth/register` responde éxito, y el contenedor se destruye por completo al finalizar la suite.
6. Modificar `.github/workflows/ci.yml`: agregar el step nuevo "Run Integration Tests" (sin flags de Coverlet/Threshold) inmediatamente después del step de unitarias, dentro del mismo job o en uno separado.
7. Verificación funcional (CI): correr el pipeline completo (push a una rama o simulación local del workflow) y confirmar que ambos steps —unitarias con gate de cobertura del 90%, e integración sin gate— se ejecutan y reportan de forma independiente, sin que el nuevo proyecto afecte el porcentaje de cobertura existente.

---

## Acceptance criteria

- [ ] `src/4.Services.WebApi/Program.cs` termina con `public partial class Program { }`.
- [ ] `tests/IntegrationTests/JOIN.IntegrationTests.csproj` existe, referencia `JOIN.Services.WebApi.csproj`, y está agregado a `JOIN.slnx`.
- [ ] El proyecto referencia `xunit`, `AutoFixture`, `FluentAssertions`, `Moq`, `Microsoft.AspNetCore.Mvc.Testing` y `Testcontainers.MsSql`.
- [ ] `CustomWebApplicationFactory` implementa `IAsyncLifetime` y `WebApplicationFactory<Program>`.
- [ ] `CustomWebApplicationFactory` arranca un contenedor `mcr.microsoft.com/mssql/server:2022-latest` vía Testcontainers en `InitializeAsync`, y lo destruye por completo en `DisposeAsync`.
- [ ] `CustomWebApplicationFactory` sobreescribe `ConnectionStrings:DefaultConnection` con la cadena de conexión del contenedor efímero, usando el ambiente `"Development"`.
- [ ] `CustomWebApplicationFactory` reemplaza el registro real de `IEmailService` por un mock, sin depender de red externa ni SendGrid real.
- [ ] Las migraciones de EF Core se aplican automáticamente al arrancar el host de test (sin lógica de migración adicional en la factory).
- [ ] Existe `RegisterEndpointTests` con al menos una prueba que ejecuta `POST /api/auth/register` contra el host de test.
- [ ] La prueba piloto verifica el código de estado HTTP de éxito y `Response<RegisterResponseDto>.IsSuccess == true`.
- [ ] La prueba piloto pasa localmente con Docker corriendo (`dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj`).
- [ ] `.github/workflows/ci.yml` incluye un step para Integration Tests, separado del step de unitarias, sin las flags `/p:CollectCoverage`, `/p:Threshold`, `/p:ThresholdType`.
- [ ] El gate de cobertura del 90% sigue aplicando exclusivamente a `tests/UnitTests/JOIN.Application.UnitTest`, sin verse afectado por el nuevo proyecto.
- [ ] No se modifica `RegisterCommandHandler.cs` ni ningún archivo de `1.Domain` o `2.Application` más allá de lo ya cubierto por specs anteriores.
- [ ] `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, la resiliencia HTTP/EF Core, y los documentos `specs/01-*.md` a `specs/05-*.md` permanecen sin modificaciones.
- [ ] La solución compila con 0 errores, incluyendo `tests/IntegrationTests/JOIN.IntegrationTests.csproj`.

---

## Decisiones

- **Sí:** usar `POST /api/auth/register` (`RegisterCommand`) como prueba piloto, en vez de `GET /health/ready` o una Query autenticada. `/health/ready` no pasa por MediatR (usa el middleware nativo de Health Checks), por lo que no cumpliría el requisito explícito de demostrar que la llamada "atraviesa... MediatR". Una Query autenticada requeriría resolver login + seed de usuario de prueba, complejidad innecesaria para un piloto cuyo único objetivo es validar que `WebApplicationFactory` y Testcontainers se comunican correctamente. `RegisterCommand` es anónimo, autocontenido (no depende de estado previo) y atraviesa todas las capas de Clean Architecture (middlewares → MediatR → behaviors de SPEC 01-03 → `UserManager` → EF Core → BD efímera).
  **Corrección de hecho:** se verificó el código de `RegisterCommandHandler.cs` — no envía ningún correo hoy (solo usa `UserManager<ApplicationUser>`, sin `IEmailService`). El mock de `IEmailService` en la factory no es necesario para que este piloto específico pase, pero se mantiene como infraestructura defensiva para pruebas de integración futuras (ver siguiente decisión).

- **Sí:** `CustomWebApplicationFactory` reemplaza `IEmailService` por un mock en `ConfigureTestServices`, como regla estricta de la factory (no solo para este piloto). Ningún I/O externo además de la base de datos efímera debe ejecutarse en CI — evita que el pipeline dependa de red externa, credenciales reales de SendGrid, o envíe correos reales durante las pruebas, independientemente de qué Command/Query se pruebe en el futuro.

- **Sí:** job/step de CI separado para Integration Tests, excluido del gate de cobertura del 90%. Mezclar unitarias e integración en el mismo comando es un anti-patrón: las unitarias deben dar retroalimentación en milisegundos, mientras que levantar contenedores Docker añade segundos/minutos. Además, las pruebas de integración tienden a inflar artificialmente las métricas de cobertura al atravesar múltiples capas en sus happy paths, lo que diluiría el rigor del 90% ya configurado para unitarias.

- **Sí:** reutilizar el ambiente `"Development"` (con `appsettings.Development.json` existente) en vez de crear un ambiente `"Testing"` dedicado. Aplica YAGNI — mantener la cantidad de archivos `appsettings` al mínimo reduce fricción operativa, y la combinación con el mock obligatorio de `IEmailService` cubre el riesgo real (I/O externo) sin necesidad de un archivo de configuración adicional.

- **Sí:** proyecto único y plano `tests/IntegrationTests/JOIN.IntegrationTests.csproj`, sin subcarpeta anidada adicional. La anidación de `tests/UnitTests/JOIN.Application.UnitTest/` se justifica porque hay un proyecto de unitarias por cada capa de arquitectura; las pruebas de integración con `WebApplicationFactory` tratan al sistema completo como una caja gris con un único punto de entrada (la API), por lo que un solo proyecto en la raíz de `IntegrationTests/` es la topología más limpia.

- **Sí:** fijar explícitamente la imagen `mcr.microsoft.com/mssql/server:2022-latest` en vez de usar el default implícito del paquete `Testcontainers.MsSql`. Depender de versiones implícitas es una vulnerabilidad clásica de "rompimiento silencioso" — si el paquete cambia su versión default a una mayor con breaking changes, el CI fallaría sin que el equipo haya tocado código. Anclar la versión blinda el pipeline y garantiza reproducibilidad determinista.

- **Sí:** agregar `public partial class Program { }` al final de `Program.cs`. Es el mecanismo estándar y documentado oficialmente por Microsoft para que `WebApplicationFactory<TEntryPoint>` pueda referenciar la clase de entrada generada por top-level statements, que de otro modo es `internal` y no accesible desde el ensamblado de test. Cambio mínimo, no funcional, sin impacto en runtime de producción.

- **No:** agregar pruebas de integración adicionales más allá del piloto en este spec. El objetivo es la infraestructura base; cada nueva prueba real se agrega en su propio spec/PR conforme se necesite, evitando que este spec crezca indefinidamente.

- **No:** modificar `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, la resiliencia HTTP/EF Core, ni los documentos `specs/01-*.md` a `specs/05-*.md`. Son specs independientes; este solo agrega infraestructura de testing nueva.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Testcontainers requiere un daemon Docker activo. Un desarrollador sin Docker instalado/corriendo localmente verá fallar `dotnet test` en `IntegrationTests` con un error de conexión al daemon, no un fallo de test legible. | El runner `ubuntu-latest` de GitHub Actions trae Docker preinstalado, así que CI no se ve afectado. Para desarrollo local, documentar el requisito de Docker como prerequisito del proyecto. |
| El arranque del contenedor SQL Server (Testcontainers) toma un tiempo fijo no trivial (~15-30s típico, hasta que el motor reporta "ready"), incluso para un único test piloto. Esto añade duración al job de CI de integración cada vez que corre. | Aceptado como costo inherente a pruebas de integración reales contra una BD real. Si se vuelve un problema de tiempo de CI, compartir un único contenedor entre múltiples clases de test (ya marcado explícitamente fuera de alcance de este spec) es la optimización natural a evaluar después. |
| Al reutilizar el ambiente `"Development"`, un cambio futuro a `PasswordPolicySettings` en `appsettings.Development.json` (ej. subir `MinimumLength`) podría romper silenciosamente la prueba piloto, ya que el password de prueba está construido a mano para cumplir la política **actual**. | Riesgo bajo y con fallo visible (el test fallaría con un error de validación claro, no silencioso); se corrige actualizando el password hardcodeado del test cuando cambie la política. |

---

## Lo que **no** está en este spec

- Pruebas de integración adicionales más allá del piloto de `RegisterCommand`.
- Mocks/Fakes de I/O externo distintos a `IEmailService`.
- Ambiente `"Testing"` dedicado o `appsettings.Testing.json`.
- Testcontainers para PostgreSQL.
- Optimización de tiempo de ejecución (contenedor compartido entre clases de test).
- Modificaciones a `RegisterCommandHandler.cs` o lógica de dominio/aplicación.
- Modificaciones a `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, la resiliencia HTTP/EF Core, o los documentos `specs/01-*.md` a `specs/05-*.md`.

Cada uno de estos, si se necesita, va en su propio spec.
