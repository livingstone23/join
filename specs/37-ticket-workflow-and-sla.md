# SPEC 37 — Transiciones de status parametrizables y SLA calculado

> **Status:** Implementado
> **Depends on:** SPEC 34 (sin cambios), SPEC 35 (`FinishTicket`, `UpdateTicketCommandHandler`, `TicketDtoAssembler` — los tres reciben modificaciones puntuales), SPEC 36 (sin cambios), SPEC 47, antes 37 (no es dependencia: está `Pospuesto`; se menciona porque no requiere cambios — `RegisterInboundTicketCommandHandler` solo crea tickets, nunca cambia su status después, así que el guard de transiciones de esta spec no lo alcanza).
> **Date:** 2026-09-25
> **Objective:** Cerrar la última brecha del brief — "la gestión del ticket deberá ser parametrizable" — dándole uso real a dos campos que ya existen en el dominio pero que ningún handler consulta hoy: `TicketComplexity.ResolutionTimeUnits` (SLA) y `TicketCompanyDefault.MaxDayTicketInactivity` (inactividad), y agregando una tabla de transiciones de status opcional por empresa (`TicketStatusTransition`) que, cuando se configura, restringe a qué status puede pasar un ticket desde cada status de origen.


> **Ajuste por SPEC 43 (2026-10-01):** el menú padre de tickets pasa a llamarse `TicketManagement` (antes `ManejoTickets`), con `ModuleName = "Tickets"` y rutas `/tickets/<recurso-con-guion>` (antes `/ManejoTickets/...`). Si esta spec se implementa **después** de SPEC 43, sus `SystemOptionSeed` usan ese padre, ese módulo y esas rutas. Si se implementa **antes**, usan `ManejoTickets` y `/ManejoTickets/...` como el resto de opciones de tickets, y SPEC 43 las renombra junto con todas las demás (`SystemOptionSeed` no tiene `ModuleName` hasta SPEC 43). Los `ControllerName` no cambian.

---

## Por qué existe esta spec y por qué es la más acotada de las cinco

Es la última de la segmentación original y, desde el análisis que dio origen a SPEC 34, quedó marcada como la de alcance más abierto — el brief la resume en una sola frase ("la gestión del ticket deberá ser parametrizable") sin especificar mecánica. Al llegar acá, con SPEC 34-36 y 99 ya redactadas, el criterio de esta spec fue deliberadamente conservador: **cerrar exactamente lo que el dominio ya insinúa y nada más.**

Evidencia concreta de qué insinúa el dominio: `TicketComplexity.ResolutionTimeUnits` y `TicketCompanyDefault.MaxDayTicketInactivity` existen desde la migración `InitialReset` —es decir, desde antes de que este proceso de specs empezara— con comentarios XML que describen exactamente su propósito de negocio ("resolution time", "trigger an alert or escalation"), pero un `grep` de ambos campos sobre todo `src/` confirma que **ningún handler los lee nunca**: se pueden crear, editar y borrar vía sus respectivos CRUD, pero no participan en ninguna decisión. Son, literalmente, campos muertos. Esta spec les da el primer uso real.

También se confirmó, antes de diseñar nada, que **no existe ninguna infraestructura de jobs en segundo plano en el repo** (`grep` de `IHostedService`, `BackgroundService`, `Hangfire`, `Quartz` sobre `src/` no devuelve nada). Eso fija el límite duro de esta spec: todo lo que requiera un proceso periódico —enviar una alerta de SLA vencido, reasignar automáticamente un ticket inactivo— necesita una decisión de infraestructura (¿un `IHostedService` con polling? ¿un job externo tipo Hangfire?) que el brief no pide y que este repo nunca tuvo que tomar todavía. Meterla acá de apuro sería la clase de decisión de arquitectura que no debería colarse dentro de una spec de dominio. Por eso esta spec se acota a **calcular y exponer** SLA e inactividad como datos de solo lectura, y a **hacer cumplir** transiciones de status configurables — ambas cosas resolubles dentro del ciclo request/response que ya tiene todo el módulo, sin agregar un solo componente de infraestructura nuevo.

---

## Scope

**In:**

### A. Dominio — cálculo puro, sin I/O

- `src/1.Domain/Messaging/TicketSlaCalculator.cs` (nuevo): clase estática, sin dependencias, sin acceso a base de datos ni reloj propio (recibe `DateTime nowUtc` como parámetro para ser determinística en tests). Ver firma completa en Data model.

### B. Dominio — transiciones de status

- `src/1.Domain/Messaging/TicketStatusTransition.cs` (nuevo): `BaseTenantEntity` con `FromStatusId` (Guid) y `ToStatusId` (Guid), navegaciones a ambos `TicketStatus`.

### C. Application — `TicketStatusTransitionGuard`

- `src/2.Application/UseCases/Messaging/Tickets/TicketStatusTransitionGuard.cs` (nuevo): clase DI-injected (`IUnitOfWork`), sin MediatR — mismo patrón que `TicketUserCompanySuperAdminCoordinator` (SPEC 34) y `TicketDtoAssembler`/`TicketUserCompanyCapabilityResolver` (SPEC 35). Expone `Task<bool> IsAllowedAsync(Guid companyId, Guid fromStatusId, Guid toStatusId, CancellationToken ct)`.
- **Semántica opt-in por status de origen**: si una empresa no tiene ninguna fila `TicketStatusTransition` con `FromStatusId` igual al status actual del ticket, **todas** las transiciones desde ese status siguen permitidas (comportamiento de hoy, sin cambios). Recién cuando existe al menos una fila para ese `FromStatusId`, el conjunto de destinos permitidos se reduce a los explícitamente listados. Es la única forma de introducir esta restricción sin romper, el mismo día que se despliega esta spec, la capacidad de cualquier empresa existente de mover un ticket entre status — nadie tiene filas configuradas todavía, así que nadie ve ningún cambio de comportamiento hasta que decida configurarlas.

### D. Application — consumidores del guard (modificaciones puntuales a SPEC 35)

- `UpdateTicketCommandHandler` (`src/2.Application/UseCases/Messaging/Tickets/Commands/UpdateTicket/`): en el código actual, `_ticketMapper.ApplyUpdate(request, entity)` (línea 139) ya muta `entity.TicketStatusId` **antes** del bloque `if (statusChanged)` que solo decide si registrar el log — ese bloque llega tarde para interceptar nada. El chequeo se inserta en el punto correcto: inmediatamente después de calcular `statusChanged = previousStatusId != request.TicketStatusId` y **antes** de la llamada a `_ticketMapper.ApplyUpdate(...)`, usando `previousStatusId`/`request.TicketStatusId` (no valores ya mutados de `entity`). Dos validaciones nuevas, en este orden, solo cuando `statusChanged` es `true`:
  1. El `TicketStatus` destino tiene `IsFinal = true` → `400 USE_FINISH_TICKET_FOR_FINAL_STATUS`. **Este chequeo exige corregir antes un defecto preexistente**: la validación `INVALID_TICKET_STATUS` que el handler ya hace (`statusRepository.GetAsync(request.TicketStatusId)`) **no filtra por `CompanyId`**, así que hoy acepta un status de otra empresa. Mientras eso solo escribía una FK equivocada era un bug latente; al empezar a leer `IsFinal` de esa fila para decidir si el ticket puede cerrarse, un status ajeno abriría un camino de cierre fuera del workflow de la empresa. Al capturar el status en una variable (en vez de descartar el resultado), se agrega la condición `|| status.CompanyId != currentUserService.CompanyId` al corte `INVALID_TICKET_STATUS`. Mismo ajuste en `FinishTicketCommandHandler` al cargar su status destino. `UpdateTicket` deja de poder llevar un ticket a un status terminal; esa transición pasa exclusivamente por `FinishTicket` (que sí genera el log `Finalization` que el cálculo de SLA de la sección G necesita — ver Decisiones).
  2. `TicketStatusTransitionGuard.IsAllowedAsync(companyId, previousStatusId, request.TicketStatusId, ct)` → `false` → `400 TICKET_STATUS_TRANSITION_NOT_ALLOWED`.
  Cualquiera de las dos corta el handler antes de `ApplyUpdate`, así que `entity` nunca queda con una mutación de status rechazada en su change tracker.
- `FinishTicketCommandHandler` (`src/2.Application/UseCases/Messaging/Tickets/Commands/FinishTicket/`, SPEC 35): entre el chequeo `TICKET_ALREADY_FINISHED` y la mutación directa `ticket.TicketStatusId = request.TicketStatusId`, se agrega la llamada al guard con `(previousStatusId, request.TicketStatusId)`. Acá sí alcanza con insertarla justo antes de la asignación, porque a diferencia de `UpdateTicket` no hay un mapper que adelante la mutación.
- `ReassignTicketCommandHandler` **no** se toca — reasignar no cambia `TicketStatusId`, no hay transición que validar.
- `TicketsController` **no** necesita ningún cambio: `Update` y `Finish` ya devuelven `BadRequest(response)` como fallback para cualquier código de error que no sea `TICKET_NOT_FOUND` (404) — los dos códigos nuevos (`USE_FINISH_TICKET_FOR_FINAL_STATUS`, `TICKET_STATUS_TRANSITION_NOT_ALLOWED`) son 400 y ya caen en ese camino existente sin tocar el controller.

### E. Application — `TicketStatusTransition` CRUD

- DTOs en `src/2.Application.DTO/Messaging/TicketStatusTransitions/`: `TicketStatusTransitionDto { Id, FromStatusId, FromStatusName, ToStatusId, ToStatusName, CreatedAt }`, `CreateTicketStatusTransitionDto { FromStatusId, ToStatusId }`. **Sin** DTO de update — ver Decisiones.
- Commands en `src/2.Application/UseCases/Messaging/TicketStatusTransitions/Commands/` (carpeta propia al mismo nivel que `Tickets/`, `TicketStatuses/`, etc. — es un sub-recurso de configuración, no un comando de `Ticket`): `CreateTicketStatusTransitionCommand`/`Handler`/`Validator`, `DeleteTicketStatusTransitionCommand`/`Handler`.
- Query: `GetTicketStatusTransitionsQuery` (lista plana tenant-scoped, Dapper, filtro opcional `fromStatusId`) + `Handler` en `src/2.Application/UseCases/Messaging/TicketStatusTransitions/Queries/`.
- Todo con el patrón nativo del módulo: `GetRepository<T>` en commands, Dapper inline en la query, sin Mapperly (dos FKs no lo justifican, mismo criterio que `TicketUserCompany`).

### F. WebApi — `TicketStatusTransitionsController`

- `src/4.Services.WebApi/Controllers/Messaging/TicketStatusTransitionsController.cs` (nuevo): `[PermissionResource("TicketStatusTransitions")]`, `GET` (lista, filtro `?fromStatusId=`), `POST` (`201`), `DELETE {id}`.
- Mapeo de errores: `COMPANY_REQUIRED` → 401; `TICKET_STATUS_TRANSITION_NOT_FOUND` → 404; `INVALID_FROM_STATUS`/`INVALID_TO_STATUS`/`SAME_STATUS_TRANSITION` → 400; `TICKET_STATUS_TRANSITION_DUPLICATE` → 409.

### G. Application — SLA e inactividad expuestos en las lecturas de `Ticket`

- `TicketDto`/`TicketListItemDto` (`src/2.Application.DTO/Messaging/Tickets/`) ganan cuatro campos: `DateTime SlaDueAt`, `bool IsSlaBreached`, `DateTime LastActivityAt`, `bool IsInactive`.
- `GetTicketByIdQueryHandler`, `GetTicketsQueryHandler`, `GetSystemWideTicketsQueryHandler`: cada `SELECT` agrega `tc.ResolutionTimeUnits`, `tcu.Code` (vía un `INNER JOIN` **nuevo** a `TimeUnits` por `tc.TimeUnitId` — ver la advertencia en Data model), `ts.IsFinal`, un subquery correlacionado `MAX(TicketLogs.Created)` para `LastActivityAt`, otro para `FinishedAt` (solo `LogType = 5`), y un `LEFT JOIN` a `TicketCompanyDefaults` para `MaxDayTicketInactivity`. El cálculo final (`SlaDueAt`/`IsSlaBreached`/`IsInactive`) se hace en C# con `TicketSlaCalculator.Compute(...)` sobre los valores crudos que trae la fila — **no** en SQL, para no necesitar aritmética de fechas específica de proveedor (`CLAUDE.md`: "usar `CONCAT()` no funciones de fecha del vendor" — el mismo principio aplicado a sumas de horas).
- `TicketDtoAssembler` (SPEC 35): se extiende para calcular los mismos cuatro campos, cargando los `TicketLog` del ticket vía `GetRepository<TicketLog>().GetAllAsync()` filtrado (mismo patrón in-memory que ya usa el módulo) para derivar `LastActivityAt`/`FinishedAt`, el `TicketCompanyDefault` de la empresa para `MaxDayTicketInactivity`, y —crítico— la `TimeUnit` **de la complejidad** (`complexity.TimeUnitId`), no la del ticket que el assembler ya carga para `TimeUnitName`. Son dos lecturas distintas de `GetRepository<TimeUnit>()`; usar la del ticket reproduciría exactamente el error que la sección Data model describe, y encima solo por esta vía (las queries Dapper darían el valor correcto), produciendo la divergencia entre las cuatro fuentes que los riesgos advierten.

**Out of scope (para specs futuras):**

- **Cualquier acción automática sobre un ticket con SLA vencido o inactivo.** Esta spec calcula y expone `IsSlaBreached`/`IsInactive`; no reasigna, no escala, no cambia de status ni notifica a nadie. Eso requiere una decisión de infraestructura de jobs en segundo plano que el repo no tiene hoy — spec aparte, una vez que negocio defina qué "escalar" significa en la práctica (¿notificar? ¿reasignar al `IsSuperAdminTicket`? ¿ambas?).
- **Activar `TicketNotification`.** Sigue sin consumidor después de esta spec, igual que quedó tras SPEC 35 y SPEC 99.

> **Deuda conocida y aceptada — la única parte del brief que las specs 34-37 y 99 no cubren.**
> El brief pide textualmente, sobre `TicketCompanyDefault.MaxDayTicketInactivity`: *"Si se puede llevar un control de días que se notifique que no tiene actividad, **se debe crear rutina con TicketUserCompany para que el SuperAdmin reciba notificaciones** si el ticket no está teniendo seguimiento."* Esa rutina **no existe** al terminar SPEC 37, y `Support.TicketNotifications` —que el brief describe como tabla operativa para registrar por qué canales se notificó un ticket— **queda sin ningún consumidor** en las cinco specs.
> Lo que sí queda entregado es toda la materia prima: `IsInactive` calculado y confiable (esta spec), el roster con `IsSuperAdminTicket` para saber a quién avisarle (SPEC 34), `IEmailService` ya operativo, y la tabla `TicketNotification` ya migrada.
> Lo que falta es exclusivamente **el disparador periódico**, y eso es una decisión de arquitectura sin precedente en el repo (no hay `IHostedService`, `BackgroundService`, Hangfire ni Quartz en todo `src/`). Se difiere de forma consciente en vez de resolverla de apuro dentro de una spec de dominio. Candidatos, para cuando se retome: un `BackgroundService` con polling configurable (sin dependencias nuevas), Hangfire (dashboard y reintentos, a cambio de una librería y tablas de infraestructura), o un endpoint `POST /Tickets/notify-inactive` agendado desde afuera (cero infraestructura, control operativo manual).
> Mientras tanto, el consumo es *pull*: el frontend muestra `IsInactive`/`IsSlaBreached` en la grilla y el `IsSuperAdminTicket` revisa. Es una degradación real frente a lo que el brief pide —nadie recibe un aviso proactivo— y debe comunicarse como tal, no presentarse como "el módulo de tickets está completo".
- **Filtro `?slaBreached=true`/`?isInactive=true` en `GetTickets`.** Filtrar por un valor calculado en C# después de una consulta ya paginada en SQL rompería la paginación (habría que calcular para todas las filas antes de paginar, o replicar la aritmética de fechas en SQL con branching por proveedor). Ninguna de las dos vale la pena todavía para un campo que hoy es solo de visualización; se revisita si aparece una necesidad de reporting concreta.
- **`Update` de `TicketStatusTransition`.** Solo alta y baja — ver Decisiones.
- **Máquina de estados completa con un status inicial y transiciones obligatorias** (validar que todo status tenga al menos una transición de entrada y una de salida, excepto los `IsFinal`). Esta spec valida transiciones puntuales que ya se están intentando, no audita la integridad del grafo completo de una empresa.
- **SLA distinto por canal o por prioridad del cliente.** El cálculo depende únicamente de `TicketComplexity`/`TimeUnit`, como ya insinuaba el dominio preexistente.
- **Pausar el conteo de SLA mientras el ticket está en un status `IsPaused`.** `TicketStatus.IsPaused` ya existe como campo pero esta spec no lo consume — el SLA corre en tiempo de reloj corrido desde la creación hasta el cierre, sin descontar pausas. Es una mejora real pero agrega una segunda fuente de verdad (intervalos de pausa) que el brief no pidió.

---

## Data model

### `TicketSlaCalculator` (nuevo, `src/1.Domain/Messaging/TicketSlaCalculator.cs`)

```csharp
namespace JOIN.Domain.Messaging;

public static class TicketSlaCalculator
{
    public sealed record Result(DateTime SlaDueAt, bool IsSlaBreached, DateTime LastActivityAt, bool IsInactive);

    /// <summary>
    /// Pure calculation — no I/O, no ambient clock.
    /// <paramref name="resolutionTimeUnits"/> comes from <see cref="TicketComplexity.ResolutionTimeUnits"/>
    /// and <paramref name="complexityTimeUnitCode"/> from the <see cref="TimeUnit.Code"/> of the
    /// time unit that complexity points at (<see cref="TicketComplexity.TimeUnitId"/>) — never from
    /// the ticket's own <see cref="Ticket.TimeUnitId"/>. See the note below on why the two differ.
    /// </summary>
    public static Result Compute(
        DateTime createdUtc,
        int resolutionTimeUnits,
        int complexityTimeUnitCode,
        DateTime lastActivityUtc,
        DateTime? finishedAtUtc,
        bool isFinalStatus,
        int? maxDayTicketInactivity,
        DateTime nowUtc)
    {
        var slaDueAt = createdUtc.AddHours(resolutionTimeUnits * (double)complexityTimeUnitCode);

        // A finished ticket is judged against when it actually finished, not "now" —
        // otherwise every closed ticket would eventually read as breached forever.
        var breachReferenceInstant = isFinalStatus ? (finishedAtUtc ?? lastActivityUtc) : nowUtc;
        var isSlaBreached = breachReferenceInstant > slaDueAt;

        var isInactive = !isFinalStatus
            && maxDayTicketInactivity.HasValue
            && (nowUtc - lastActivityUtc).TotalDays > maxDayTicketInactivity.Value;

        return new Result(slaDueAt, isSlaBreached, lastActivityUtc, isInactive);
    }
}
```

#### La unidad de tiempo del SLA es la de la complejidad, no la del ticket

Hay **dos** `TimeUnit` en juego y confundirlas produce un SLA silenciosamente equivocado por un factor de 24:

| Campo | Qué significa | Rol en el SLA |
|---|---|---|
| `TicketComplexity.ResolutionTimeUnits` + `TicketComplexity.TimeUnitId` | "un ticket de complejidad Alta debe resolverse en 3 Días" — el compromiso de la empresa por nivel de complejidad | **Es el SLA.** Ambos valores salen de acá |
| `Ticket.EstimatedTime` + `Ticket.TimeUnitId` | "el agente estimó 8 Horas para este ticket puntual", y la unidad con la que se registra `ConsumedTime` en cada `TicketLog` | Registro de esfuerzo, **no** entra en el cálculo |

El SQL de `GetTicketByIdQueryHandler` ya tiene un alias `tu`, pero es `INNER JOIN Messaging.TimeUnits tu ON t.TimeUnitId = tu.Id` — la unidad **del ticket**. Usar ese `tu.Code` junto con `tc.ResolutionTimeUnits` mezcla las dos filas de la tabla: con complejidad "Alta = 3 × Día" sobre un ticket registrado en Horas, `SlaDueAt` daría `Created + 3` horas en vez de `Created + 72`. Todas las consultas de esta spec deben unir **explícitamente** la unidad de la complejidad con un alias propio:

```sql
INNER JOIN Messaging.TicketComplexities tc  ON t.TicketComplexityId = tc.Id   -- ya existe
INNER JOIN Messaging.TimeUnits          tcu ON tc.TimeUnitId       = tcu.Id   -- NUEVO: la del SLA
```

y pasar `tcu.Code` a `TicketSlaCalculator.Compute(...)`. El alias `tu` existente se deja como está: sigue alimentando `TimeUnitName` del DTO, que describe el registro de esfuerzo del ticket y no tiene nada que ver con el SLA.

`resolutionTimeUnits`/`complexityTimeUnitCode` siempre están disponibles porque `Ticket.TicketComplexityId` es FK obligatoria y `TicketComplexity.TimeUnitId` también — no hay caso de "ticket sin complejidad" ni de "complejidad sin unidad" que deje `SlaDueAt` sin poder calcularse.

### `TicketStatusTransition` (nuevo, schema `Messaging`)

```csharp
// src/1.Domain/Messaging/TicketStatusTransition.cs
public class TicketStatusTransition : BaseTenantEntity
{
    public Guid FromStatusId { get; set; }
    public Guid ToStatusId { get; set; }

    public virtual TicketStatus FromStatus { get; set; } = null!;
    public virtual TicketStatus ToStatus { get; set; } = null!;
}
```

EF config: tabla `Messaging.TicketStatusTransitions`, índice único filtrado `(CompanyId, FromStatusId, ToStatusId) WHERE GcRecord = 0`, `HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(Restrict)` explícito (mismo patrón que `TicketComplexityConfiguration`, no se deja a la convención de EF), dos FKs a `Messaging.TicketStatuses` (`FromStatusId`/`ToStatusId`) `Restrict` — **sin** `WithMany` compartido entre ambas para no generar una navegación ambigua en `TicketStatus` (dos `HasOne` con `.WithMany()` vacío, cada una con su propio `HasForeignKey`), query filter `GcRecord == 0`.

### `TicketStatusTransitionGuard`

```csharp
// src/2.Application/UseCases/Messaging/Tickets/TicketStatusTransitionGuard.cs
namespace JOIN.Application.UseCases.Messaging.Tickets;

public sealed class TicketStatusTransitionGuard(IUnitOfWork unitOfWork)
{
    public async Task<bool> IsAllowedAsync(
        Guid companyId,
        Guid fromStatusId,
        Guid toStatusId,
        CancellationToken cancellationToken)
    {
        if (fromStatusId == toStatusId)
        {
            return true; // not a real transition
        }

        var repository = unitOfWork.GetRepository<TicketStatusTransition>();
        var all = await repository.GetAllAsync();

        var rulesForFromStatus = all
            .Where(x => x.GcRecord == 0 && x.CompanyId == companyId && x.FromStatusId == fromStatusId)
            .ToList();

        // No rules configured for this source status: unrestricted, same as today.
        if (rulesForFromStatus.Count == 0)
        {
            return true;
        }

        return rulesForFromStatus.Any(x => x.ToStatusId == toStatusId);
    }
}
```

### SQL agregado en las queries de `Ticket` (fragmento representativo, `GetTicketsQueryHandler`)

```sql
SELECT
    -- ... columnas existentes ...
    ts.IsFinal            AS IsFinalStatus,
    tc.ResolutionTimeUnits,
    tcu.Code              AS ComplexityTimeUnitCode,
    tcd.MaxDayTicketInactivity,
    (SELECT MAX(tl.Created) FROM Support.TicketLogs tl
     WHERE tl.TicketId = t.Id AND tl.GcRecord = 0) AS LastActivityAt,
    (SELECT MAX(tl2.Created) FROM Support.TicketLogs tl2
     WHERE tl2.TicketId = t.Id AND tl2.LogType = 5 AND tl2.GcRecord = 0) AS FinishedAt
FROM Messaging.Tickets t
-- ... joins existentes (ts, tc, co, c, au) ...
INNER JOIN Messaging.TimeUnits tcu
    ON tc.TimeUnitId = tcu.Id
LEFT JOIN Messaging.TicketCompanyDefaults tcd
    ON tcd.CompanyId = t.CompanyId AND tcd.GcRecord = 0
WHERE -- ... sin cambios ...
```

Dos advertencias sobre este fragmento, que valen para los tres query handlers:

1. **`GetTicketsQueryHandler` no tiene hoy ningún join a `TimeUnits`.** Su `SELECT` solo une `co`, `ts`, `tc`, `c` y `au` (el listado no expone `TimeUnitName`). El `INNER JOIN ... tcu` de arriba es la única fuente del multiplicador ahí, así que agregarlo no es opcional. En `GetTicketByIdQueryHandler` sí existe además el alias `tu` (la unidad del ticket), que se conserva sin cambios para `TimeUnitName` — son dos joins distintos a la misma tabla y deben convivir.
2. **Las columnas crudas no se materializan en el DTO.** Hoy los handlers hacen `ReadFirstOrDefaultAsync<TicketDto>()` / `ReadAsync<TicketListItemDto>()` directo; `ResolutionTimeUnits`, `ComplexityTimeUnitCode`, `MaxDayTicketInactivity`, `LastActivityAt`, `FinishedAt` e `IsFinalStatus` no tienen propiedad destino y Dapper las descartaría en silencio. Cada handler necesita un record de proyección intermedio, privado al handler, y un paso de mapeo explícito:

```csharp
// privado dentro del handler
private sealed record TicketSlaRow
{
    public required TicketListItemDto Item { get; init; }   // o TicketDto en el de detalle
    public int ResolutionTimeUnits { get; init; }
    public int ComplexityTimeUnitCode { get; init; }
    public bool IsFinalStatus { get; init; }
    public int? MaxDayTicketInactivity { get; init; }
    public DateTime? LastActivityAt { get; init; }
    public DateTime? FinishedAt { get; init; }
}
```

En la práctica se resuelve con un DTO plano que hereda o compone al existente, o con `connection.Query<TicketListItemDto, SlaRawColumns, TicketListItemDto>(..., splitOn: "ResolutionTimeUnits")` — cualquiera de las dos sirve, pero **algo** tiene que recibir las columnas crudas. La forma exacta queda a criterio de la implementación; lo que no es negociable es que los cuatro campos publicados salgan de `TicketSlaCalculator.Compute(...)` y no de SQL.

`IsFinalStatus` viaja como columna (`ts.IsFinal`) en vez de resolverse aparte: el cálculo lo necesita para decidir contra qué instante medir el vencimiento, y el join a `TicketStatuses` ya existe en los tres handlers.

`LastActivityAt` nunca es `NULL` en la práctica (todo ticket tiene al menos su log `Creation`), pero el handler defiende con `?? ticket.CreatedAt` por si acaso. El mapeo de cada fila pasa los crudos por `TicketSlaCalculator.Compute(...)` con `nowUtc = DateTime.UtcNow` resuelto una sola vez al principio del `Handle(...)` (no por fila, para que todas las filas de una misma respuesta se evalúen contra el mismo instante).

---

## Implementation plan

### F1 — `TicketSlaCalculator`

1. Crear la clase estática con la firma de la sección Data model.
2. `dotnet build -c Release` → 0 errores. Sin efecto en runtime todavía (nada la llama).

### F2 — Dominio y persistencia de `TicketStatusTransition`

1. Crear `TicketStatusTransition.cs`.
2. Crear `TicketStatusTransitionConfiguration.cs` (`src/3.Persistence/Configuration/Messaging/`) con la sección Data model.
3. `DbSet<TicketStatusTransition>` en `ApplicationDbContext`.
4. `dotnet ef migrations add AddTicketStatusTransitions --project ../3.Persistence --startup-project .`. Verificar el índice único filtrado y las dos FKs `Restrict` sin ciclo (mismo cuidado que ya tomó `TicketConfiguration` con `PrecedentTicket`/`Project`/`Area` usando `NoAction` donde EF detectó ciclos — acá no debería hacer falta porque ambas FKs apuntan al mismo catálogo pero no hay ciclo de cascada).
5. `dotnet ef database update`.

### F3 — `TicketStatusTransition` CRUD

1. DTOs, `CreateTicketStatusTransitionCommand`/`Validator`/`Handler` (valida `FromStatusId != ToStatusId` → `SAME_STATUS_TRANSITION`; ambos existen y pertenecen a la empresa → `INVALID_FROM_STATUS`/`INVALID_TO_STATUS`; par no duplicado → `TICKET_STATUS_TRANSITION_DUPLICATE`).
2. `DeleteTicketStatusTransitionCommand`/`Handler` (soft delete estándar).
3. `GetTicketStatusTransitionsQuery`/`Handler` (Dapper, join a `TicketStatuses` dos veces para `FromStatusName`/`ToStatusName`).
4. `dotnet build` → 0 errores.

### F4 — `TicketStatusTransitionsController`

1. Crear el controller con los 3 endpoints y el mapeo de errores del Scope.
2. `dotnet build` → 0 errores.

### F5 — Enforcement en `UpdateTicket`/`FinishTicket`

1. Inyectar `TicketStatusTransitionGuard` en `UpdateTicketCommandHandler`. Reordenar: inmediatamente después de calcular `statusChanged` y **antes** de la llamada existente a `_ticketMapper.ApplyUpdate(request, entity)`, agregar (solo si `statusChanged`): el chequeo `IsFinal` del `TicketStatus` destino (reutilizar el resultado de la validación `INVALID_TICKET_STATUS` que el handler ya hace más arriba, capturándolo en una variable en vez de descartarlo, para no consultar el mismo `TicketStatus` dos veces) → `400 USE_FINISH_TICKET_FOR_FINAL_STATUS`; luego la llamada al guard → `400 TICKET_STATUS_TRANSITION_NOT_ALLOWED`.
2. Inyectar el mismo guard en `FinishTicketCommandHandler` (SPEC 35); agregar la llamada justo antes de la asignación directa `ticket.TicketStatusId = request.TicketStatusId` (acá no hace falta reordenar nada: no hay mapper que adelante la mutación).
3. Registrar `TicketStatusTransitionGuard` como `Scoped` en `ConfigureServices.cs`.
4. `dotnet build` → 0 errores. `dotnet test --filter "FullyQualifiedName~UpdateTicket|FullyQualifiedName~FinishTicket"` sigue en verde: sin filas de `TicketStatusTransition` el guard siempre devuelve `true`, y la única transición que `UpdateTicket` deja de poder hacer (llevar a un status final) es una restricción nueva de esta spec — ajustar los tests existentes que hoy prueben ese camino por `UpdateTicket` para que lo hagan por `FinishTicket` en su lugar.

### F6 — SLA/inactividad en las lecturas

1. Agregar los 4 campos a `TicketDto`/`TicketListItemDto`.
2. Modificar el SQL de `GetTicketByIdQueryHandler`, `GetTicketsQueryHandler`, `GetSystemWideTicketsQueryHandler` según la sección Data model: `INNER JOIN Messaging.TimeUnits tcu ON tc.TimeUnitId = tcu.Id` (nuevo en los tres), `LEFT JOIN Messaging.TicketCompanyDefaults`, los dos subqueries de `TicketLogs`, y las columnas `ts.IsFinal`/`tc.ResolutionTimeUnits`/`tcu.Code`. Agregar en cada handler la proyección intermedia que reciba las columnas crudas (hoy los DTOs se materializan directo y Dapper las descartaría) e invocar `TicketSlaCalculator.Compute(...)` por fila con un `nowUtc` resuelto una vez al inicio del `Handle`. Verificar que la segunda consulta de `GetTicketsQueryHandler` (`SELECT COUNT(*) ... {whereClause}`) sigue compilando: reutiliza el mismo `whereClause` sin los joins, así que **ninguna** de las condiciones nuevas puede terminar en el `WHERE`.
3. Extender `TicketDtoAssembler` (SPEC 35) para calcular los mismos 4 campos vía `GetRepository<TicketLog>()`/`GetRepository<TicketCompanyDefault>()`/`GetRepository<TimeUnit>()`, resolviendo el multiplicador con `complexity.TimeUnitId` — **no** con la `TimeUnit` del ticket que el assembler ya tiene cargada para `TimeUnitName`.
4. `dotnet build` → 0 errores.

### F7 — Seed

1. Sin datos de ejemplo obligatorios: `TicketStatusTransition` es opt-in por diseño, así que **no** sembrarla para las empresas de desarrollo mantiene el comportamiento sin restricciones, que es el caso más útil para probar el resto del módulo sin fricción.
2. Agregar el `SystemOptionSeed` de `TicketStatusTransitions` (CRUD completo, grupo `TicketManagement`, `ModuleName = "Tickets"`, ruta `/tickets/ticket-status-transitions`) a `GetAdministrativeSystemOptionSeeds()`, **y** las filas de `RoleSystemOptionSeed` en `GetRoleSystemOptionSeeds()` (mismo motivo detallado en SPEC 34 sección H: la `SystemOption` declara el recurso, no otorga permiso a ningún rol fuera de `Admin`/`SuperAdminCompany`):
   ```csharp
   new("Manager", "TicketStatusTransitions", true, true, false, true, CanDownload: false, CanExport: false, CanExecute: true),
   new("Supervisor", "TicketStatusTransitions", true, false, false, false),
   new("UsuarioSimple", "TicketStatusTransitions", true, false, false, false, CanDownload: false, CanExport: false, CanExecute: false),
   ```
   (`CanUpdate = false`: este recurso no tiene endpoint de update por diseño.)
3. `dotnet build` → 0 errores.

### F8 — Tests (~29 casos)

- `TicketSlaCalculatorTests` (9, puros, sin mocks): ticket no final, `nowUtc` antes del vencimiento → `IsSlaBreached = false`; después → `true`; ticket final terminado antes del vencimiento → `false` aunque `nowUtc` sea mucho después; ticket final terminado después del vencimiento → `true`; `maxDayTicketInactivity = null` → `IsInactive` siempre `false`; inactividad dentro del umbral → `false`; fuera del umbral → `true`; ticket final nunca es `IsInactive` aunque supere el umbral; `resolutionTimeUnits = 3` con `complexityTimeUnitCode = 24` da `SlaDueAt = createdUtc + 72h` (no `+3h`).
- `TicketStatusTransitionGuardTests` (5): sin filas para el `FromStatusId` → `true`; con filas pero el destino no está listado → `false`; con filas y el destino listado → `true`; `fromStatusId == toStatusId` → `true` sin consultar el repositorio; filas de otra empresa no cuentan.
- `TicketStatusTransition` CRUD (7): `CompanyId` vacío → 401; `FromStatusId == ToStatusId` → 400 `SAME_STATUS_TRANSITION`; status inexistente → 400; par duplicado → 409; `Delete` no encontrado → 404; happy paths de create/delete/list.
- `UpdateTicketCommandHandlerTests` (+3 casos nuevos): status destino con `IsFinal = true` → 400 `USE_FINISH_TICKET_FOR_FINAL_STATUS`, verificado con `_ticketMapperMock.Verify(m => m.ApplyUpdate(It.IsAny<UpdateTicketCommand>(), It.IsAny<Ticket>()), Times.Never)`; transición no permitida (destino no final) → 400 `TICKET_STATUS_TRANSITION_NOT_ALLOWED`, no persiste; transición permitida (o sin reglas configuradas) → 200 como antes.
- `FinishTicketCommandHandlerTests` (+1 caso nuevo): transición al status final no permitida → 400, no persiste, no genera el log `Finalization`.
- `GetTicketByIdQueryHandlerTests`/`GetTicketsQueryHandlerTests` (+5 casos): `SlaDueAt` calculado a partir de `tc.ResolutionTimeUnits` y del `Code` de la `TimeUnit` **de la complejidad**; `IsSlaBreached`/`IsInactive` reflejan los valores esperados para un ticket armado con fechas conocidas en el fixture; y el caso discriminante — ticket cuya `TimeUnit` propia tiene un `Code` distinto al de su complejidad devuelve el `SlaDueAt` de la complejidad (este es el test que atrapa la regresión si alguien reusa el alias `tu` por error).
- `TicketDtoAssemblerTests` (+2 casos): los 4 campos nuevos se calculan igual que en las queries de lectura; y el mismo caso discriminante de unidades distintas, porque el assembler es la vía más propensa a tomar la `TimeUnit` equivocada (ya tiene cargada la del ticket para `TimeUnitName`).
- `dotnet test --filter "FullyQualifiedName~TicketStatusTransition|FullyQualifiedName~TicketSla"` → 0 fallidos.

### F9 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → gate de 90% en `JOIN.Application` sigue en verde.
3. `dotnet ef database update` sobre base limpia → migración aplica limpio.
4. Smoke: sin ninguna fila de `TicketStatusTransition`, `UpdateTicket` cambia el status libremente, igual que hoy.
5. Smoke: crear una regla `Open → InProgress` únicamente (ninguno de los dos es `IsFinal`); intentar `Open → Closed` directo por `UpdateTicket` → 400 `TICKET_STATUS_TRANSITION_NOT_ALLOWED` (no llega a evaluar `IsFinal` porque `InProgress`/`Open` no lo son, pero el destino tampoco está en la lista permitida). Pasar primero `Open → InProgress` por `UpdateTicket` → 200. Cerrar con `FinishTicket` hacia `Closed` (`IsFinal = true`) → si hay una regla `TicketStatusTransition` para `InProgress` que no incluye `Closed`, `FinishTicket` también la respeta y devuelve 400; si no hay regla para `InProgress`, cierra en 200.
6. Smoke: `GET /Tickets/{id}` de un ticket creado hace más tiempo que su `ResolutionTimeUnits` convertido a horas, sin finalizar → `isSlaBreached: true`.
7. Smoke: el mismo ticket, finalizado antes del vencimiento (`FinishTicket` con fecha de log `Created` simulada temprano en un test de integración, o verificado lógicamente) → `isSlaBreached: false` permanente.
8. `CURL_REQUESTS.md` — agregar los 3 endpoints de `TicketStatusTransitions` y documentar los 4 campos nuevos en la respuesta de `Tickets`.

---

## Acceptance criteria

### F1 — `TicketSlaCalculator`

- [ ] Es una clase estática pura: sin inyección de dependencias, sin `DateTime.UtcNow` interno (siempre recibido por parámetro).
- [ ] Un ticket final se evalúa contra su instante de finalización, no contra "ahora".
- [ ] `maxDayTicketInactivity == null` nunca produce `IsInactive = true`.
- [ ] Un ticket en status final nunca es `IsInactive`, sin importar cuánto tiempo pasó.

### F2/F3/F4 — `TicketStatusTransition`

- [ ] Entidad, configuración EF, migración e índice único filtrado según la sección Data model.
- [ ] `Create` rechaza `FromStatusId == ToStatusId` con `400 SAME_STATUS_TRANSITION`.
- [ ] `Create` rechaza un `FromStatusId`/`ToStatusId` que no exista o no pertenezca a la empresa con `400`.
- [ ] `Create` de un par ya existente → `409 TICKET_STATUS_TRANSITION_DUPLICATE`.
- [ ] No existe ningún endpoint de `Update` para este recurso.
- [ ] Los 3 endpoints del controller existen con el mapeo de errores del Scope.

### F5 — Enforcement

- [ ] `TicketStatusTransitionGuard.IsAllowedAsync` devuelve `true` cuando no hay ninguna fila para el `FromStatusId` consultado (comportamiento sin cambios por defecto).
- [ ] Devuelve `true` cuando `fromStatusId == toStatusId`, sin tocar el repositorio.
- [ ] `UpdateTicketCommandHandler` rechaza con `400 USE_FINISH_TICKET_FOR_FINAL_STATUS` cualquier intento de llevar el ticket a un `TicketStatus` con `IsFinal = true`, sin llegar a llamar `_ticketMapper.ApplyUpdate(...)`.
- [ ] `UpdateTicketCommandHandler` y `FinishTicketCommandHandler` rechazan con `INVALID_TICKET_STATUS` un `TicketStatusId` que existe pero pertenece a **otra empresa** (defecto preexistente corregido en F5; sin esto, el `IsFinal` de un status ajeno decidiría el cierre).
- [ ] `UpdateTicketCommandHandler` rechaza con `400 TICKET_STATUS_TRANSITION_NOT_ALLOWED` una transición no permitida (a un status no final), **sin** persistir ningún cambio ni generar el log `StatusChange`.
- [ ] Ambos chequeos de `UpdateTicketCommandHandler` corren usando `previousStatusId`/`request.TicketStatusId`, antes de que `_ticketMapper.ApplyUpdate(...)` mute la entidad trackeada.
- [ ] `FinishTicketCommandHandler` aplica el chequeo de `TicketStatusTransitionGuard` antes de mutar el status, con el mismo código de error `TICKET_STATUS_TRANSITION_NOT_ALLOWED`.
- [ ] `ReassignTicketCommandHandler` no fue modificado por esta spec.

### F6 — SLA/inactividad

- [ ] `TicketDto` y `TicketListItemDto` exponen `SlaDueAt`, `IsSlaBreached`, `LastActivityAt`, `IsInactive`.
- [ ] `SlaDueAt` se calcula como `Created + (TicketComplexity.ResolutionTimeUnits × TimeUnit.Code de TicketComplexity.TimeUnitId)` horas.
- [ ] El multiplicador **nunca** sale de `Ticket.TimeUnitId`. Test explícito: un ticket cuya complejidad usa `Día` (`Code = 24`) y cuyo `Ticket.TimeUnitId` apunta a `Hora` (`Code = 1`) con `ResolutionTimeUnits = 3` produce `SlaDueAt = Created + 72h`, no `+3h`.
- [ ] `GetTicketsQueryHandler` y `GetSystemWideTicketsQueryHandler` ganan el `INNER JOIN` a `Messaging.TimeUnits` por `tc.TimeUnitId` (hoy no unen esa tabla en absoluto).
- [ ] `GetTicketByIdQueryHandler` conserva su join `tu` por `t.TimeUnitId` para `TimeUnitName`, **además** del join nuevo `tcu` por `tc.TimeUnitId` para el SLA.
- [ ] `Ticket.EstimatedTime` no participa del cálculo de SLA en ninguna de las cuatro vías.
- [ ] Los tres query handlers (`GetTicketById`, `GetTickets`, `GetSystemWideTickets`) y `TicketDtoAssembler` calculan los cuatro campos de forma idéntica (mismo resultado para el mismo ticket consultado por cualquiera de las cuatro vías), verificado con el mismo fixture de fechas y unidades.
- [ ] Ningún cálculo de fecha ocurre en SQL — toda la aritmética vive en `TicketSlaCalculator`, invocado desde C#.

### F7 — Seed

- [ ] `SystemOption` con `ControllerName = "TicketStatusTransitions"` existe en el seed idempotente.
- [ ] `GetRoleSystemOptionSeeds()` incluye sus tres filas de rol, y `manager@join.com` obtiene 200 —no 403— en `GET /api/v1/TicketStatusTransitions` tras el seed.
- [ ] **Cero** filas `TicketStatusTransition` sembradas para las empresas de desarrollo — el comportamiento sin restricciones es el estado inicial esperado.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test` → 0 fallidos, ≥ 29 tests nuevos.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate de CI).
- [ ] Ninguna empresa existente ve un cambio de comportamiento en `UpdateTicket`/`FinishTicket` el día del despliegue (cero filas de `TicketStatusTransition` sembradas).
- [ ] `CURL_REQUESTS.md` documenta los 3 endpoints nuevos y los 4 campos agregados a las respuestas de `Tickets`.

---

## Decisions taken and discarded

- **Transiciones opt-in por status de origen, no una máquina de estados obligatoria** (elegido) vs. exigir que toda empresa configure el grafo completo antes de poder cambiar cualquier status. La opción obligatoria habría sido un breaking change real el día del despliegue para cualquier empresa que ya esté usando tickets — cero empresas tienen hoy ninguna configuración de este tipo, así que exigirla de entrada dejaría a todo el mundo sin poder mover un ticket hasta configurar manualmente cada transición. Opt-in por status de origen permite adopción incremental sin romper nada.
- **Sin `Update` para `TicketStatusTransition`** (elegido) vs. permitir editar `ToStatusId` de una regla existente. Mismo criterio que `TicketUserCompany` (SPEC 34): el par `(FromStatusId, ToStatusId)` **es** la identidad de la regla, no un dato editable de ella — "cambiar a dónde apunta" es borrar una regla y crear otra, no una edición.
- **El multiplicador del SLA sale de la `TimeUnit` de la complejidad, no de la del ticket** (elegido; corrige un error de la primera redacción de esta spec, que tomaba el alias `tu` existente sin notar que apunta a `t.TimeUnitId`). Las dos alternativas reales eran: (a) SLA = `TicketComplexity.ResolutionTimeUnits × TimeUnit(TicketComplexity.TimeUnitId).Code`, o (b) SLA = `Ticket.EstimatedTime × TimeUnit(Ticket.TimeUnitId).Code`. Se eligió (a) porque el SLA es un compromiso **de la empresa por nivel de complejidad**, no una estimación que cada agente pueda ajustar al crear el ticket: con (b), quien crea el ticket decide su propio plazo de cumplimiento y la métrica deja de ser comparable entre tickets. `TicketComplexity.ResolutionTimeUnits` y su `TimeUnitId` existen en el dominio desde `InitialReset` precisamente como par, y el comentario XML de `ResolutionTimeUnits` lo dice ("la cantidad de unidades de tiempo configuradas requeridas para resolver un ticket de esta complejidad"). `Ticket.EstimatedTime`/`Ticket.TimeUnitId` quedan como registro de esfuerzo —lo que el agente estima y lo que `TicketLog.ConsumedTime` acumula— y no participan del cálculo. Descartada también una tercera opción ("usar el estimado si está informado, si no el de la complejidad") por introducir una rama de negocio que el brief no pide y que volvería el SLA no determinístico entre tickets de la misma complejidad.
- **Cálculo de SLA en C#, no en SQL** (elegido) vs. `DATEADD`/`+ interval` directamente en las queries Dapper. `CLAUDE.md` exige portabilidad cross-DB explícita para fechas ("usar `CONCAT()` no funciones de fecha del vendor" es el ejemplo dado para paginación; el mismo principio se extiende naturalmente a sumar horas). Traer los valores crudos (`ResolutionTimeUnits`, `TimeUnitCode`, `LastActivityAt`, `FinishedAt`) y calcular en `TicketSlaCalculator` evita escribir dos variantes de aritmética de fechas (SQL Server / Postgres) para lo mismo.
- **`TicketSlaCalculator` como clase estática de dominio, no un servicio DI-injected de Application** (elegido) vs. seguir el patrón de `TicketDtoAssembler`/`TicketUserCompanyCapabilityResolver`. La diferencia real: esos dos necesitan `IUnitOfWork` (hacen I/O); `TicketSlaCalculator` es una función pura sobre los datos que el caller ya tiene en mano. Inyectarlo por DI sin ninguna dependencia real sería ceremonia sin beneficio — una clase estática es más simple y se testea igual de bien.
- **Un ticket finalizado se evalúa contra su instante de finalización, no contra "ahora"** (elegido) vs. evaluar siempre contra `nowUtc`. Sin esto, todo ticket cerrado terminaría marcado `IsSlaBreached = true` con el correr del tiempo simplemente porque "ahora" sigue avanzando después del cierre — un dato que debería congelarse en el momento del cierre y no seguir cambiando después. Requiere el `FinishedAt` (log `Finalization`) como referencia estable.
- **`IsInactive` nunca es `true` para un ticket en status final** (elegido, mismo razonamiento). Un ticket cerrado hace un año no está "inactivo" en el sentido que el campo intenta capturar (alguien debería estar mirándolo) — está correctamente cerrado. La inactividad solo tiene sentido operativo sobre tickets todavía abiertos.
- **Sin pausar el SLA durante un status `IsPaused`** (elegido, ver Out of scope). `TicketStatus.IsPaused` existe pero introducir "tiempo efectivo vs. tiempo de pausa" en el cálculo de SLA agrega una segunda fuente de verdad (intervalos de pausa por ticket) que ni el brief ni el dominio preexistente piden. Si negocio lo necesita, es una extensión bien delimitada de `TicketSlaCalculator` en una spec aparte, no una razón para no entregar el cálculo simple ahora.
- **Ninguna acción automática sobre SLA vencido o inactividad** (elegido, la decisión más importante de esta spec). Construir un `IHostedService` de polling —o peor, adoptar una librería de jobs nueva (Hangfire/Quartz) solo para esto— sin que el repo tenga ya un precedente de infraestructura de background jobs sería la clase de decisión de arquitectura que necesita su propia conversación, no un efecto colateral de "parametrizar el workflow". Se entrega el dato calculado y confiable; qué hacer con él (notificar, reasignar, escalar) es una decisión de producto todavía sin definir en el brief.
- **`GetSystemWideTicketsQueryHandler` recibe el mismo tratamiento que `GetTicketsQueryHandler`** (elegido, consistencia) — un `SuperAdmin` viendo tickets de todas las empresas necesita los mismos indicadores de SLA/inactividad que un operador tenant-scoped, calculados igual.
- **`UpdateTicket` deja de poder llevar un ticket a un status final; esa transición pasa exclusivamente por `FinishTicket`** (elegido, descubierto durante la revisión de esta misma spec) vs. dejar que ambos caminos puedan cerrar un ticket. Sin esta regla, un ticket podía llegar a un status `IsFinal` sin pasar nunca por `FinishTicketCommandHandler` — y por lo tanto sin generar el log `LogType.Finalization` que `TicketSlaCalculator` usa como referencia exacta de cierre (`FinishedAt`). El resultado habría sido que ese ticket quedara con `FinishedAt = null` pese a estar cerrado, y el cálculo de SLA tendría que aproximar con `LastActivityAt` en vez del instante real de cierre. Forzar el único camino de cierre a pasar por `FinishTicket` (que además ya está gateado por `CanFinishTicket`/`IsSuperAdminTicket` desde SPEC 35) resuelve la inconsistencia de raíz en vez de tolerarla con un fallback. El fallback a `LastActivityAt` se mantiene en `TicketSlaCalculator` únicamente para tickets finalizados **antes** de que esta spec se desplegara, que sí pueden tener `FinishedAt = null` de forma legítima (no hay backfill retroactivo — ver Riesgos).

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **Una empresa configura una regla de transición que deja un status sin ninguna salida** (ej. solo permite `Open → Closed` y olvida `Open → InProgress`, bloqueando su propio flujo habitual). | Aceptado como responsabilidad de quien configura — esta spec valida que cada regla individual sea coherente (status existentes, no auto-transición, sin duplicados) pero no audita la integridad del grafo completo (ver Out of scope). Es reversible: `DELETE` de la regla restaura el comportamiento sin restricciones para ese status de origen. |
| **`SlaDueAt` cambia retroactivamente si alguien edita `TicketComplexity.ResolutionTimeUnits` o `TimeUnit.Code` después de que el ticket ya fue creado**, porque el cálculo es dinámico (no se guarda un valor congelado en el momento de creación del ticket). | Aceptado y documentado: es la consecuencia directa de "calculado, no almacenado" (ver Decisiones — evita una columna derivada que se desincroniza). Si negocio necesita que el SLA quede fijo al momento de creación del ticket, es un cambio de diseño (columna `SlaDueAt` persistida en `Ticket`, poblada una sola vez en `CreateTicketCommandHandler`) que amerita su propia spec una vez que se confirme que es el comportamiento deseado. |
| **Tickets finalizados antes del despliegue de esta spec** (vía el `UpdateTicket` sin restricción que existía hasta acá) pueden estar en un status `IsFinal` sin ningún log `LogType.Finalization` — `FinishedAt` da `null` para ellos aunque estén cerrados. | `TicketSlaCalculator` cae a `LastActivityAt` como referencia de cierre para ese caso (ver Decisiones) — una aproximación razonable, no el instante exacto, pero nunca peor que evaluar contra "ahora" indefinidamente. Sin backfill retroactivo: no hay forma de reconstruir cuándo se cerraron esos tickets con certeza, y no vale la pena inventar una fecha. |
| **`TicketDtoAssembler` cargando `TicketLog`/`TicketCompanyDefault` completos en memoria por cada `Create`/`Update`/`Reassign`/`Finish`** agrega dos lecturas más al mismo patrón in-memory ya aceptado en SPEC 34/35. | Mismo criterio de riesgo ya asumido: volumen esperado bajo, optimización de query si el volumen lo exige más adelante. |
| **Ningún mecanismo evita que las cuatro fuentes de cálculo (3 query handlers + el assembler) diverjan con el tiempo** si alguien modifica una sin la otra. El caso concreto más probable es tomar la `TimeUnit` equivocada: hay dos en juego (la del ticket y la de la complejidad) y el alias `tu` preexistente apunta a la que **no** corresponde. | `TicketSlaCalculator` centraliza la *fórmula*; lo que puede divergir es cómo cada handler arma los parámetros crudos que le pasa. Por eso el parámetro se llama `complexityTimeUnitCode` y no `timeUnitCode` —el nombre es la primera defensa en el call site— y los tests de F8 incluyen un caso discriminante donde las dos unidades tienen `Code` distinto, en cada una de las cuatro vías: cualquier divergencia futura rompe un test en vez de producir un SLA silenciosamente 24× más corto. |
| **El `LEFT JOIN` adicional a `TicketCompanyDefaults` y los dos subqueries correlacionados a `TicketLogs`** agregan costo a cada fila de `GetTickets`/`GetSystemWideTickets`, que ya son las queries de mayor volumen del módulo. | Ambos subqueries filtran por `TicketId` (o `TicketId` + `LogType`) sobre una tabla ya indexada por el patrón de auditoría existente; el `LEFT JOIN` a `TicketCompanyDefaults` es por `CompanyId`, una tabla con como mucho una fila activa por empresa. Si el volumen real de producción lo justifica, es una optimización de índices, no un cambio de contrato. |

---

## What is **not** in this spec

- Cualquier job en segundo plano, notificación o reasignación automática por SLA vencido o inactividad.
- Activar `TicketNotification`.
- Filtros `?slaBreached=`/`?isInactive=` en el listado de tickets.
- `Update` de `TicketStatusTransition`.
- Auditoría de integridad del grafo completo de transiciones de una empresa.
- SLA distinto por canal o prioridad de cliente.
- Pausar el conteo de SLA durante un status `IsPaused`.
- `SlaDueAt` persistido/congelado en el momento de creación del ticket (hoy es 100% calculado en cada lectura).

Con esta spec se completa la segmentación original de las cinco piezas core del módulo de tickets (34 → 35 → 36 → 37 → 38), **con una excepción explícita**: la rutina de notificación por inactividad que el brief pide textualmente queda sin implementar (ver el bloque "Deuda conocida y aceptada" en Out of scope). Es la primera candidata para una spec de seguimiento — **SPEC 39**, no SPEC 38: ese número quedó asignado a una spec no relacionada con tickets (consolidación de query filters globales en `ApplicationDbContext`, ver `specs/38-global-query-filters-consolidation.md`), descubierta y priorizada durante la revisión de este mismo módulo por cerrar una inconsistencia de alcance más amplio que el de tickets. El resto de las extensiones posibles (jobs de escalamiento por SLA vencido, portal de cliente, otros proveedores de canal, pausa de SLA en status `IsPaused`) van en specs nuevas numeradas a partir de la 41.
