# SPEC 04 — Serilog como motor de logging definitivo

> **Status:** Implementado
> **Depends on:** SPEC 01, SPEC 02, SPEC 03 (referencia informativa únicamente — este spec no modifica ninguno de los tres; les provee el motor de infraestructura de logging que sus mensajes estructurados ya estaban preparados para usar)
> **Date:** 2026-07-13
> **Objective:** Instalar y configurar Serilog.AspNetCore como motor de logging definitivo de la aplicación, con sink de consola en texto legible en Development y JSON compacto en Production, sin modificar ninguna abstracción `ILogger<T>` existente en handlers, controllers o behaviors.

---

## Scope

**In:**

- Paquetes NuGet en `src/4.Services.WebApi/JOIN.Services.WebApi.csproj`: `Serilog.AspNetCore`, `Serilog.Settings.Configuration`, `Serilog.Formatting.Compact`.
- `Program.cs`: patrón de bootstrap en dos etapas recomendado por Serilog — un logger mínimo de arranque (`Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger()`) capturando errores de configuración del host antes de que el logger definitivo exista, seguido de `builder.Host.UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration))` para el logger real.
- `app.Run()` envuelto en `try/catch/finally`, con `Log.CloseAndFlush()` garantizado en el `finally` para asegurar el flush de logs pendientes al apagar la aplicación.
- Sink de consola condicionado por ambiente dentro de la configuración de `Serilog:WriteTo` (o vía código en `Program.cs` si la sección de configuración no permite condicionar el formatter directamente): texto legible en Development (formato default de Serilog), JSON compacto (`Serilog.Formatting.Compact.CompactJsonFormatter`) en Production.
- Nueva sección `"Serilog"` (formato `Serilog.Settings.Configuration`) en `appsettings.json` y `appsettings.Development.json`, reemplazando por completo la sección `"Logging": { "LogLevel": {...} }` existente en ambos archivos (se elimina, no convive).
- Creación de `appsettings.Production.json` (no existe hoy) con su propia sección `"Serilog"`: sink de consola en JSON, y `MinimumLevel.Default` más estricto (`Warning`) con un override específico para el namespace `JOIN` en `Information`, de modo que los logs de negocio de `LoggingBehavior`/`PerformanceBehavior`/`UnhandledExceptionBehavior` (SPEC 02 y 03) sigan visibles en producción aunque se silencie el ruido de framework.
- Ningún cambio en ningún handler, controller o behavior que use `ILogger<T>` — Serilog se conecta vía el adaptador de `Microsoft.Extensions.Logging` que `Serilog.AspNetCore` provee, de forma transparente.

**Out of scope (para specs futuros):**

- Modificar `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs` o los documentos `specs/01-*.md`, `specs/02-*.md`, `specs/03-*.md` — este spec solo les provee el motor de infraestructura; sus mensajes (`{@UserId}`, etc.) ya estaban preparados para esto y no cambian.
- Enrichers (`Serilog.Enrichers.Environment`, `Serilog.Enrichers.Thread`, etc.) — decisión explícita YAGNI del usuario, se evalúan en un spec futuro cuando exista un sink de red real que los aproveche.
- Sinks externos de red (Application Insights, Seq, ELK, Datadog, Elasticsearch) — este spec deja la infraestructura lista (motor + formato JSON en Production) pero **no** conecta ningún destino externo; sigue siendo consola únicamente.
- `UseSerilogRequestLogging()` (middleware de ASP.NET Core que loguea cada request HTTP en una línea compacta) — es una funcionalidad distinta y complementaria al `LoggingBehavior` de SPEC 03 (que opera a nivel de Command/Query de MediatR, no de request HTTP crudo); no fue pedida explícitamente y queda para un spec futuro si se necesita.
- Modificar `GlobalExceptionHandler.cs` — sigue intacto, su `ILogger<GlobalExceptionHandler>` ahora simplemente será servido por Serilog por debajo, sin cambios de código.
- Tests de integración que verifiquen el comportamiento de Serilog en runtime — se verificó que no existen tests basados en `WebApplicationFactory` que arranquen el host real, así que no hay superficie de test automatizado para este spec; la verificación es manual (ver Implementation plan).

---

## Data model

Este spec no introduce clases C# de datos — reemplaza configuración existente. Los artefactos concretos son las secciones `"Serilog"` en los tres archivos de configuración:

```json
// appsettings.json (base — fallback si un ambiente no la sobreescribe)
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "WriteTo": [
    { "Name": "Console" }
  ]
}
```

```json
// appsettings.Development.json (texto legible)
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "WriteTo": [
    {
      "Name": "Console",
      "Args": {
        "outputTemplate": "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
      }
    }
  ]
}
```

```json
// appsettings.Production.json (nuevo archivo — JSON compacto)
"Serilog": {
  "MinimumLevel": {
    "Default": "Warning",
    "Override": {
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning",
      "JOIN": "Information"
    }
  },
  "WriteTo": [
    {
      "Name": "Console",
      "Args": {
        "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact"
      }
    }
  ]
}
```

Conventions:

- La sección `"Logging": { "LogLevel": {...} }` existente se **elimina por completo** de `appsettings.json` y `appsettings.Development.json` — `"Serilog"` pasa a ser la única fuente de verdad de niveles de log.
- El sink `Console` en modo JSON usa `CompactJsonFormatter` provisto por el paquete `Serilog.Formatting.Compact`; el sink `Console` base está disponible transitivamente vía `Serilog.AspNetCore` (que ya incluye `Serilog.Sinks.Console`), sin necesidad de un paquete NuGet adicional explícito.
- El override `"JOIN": "Information"` en Producción existe específicamente para que los logs de `LoggingBehavior`/`PerformanceBehavior`/`UnhandledExceptionBehavior` (categorías `ILogger<TRequest>`, cuyo namespace siempre empieza con `JOIN.Application...`) sigan visibles en producción aunque se suba el nivel default a `Warning` para silenciar el ruido de framework.

---

## Implementation plan

1. Agregar los 3 paquetes NuGet a `src/4.Services.WebApi/JOIN.Services.WebApi.csproj`: `Serilog.AspNetCore`, `Serilog.Settings.Configuration`, `Serilog.Formatting.Compact`. Build sin errores (todavía nada los usa).
2. Reemplazar la sección `"Logging"` por `"Serilog"` (según Data model) en `appsettings.json` y `appsettings.Development.json`; crear `appsettings.Production.json` nuevo con su propia sección `"Serilog"`. La app sigue arrancando exactamente igual que antes — estas secciones aún no tienen efecto porque Serilog no está conectado en `Program.cs`.
3. En `Program.cs`, antes de `var builder = WebApplication.CreateBuilder(args);`, configurar el logger de bootstrap (`Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();`) y envolver el resto del archivo en `try { ... } catch (Exception ex) { Log.Fatal(ex, "JOIN host terminated unexpectedly"); } finally { Log.CloseAndFlush(); }`. Build sin errores.
4. Agregar `builder.Host.UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration));` inmediatamente después de crear `builder`. Build completo de la solución (`4.Services.WebApi`) sin errores.
5. Verificación manual (Development): ejecutar `dotnet run` localmente, confirmar que la consola muestra el formato `{Timestamp:HH:mm:ss} [{Level:u3}] {Message}` en vez del formato default de .NET, y que un log de `LoggingBehavior` (SPEC 03) aparece correctamente formateado con ese template.
6. Verificación manual (Production): correr la app con la variable de entorno `ASPNETCORE_ENVIRONMENT=Production` temporalmente, confirmar que la consola emite líneas JSON compactas en vez de texto legible, que un log `Warning` de `Microsoft.AspNetCore` **no** aparece (silenciado), y que un log de categoría `JOIN.*` (ej. de `PerformanceBehavior`) **sí** aparece pese al `MinimumLevel.Default: Warning` (valida el override). Revertir la variable de entorno al terminar.
7. Verificación manual (shutdown limpio): detener la aplicación (`Ctrl+C`) y confirmar que no se lanza ninguna excepción durante el apagado y que los últimos logs pendientes se escriben antes de que el proceso termine (confirma que `Log.CloseAndFlush()` se ejecuta correctamente).

---

## Acceptance criteria

- [ ] `JOIN.Services.WebApi.csproj` referencia `Serilog.AspNetCore`, `Serilog.Settings.Configuration` y `Serilog.Formatting.Compact`.
- [ ] `Program.cs` inicializa un logger de bootstrap con `CreateBootstrapLogger()` antes de construir el `WebApplicationBuilder`.
- [ ] `Program.cs` llama `builder.Host.UseSerilog(...)` leyendo la configuración vía `ReadFrom.Configuration(context.Configuration)`.
- [ ] `Program.cs` envuelve el arranque del host en `try/catch/finally`, con `Log.CloseAndFlush()` garantizado en el `finally`.
- [ ] `appsettings.json` y `appsettings.Development.json` ya no contienen la sección `"Logging": { "LogLevel": {...} }` — fue reemplazada por `"Serilog"`.
- [ ] `appsettings.Production.json` existe, con su propia sección `"Serilog"` configurando sink JSON compacto y `MinimumLevel.Default: "Warning"`.
- [ ] En Development, la consola muestra logs en texto legible con el `outputTemplate` definido.
- [ ] En Production, la consola muestra logs en formato JSON compacto (`CompactJsonFormatter`).
- [ ] En Production, un log `Warning` de la categoría `Microsoft.AspNetCore` no aparece en consola (silenciado por `MinimumLevel.Override`).
- [ ] En Production, un log `Information` de una categoría `JOIN.*` (ej. `LoggingBehavior`/`PerformanceBehavior`) sí aparece en consola, pese al `MinimumLevel.Default: "Warning"`.
- [ ] Ningún handler, controller o behavior existente que use `ILogger<T>` fue modificado.
- [ ] `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, `GlobalExceptionHandler.cs` y los documentos `specs/01-*.md`, `specs/02-*.md`, `specs/03-*.md` permanecen sin modificaciones.
- [ ] Al detener la aplicación (`Ctrl+C` o shutdown normal), no se lanza ninguna excepción no controlada durante el apagado.
- [ ] La solución compila con 0 errores en `4.Services.WebApi`.

---

## Decisiones

- **Sí:** formato dual por ambiente — texto legible en Development, JSON compacto (`Serilog.Formatting.Compact`) en Production. Mantiene la ergonomía del desarrollador en local mientras cumple con los estándares de observabilidad de sistemas centralizados en producción (formato estructurado listo para ingestionadores como ELK, Datadog o Application Insights), consumiendo de forma elegante la infraestructura de mensajes estructurados que ya dejaron preparada SPEC 02 y SPEC 03.

- **Sí:** migrar por completo `"Logging": { "LogLevel": {...} }` a `"Serilog": { "MinimumLevel": {...} }`, eliminando la sección vieja en vez de dejarla convivir. Mantiene una única fuente de verdad para el sistema de logging y evita configuración "zombi" que confunda sobre dónde se rigen realmente los umbrales de log en cada ambiente.

- **Sí:** crear `appsettings.Production.json` como archivo nuevo, siguiendo el patrón estándar de .NET de segmentar configuración por ambiente (ya usado hoy por `appsettings.Development.json`). Es coherente con la metodología 12-Factor App, y da el contenedor aislado necesario para el comportamiento dual (JSON en producción, texto en desarrollo) y para afinar niveles mínimos de log de forma independiente al entorno de desarrollo.

- **Sí:** en `appsettings.Production.json`, subir `MinimumLevel.Default` a `Warning` pero mantener un override específico `"JOIN": "Information"`. Sin este override, los logs de negocio de `LoggingBehavior`/`PerformanceBehavior` (que usan `LogInformation`) quedarían completamente silenciados en producción — el override garantiza que se sigan viendo, mientras se filtra el ruido de framework (`Microsoft.AspNetCore`, `Microsoft.EntityFrameworkCore`).

- **No:** instalar enrichers (`Serilog.Enrichers.Environment`, `Serilog.Enrichers.Thread`, etc.) en esta fase. Principio YAGNI — inflarían el payload JSON sin que exista todavía un recolector (ELK/Datadog) configurado para indexarlos y sacarles provecho; se evaluarán en un spec futuro cuando se conecte el sink de red definitivo.

- **No:** conectar ningún sink de red externo (Application Insights, Seq, ELK, Datadog). Este spec resuelve el motor de logging y el formato de salida, no el destino final — coherente con las decisiones de SPEC 02/03 que ya dejaron esa integración explícitamente para un spec futuro.

- **No:** agregar `UseSerilogRequestLogging()` (logging de requests HTTP a nivel de middleware). No fue pedido explícitamente y es una capa distinta y complementaria al `LoggingBehavior` de SPEC 03 (que opera a nivel de MediatR, no de request HTTP crudo); se evalúa en un spec futuro si se necesita esa visibilidad adicional.

- **No:** modificar `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs` ni `GlobalExceptionHandler.cs`. Todos siguen usando `ILogger<T>` sin cambios — Serilog se conecta de forma transparente vía el adaptador de `Microsoft.Extensions.Logging` que provee `Serilog.AspNetCore`.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| El patrón de bootstrap logger + `try/catch/finally` requiere envolver **todo** el contenido existente de `Program.cs` (~130+ líneas: Identity, JWT, health checks, CORS, migraciones automáticas, etc.) dentro de un bloque `try`. Es un cambio estructural grande en un archivo central y crítico, con riesgo de error de sintaxis o indentación accidental. | Revisión cuidadosa del diff completo de `Program.cs` línea por línea antes de dar por cerrado el paso 3 del plan; verificar manualmente que todos los endpoints existentes (login, health checks, `/scalar/v1`) siguen respondiendo igual tras el cambio. |
| Al eliminar la sección `"Logging": { "LogLevel": {...} }`, si algún paquete de terceros ya instalado (ej. `AspNetCore.HealthChecks.UI`) leyera esa sección directamente en vez de resolver `ILogger<T>` vía DI, podría perder configuración silenciosamente. | Bajo riesgo — no se detectó ningún uso directo de `IConfiguration.GetSection("Logging")` en el código actual. Se valida igual en la verificación manual del plan (pasos 5-6). |
| `MinimumLevel.Default: "Warning"` en producción no soporta cambio en caliente (`ReloadOnChange`) en este spec. Si en un incidente real se necesita bajar temporalmente a `Information` para diagnosticar, requiere editar `appsettings.Production.json` y reiniciar/redeployar el proceso — no hay forma de ajustarlo sin downtime. | Aceptado como limitación de este spec. Si se necesita ajuste de nivel sin reinicio, es un spec futuro (`ReloadOnChange` + `Serilog.Settings.Configuration`, o un sink dinámico controlado por endpoint administrativo). |

---

## Lo que **no** está en este spec

- Modificaciones a `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, `GlobalExceptionHandler.cs` o los documentos `specs/01-*.md`, `specs/02-*.md`, `specs/03-*.md`.
- Enrichers (`Serilog.Enrichers.Environment`, `Serilog.Enrichers.Thread`, etc.).
- Sinks externos de red (Application Insights, Seq, ELK, Datadog, Elasticsearch).
- `UseSerilogRequestLogging()` (logging de requests HTTP a nivel de middleware).
- Cambio de nivel de log en caliente (`ReloadOnChange`) sin reinicio del proceso.

Cada uno de estos, si se necesita, va en su propio spec.
