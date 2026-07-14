# SPEC 02 — PerformanceBehavior para medición de latencia en MediatR

> **Status:** Draft
> **Depends on:** SPEC 01 (referencia informativa únicamente — este spec no modifica SPEC 01 ni TransactionBehavior.cs)
> **Date:** 2026-07-13
> **Objective:** Implementar PerformanceBehavior, un pipeline behavior de MediatR que mida con Stopwatch la duración de todo IRequest (Commands y Queries) y emita un warning estructurado cuando supere un umbral configurable vía appsettings (default 200ms).

---

## Scope

**In:**

- Clase `PerformanceBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>` en `src/2.Application/Common/PerformanceBehavior.cs`.
- Aplica al 100% de los `IRequest` (Commands y Queries), sin interfaz marcadora — a diferencia de `TransactionBehavior` (SPEC 01), no hay opt-in.
- Clase `PerformanceSettings` en `src/2.Application/Common/PerformanceSettings.cs`, con una propiedad `ThresholdMilliseconds` (default `200`), siguiendo el mismo patrón que `PaginationSettings`.
- Binding de `PerformanceSettings` en `Program.cs` vía `builder.Configuration.GetSection("Performance")` + `builder.Services.Configure<PerformanceSettings>(...)`.
- Nueva sección `"Performance": { "ThresholdMilliseconds": 200 }` en `appsettings.json` (y, si existen, en `appsettings.Development.json`/`appsettings.Production.json`).
- Medición con `System.Diagnostics.Stopwatch` dentro de un bloque `try/finally`: el `Stopwatch.Stop()` y la evaluación del umbral ocurren siempre en `finally`, sin importar si `next()` tuvo éxito o lanzó excepción.
- Si el tiempo transcurrido supera `ThresholdMilliseconds`, se inyecta `ILogger<TRequest>` y se emite:
  `_logger.LogWarning("JOIN Long Running Request: {Name} ({ElapsedMilliseconds} ms) {@UserId}", requestName, elapsedMilliseconds, userId);`
- `userId` se obtiene de `ICurrentUserService.UserId` (inyectado en el behavior); si es `null` (contexto no autenticado), se loguea tal cual (`null`).
- Registro de `PerformanceBehavior` en `ConfigureServices.cs` — el orden exacto respecto a `ValidationBehavior` y `TransactionBehavior` se define y justifica en la sección de Decisiones de este documento.

**Out of scope (para specs futuros):**

- Modificar `TransactionBehavior.cs`, `ITransactionalCommand.cs` o el documento `specs/01-transaction-behavior-mediatr.md` — SPEC 01 permanece intacto.
- Integrar Serilog, Application Insights o cualquier sink estructurado real — este spec solo deja el *formato* del mensaje listo (`{@UserId}`) para cuando eso ocurra; la infraestructura de logging en sí (providers, sinks) es un spec aparte.
- Loguear el payload del request (`{@Request}`) — decisión de seguridad ya cerrada, explícitamente fuera de alcance.
- Cualquier acción automática además de loguear (alertas, métricas a un sistema externo tipo Prometheus/Grafana, circuit breakers, cancelación de requests lentos).
- Umbrales distintos por tipo de request (ej. un umbral más alto para reportes pesados) — el umbral es único y global vía `PerformanceSettings.ThresholdMilliseconds`.

---

## Data model

```csharp
// src/2.Application/Common/PerformanceSettings.cs
public class PerformanceSettings
{
    /// <summary>
    /// Umbral en milisegundos a partir del cual se emite un warning de rendimiento.
    /// </summary>
    public int ThresholdMilliseconds { get; set; } = 200;
}
```

```json
// appsettings.json (nueva sección)
"Performance": {
  "ThresholdMilliseconds": 200
}
```

Conventions:

- `PerformanceSettings` sigue el mismo patrón que `PaginationSettings` (`src/2.Application/Common/PaginationSettings.cs`): clase POCO simple con valores por defecto, sin validación adicional.
- El nombre de sección en `appsettings.json` es `"Performance"` (no `"PerformanceSettings"`), igual que `"AreaPagination"` no se llama `"PaginationSettings"` — se nombra por el dominio de configuración, no por el nombre de la clase C#.
- No se introduce ninguna entidad persistida en base de datos ni DTO — este spec es puramente infraestructura de observabilidad en memoria.

---

## Implementation plan

1. Crear `PerformanceSettings` en `src/2.Application/Common/PerformanceSettings.cs`. El sistema sigue compilando y corriendo igual (clase sin uso todavía).
2. En `Program.cs`, agregar `builder.Configuration.GetSection("Performance")` + `builder.Services.Configure<PerformanceSettings>(...)` (mismo patrón que `AreaPagination`/`PaginationSettings`), y agregar la sección `"Performance": { "ThresholdMilliseconds": 200 }` a `appsettings.json`. Verificación manual: la app arranca sin errores de configuración.
3. Crear `PerformanceBehavior<TRequest, TResponse>` en `src/2.Application/Common/PerformanceBehavior.cs`: inyecta `ILogger<TRequest> logger`, `IOptions<PerformanceSettings> options` e `ICurrentUserService currentUserService`. En `Handle`, inicia un `Stopwatch`, ejecuta `await next()` dentro de un `try`, y en el `finally` detiene el stopwatch y compara `ElapsedMilliseconds` contra `options.Value.ThresholdMilliseconds`, emitiendo el `LogWarning` si lo supera. Build de `2.Application` sin errores.
4. Registrar `PerformanceBehavior` en `ConfigureServices.cs` como el **primer** behavior de la cadena `AddMediatR` (antes de `ValidationBehavior`), según el orden justificado en la sección de Decisiones.
5. Build completo de la solución (`2.Application`, `4.Services.WebApi`). Verificación manual: 0 errores.
6. Verificación funcional manual: bajar temporalmente `ThresholdMilliseconds` a `0` en `appsettings.Development.json`, ejecutar cualquier endpoint existente (ej. `GET /api/persons`), y confirmar en consola el log `"JOIN Long Running Request: {Name} (X ms) {UserId}"` con los tres valores poblados y **sin** ningún dato del payload del request. Revertir el valor a `200` al terminar la verificación.

---

## Acceptance criteria

- [ ] `PerformanceSettings` existe en `src/2.Application/Common/PerformanceSettings.cs` con la propiedad `ThresholdMilliseconds` (default `200`).
- [ ] `appsettings.json` contiene la sección `"Performance": { "ThresholdMilliseconds": 200 }`.
- [ ] `Program.cs` enlaza `PerformanceSettings` a la sección `"Performance"` vía `IOptions<PerformanceSettings>`.
- [ ] `PerformanceBehavior<TRequest, TResponse>` existe en `src/2.Application/Common/PerformanceBehavior.cs` e implementa `IPipelineBehavior<TRequest, TResponse>`.
- [ ] `PerformanceBehavior` se ejecuta para **cualquier** `IRequest` (Commands y Queries), sin necesidad de implementar ninguna interfaz marcadora.
- [ ] La medición usa `System.Diagnostics.Stopwatch`, y `Stop()` + la evaluación del umbral ocurren dentro de un bloque `finally`.
- [ ] Si `next()` lanza una excepción, el warning de rendimiento se evalúa igual (si corresponde) y la excepción original se sigue propagando sin ser envuelta ni silenciada.
- [ ] Cuando el tiempo transcurrido supera `ThresholdMilliseconds`, se emite exactamente: `logger.LogWarning("JOIN Long Running Request: {Name} ({ElapsedMilliseconds} ms) {@UserId}", requestName, elapsedMilliseconds, userId)`.
- [ ] Cuando el tiempo transcurrido **no** supera el umbral, no se emite ningún log.
- [ ] El log nunca incluye el objeto `request` ni ninguna de sus propiedades.
- [ ] `userId` proviene de `ICurrentUserService.UserId` (puede ser `null` en contexto no autenticado, y se loguea como tal).
- [ ] `ILogger<TRequest>` se inyecta parametrizado por el tipo concreto de request (no una categoría fija como `ILogger<PerformanceBehavior<TRequest,TResponse>>`).
- [ ] `PerformanceBehavior` está registrado en `ConfigureServices.cs` como el primer behavior de la cadena, antes de `ValidationBehavior` y `TransactionBehavior`.
- [ ] `specs/01-transaction-behavior-mediatr.md` y `TransactionBehavior.cs` permanecen sin modificaciones.
- [ ] La solución compila con 0 errores en `2.Application` y `4.Services.WebApi`.

---

## Decisiones

- **Sí:** aplicar `PerformanceBehavior` al 100% de los `IRequest`, sin interfaz marcadora (a diferencia de `ITransactionalCommand` en SPEC 01). La observabilidad de latencia debe cubrir también las 75+ Queries del sistema — son justamente las que más impactan la reactividad percibida por el frontend.

- **Sí:** registrar `PerformanceBehavior` como el **primer** behavior de la cadena, antes de `ValidationBehavior` y `TransactionBehavior`. Orden final: `PerformanceBehavior → ValidationBehavior → TransactionBehavior → Handler`.
  **Motivo:** el objetivo explícito es "proteger la reactividad de nuestro ecosistema frontend" — eso significa medir la latencia *tal como la experimenta el cliente HTTP*, desde que MediatR recibe el request hasta que devuelve la respuesta. Si `PerformanceBehavior` se registrara después de `ValidationBehavior`, un request que falla validación (rápido) no distorsionaría la métrica, pero tampoco mediríamos el overhead real de validar payloads grandes. Si se registrara después de `TransactionBehavior`, quedaría *fuera* de la medición el tiempo de `BeginTransactionAsync`/`CommitAsync` — que si el pool de conexiones está saturado, es exactamente el síntoma que este behavior debe detectar. Midiendo desde afuera de todo el pipeline, `PerformanceBehavior` captura el costo total real, sin puntos ciegos.

- **Sí:** medir con `try/finally`, evaluando el umbral incluso si `next()` lanza excepción. Un request que agota el pool de conexiones y falla después de 3 segundos es un incidente de rendimiento tan válido como uno que tarda 3 segundos y tiene éxito — silenciar ese caso dejaría un punto ciego justo en el escenario más crítico (fallos bajo carga).

- **Sí:** excluir `{@Request}` del mensaje de log por completo, en vez de enmascarar propiedades sensibles por reflexión. Elimina de raíz el riesgo de filtrar `LoginCommand.Password`, `RegisterCommand.Password` y los campos de `ChangeMyPasswordCommand` en logs de texto plano, y evita agregar overhead de reflexión dentro del componente que mide performance — sería contradictorio que el propio medidor de latencia introdujera latencia.

- **Sí:** mantener el operador de desestructuración `{@UserId}` en el template del mensaje, aunque hoy el proyecto no usa Serilog (solo providers default de Microsoft.Extensions.Logging, donde `@` no tiene efecto especial). Decisión explícita del usuario: deja la infraestructura preparada para una migración futura a Serilog/Application Insights/ELK sin tener que reeditar este archivo. Con el logger nativo actual, el comportamiento es idéntico a `{UserId}` — no hay downside funcional hoy.

- **Sí:** implementar el umbral vía `IOptions<PerformanceSettings>` en vez de una constante hardcodeada. Mantiene coherencia con el patrón ya usado por `PaginationSettings`/`AreaPagination`, y permite ajustar el umbral por ambiente (`appsettings.Production.json`) sin recompilar, siguiendo el principio de 12-Factor App de separar configuración de código.

- **No:** integrar Serilog, Application Insights o cualquier sink real en este spec. Es un cambio de infraestructura transversal (no solo de este behavior) que merece su propio spec — aquí solo se deja el formato del mensaje listo para esa migración futura.

- **No:** loguear a ningún nivel (Debug/Trace) cuando el request está por debajo del umbral. Evita ruido en los logs; el objetivo es señal de alerta, no traza completa de cada request.

- **No:** modificar `TransactionBehavior.cs`, `ITransactionalCommand.cs` ni el documento `specs/01-transaction-behavior-mediatr.md`. Son specs independientes; este solo agrega una línea de registro nueva en `ConfigureServices.cs`, sin tocar lo que SPEC 01 ya definió.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| El umbral de 200ms podría generar ruido de logs en endpoints que ya se sabe que son lentos por diseño (ej. queries con `LIKE '%...%'` sin índice, como las de `RoleSystemOptions`), inflando los logs de warning sin que sea un incidente real. | Es configurable vía `appsettings.json` por ambiente sin recompilar; el ajuste fino del umbral queda para observación post-deploy, no bloquea este spec. |
| `ILogger<TRequest>` genera una categoría de logger distinta por cada tipo de Command/Query (~180 tipos en el sistema hoy). | Los providers default de .NET Logging soportan esto sin configuración adicional (la categoría es simplemente el nombre completo del tipo); si en el futuro se define un nivel mínimo por categoría, deberá tenerse en cuenta esta cardinalidad. |
| `ICurrentUserService.UserId` no fue verificado fuera de un contexto HTTP autenticado (ej. si en el futuro se disparara un Command desde un job en background o un seeder). Podría devolver `null` sin problema, o comportarse distinto según la implementación concreta de `ICurrentUserService`. | Hoy todo `IRequest` se dispara desde Controllers con contexto HTTP, así que no aplica. Se documenta como riesgo latente a validar si se introduce ejecución de Commands fuera de un request HTTP. |

---

## Lo que **no** está en este spec

- Integración de Serilog, Application Insights o cualquier sink de logging real.
- Loguear el payload del request (`{@Request}`) — excluido por seguridad.
- Alertas, métricas externas (Prometheus/Grafana), circuit breakers o cancelación automática de requests lentos.
- Umbrales distintos por tipo de request.
- Modificaciones a `TransactionBehavior.cs`, `ITransactionalCommand.cs` o `specs/01-transaction-behavior-mediatr.md`.

Cada uno de estos, si se necesita, va en su propio spec.
