# SPEC 05 — Resiliencia HTTP (Polly v8) y CommandTimeout de EF Core

> **Status:** Draft
> **Depends on:** Ninguna
> **Date:** 2026-07-13
> **Objective:** Configurar Microsoft.Extensions.Http.Resilience (Polly v8) sobre un HttpClient tipado para SendGrid con retry/circuit-breaker/timeout estándar, y establecer explícitamente el CommandTimeout de EF Core en SQL Server, sin modificar lógica de dominio.

---

## Scope

**In:**

- Paquete NuGet `Microsoft.Extensions.Http.Resilience` agregado a `src/3.Infrastructure/JOIN.Infrastructure.csproj`.
- Modificar `SendGridEmailAdapter.cs` (`src/3.Infrastructure/Messaging/SendGrid/`): el constructor pasa a aceptar `HttpClient httpClient` (patrón Typed Client), y `SendEmailAsync` construye `new SendGridClient(httpClient, _options.ApiKey)` en vez de `new SendGridClient(_options.ApiKey)`.
- Modificar `DependencyInjection.cs` (`src/3.Infrastructure/`): reemplazar `services.AddTransient<IEmailService, SendGridEmailAdapter>()` por `services.AddHttpClient<IEmailService, SendGridEmailAdapter>().AddStandardResilienceHandler(options => { ... })`, registrando `SendGridEmailAdapter` como Typed Client de `IHttpClientFactory`.
- Dentro de `AddStandardResilienceHandler`, personalizar únicamente lo pedido explícitamente sobre el preset estándar de Microsoft:
  - `Retry.MaxRetryAttempts = 3`.
  - `Retry.ShouldHandle` extendido para tratar como transitorio: `5xx`, `408 Request Timeout`, `429 Too Many Requests`, y excepciones de red (`HttpRequestException`).
  - `Retry.BackoffType`, `AttemptTimeout`, `CircuitBreaker` y `TotalRequestTimeout` quedan en los valores default del preset de Microsoft (`AddStandardResilienceHandler()`), sin override adicional.
- Modificar `src/3.Persistence/Configuration/ConfigureServices.cs`: agregar `.CommandTimeout(30)` al builder de `UseSqlServer` (línea ~61), haciendo explícito el timeout de 30 segundos que hoy usa el default implícito del driver.
- Ambos valores (reintentos/códigos HTTP, `CommandTimeout`) quedan **hardcodeados en código**, no configurables vía `appsettings.json` — decisión explícita, ver Decisiones.

**Out of scope (para specs futuros):**

- Modificar `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, o los documentos `specs/01-*.md` a `specs/04-*.md`.
- Resiliencia para las **Queries vía Dapper** (`ISqlConnectionFactory`) — el `CommandTimeout` de este spec cubre únicamente el `DbContext` de EF Core (usado por los Commands). Las ~75 Queries del sistema, que van 100% por Dapper con SQL crudo, **no quedan protegidas contra cuelgues** por este spec — es un gap real pero fuera del alcance textual pedido ("CommandTimeout de Entity Framework Core"); candidato explícito para un spec futuro (`CommandDefinition(commandTimeout: ...)` en cada query, o un valor default centralizado en `ISqlConnectionFactory`).
- `EnableRetryOnFailure` de EF Core (reintentos automáticos ante fallos transitorios de conexión SQL) — es una funcionalidad distinta al `CommandTimeout`; no fue pedida y queda para evaluación futura.
- Resiliencia para el proveedor PostgreSQL — no está registrado activamente en `ConfigureServices.cs` hoy (solo `UseSqlServer`), así que no aplica (YAGNI).
- Cualquier otra integración HTTP externa además de SendGrid — no existe ninguna otra en el código hoy; el patrón queda como plantilla reutilizable, pero no se crea ningún cliente adicional.
- Hacer los valores de resiliencia/timeout configurables vía `appsettings.json` (patrón `IOptions`) — decisión explícita de mantenerlos en código por simplicidad, ver Decisiones.

---

## Data model

Este spec no introduce clases C# de datos nuevas ni secciones de `appsettings.json` — los valores de resiliencia y timeout quedan hardcodeados directamente en la configuración de DI. Los artefactos concretos son:

```csharp
// src/3.Infrastructure/DependencyInjection.cs (reemplaza AddTransient<IEmailService, SendGridEmailAdapter>())
services.AddHttpClient<IEmailService, SendGridEmailAdapter>()
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or >= HttpStatusCode.InternalServerError
            || args.Outcome.Exception is HttpRequestException);
    });
```

```csharp
// src/3.Persistence/Configuration/ConfigureServices.cs (dentro de AddDbContext → UseSqlServer)
options.UseSqlServer(connectionString,
    builder => builder
        .MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)
        .CommandTimeout(30));
```

```csharp
// src/3.Infrastructure/Messaging/SendGrid/SendGridEmailAdapter.cs (firma del constructor)
public sealed class SendGridEmailAdapter(
    HttpClient httpClient,
    IOptions<SendGridOptions> options,
    ILogger<SendGridEmailAdapter> logger) : IEmailService
```

Conventions:

- `AddHttpClient<IEmailService, SendGridEmailAdapter>()` registra `SendGridEmailAdapter` como Typed Client — `IHttpClientFactory` construye y le inyecta el `HttpClient` automáticamente vía el constructor, reemplazando el registro `AddTransient` anterior por completo (no coexisten).
- `SendGridClient` se construye ahora como `new SendGridClient(httpClient, _options.ApiKey)` dentro de `SendEmailAsync`, usando el `HttpClient` inyectado en vez de crear uno nuevo.
- `.CommandTimeout(30)` es un método de `SqlServerDbContextOptionsBuilder`, específico del provider SQL Server — no aplica a otros providers.

---

## Implementation plan

1. Agregar el paquete `Microsoft.Extensions.Http.Resilience` a `src/3.Infrastructure/JOIN.Infrastructure.csproj`. Build sin errores (todavía nada lo usa).
2. Modificar `SendGridEmailAdapter.cs` (constructor + `SendEmailAsync`) y `DependencyInjection.cs` (reemplazar `AddTransient<IEmailService, SendGridEmailAdapter>()` por `AddHttpClient<IEmailService, SendGridEmailAdapter>().AddStandardResilienceHandler(...)`) **en el mismo paso** — son cambios interdependientes; si se separan, el sistema quedaría en un estado no funcional entre commits (constructor exige `HttpClient` que el DI todavía no provee). Build completo de `3.Infrastructure` sin errores.
3. Modificar `src/3.Persistence/Configuration/ConfigureServices.cs` agregando `.CommandTimeout(30)` al builder de `UseSqlServer`. Build de `3.Persistence` sin errores. Paso independiente del anterior.
4. Build completo de la solución (`3.Infrastructure`, `3.Persistence`, `4.Services.WebApi`). Verificación: 0 errores.
5. Verificación funcional manual (SendGrid, camino feliz): disparar un envío de correo real de prueba (ej. `RequestEmailChangeCommand` con credenciales válidas de SendGrid en `appsettings.Development.json`) y confirmar que el correo se sigue enviando correctamente con el `HttpClient` inyectado — sin regresión funcional respecto al comportamiento actual.
6. Verificación funcional manual (resiliencia): apuntar temporalmente la configuración de SendGrid a un endpoint inaccesible o forzar un fallo de red, y confirmar (por logs de consola, o por el tiempo total transcurrido antes del fallo definitivo) que se observan múltiples intentos antes de que la operación falle, en vez de un único intento inmediato. Revertir la configuración de prueba al terminar.
7. Verificación funcional (`CommandTimeout`): sin necesidad de una conexión real a base de datos, confirmar mediante una verificación rápida (ej. resolver `ApplicationDbContext` y leer `context.Database.GetCommandTimeout()`) que el valor configurado es `30`.

---

## Acceptance criteria

- [ ] `src/3.Infrastructure/JOIN.Infrastructure.csproj` referencia `Microsoft.Extensions.Http.Resilience`.
- [ ] `SendGridEmailAdapter` recibe `HttpClient` por constructor (patrón Typed Client) en vez de instanciar `SendGridClient` con un `HttpClient` propio.
- [ ] `DependencyInjection.cs` registra `IEmailService`/`SendGridEmailAdapter` vía `AddHttpClient<IEmailService, SendGridEmailAdapter>()`, no vía `AddTransient`.
- [ ] El pipeline de resiliencia se configura con `AddStandardResilienceHandler(...)` (Polly v8 / `ResiliencePipelineBuilder` por debajo) — no se usa sintaxis legacy de Polly v7 (`Policy.Handle(...)`).
- [ ] `Retry.MaxRetryAttempts` está configurado explícitamente en `3`.
- [ ] El predicado de reintento trata como transitorios: cualquier `5xx`, `408`, `429`, y `HttpRequestException`.
- [ ] `CircuitBreaker`, `AttemptTimeout` y `TotalRequestTimeout` permanecen en los valores default del preset `AddStandardResilienceHandler()`, sin overrides adicionales.
- [ ] `src/3.Persistence/Configuration/ConfigureServices.cs` configura `.CommandTimeout(30)` en el builder de `UseSqlServer`.
- [ ] `ApplicationDbContext.Database.GetCommandTimeout()` devuelve `30` tras la configuración.
- [ ] Ningún valor de resiliencia o timeout está expuesto vía `appsettings.json` — quedan hardcodeados en código, según decisión explícita.
- [ ] Un envío de correo real vía `IEmailService` sigue funcionando correctamente tras el cambio (sin regresión).
- [ ] Ante un fallo simulado del endpoint de SendGrid, se observan múltiples intentos antes del fallo definitivo (evidencia de que el Retry está activo).
- [ ] No se modifica ninguna lógica de dominio (`src/1.Domain`) ni de Application (`src/2.Application`), salvo lo estrictamente necesario en `3.Infrastructure` y `3.Persistence`.
- [ ] `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, y los documentos `specs/01-*.md` a `specs/04-*.md` permanecen sin modificaciones.
- [ ] La solución compila con 0 errores en `3.Infrastructure`, `3.Persistence` y `4.Services.WebApi`.

---

## Decisiones

- **Sí:** modificar `SendGridEmailAdapter.cs` para inyectar `HttpClient` vía `IHttpClientFactory`, en vez de dejar el pipeline de resiliencia sin conectar. Es requisito obligatorio de `Microsoft.Extensions.Http.Resilience` (opera sobre `HttpClient`s gestionados por `IHttpClientFactory`), es código puramente de Infraestructura (no lógica de dominio, respeta la regla de aislamiento), y un pipeline de resiliencia sin nada que lo consuma no tendría valor demostrable.

- **Sí:** usar el patrón **Typed Client** (`AddHttpClient<IEmailService, SendGridEmailAdapter>()`) en vez de cliente nombrado por string (`AddHttpClient("SendGrid")`). Evita contaminar el `HttpClient` global, es la mejor práctica idiomática de .NET, y deja establecida una plantilla limpia y tipada para cualquier futura integración de I/O externo (cada una con su propio Typed Client y su propia configuración de resiliencia a medida).

- **Sí:** usar el preset `AddStandardResilienceHandler()` de Microsoft en vez de un `ResiliencePipelineBuilder` custom desde cero. Al trabajar sobre .NET 10, la mejor decisión es apalancarse en los bloques de construcción oficiales e idiomáticos del framework — el preset entrega una topología (Retry + Circuit Breaker + Timeout por intento + Timeout total) ya probada en batalla. El Circuit Breaker incluido no es sobreingeniería: es el mecanismo de "fail-fast" que evita que un proveedor externo caído ahogue el servidor propio acumulando hilos en reintentos inútiles.

- **Sí:** `Retry.MaxRetryAttempts = 3`, con backoff exponencial (default del preset). Es el balance estándar de la industria entre absorber fallos transitorios de red y no generar latencia inaceptable para el usuario final.

- **Sí:** incluir explícitamente `429 Too Many Requests` en el predicado de reintento, además de `5xx`/`408`. Las APIs externas modernas (incluida SendGrid) aplican rate limiting; tratar el 429 como transitorio y dejar que el pipeline reintente con backoff evita que un cuello de botella temporal de infraestructura del proveedor se traduzca en un fallo lógico de negocio (ej. un correo de recuperación de contraseña que no se envía) en nuestro sistema.

- **Sí:** `CommandTimeout` de EF Core explícito en `30` segundos, aplicado únicamente a `UseSqlServer`. Hacer explícito el valor (que hoy es un default implícito del driver) lo saca de las "sombras" y lo pone al frente en la capa de infraestructura, visible y gobernable. No se configura para PostgreSQL porque ese provider no está registrado activamente en `ConfigureServices.cs` hoy — configurar resiliencia para infraestructura que no existe en el contenedor de DI violaría YAGNI.

- **Sí:** mantener `Retry`, `CircuitBreaker`, timeouts y `CommandTimeout` **hardcodeados en código**, sin exponerlos vía `appsettings.json`/`IOptions`. A diferencia de `PerformanceSettings` (SPEC 02), estos valores no fueron pedidos como configurables por ambiente, y mantenerlos en código evita superficie de configuración adicional sin un caso de uso concreto que la justifique hoy.

- **No:** dar resiliencia a las Queries vía Dapper (`ISqlConnectionFactory`) en este spec. El pedido fue explícitamente "CommandTimeout de Entity Framework Core" — las Queries no pasan por EF Core en este proyecto (van 100% por Dapper, por convención CQRS del repositorio). Es un gap real para el objetivo de "que las consultas nunca se queden colgadas", documentado como riesgo y candidato a spec futuro.

- **No:** agregar `EnableRetryOnFailure` de EF Core (reintentos ante fallos transitorios de conexión). Es una funcionalidad de resiliencia distinta al `CommandTimeout` (uno reintenta conexiones caídas, el otro limita cuánto puede tardar un comando ya conectado); no fue pedida explícitamente.

- **No:** modificar `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, ni los documentos `specs/01-*.md` a `specs/04-*.md`. Son specs independientes de infraestructura distinta (pipeline de MediatR vs. resiliencia de I/O); este spec no los toca.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Las Queries vía Dapper (`ISqlConnectionFactory`) no reciben ningún timeout explícito en este spec — solo el `DbContext` de EF Core (usado por Commands) queda cubierto. El objetivo general de "que las consultas nunca se queden colgadas" queda parcialmente cumplido. | Documentado explícitamente en Scope/Decisiones como gap conocido; candidato directo a spec futuro (`CommandDefinition(commandTimeout: ...)` por query o un default centralizado). |
| Con `Retry.MaxRetryAttempts = 3` + backoff exponencial + `TotalRequestTimeout` default (~30s) del preset, una llamada a `SendEmailAsync` que falla puede tardar hasta ~30s en fallar definitivamente, en vez de fallar rápido como hoy. Esto afecta la latencia HTTP percibida por el usuario en Commands síncronos como `RequestEmailChangeCommand`, que esperan la respuesta completa de `IEmailService` antes de responder al cliente. | `RequestEmailChangeCommand` fue excluido de `TransactionBehavior` en SPEC 01 (I/O externo), así que este retraso no mantiene abierta ninguna transacción de base de datos ni bloquea el pool de conexiones SQL — el impacto se limita a la latencia HTTP de ese endpoint puntual. Si se vuelve un problema real, hacer el envío de email asíncrono/fire-and-forget es candidato a spec futuro. |
| El Circuit Breaker del preset estándar usa umbrales default (`FailureRatio`, `SamplingDuration`, `MinimumThroughput`) pensados para servicios de alto tráfico. En ambientes de bajo volumen (desarrollo, staging), es posible que nunca se alcance el mínimo de throughput necesario para que el circuito llegue a abrirse, dejando esa capa de protección efectivamente inactiva ahí. | Aceptado — es un límite conocido de usar el preset sin overrides (decisión explícita de SPEC 05). No bloquea Producción, donde el volumen sí puede activar la protección. |

---

## Lo que **no** está en este spec

- Timeout/resiliencia para las Queries vía Dapper (`ISqlConnectionFactory`).
- `EnableRetryOnFailure` de EF Core (reintentos de conexión transitorios).
- Resiliencia para el proveedor PostgreSQL (no registrado activamente hoy).
- Cualquier integración HTTP externa además de SendGrid.
- Configuración de resiliencia/timeout vía `appsettings.json` (`IOptions`) — queda hardcodeada en código.
- Envío de email asíncrono/fire-and-forget.
- Modificaciones a `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, o los documentos `specs/01-*.md` a `specs/04-*.md`.

Cada uno de estos, si se necesita, va en su propio spec.
