# SPEC 03 — UnhandledExceptionBehavior y LoggingBehavior para observabilidad de excepciones y ciclo de vida

> **Status:** Implementado
> **Depends on:** SPEC 01, SPEC 02 (referencia informativa únicamente — este spec no modifica ninguno de los dos)
> **Date:** 2026-07-13
> **Objective:** Implementar UnhandledExceptionBehavior y LoggingBehavior, dos pipeline behaviors independientes de MediatR que registren excepciones genuinamente inesperadas y el ciclo de vida (inicio y finalización exitosa) de cada Command/Query, sin loguear el payload del request.

---

## Scope

**In:**

- Clase `UnhandledExceptionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>` en `src/2.Application/Common/UnhandledExceptionBehavior.cs`.
- Clase `LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>` en `src/2.Application/Common/LoggingBehavior.cs`.
- Ambos aplican al 100% de los `IRequest` (Commands y Queries), sin interfaz marcadora, igual que `PerformanceBehavior` (SPEC 02).
- `UnhandledExceptionBehavior`: envuelve `next()` en `try/catch`; si la excepción capturada **no** es `ValidationException`, `NotFoundException` ni `DomainException`, emite `logger.LogError(exception, "JOIN Unhandled Exception: {Name} {@UserId}", requestName, userId)` y relanza la excepción original sin envolverla. Si la excepción **sí** es una de esas 3, no loguea nada y relanza igual (deja que `GlobalExceptionHandler` la maneje como ya lo hace hoy).
- `LoggingBehavior`: antes de `next()`, emite `logger.LogInformation("JOIN Request Started: {Name} {@UserId}", requestName, userId)`; si `next()` retorna sin lanzar excepción, emite `logger.LogInformation("JOIN Request Finished: {Name} {@UserId}", requestName, userId)`. No usa `try/catch` — si `next()` lanza, el log de "Finished" simplemente no se emite y la excepción propaga sin intervención de este behavior.
- Ambos inyectan `ILogger<TRequest>` (categoría por tipo concreto de request, igual que `PerformanceBehavior`) e `ICurrentUserService` para obtener `{@UserId}`.
- Registro de ambos en `ConfigureServices.cs`, en el orden: `UnhandledExceptionBehavior → PerformanceBehavior → ValidationBehavior → LoggingBehavior → TransactionBehavior → Handler` (justificado en Decisiones).

**Out of scope (para specs futuros):**

- Modificar `specs/01-transaction-behavior-mediatr.md`, `specs/02-performance-behavior-mediatr.md`, `TransactionBehavior.cs` o `PerformanceBehavior.cs` — ambos specs anteriores permanecen intactos; este spec solo agrega registros nuevos en `ConfigureServices.cs`.
- Modificar `GlobalExceptionHandler.cs` (middleware HTTP) — sigue siendo el único responsable de convertir excepciones a `ProblemDetails`; `UnhandledExceptionBehavior` solo agrega logging estructurado antes de que la excepción llegue ahí.
- Loguear el payload del request (`{@Request}`) o cualquier propiedad sensible — misma exclusión de seguridad que SPEC 01 y SPEC 02.
- Chequear `Response<T>.IsSuccess` en `LoggingBehavior` — limitación aceptada explícitamente, documentada en Decisiones y Riesgos.
- Integrar Serilog/Application Insights/ELK — igual que SPEC 02, se deja el formato de mensaje listo (`{@UserId}`) pero no se integra ningún sink real.
- Cualquier acción además de loguear (alertas, métricas externas, reintentos automáticos).

---

## Data model

Este spec no introduce entidades, DTOs ni clases de configuración nuevas — reutiliza tipos ya existentes en el proyecto:

- `JOIN.Application.Exceptions.ValidationException` (lanzada por `ValidationBehavior`, SPEC previo a este).
- `JOIN.Application.Exceptions.NotFoundException` y `JOIN.Domain.Exceptions.DomainException` (ya usadas y manejadas hoy en `GlobalExceptionHandler.cs`).

`UnhandledExceptionBehavior` y `LoggingBehavior` son las únicas dos clases nuevas de este spec, ambas comportamiento de pipeline, sin estado persistido.

Conventions:

- El chequeo de "excepción esperada" en `UnhandledExceptionBehavior` es un `is` inline contra los 3 tipos listados arriba — no se crea ninguna lista/registro configurable de tipos excluidos; si se necesita hacerlo extensible en el futuro, es un spec aparte.

---

## Implementation plan

1. Crear `UnhandledExceptionBehavior<TRequest, TResponse>` en `src/2.Application/Common/UnhandledExceptionBehavior.cs`: inyecta `ILogger<TRequest> logger` e `ICurrentUserService currentUserService`. En `Handle`, envuelve `await next()` en `try/catch (Exception ex)`; si `ex` no es `ValidationException`, `NotFoundException` ni `DomainException`, emite `logger.LogError(ex, "JOIN Unhandled Exception: {Name} {@UserId}", requestName, userId)`; en cualquier caso, siempre relanza la excepción original sin envolverla (`throw;`). Build de `2.Application` sin errores.
2. Crear `LoggingBehavior<TRequest, TResponse>` en `src/2.Application/Common/LoggingBehavior.cs`: inyecta `ILogger<TRequest> logger` e `ICurrentUserService currentUserService`. En `Handle`, emite `LogInformation` de "Started" antes de `await next()`, y `LogInformation` de "Finished" inmediatamente después de que `next()` retorna (sin `try/catch`). Build de `2.Application` sin errores.
3. Registrar ambos behaviors en `ConfigureServices.cs` respetando el orden completo: `UnhandledExceptionBehavior` como el primero de la cadena (antes de `PerformanceBehavior`), y `LoggingBehavior` entre `ValidationBehavior` y `TransactionBehavior`. Build completo de la solución (`2.Application`, `4.Services.WebApi`) sin errores.
4. Verificación funcional manual (camino feliz): ejecutar cualquier Command/Query existente con datos válidos (ej. `GET /api/persons`) y confirmar en consola los logs `"JOIN Request Started: ..."` y `"JOIN Request Finished: ..."`, en ese orden, sin log de error.
5. Verificación funcional manual (validación esperada): enviar un payload inválido a un endpoint con `CommandValidator` existente (ej. `POST /api/persons` sin campos requeridos) y confirmar que la respuesta HTTP sigue siendo el 400 `ProblemDetails` de siempre, y que **no** aparece ningún log `"JOIN Unhandled Exception"` en consola (la `ValidationException` queda excluida como se definió).
6. Verificación funcional manual (excepción real): provocar temporalmente una excepción no controlada en un handler existente (ej. un `throw new InvalidOperationException("test")` de prueba), confirmar que aparece `"JOIN Unhandled Exception: {Name} {UserId}"` en consola con el stack trace completo, y revertir el cambio de prueba al terminar.

---

## Acceptance criteria

- [ ] `UnhandledExceptionBehavior<TRequest, TResponse>` existe en `src/2.Application/Common/UnhandledExceptionBehavior.cs` e implementa `IPipelineBehavior<TRequest, TResponse>`.
- [ ] `LoggingBehavior<TRequest, TResponse>` existe en `src/2.Application/Common/LoggingBehavior.cs` e implementa `IPipelineBehavior<TRequest, TResponse>`.
- [ ] Ambos aplican al 100% de los `IRequest`, sin interfaz marcadora.
- [ ] `UnhandledExceptionBehavior` relanza siempre la excepción original (`throw;`), nunca la envuelve ni la silencia, para cualquier tipo de excepción.
- [ ] `UnhandledExceptionBehavior` emite `LogError` solo cuando la excepción capturada **no** es `ValidationException`, `NotFoundException` ni `DomainException`.
- [ ] El mensaje de error usa exactamente: `logger.LogError(exception, "JOIN Unhandled Exception: {Name} {@UserId}", requestName, userId)`.
- [ ] `LoggingBehavior` emite `LogInformation` de inicio antes de `next()` y `LogInformation` de finalización solo si `next()` retorna sin lanzar excepción.
- [ ] `LoggingBehavior` no usa `try/catch`; si `next()` lanza, no emite el log de finalización y no interfiere con la propagación de la excepción.
- [ ] Ninguno de los dos logs (`Started`, `Finished`, `Unhandled Exception`) incluye el objeto `request` ni ninguna de sus propiedades.
- [ ] `{@UserId}` en ambos behaviors proviene de `ICurrentUserService.UserId`.
- [ ] `ILogger<TRequest>` se inyecta parametrizado por el tipo concreto de request en ambos behaviors (misma convención que `PerformanceBehavior`, SPEC 02).
- [ ] El orden de registro en `ConfigureServices.cs` es exactamente: `UnhandledExceptionBehavior → PerformanceBehavior → ValidationBehavior → LoggingBehavior → TransactionBehavior`.
- [ ] Un request con datos inválidos sigue devolviendo el 400 `ProblemDetails` estándar de `GlobalExceptionHandler`, sin ningún log `"JOIN Unhandled Exception"` adicional.
- [ ] `specs/01-transaction-behavior-mediatr.md`, `specs/02-performance-behavior-mediatr.md`, `TransactionBehavior.cs` y `PerformanceBehavior.cs` permanecen sin modificaciones.
- [ ] La solución compila con 0 errores en `2.Application` y `4.Services.WebApi`.

---

## Decisiones

- **Sí:** `UnhandledExceptionBehavior` excluye `ValidationException`, `NotFoundException` y `DomainException` del log de error — son exactamente los 3 tipos que `GlobalExceptionHandler` ya trata como fallos de negocio esperados con su propio status HTTP. Loguearlos como Error saturaría el sistema de falsos positivos (cada formulario mal llenado generaría "ruido operativo" de error); el nombre del componente es "Unhandled" — solo lo genuinamente inesperado (fallos de infraestructura, errores de BD no capturados, bugs) debe escalar como Error crítico en este punto del pipeline.

- **Sí:** pasar la excepción como primer parámetro posicional a `logger.LogError(exception, ...)`, siguiendo la convención estándar de Microsoft.Extensions.Logging. Garantiza que el stack trace y la traza completa se capturen e indexen correctamente por cualquier provider/sink, presente o futuro.

- **Sí:** mantener `ILogger<TRequest>` (categoría por tipo concreto de request) en ambos behaviors nuevos, en vez de una categoría fija por clase de behavior. Garantiza consistencia total con `PerformanceBehavior` (SPEC 02) — la categoría de logging queda siempre alineada al tipo de request que fluye por el pipeline, sin importar qué behavior está logueando.

- **Sí:** aceptar que `LoggingBehavior` no distingue `Response<T>.IsSuccess = false` de un éxito real — un Command que devuelve `Response<T>.Error(...)` sin lanzar excepción se loguea igual como "Finished" exitoso a nivel de MediatR. Introducir reflection para leer `.IsSuccess` en un `TResponse` genérico sería contradictorio con el objetivo explícito de que `LoggingBehavior` sea "un componente ligero" — la validación fina de estados de negocio internos del `Response` corresponde a capas superiores, no al pipeline transversal de logging. Documentado también como riesgo conocido.

- **Sí:** orden de registro `UnhandledExceptionBehavior → PerformanceBehavior → ValidationBehavior → LoggingBehavior → TransactionBehavior → Handler`.
  **Motivo:** `UnhandledExceptionBehavior` va primero (el más externo) porque debe capturar cualquier excepción de todo lo que ocurre por debajo, sin excepción (incluida una `ValidationException` disparada dentro de `ValidationBehavior`, aunque no se loguee como error). `PerformanceBehavior` sigue siendo el segundo, tal como se fijó en SPEC 02 — mide el costo total real del pipeline. `LoggingBehavior` va después de `ValidationBehavior` y antes de `TransactionBehavior`: el log de "inicio" debe representar que el negocio realmente va a procesarse (ya pasó validación), no ruido de requests que ni siquiera llegaron al handler; y el log de "fin" debe incluir el commit de la transacción, ya que `LoggingBehavior` envuelve a `TransactionBehavior`.

- **No:** modificar `GlobalExceptionHandler.cs`. Sigue siendo el único responsable de convertir excepciones a `ProblemDetails` HTTP; `UnhandledExceptionBehavior` solo añade una capa de logging estructurado *antes* de que la excepción llegue ahí, sin cambiar su comportamiento HTTP.

- **No:** modificar `specs/01-transaction-behavior-mediatr.md`, `specs/02-performance-behavior-mediatr.md`, `TransactionBehavior.cs` ni `PerformanceBehavior.cs`. Son specs independientes; este solo agrega dos registros nuevos en `ConfigureServices.cs`.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| `LoggingBehavior` registra "Finished" como éxito incluso cuando el Command devuelve `Response<T>.Error(...)` (fallo de negocio sin excepción) — puede dar una falsa sensación de que todo funcionó bien al revisar los logs, ocultando fallos de negocio silenciosos. | Limitación aceptada explícitamente (ver Decisiones). Si en el futuro se necesita visibilidad real de fallos de negocio en logs, requiere un spec propio que introduzca una interfaz `IResponse` no genérica con `IsSuccess`. |
| `LoggingBehavior` emite 2 logs `Information` (Started + Finished) por cada request exitoso, para el 100% del tráfico — a diferencia de `PerformanceBehavior`, que solo loguea cuando se supera el umbral. Bajo alta carga en producción, esto puede generar un volumen considerable de logs. | El nivel mínimo de logging es configurable por ambiente vía `appsettings.Production.json` (`Logging:LogLevel`); se puede subir a `Warning` en producción para silenciar `Information` sin tocar código. |
| La lista de excepciones excluidas en `UnhandledExceptionBehavior` (`ValidationException`, `NotFoundException`, `DomainException`) está hardcodeada de forma independiente al switch de `GlobalExceptionHandler.cs`. Si en el futuro se agrega un nuevo tipo de excepción "esperada" al `GlobalExceptionHandler` (como ya pasó con `DbUpdateException` para duplicate keys), `UnhandledExceptionBehavior` no lo excluirá automáticamente y lo logueará como Error pese a que HTTP lo trate como esperado. | No hay sincronización automática en este spec; queda documentado como dependencia implícita a mantener manualmente — cualquier cambio futuro al switch de `GlobalExceptionHandler` debe revisar si también aplica a esta lista. |

---

## Lo que **no** está en este spec

- Modificaciones a `specs/01-transaction-behavior-mediatr.md`, `specs/02-performance-behavior-mediatr.md`, `TransactionBehavior.cs` o `PerformanceBehavior.cs`.
- Modificaciones a `GlobalExceptionHandler.cs`.
- Chequeo de `Response<T>.IsSuccess` en `LoggingBehavior`.
- Integración de Serilog, Application Insights o cualquier sink de logging real.
- Sincronización automática entre la lista de excepciones excluidas de `UnhandledExceptionBehavior` y el switch de `GlobalExceptionHandler`.
- Alertas, métricas externas o cualquier acción además de loguear.

Cada uno de estos, si se necesita, va en su propio spec.
