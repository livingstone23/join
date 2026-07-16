# SPEC 01 — TransactionBehavior para Commands en MediatR

> **Status:** Implementado
> **Depends on:** Ninguna (primer spec del repositorio)
> **Date:** 2026-07-13
> **Objective:** Envolver automáticamente en una transacción explícita de EF Core (Begin/Commit/Rollback) a todo Command 100% de base de datos, vía un `IPipelineBehavior` de MediatR que se activa con una interfaz marcadora opt-in, migrando de forma total los Commands existentes que califiquen y dejando intactas las Queries y los Commands con I/O externo.

---

## Sección 1 — Por qué existe este spec

Hoy la atomicidad de cada Command depende de que su handler llame `SaveChangesAsync()`/`SaveAsync()` correctamente (verificado: 96 de 102 handlers lo hacen manualmente). No existe ningún mecanismo que garantice rollback si un Command muta múltiples agregados vía distintos repositorios y falla a mitad de camino. `TransactionBehavior` centraliza el límite transaccional en el pipeline de MediatR sin tocar la lógica interna de los handlers, protegiendo además el pool de conexiones de SQL Server al garantizar que las transacciones abiertas sean cortas y nunca abarquen I/O externo (email, HTTP, colas).

---

## Scope

**In:**

- Interfaz marcadora `ITransactionalCommand<TResponse> : IRequest<TResponse>` en `src/2.Application/Common/ITransactionalCommand.cs`.
- Clase `TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>` en `src/2.Application/Common/TransactionBehavior.cs`.
- Extensión de `IUnitOfWork` (`src/2.Application/Interface/Persistence/IUnitOfWork.cs`) con `BeginTransactionAsync`, `CommitAsync`, `RollbackAsync`.
- Implementación de esos 3 métodos en `UnitOfWork` (`src/3.Persistence/Repositories/UnitOfWork.cs`) usando `_dbContext.Database.BeginTransactionAsync()` de EF Core.
- Registro de `TransactionBehavior` en `ConfigureServices.cs` (`src/2.Application/Common/ConfigureServices.cs`), inmediatamente **después** de `ValidationBehavior` en la cadena de `AddMediatR`.
- Migración de la declaración de clase (solo la firma `: IRequest<TResponse>` → `: ITransactionalCommand<TResponse>`, sin tocar el cuerpo) de **100 de los 102 Commands existentes**:
  - **94 Commands que usan `IUnitOfWork`/repositorios exclusivamente** (todo `UseCases/Admin/**/Commands/**`, `UseCases/Common/**/Commands/**`, `UseCases/Messaging/**/Commands/**` y `UseCases/Security/RoleSystemOptions|SystemOptions|UserCompanies/Commands/**`, excepto las 2 excepciones listadas abajo).
  - **2 Commands mixtos `UserManager` + `IUnitOfWork`, sin I/O externo**: `LoginCommand` (`src/2.Application/UseCases/Security/Auth/Login/LoginCommand.cs`), `RefreshTokenCommand` (`src/2.Application/UseCases/Security/Auth/Refresh/RefreshTokenCommand.cs`).
  - **4 Commands solo `UserManager` (Identity, mismo `ApplicationDbContext`), sin I/O externo**: `ChangeMyPasswordCommand` (`.../Account/Commands/ChangeMyPassword/ChangeMyPasswordCommand.cs`), `UpdateMyProfileCommand` (`.../Account/Commands/UpdateMyProfile/UpdateMyProfileCommand.cs`), `RegisterCommand` (`.../Auth/Register/RegisterCommand.cs`), `ReplaceUserRolesCommand` (`.../Users/Commands/ReplaceUserRoles/ReplaceUserRolesCommand.cs`).
- Los handlers migrados **no se modifican por dentro**: siguen llamando `SaveChangesAsync()`/`SaveAsync()` ellos mismos, exactamente como hoy.

**Out of scope (para specs futuros):**

- `LoggingBehavior`, `PerformanceBehavior`, `UnhandledExceptionBehavior` (discutidos en conversación previa, no en este spec).
- `RequestEmailChangeCommand` (`.../Account/Commands/RequestEmailChange/RequestEmailChangeCommand.cs`) — usa `IEmailService`, queda como `IRequest<TResponse>` sin cambios.
- `CleanCacheCommand`/`InvalidateSidebarCache` (`.../Users/Commands/InvalidateSidebarCache/InvalidateSidebarCacheCommand.cs`) — solo usa `IMemoryCache`, no toca base de datos, queda como `IRequest<TResponse>` sin cambios.
- `ForgotPasswordCommand` y `SetupPasswordCommand` — tienen el Command definido pero **no tienen handler implementado todavía**; no hay nada que migrar. Cuando se implementen deberán evaluarse con esta misma regla.
- Nivel de aislamiento de transacción personalizado (se usa el default de EF Core).
- Transacciones anidadas o distribuidas entre múltiples `DbContext`.
- Lógica de reintento (retry/resiliencia) ante fallos de transacción.
- Cambiar el registro de `TransactionBehavior` a un constraint genérico en DI (`where TRequest : ITransactionalCommand<TResponse>`) en vez del chequeo runtime con bypass — evaluado y descartado, ver Decisiones.

---

## Data model

Este spec no introduce entidades ni DTOs persistidos. Introduce dos contratos de código:

```csharp
// src/2.Application/Common/ITransactionalCommand.cs
public interface ITransactionalCommand<TResponse> : IRequest<TResponse>
{
}
```

```csharp
// src/2.Application/Interface/Persistence/IUnitOfWork.cs (adición)
Task BeginTransactionAsync(CancellationToken cancellationToken = default);
Task CommitAsync(CancellationToken cancellationToken = default);
Task RollbackAsync(CancellationToken cancellationToken = default);
```

Convenciones:

- `TransactionBehavior` es el único componente que llama `BeginTransactionAsync`/`CommitAsync`/`RollbackAsync`. Ningún handler los invoca directamente.
- `TransactionBehavior` nunca llama `SaveChangesAsync`/`SaveAsync` — esa responsabilidad se queda en el handler, sin cambios respecto al comportamiento actual.

---

## Implementation plan

1. Extender `IUnitOfWork` con las 3 firmas de transacción (`BeginTransactionAsync`, `CommitAsync`, `RollbackAsync`). Sistema sigue compilando y corriendo igual (nadie las implementa aún).
2. Implementar las 3 firmas en `UnitOfWork` usando `_dbContext.Database.BeginTransactionAsync(cancellationToken)` / `.CommitAsync(cancellationToken)` / `.RollbackAsync(cancellationToken)`, guardando la transacción activa en un campo privado `IDbContextTransaction?`. Verificación manual: build de `3.Persistence` sin errores.
3. Crear `ITransactionalCommand<TResponse>` en `src/2.Application/Common/ITransactionalCommand.cs`. Build de `2.Application` sin errores.
4. Crear `TransactionBehavior<TRequest, TResponse>` en `src/2.Application/Common/TransactionBehavior.cs`: si `request is not ITransactionalCommand<TResponse>` → `return await next()`; si sí lo es → `BeginTransactionAsync` → `next()` → `CommitAsync` → return; en `catch` → `RollbackAsync` → `throw`.
5. Registrar `TransactionBehavior` en `ConfigureServices.cs`, después de `ValidationBehavior`, dentro del mismo bloque `AddMediatR`. Verificación manual: la app arranca y un endpoint existente (ej. `POST /api/persons`) sigue respondiendo 200 igual que antes (todavía ningún Command implementa la interfaz marcadora, así que todo sigue en bypass).
6. Migrar los 94 Commands de la categoría "solo `IUnitOfWork`" cambiando su firma a `ITransactionalCommand<TResponse>`. Build completo de `2.Application` sin errores después de este paso.
7. Migrar los 2 Commands mixtos (`LoginCommand`, `RefreshTokenCommand`).
8. Migrar los 4 Commands solo-Identity (`ChangeMyPasswordCommand`, `UpdateMyProfileCommand`, `RegisterCommand`, `ReplaceUserRolesCommand`).
9. Build completo de la solución (`2.Application`, `3.Persistence`, `4.Services.WebApi`) y corrida del suite de tests unitarios, confirmando que no se agregan fallos nuevos respecto a la línea base actual (existen fallos preexistentes no relacionados en tests de `Person*`, ajenos a este cambio).

---

## Acceptance criteria

- [ ] `IUnitOfWork` expone `BeginTransactionAsync`, `CommitAsync` y `RollbackAsync`.
- [ ] `UnitOfWork` implementa los 3 métodos usando `_dbContext.Database.BeginTransactionAsync()` de EF Core.
- [ ] `ITransactionalCommand<TResponse>` existe en `src/2.Application/Common/` y hereda de `IRequest<TResponse>`.
- [ ] `TransactionBehavior<TRequest, TResponse>` existe en `src/2.Application/Common/`.
- [ ] Para un request que **no** implementa `ITransactionalCommand<TResponse>`, `TransactionBehavior` llama a `next()` directo, sin invocar ningún método de `IUnitOfWork`.
- [ ] Para un request que **sí** implementa `ITransactionalCommand<TResponse>`, `TransactionBehavior` llama `BeginTransactionAsync` antes de `next()` y `CommitAsync` después de un `next()` exitoso.
- [ ] Si `next()` lanza una excepción, `TransactionBehavior` llama `RollbackAsync` y vuelve a lanzar la misma excepción (no la envuelve ni la silencia).
- [ ] `TransactionBehavior` no llama `SaveChangesAsync` ni `SaveAsync` en ningún punto.
- [ ] `TransactionBehavior` está registrado después de `ValidationBehavior` en `ConfigureServices.cs`.
- [ ] Los 94 Commands de la categoría "solo `IUnitOfWork`" implementan `ITransactionalCommand<TResponse>`.
- [ ] `LoginCommand` y `RefreshTokenCommand` implementan `ITransactionalCommand<TResponse>`.
- [ ] `ChangeMyPasswordCommand`, `UpdateMyProfileCommand`, `RegisterCommand` y `ReplaceUserRolesCommand` implementan `ITransactionalCommand<TResponse>`.
- [ ] `RequestEmailChangeCommand` y `InvalidateSidebarCache`/`CleanCacheCommand` siguen implementando `IRequest<TResponse>` sin cambios.
- [ ] Ningún Query Handler ni Query implementa `ITransactionalCommand<TResponse>`.
- [ ] La solución compila con 0 errores en `2.Application`, `3.Persistence` y `4.Services.WebApi`.
- [ ] El suite de tests unitarios no introduce fallos nuevos respecto a la línea base actual.

---

## Decisiones

- **Sí:** extender `IUnitOfWork` existente con los métodos de transacción, en vez de crear una interfaz `ITransactionManager` separada. Es donde ya vive el `DbContext` y el `SaveChangesAsync`; decisión explícita del usuario.
- **No:** que `TransactionBehavior` llame `SaveChangesAsync`. El handler se queda con esa responsabilidad, exactamente como hoy, para no tener que refactorizar el cuerpo de 100 handlers y para que el handler conserve control sobre IDs autogenerados a mitad de flujo si los necesita.
- **Sí:** chequeo runtime `request is ITransactionalCommand<TResponse>` con bypass explícito a `next()`, en vez de un constraint genérico en el registro de DI (`where TRequest : ITransactionalCommand<TResponse>`, que haría que MediatR/DI simplemente no construya el behavior para requests no calificados). Decisión explícita del usuario en el requerimiento original; la alternativa de constraint genérico habría sido "zero overhead" para las 75 Queries pero se descarta para mantener el comportamiento tal como fue especificado.
- **Sí:** migración total de los 100 Commands calificados en un solo spec, sin piloto. Decisión explícita del usuario, priorizando que la regla arquitectónica quede "integral y definitiva" sobre un rollout incremental.
- **Sí:** incluir los 4 Commands solo-`UserManager` (Categoría C) en la migración, pese a no usar `IUnitOfWork`/repositorios literalmente. Se verificó que Identity comparte el mismo `ApplicationDbContext` (`AddEntityFrameworkStores<ApplicationDbContext>()`), por lo que participan de la misma transacción sin problema; `ReplaceUserRolesCommand` en particular ejecuta `AddToRolesAsync` + `RemoveFromRolesAsync` en la misma operación, exactamente el escenario de inconsistencia que este behavior busca prevenir. Recomendación del asistente, aceptada por el usuario al no objetar tras la propuesta explícita.
- **No:** migrar `RequestEmailChangeCommand`. Depende de `IEmailService` (I/O externo prolongado) — regla de exclusión explícita del usuario.
- **No:** migrar `CleanCacheCommand`. No toca base de datos (`IMemoryCache` únicamente) — no hay nada que transaccionar.
- **No:** migrar `ForgotPasswordCommand`/`SetupPasswordCommand`. No tienen handler implementado; no hay código que cambiar en este spec.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Transacciones anidadas: un Command transaccional que en el futuro invoque otro Command vía `IMediator.Send` haría un segundo `BeginTransactionAsync` sobre una transacción ya abierta en el mismo `DbContext`. | Verificado que hoy no ocurre (no hay `IMediator`/`ISender` inyectado en ningún handler de Commands). Queda fuera de alcance; si se introduce en el futuro, `TransactionBehavior` necesitará lógica de detección de transacción activa. |
| Los 6 Commands de Categoría B/C dependen de que Identity siga compartiendo el `ApplicationDbContext` (`AddEntityFrameworkStores<ApplicationDbContext>()`). Si eso cambiara, dejarían de participar en la transacción silenciosamente (sin error visible). | Ninguna automática en este spec; queda documentado aquí como dependencia implícita a vigilar si se toca la configuración de Identity. |
| El diff de este spec toca ~100 archivos (un cambio de una línea cada uno) más 4 archivos de infraestructura nueva. Alto volumen aunque cada cambio individual es mecánico y de bajo riesgo. | Revisión por muestreo dirigida (verificar los 6 casos especiales + una muestra de los 94 genéricos) en vez de revisión línea por línea de los 100. |

---

## Lo que **no** está en este spec

- `LoggingBehavior`, `PerformanceBehavior`, `UnhandledExceptionBehavior`.
- Migración de `RequestEmailChangeCommand`, `CleanCacheCommand`, `ForgotPasswordCommand`, `SetupPasswordCommand`.
- Nivel de aislamiento de transacción configurable.
- Soporte de transacciones anidadas/distribuidas.
- Reintentos automáticos ante fallo de transacción.

Cada uno de estos, si se necesita, va en su propio spec.
