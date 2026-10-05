# SPEC 35 — Acciones de ciclo de vida del ticket gateadas por `TicketUserCompany`

> **Status:** Implementado
> **Depends on:** SPEC 34 (`TicketUserCompany`, `TicketUserCompanySuperAdminCoordinator`). Requiere que el roster de agentes de tickets ya exista y esté poblado por empresa.
> **Date:** 2026-09-25
> **Objective:** Reemplazar la reasignación implícita de `UpdateTicket` por un comando dedicado `ReassignTicket` gateado por `IsSuperAdminTicket`/`CanResolveTicket`, agregar `FinishTicket` gateado por `CanFinishTicket`, agregar `AddTicketNote` para registrar notas internas/externas explícitas, y corregir la fuga de visibilidad de `TicketLog.IsOnlyForCreatedAndAssigned` (hoy grabada pero nunca filtrada en la lectura).

---

## Por qué existe esta spec

SPEC 34 creó `TicketUserCompany` y su invariante ("al menos un `IsSuperAdminTicket` activo por empresa"), pero dejó explícitamente fuera de alcance tocar cualquier handler de `Ticket`. El resultado, tal como quedó documentado ahí, es que la tabla existe y se puede administrar, pero **no gatea nada todavía**: `CreateTicketCommandHandler` y `UpdateTicketCommandHandler` (`src/2.Application/UseCases/Messaging/Tickets/Commands/`) siguen validando únicamente que el usuario asignado tenga un `UserCompany` activo (sea miembro de la empresa) — cualquier miembro puede ser asignado o reasignado por cualquier otro usuario con `CanUpdate` sobre `Tickets`, sin mirar `IsSuperAdminTicket` ni `CanResolveTicket`. Esta spec es la que efectivamente cierra la brecha original del brief:

> "El ticket una vez asignado podrá cambiarse entre usuario y debe haber un usuario administrador por empresa que permita distribuir los tickets entre los usuarios registrados para una empresa."
> "`CanFinishTicket` Booleano permite a usuarios operativos finalizar ticket."
> "`CanResolveTicket` Booleano permite indicar entre quiénes se pueden asignar ticket... permite recibir un ticket y gestionarlo."

Además, al auditar `GetTicketByIdQueryHandler` para esta spec se confirmó un segundo defecto ya señalado en el análisis original: `TicketLog.IsOnlyForCreatedAndAssigned` se graba en el dominio (`Ticket.AddLog`, con el comentario "internal notes or sensitive information that should not be visible to all users") pero el `SELECT` de logs en `GetTicketByIdQueryHandler` no filtra por esa columna — **cualquiera que pueda leer el ticket ve todos los logs, privados o no**. Como esta spec introduce la primera vía real para crear notas internas (`AddTicketNote`), corregir el filtro de lectura en el mismo momento es obligatorio: sin la corrección, la nueva feature nacería rota.

También se detectó que `Ticket.AddLog(...)` ignora el parámetro de visibilidad: siempre persiste `IsOnlyForCreatedAndAssigned = false`, sin importar el `LogType`. Hoy es inofensivo porque nada llama `AddLog` con `LogType.InternalNote`/`ExternalNote` todavía. Se corrige acá porque `AddTicketNote` es la primera feature que sí lo necesita.

---

## Scope

**In:**

### A. Dominio

- `src/1.Domain/Enums/LogType.cs`: agregar `Finalization = 5`. Un `FinishTicket` no es un `StatusChange` genérico — necesita su propio tipo para que un reporte pueda filtrar "cuándo se cerró este ticket" sin tener que inspeccionar si el `StatusChange` de turno aterrizó en un status `IsFinal`.
- `src/1.Domain/Messaging/ticket.cs` — `AddLog(...)`: agregar parámetro `bool isOnlyForCreatedAndAssigned = false` (al final de la firma, default `false` preserva el comportamiento de los tres call sites existentes sin tocarlos) y usarlo en vez del literal `false` hardcodeado al construir el `TicketLog`.

### B. Application — nuevo: resolutor de capacidades

- `src/2.Application/UseCases/Messaging/Tickets/TicketUserCompanyCapabilityResolver.cs` (nuevo): clase DI-injected (`IUnitOfWork`), sin MediatR. Expone `Task<TicketUserCompanyCapability> ResolveAsync(Guid userId, Guid companyId, CancellationToken ct)`, donde `TicketUserCompanyCapability` es un `record struct (bool IsSuperAdminTicket, bool CanFinishTicket, bool CanResolveTicket)`. Sin fila activa → los tres en `false`. Consumida por `CreateTicketCommandHandler`, `ReassignTicketCommandHandler` y `FinishTicketCommandHandler` — es el único punto que sabe leer `TicketUserCompany` para decisiones de autorización, evitando que cada handler repita el `GetAllAsync()` + filtro LINQ.
- Registro en `src/2.Application/Common/ConfigureServices.cs`: `services.AddScoped<TicketUserCompanyCapabilityResolver>();`.

### C. Application — nuevo: ensamblador de `TicketDto` (consolidación, no feature nueva)

- `src/2.Application/UseCases/Messaging/Tickets/TicketDtoAssembler.cs` (nuevo): clase DI-injected (`IUnitOfWork`) que concentra la proyección "`Ticket` entity → `TicketDto`" (resolver `Status`/`Complexity`/`TimeUnit`/`Person`/`Project`/`Area`/`Channel`/`CreatedByUser`/`AssignedToUser`/`PrecedentTicket` por `Id` y armar el DTO), hoy duplicada byte-a-byte entre `CreateTicketCommandHandler` y `UpdateTicketCommandHandler` (incluyendo los métodos privados `ResolvePersonName`/`ResolveUserName`, también duplicados). Agregar `ReassignTicket` y `FinishTicket` sin este consolidado habría llevado la misma proyección de 2 a 4 copias; con 4 handlers que la necesitan, extraerla dejó de ser una abstracción prematura. `CreateTicketCommandHandler` y `UpdateTicketCommandHandler` se refactorizan para usarla (eliminando sus copias privadas de `ResolvePersonName`/`ResolveUserName`); su comportamiento de negocio no cambia.
- Registro en `ConfigureServices.cs`: `services.AddScoped<TicketDtoAssembler>();`.

### D. Application — `ReassignTicket` (reemplaza la reasignación implícita de `UpdateTicket`)

- `src/2.Application/UseCases/Messaging/Tickets/Commands/ReassignTicket/ReassignTicketCommand.cs` + `CommandHandler` + `CommandValidator`.
- Cubre tanto la primera asignación de un ticket que se creó sin `AssignedToUserId` como la reasignación de uno ya asignado — un único comando gateado, sin distinguir "assign" de "reassign".
- Gateo: el actor (`ICurrentUserService.UserId`) debe tener `IsSuperAdminTicket = true` para la empresa del token (vía `TicketUserCompanyCapabilityResolver`). Si no, `403` `TICKET_REASSIGN_FORBIDDEN` — mismo patrón de `StatusCode(StatusCodes.Status403Forbidden, response)` que ya usa `RolesController.cs:274` para un rechazo de negocio que no es un `[Authorize]` de framework.
- El usuario destino debe (a) tener `UserCompany` activo en la empresa (miembro del tenant — mismo chequeo que ya existía) y (b) tener `CanResolveTicket = true` (vía el resolver). Si no cumple (b), `400` `TARGET_NOT_ELIGIBLE_RESOLVER`.
- Reasignar al mismo usuario que ya tiene el ticket es un no-op exitoso: no genera un segundo log `Reassignment` **y no persiste nada** (no llama `UpdateAsync`/`SaveChangesAsync`; ver paso 8 del flujo).
- `src/2.Application.DTO/Messaging/Tickets/ReassignTicketDto.cs` (nuevo, webapi-facing): `{ Guid NewAssignedToUserId }`.

### E. Application — `FinishTicket`

- `src/2.Application/UseCases/Messaging/Tickets/Commands/FinishTicket/FinishTicketCommand.cs` + `CommandHandler` + `CommandValidator`.
- Gateo: el actor debe tener `CanFinishTicket = true` **o** `IsSuperAdminTicket = true`. Si no, `403` `TICKET_FINISH_FORBIDDEN`.
- El `TicketStatusId` recibido debe existir y tener `IsFinal = true` (validado contra `TicketStatus`, catálogo ya gestionado por `TicketStatusesController`). Si no existe, `400` `INVALID_TICKET_STATUS`; si existe pero no es terminal, `400` `TICKET_STATUS_NOT_FINAL`.
- Si el ticket ya está en un status con `IsFinal = true`, `409` `TICKET_ALREADY_FINISHED` (no permite "cerrar" dos veces ni cambiar de status final a otro status final por esta vía).
- Registra `LogType.Finalization` con el `ResolutionSummary` opcional como resumen (o un texto por defecto si viene vacío).
- `src/2.Application.DTO/Messaging/Tickets/FinishTicketDto.cs` (nuevo): `{ Guid TicketStatusId, string? ResolutionSummary }`.

### F. Application — `AddTicketNote`

- `src/2.Application/UseCases/Messaging/Tickets/Commands/AddTicketNote/AddTicketNoteCommand.cs` + `CommandHandler` + `CommandValidator`.
- Sin gateo por flags de `TicketUserCompany`: cualquier usuario con `CanUpdate` sobre el recurso `Tickets` puede agregar una nota. Como el endpoint es `POST` (cuyo flag por defecto sería `CanCreate`), el action declara `[RequirePermission(PermissionFlags.CanUpdate)]` explícitamente: agregar una nota modifica un ticket existente, no crea uno. Lo que cambia con esta spec no es quién puede escribir una nota, sino **quién puede leerla después** (sección G).
- `LogType` recibido debe ser `InternalNote` o `ExternalNote`; cualquier otro valor → `400` (los demás tipos son generados por el sistema, no por el usuario). En la práctica lo rechaza primero `AddTicketNoteCommandValidator` vía `ValidationBehavior`, que responde `400` con `ValidationProblemDetails` (`"Validation failure"`, errores por campo) — **no** con un `Response<T>` que lleve el código `INVALID_LOG_TYPE`. El chequeo `INVALID_LOG_TYPE` del handler se mantiene como defensa por si el pipeline de validación se omite.
- `IsOnlyForCreatedAndAssigned` se deriva del `LogType`: `true` para `InternalNote`, `false` para `ExternalNote`. No es un campo que el cliente controle directamente.
- Retorna `Response<TicketLogDto>` (no `TicketDto` completo — el caller ya tiene el ticket abierto, solo necesita confirmar la nota creada).
- `src/2.Application.DTO/Messaging/Tickets/AddTicketNoteDto.cs` (nuevo, webapi-facing): `{ int LogType, string Summary }`. `LogType` viaja como el entero del enum (`2` = `InternalNote`, `3` = `ExternalNote`) para que la capa DTO no referencie `JOIN.Domain`; el controller lo castea a `LogType` al construir el comando.

### G. Application — corrección de visibilidad en `GetTicketByIdQueryHandler`

- El segundo `SELECT` (logs) de `GetTicketByIdQueryHandler` agrega, a la cláusula `WHERE` existente, la condición: incluir la fila si `tl.IsOnlyForCreatedAndAssigned = 0`, **o** el viewer es `t.CreatedByUserId`, **o** el viewer es `t.AssignedToUserId`, **o** el viewer tiene `IsSuperAdminTicket = 1` activo en `Messaging.TicketUserCompanies` para la empresa. Parámetro nuevo `@ViewerId` = `Guid.Parse(currentUserService.UserId)`.
- El `CASE tl.LogType` que traduce el enum a string gana la rama `WHEN 5 THEN 'Finalization'`.

### H. Application — cambios en `CreateTicket`/`UpdateTicket`

- `CreateTicketCommandHandler`: si `request.AssignedToUserId` viene informado, **además** del chequeo existente de `UserCompany` activo, resolver la capacidad del usuario destino vía `TicketUserCompanyCapabilityResolver` y exigir `CanResolveTicket = true`; si no, `400` `INVALID_ASSIGNED_USER_NOT_RESOLVER`. La creación sin `AssignedToUserId` (ticket sin asignar, para asignación posterior vía `ReassignTicket`) sigue permitida sin este chequeo.
- `UpdateTicketCommand.cs` / `UpdateTicketDto.cs`: **se elimina el campo `AssignedToUserId`**. `UpdateTicket` deja de poder tocar la asignación — es responsabilidad exclusiva de `ReassignTicket` a partir de esta spec.
- `src/2.Application/Mappings/Messaging/Ticket/TicketMapper.cs`: agregar `[MapperIgnoreTarget(nameof(Ticket.AssignedToUserId))]` al método `ApplyUpdate`. **Obligatorio, no cosmético**: hoy el mapper solo ignora `Ticket.AssignedToUser` (la *navegación*), no la FK escalar `AssignedToUserId`, que se mapea desde el comando. Al quitar la propiedad del comando, Mapperly deja de encontrar el miembro origen y emite el diagnóstico de miembro destino sin mapear (`RMG020`) al compilar — lo que viola directamente el criterio "0 warnings nuevos" de esta spec.
- `UpdateTicketCommandHandler.cs`: se elimina el bloque de validación de `AssignedToUserId` (existencia + tenant) y el bloque `assignmentChanged` / `AddLog(..., LogType.Reassignment, ...)`. El resto de la actualización (status, complejidad, canal, etc.) no cambia.
- `TicketsController.Update`: se elimina `AssignedToUserId = dto.AssignedToUserId` del mapeo `UpdateTicketDto → UpdateTicketCommand`.

### I. Controller

- `src/4.Services.WebApi/Controllers/Messaging/TicketsController.cs`:
  - `PUT /api/v1/Tickets/{id:guid}/reassign` — body `ReassignTicketDto`. `200` con `TicketDto` / `400` / `403` / `404`.
  - `PUT /api/v1/Tickets/{id:guid}/finish` — body `FinishTicketDto`. `200` con `TicketDto` / `400` / `403` / `404` / `409`.
  - `POST /api/v1/Tickets/{id:guid}/notes` — body `AddTicketNoteDto`. `201` con `Response<TicketLogDto>` / `400` / `404`.
  - Nuevo mapeo de códigos de negocio → HTTP en los tres endpoints:

    | Código | HTTP |
    |---|---|
    | `COMPANY_REQUIRED` / `USER_REQUIRED` | 401 |
    | `TICKET_NOT_FOUND` | 404 |
    | `TICKET_REASSIGN_FORBIDDEN` / `TICKET_FINISH_FORBIDDEN` | 403 |
    | `TARGET_NOT_ELIGIBLE_RESOLVER` / `INVALID_ASSIGNED_USER` / `INVALID_ASSIGNED_USER_TENANT` / `INVALID_TICKET_STATUS` / `TICKET_STATUS_NOT_FINAL` / `INVALID_LOG_TYPE` | 400 |
    | `TICKET_ALREADY_FINISHED` | 409 |

  - **Alineación de `COMPANY_REQUIRED` en los endpoints preexistentes del mismo controller.** Hoy `TicketsController` no mapea `COMPANY_REQUIRED` en absoluto: cae al `return BadRequest(response)` final de cada action, así que un token sin `CompanyId` devuelve **400**. `CLAUDE.md` fija la convención contraria para este proyecto ("`401 Unauthorized` para token inválido o `CompanyId` faltante") y los tres endpoints nuevos de esta spec la respetan. Dejar el controller con 401 en los endpoints nuevos y 400 en `GetPaged`/`GetById`/`Create`/`Update`/`Delete` sería una incoherencia introducida por esta spec, así que se agrega el mapeo `COMPANY_REQUIRED → 401` también a los cinco actions existentes. Es un cambio de contrato acotado y deliberado: se documenta junto al de `UpdateTicketDto` en `CURL_REQUESTS.md`.

### J. Tests

- Actualizar `UpdateTicketCommandHandlerTests`/`UpdateTicketCommandValidatorTests` (quitar los casos de `AssignedToUserId` que ya no aplican).
- Actualizar `CreateTicketCommandHandlerTests` (agregar el caso `INVALID_ASSIGNED_USER_NOT_RESOLVER`).
- Actualizar `GetTicketByIdQueryHandlerTests` (agregar los casos de visibilidad de logs).
- Nuevos: `ReassignTicketCommandHandlerTests`, `ReassignTicketCommandValidatorTests`, `FinishTicketCommandHandlerTests`, `FinishTicketCommandValidatorTests`, `AddTicketNoteCommandHandlerTests`, `AddTicketNoteCommandValidatorTests`, `TicketUserCompanyCapabilityResolverTests`, `TicketDtoAssemblerTests`.
- Cobertura ≥ 90% en clases nuevas/modificadas (gate de CI).

**Out of scope (para specs futuras):**

- **Auto-asignación / "tomar" un ticket sin asignar** (un agente con `CanResolveTicket` que se auto-asigna sin pasar por `IsSuperAdminTicket`). El brief solo describe distribución centralizada por el admin; self-service queda para una spec aparte si negocio lo pide.
- **Reabrir un ticket finalizado** (volver de un status `IsFinal` a uno no final). `FinishTicket` es de una sola dirección en esta spec.
- **Workflow parametrizable** (qué transiciones de status son válidas entre sí, más allá de "el status de finish debe ser `IsFinal`"): SPEC 37.
- **Notificar** al nuevo asignado o al creador cuando se reasigna/finaliza/se agrega una nota externa.
- **Adjuntar documentos a una nota** (`TicketDocuments`, `TicketLogsId`): SPEC 36. `AddTicketNote` solo crea el `TicketLog`; la vinculación de archivos a ese log específico es responsabilidad de SPEC 36, que ya diseña `TicketDocuments.TicketLogsId` apuntando a filas de `TicketLog` — incluidas las que esta spec crea.
- **Editar o borrar una nota ya creada.** `TicketLog` es append-only (mismo criterio que `AuditLog` de SPEC 29, aunque acá no comparte tabla).
- **Endpoint paginado de solo-logs** (`GET /Tickets/{id}/logs`) independiente del detalle del ticket: hoy los logs viajan embebidos en `GetTicketById`; si el volumen de logs por ticket lo justifica, es una spec de paginación aparte.
- **Exponer `TicketUserCompanyCapability` del usuario autenticado como endpoint** (`GET /Tickets/my-capabilities`): se evaluó y se descartó por ahora — el frontend puede inferir qué botones mostrar a partir de un 403 real, o SPEC 34 puede agregar `GET /TicketUserCompanies/me` si se vuelve necesario.

---

## Data model

### `LogType` (modificado)

```csharp
// src/1.Domain/Enums/LogType.cs
public enum LogType
{
    Creation = 0,
    StatusChange = 1,
    InternalNote = 2,
    ExternalNote = 3,
    Reassignment = 4,

    /// <summary>
    /// Represents the ticket reaching a terminal, closed state.
    /// </summary>
    Finalization = 5,

    ChangeStatus = StatusChange,
    Reasignment = Reassignment
}
```

### `Ticket.AddLog` (modificado)

```csharp
// src/1.Domain/Messaging/ticket.cs
public void AddLog(
    Guid userId,
    LogType logType,
    string summary,
    Guid? previousStatusId = null,
    Guid? newAssignedToUserId = null,
    bool isOnlyForCreatedAndAssigned = false)
{
    // ... validaciones existentes sin cambios ...

    TicketLogs.Add(new TicketLog
    {
        TicketId = Id,
        CompanyId = CompanyId,
        LogType = logType,
        Summary = summary.Trim(),
        UserRegisterLogId = userId,
        PreviousStatusId = previousStatusId,
        TicketStatusId = TicketStatusId,
        TimeUnitId = TimeUnitId == Guid.Empty ? null : TimeUnitId,
        ConsumedTime = ConsumedTime,
        IsOnlyForCreatedAndAssigned = isOnlyForCreatedAndAssigned, // antes: literal false
        NewAssignedToUserId = newAssignedToUserId,
        Created = DateTime.UtcNow,
        CreatedBy = userId.ToString(),
        GcRecord = BaseAuditableEntity.ActiveGcRecord
    });
}
```

Los tres call sites existentes (`Creation` en `CreateTicketCommandHandler`, `StatusChange` en `UpdateTicketCommandHandler`, y el `Reassignment` que se **elimina** de `UpdateTicketCommandHandler` en esta spec y pasa a `ReassignTicketCommandHandler`) no pasan el nuevo parámetro — siguen grabando `false`, comportamiento idéntico al actual.

### `TicketUserCompanyCapabilityResolver`

```csharp
// src/2.Application/UseCases/Messaging/Tickets/TicketUserCompanyCapabilityResolver.cs
namespace JOIN.Application.UseCases.Messaging.Tickets;

public readonly record struct TicketUserCompanyCapability(
    bool IsSuperAdminTicket,
    bool CanFinishTicket,
    bool CanResolveTicket);

public sealed class TicketUserCompanyCapabilityResolver(IUnitOfWork unitOfWork)
{
    public async Task<TicketUserCompanyCapability> ResolveAsync(
        Guid userId,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<TicketUserCompany>();
        var all = await repository.GetAllAsync();

        var match = all.FirstOrDefault(x =>
            x.GcRecord == 0
            && x.CompanyId == companyId
            && x.UserId == userId);

        return match is null
            ? new TicketUserCompanyCapability(false, false, false)
            : new TicketUserCompanyCapability(match.IsSuperAdminTicket, match.CanFinishTicket, match.CanResolveTicket);
    }
}
```

### Commands

```csharp
// ReassignTicketCommand.cs
public sealed record ReassignTicketCommand(Guid TicketId, Guid NewAssignedToUserId)
    : ITransactionalCommand<Response<TicketDto>>;

// FinishTicketCommand.cs
public sealed record FinishTicketCommand(Guid TicketId, Guid TicketStatusId, string? ResolutionSummary)
    : ITransactionalCommand<Response<TicketDto>>;

// AddTicketNoteCommand.cs
public sealed record AddTicketNoteCommand(Guid TicketId, LogType LogType, string Summary)
    : ITransactionalCommand<Response<TicketLogDto>>;
```

### `ReassignTicketCommandHandler` — flujo

1. `CompanyId == Guid.Empty` → `COMPANY_REQUIRED` (401).
2. `Guid.TryParse(currentUserService.UserId, ...)` falla → `USER_REQUIRED` (401).
3. Cargar `Ticket` por `TicketId` filtrado por `CompanyId`; `null` → `TICKET_NOT_FOUND` (404).
4. `capabilityResolver.ResolveAsync(actorId, companyId)`; si `!IsSuperAdminTicket` → `TICKET_REASSIGN_FORBIDDEN` (403).
5. Cargar `ApplicationUser` de `NewAssignedToUserId`; `null` → `INVALID_ASSIGNED_USER` (400).
6. Verificar `UserCompany` activo del destino en la empresa (mismo query que ya existía en `CreateTicket`/`UpdateTicket`) → si no, `INVALID_ASSIGNED_USER_TENANT` (400).
7. `capabilityResolver.ResolveAsync(newAssignedToUserId, companyId)`; si `!CanResolveTicket` → `TARGET_NOT_ELIGIBLE_RESOLVER` (400).
8. Si `ticket.AssignedToUserId == request.NewAssignedToUserId` → no-op: saltar los pasos 9 y 10 e ir directo al 11. **No** se llama `UpdateAsync`/`SaveChangesAsync`: sobre una entidad sin cambios EF reporta 0 filas afectadas, y el corte `result <= 0 → REASSIGN_FAILED` del paso 10 convertiría este caso exitoso en un `400`.
9. `previousAssignedToUserId = ticket.AssignedToUserId; ticket.AssignedToUserId = request.NewAssignedToUserId; ticket.AddLog(actorId, LogType.Reassignment, "Ticket reasignado", newAssignedToUserId: request.NewAssignedToUserId);`
10. `UpdateAsync` + `SaveChangesAsync`. `result <= 0` → `REASSIGN_FAILED` (400).
11. `await ticketDtoAssembler.BuildAsync(ticket, company, cancellationToken)` → `Response<TicketDto>.Ok`.

### `FinishTicketCommandHandler` — flujo

1. `CompanyId == Guid.Empty` → `COMPANY_REQUIRED` (401).
2. `UserId` no parseable → `USER_REQUIRED` (401).
3. Cargar `Ticket`; `null` → `TICKET_NOT_FOUND` (404).
4. `capabilityResolver.ResolveAsync(actorId, companyId)`; si `!CanFinishTicket && !IsSuperAdminTicket` → `TICKET_FINISH_FORBIDDEN` (403).
5. Cargar `TicketStatus` destino; `null` → `INVALID_TICKET_STATUS` (400); `!IsFinal` → `TICKET_STATUS_NOT_FINAL` (400).
6. Cargar `TicketStatus` actual del ticket; si `IsFinal` → `TICKET_ALREADY_FINISHED` (409).
7. `previousStatusId = ticket.TicketStatusId; ticket.TicketStatusId = request.TicketStatusId; ticket.AddLog(actorId, LogType.Finalization, string.IsNullOrWhiteSpace(request.ResolutionSummary) ? "Ticket finalizado" : request.ResolutionSummary!, previousStatusId: previousStatusId);`
8. `UpdateAsync` + `SaveChangesAsync`. `result <= 0` → `FINISH_FAILED` (400).
9. `await ticketDtoAssembler.BuildAsync(...)` → `Response<TicketDto>.Ok`.

### `AddTicketNoteCommandHandler` — flujo

1. `CompanyId == Guid.Empty` → `COMPANY_REQUIRED` (401).
2. `UserId` no parseable → `USER_REQUIRED` (401).
3. `request.LogType` no es `InternalNote` ni `ExternalNote` → `INVALID_LOG_TYPE` (400). Defensa redundante: normalmente el validator ya cortó antes (ver Scope F).
4. Cargar `Ticket`; `null` → `TICKET_NOT_FOUND` (404).
5. `var isPrivate = request.LogType == LogType.InternalNote; ticket.AddLog(actorId, request.LogType, request.Summary, isOnlyForCreatedAndAssigned: isPrivate);`
6. `UpdateAsync` + `SaveChangesAsync`. `result <= 0` → `ADD_NOTE_FAILED` (400).
7. Construir `TicketLogDto` a mano con los datos ya disponibles: `LogType` como string, `Summary`, `CreatedAt = DateTime.UtcNow` (el mismo timestamp que usó `AddLog`), `UserRegisteredName` resuelto del `ApplicationUser` actor ya cargado, `PreviousStatusName = null`, `NewStatusName` resuelto del `TicketStatus` actual del ticket, `ConsumedTime = null`.

### `GetTicketByIdQueryHandler` — SQL de logs (modificado)

```sql
SELECT
    tl.Id,
    CASE tl.LogType
        WHEN 0 THEN 'Creation'
        WHEN 1 THEN 'StatusChange'
        WHEN 2 THEN 'InternalNote'
        WHEN 3 THEN 'ExternalNote'
        WHEN 4 THEN 'Reassignment'
        WHEN 5 THEN 'Finalization'
        ELSE CONCAT('Unknown(', tl.LogType, ')')
    END AS LogType,
    tl.Summary,
    tl.Created AS CreatedAt,
    CONCAT(usr.FirstName, ' ', usr.LastName) AS UserRegisteredName,
    ps.Name AS PreviousStatusName,
    ns.Name AS NewStatusName,
    tl.ConsumedTime
FROM Support.TicketLogs tl
LEFT JOIN Security.Users usr ON tl.UserRegisterLogId = usr.Id
LEFT JOIN Messaging.TicketStatuses ps ON tl.PreviousStatusId = ps.Id
LEFT JOIN Messaging.TicketStatuses ns ON tl.TicketStatusId = ns.Id
WHERE tl.TicketId = @Id
  AND tl.CompanyId = @TenantId
  AND tl.GcRecord = 0
  AND (
        tl.IsOnlyForCreatedAndAssigned = 0
        OR t.CreatedByUserId = @ViewerId
        OR t.AssignedToUserId = @ViewerId
        OR EXISTS (
            SELECT 1 FROM Messaging.TicketUserCompanies tuc
            WHERE tuc.UserId = @ViewerId
              AND tuc.CompanyId = @TenantId
              AND tuc.GcRecord = 0
              AND tuc.IsSuperAdminTicket = 1
        )
      )
ORDER BY tl.Created DESC;
```

`t.CreatedByUserId`/`t.AssignedToUserId` requieren que este segundo `SELECT` también tenga acceso a la fila de `Tickets` — se resuelve agregando `INNER JOIN Messaging.Tickets t ON tl.TicketId = t.Id` (la primera consulta del mismo handler ya confirmó que el ticket existe y pertenece al tenant; este join es solo para los dos campos de comparación, no repite la validación de existencia). `@ViewerId` se agrega a los parámetros del `CommandDefinition` junto a `@Id`/`@TenantId`, resuelto de `Guid.Parse(currentUserService.UserId!)` tras el chequeo `COMPANY_REQUIRED` habitual (si `UserId` no parsea, se corta antes con el mismo criterio que usan los commands: `USER_REQUIRED`).

### `TicketDtoAssembler`

```csharp
// src/2.Application/UseCases/Messaging/Tickets/TicketDtoAssembler.cs
namespace JOIN.Application.UseCases.Messaging.Tickets;

public sealed class TicketDtoAssembler(IUnitOfWork unitOfWork)
{
    public async Task<TicketDto> BuildAsync(Ticket entity, Company company, CancellationToken cancellationToken)
    {
        // Misma secuencia de lookups por Id que ya hacían CreateTicketCommandHandler
        // y UpdateTicketCommandHandler al final de su Handle(): Status, Complexity,
        // TimeUnit, Channel, Person, Project, Area, CreatedByUser, AssignedToUser,
        // PrecedentTicket. Arma el TicketDto con los mismos campos que hoy.
    }

    // ResolvePersonName / ResolveUserName migran acá como métodos privados,
    // removidos de CreateTicketCommandHandler y UpdateTicketCommandHandler.
}
```

No incluye `Logs` — eso sigue siendo exclusivo de `GetTicketByIdQueryHandler` (Dapper), que ya no pasa por este assembler.

---

## Implementation plan

### F1 — Dominio

1. Agregar `LogType.Finalization = 5` a `src/1.Domain/Enums/LogType.cs`.
2. Modificar `Ticket.AddLog(...)` en `src/1.Domain/Messaging/ticket.cs` para aceptar `isOnlyForCreatedAndAssigned` y usarlo en vez del literal `false`.
3. `dotnet build -c Release` → 0 errores (los 3 call sites existentes compilan sin cambios por el default `= false`).

### F2 — `TicketUserCompanyCapabilityResolver`

1. Crear el record struct + la clase con la firma de la sección Data model.
2. Registrar `services.AddScoped<TicketUserCompanyCapabilityResolver>();` en `ConfigureServices.cs`.
3. `dotnet build` → 0 errores.

### F3 — `TicketDtoAssembler` (refactor de consolidación)

1. Crear `TicketDtoAssembler` con la proyección hoy duplicada en `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` (incluye `ResolvePersonName`/`ResolveUserName` como privados).
2. Refactorizar `CreateTicketCommandHandler` y `UpdateTicketCommandHandler` para inyectar `TicketDtoAssembler` y reemplazar su bloque final de construcción de `TicketDto` por `await _ticketDtoAssembler.BuildAsync(entity, company, cancellationToken)`. Eliminar sus copias privadas de `ResolvePersonName`/`ResolveUserName`.
3. Registrar `services.AddScoped<TicketDtoAssembler>();`.
4. `dotnet build` → 0 errores. `dotnet test --filter "FullyQualifiedName~CreateTicket|FullyQualifiedName~UpdateTicket"` → sigue en verde sin cambios de comportamiento (es un refactor puro).

### F4 — Quitar la reasignación de `UpdateTicket`

1. Quitar `AssignedToUserId` de `UpdateTicketCommand.cs` y `UpdateTicketDto.cs`.
1b. Agregar `[MapperIgnoreTarget(nameof(Ticket.AssignedToUserId))]` a `TicketMapper.ApplyUpdate` (junto al `[MapperIgnoreTarget(nameof(Ticket.AssignedToUser))]` que ya está). Compilar y confirmar que no aparece ningún `RMG0xx` nuevo en la salida.
2. En `UpdateTicketCommandHandler.cs`: quitar el bloque de validación de `AssignedToUserId` (existencia + `UserCompany` del tenant) y el bloque `assignmentChanged`/`AddLog(..., Reassignment, ...)`. `entity.AssignedToUserId` deja de mutarse acá.
3. En `TicketsController.Update`: quitar `AssignedToUserId = dto.AssignedToUserId` del mapeo.
4. `dotnet build` → 0 errores. Actualizar `UpdateTicketCommandHandlerTests`/`UpdateTicketCommandValidatorTests` quitando los casos que ya no aplican (`INVALID_ASSIGNED_USER`, `INVALID_ASSIGNED_USER_TENANT`, el caso de log `Reassignment`).

### F5 — Gatear la asignación inicial en `CreateTicket`

1. En `CreateTicketCommandHandler.cs`, después del chequeo existente de `UserCompany` del `AssignedToUserId`, agregar la llamada a `TicketUserCompanyCapabilityResolver.ResolveAsync` y el corte `INVALID_ASSIGNED_USER_NOT_RESOLVER` (400) si `!CanResolveTicket`.
2. `dotnet build` → 0 errores. Agregar el caso a `CreateTicketCommandHandlerTests`.

### F6 — `ReassignTicket`

1. Crear `ReassignTicketCommand`, `ReassignTicketCommandValidator` (`RuleFor(x => x.TicketId).NotEqual(Guid.Empty); RuleFor(x => x.NewAssignedToUserId).NotEqual(Guid.Empty);`), `ReassignTicketCommandHandler` con el flujo de la sección Data model.
2. `dotnet build` → 0 errores.

### F7 — `FinishTicket`

1. Crear `FinishTicketCommand`, `FinishTicketCommandValidator` (`RuleFor(x => x.TicketId).NotEqual(Guid.Empty); RuleFor(x => x.TicketStatusId).NotEqual(Guid.Empty); RuleFor(x => x.ResolutionSummary).MaximumLength(500).When(x => x.ResolutionSummary is not null);`), `FinishTicketCommandHandler` con el flujo de la sección Data model.
2. `dotnet build` → 0 errores.

### F8 — `AddTicketNote`

1. Crear `AddTicketNoteCommand`, `AddTicketNoteCommandValidator` (`RuleFor(x => x.TicketId).NotEqual(Guid.Empty); RuleFor(x => x.Summary).NotEmpty().MaximumLength(1000); RuleFor(x => x.LogType).Must(t => t is LogType.InternalNote or LogType.ExternalNote).WithMessage("LogType must be InternalNote or ExternalNote.");`), `AddTicketNoteCommandHandler` con el flujo de la sección Data model.
2. `dotnet build` → 0 errores.

### F9 — Corregir visibilidad en `GetTicketByIdQueryHandler`

1. Agregar el `INNER JOIN Messaging.Tickets t` y la condición de visibilidad al segundo `SELECT`, más el parámetro `@ViewerId`, según la sección Data model.
2. Agregar la rama `WHEN 5 THEN 'Finalization'` al `CASE`.
3. Resolver `@ViewerId` con el mismo criterio `USER_REQUIRED` que ya usan los commands (`Guid.TryParse(currentUserService.UserId, ...)`).
4. `dotnet build` → 0 errores. Actualizar `GetTicketByIdQueryHandlerTests` con los casos de visibilidad.

### F10 — Controller

1. Agregar `Reassign`, `Finish`, `AddNote` a `TicketsController.cs` con el mapeo de errores de la sección Scope.
2. Agregar el mapeo `COMPANY_REQUIRED → 401` a los cinco actions preexistentes del controller (`GetPaged`, `GetSystemWide`, `GetById`, `Create`, `Update`, `Delete`), que hoy lo devuelven como 400 por el fallback.
3. `dotnet build` → 0 errores.

### F11 — Tests (~34 casos nuevos + ajustes a los existentes)

- `TicketUserCompanyCapabilityResolverTests` (3): sin fila activa → los tres `false`; fila activa devuelve sus flags tal cual; fila soft-deleted no cuenta.
- `TicketDtoAssemblerTests` (2): construye el DTO con todos los campos relacionados resueltos; campos opcionales ausentes (`PersonId`, `ProjectId`, `AreaId`, `PrecedentTicketId` null) no rompen el armado.
- `ReassignTicketCommandHandlerTests` (9): `CompanyId` vacío → 401; `UserId` no parseable → 401; ticket no encontrado o cross-tenant → 404; actor sin `IsSuperAdminTicket` → 403 `TICKET_REASSIGN_FORBIDDEN`; destino inexistente → 400; destino sin `UserCompany` en el tenant → 400; destino sin `CanResolveTicket` → 400 `TARGET_NOT_ELIGIBLE_RESOLVER`; reasignar al mismo usuario → 200 sin log nuevo; happy path → 200, log `Reassignment` creado, `AssignedToUserId` actualizado.
- `ReassignTicketCommandValidatorTests` (2): `TicketId`/`NewAssignedToUserId` vacíos fallan.
- `FinishTicketCommandHandlerTests` (8): `CompanyId` vacío → 401; ticket no encontrado → 404; actor sin `CanFinishTicket` ni `IsSuperAdminTicket` → 403; actor con solo `IsSuperAdminTicket` (sin `CanFinishTicket`) → 200 (el super admin también puede finalizar); status inexistente → 400 `INVALID_TICKET_STATUS`; status no `IsFinal` → 400 `TICKET_STATUS_NOT_FINAL`; ticket ya en status final → 409; happy path → 200, log `Finalization` con el `previousStatusId` correcto.
- `FinishTicketCommandValidatorTests` (2): `TicketId`/`TicketStatusId` vacíos fallan; `ResolutionSummary` de 501 caracteres falla.
- `AddTicketNoteCommandHandlerTests` (6): `CompanyId` vacío → 401; `LogType.Creation` (o cualquier no permitido) → 400 `INVALID_LOG_TYPE`; ticket no encontrado → 404; `InternalNote` persiste `IsOnlyForCreatedAndAssigned = true`; `ExternalNote` persiste `false`; happy path devuelve `TicketLogDto` con los campos esperados.
- `AddTicketNoteCommandValidatorTests` (2): `Summary` vacío falla; `LogType.StatusChange` falla la regla `Must`.
- `GetTicketByIdQueryHandlerTests` (nuevos, 5): log con `IsOnlyForCreatedAndAssigned = false` visible para cualquier viewer con acceso al ticket; log privado visible para el creador; visible para el asignado; visible para un `IsSuperAdminTicket` de la empresa; **no** visible para un tercer usuario sin ninguna de esas tres condiciones.
- Ajustes: `UpdateTicketCommandHandlerTests`/`UpdateTicketCommandValidatorTests` (quitar casos de `AssignedToUserId`), `CreateTicketCommandHandlerTests` (+1 caso `INVALID_ASSIGNED_USER_NOT_RESOLVER`).
- `dotnet test --filter "FullyQualifiedName~Tickets"` → 0 fallidos.

### F12 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test --collect:"XPlat Code Coverage"` → gate de 90% en `JOIN.Application` sigue en verde.
3. Smoke: crear un ticket sin asignar → `ReassignTicket` con un usuario `CanResolveTicket = true` como actor `IsSuperAdminTicket` → 200. Repetir el `ReassignTicket` como actor sin `IsSuperAdminTicket` → 403.
4. Smoke: `FinishTicket` con un status no final → 400. Con un status final por un actor `CanFinishTicket` (sin ser super admin) → 200. Repetir sobre el mismo ticket → 409.
5. Smoke: `AddTicketNote` con `InternalNote` → la nota aparece en `GetTicketById` para el creador/asignado/super admin, y desaparece de la respuesta para un tercer usuario de la misma empresa con acceso de lectura a `Tickets` pero sin ninguna de esas tres condiciones.
6. Smoke: `UpdateTicket` con un payload que antes traía `AssignedToUserId` ahora lo ignora silenciosamente si el frontend todavía lo envía (el campo ya no existe en el DTO, así que el deserializador de JSON simplemente no lo mapea — no rompe, no reasigna).
7. `CURL_REQUESTS.md` — agregar los 3 endpoints nuevos y actualizar el bloque de `PUT /Tickets/{id}` para reflejar que ya no acepta `AssignedToUserId`.

---

## Acceptance criteria

### F1 — Dominio

- [ ] `LogType.Finalization = 5` existe.
- [ ] `Ticket.AddLog` acepta `isOnlyForCreatedAndAssigned` con default `false` y lo usa al construir el `TicketLog` (ya no hay literal `false` hardcodeado ahí).
- [ ] Los tres call sites preexistentes de `AddLog` (Creation, StatusChange, y el que queda en `UpdateTicketCommandHandler` para status) siguen grabando `IsOnlyForCreatedAndAssigned = false` sin cambios de comportamiento.

### F2/F3 — Resolutor y ensamblador

- [ ] `TicketUserCompanyCapabilityResolver.ResolveAsync` devuelve `(false, false, false)` cuando no hay fila `TicketUserCompany` activa para `(userId, companyId)`.
- [ ] Ignora filas con `GcRecord != 0`.
- [ ] `TicketDtoAssembler.BuildAsync` produce el mismo `TicketDto` que antes producían inline `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` (mismos campos, mismos valores) — verificado porque los tests existentes de esos dos handlers siguen pasando sin modificar sus aserciones sobre el DTO.
- [ ] `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` ya no tienen métodos privados `ResolvePersonName`/`ResolveUserName` (viven solo en el assembler).

### F4 — `UpdateTicket` sin reasignación

- [ ] `UpdateTicketCommand`/`UpdateTicketDto` no tienen la propiedad `AssignedToUserId`.
- [ ] `TicketMapper.ApplyUpdate` tiene `[MapperIgnoreTarget(nameof(Ticket.AssignedToUserId))]` y el build no emite diagnósticos `RMG0xx` nuevos.
- [ ] `UpdateTicketCommandHandler` no muta `entity.AssignedToUserId` bajo ninguna condición.
- [ ] `UpdateTicketCommandHandler` no llama `AddLog` con `LogType.Reassignment`.
- [ ] `PUT /Tickets/{id}` con un JSON que incluye `assignedToUserId` no falla (el campo se ignora) y el ticket conserva su asignación previa.

### F5 — `CreateTicket` gateado

- [ ] `CreateTicketCommandHandler` retorna 400 `INVALID_ASSIGNED_USER_NOT_RESOLVER` si `AssignedToUserId` viene informado y el usuario destino no tiene `CanResolveTicket = true` activo en la empresa.
- [ ] Crear un ticket sin `AssignedToUserId` sigue funcionando sin este chequeo.

### F6 — `ReassignTicket`

- [ ] Retorna 401 `COMPANY_REQUIRED` sin `CompanyId` en el token.
- [ ] Retorna 403 `TICKET_REASSIGN_FORBIDDEN` si el actor no tiene `IsSuperAdminTicket = true` en la empresa.
- [ ] Retorna 400 `TARGET_NOT_ELIGIBLE_RESOLVER` si el destino no tiene `CanResolveTicket = true`.
- [ ] Retorna 400 `INVALID_ASSIGNED_USER_TENANT` si el destino no tiene `UserCompany` activo en la empresa.
- [ ] Reasignar al mismo usuario ya asignado no crea un log `Reassignment` nuevo.
- [ ] El happy path actualiza `Ticket.AssignedToUserId`, crea un log `Reassignment` con `NewAssignedToUserId` correcto, y devuelve el `TicketDto` actualizado.
- [ ] Funciona igual sobre un ticket recién creado sin asignar (primera asignación) que sobre uno ya asignado (reasignación).

### F7 — `FinishTicket`

- [ ] Retorna 403 `TICKET_FINISH_FORBIDDEN` si el actor no tiene `CanFinishTicket` ni `IsSuperAdminTicket`.
- [ ] Un actor con solo `IsSuperAdminTicket` (sin `CanFinishTicket`) puede finalizar.
- [ ] Retorna 400 `INVALID_TICKET_STATUS` si el `TicketStatusId` no existe.
- [ ] Retorna 400 `TICKET_STATUS_NOT_FINAL` si el status existe pero `IsFinal = false`.
- [ ] Retorna 409 `TICKET_ALREADY_FINISHED` si el ticket ya está en un status `IsFinal = true`.
- [ ] El happy path graba un log `LogType.Finalization` con `PreviousStatusId` correcto y actualiza `Ticket.TicketStatusId`.

### F8 — `AddTicketNote`

- [ ] Retorna 400 si `LogType` no es `InternalNote` ni `ExternalNote` (vía `ValidationProblemDetails` del validator; el handler conserva `INVALID_LOG_TYPE` como defensa).
- [ ] `POST /Tickets/{id}/notes` exige `CanUpdate` (no `CanCreate`) sobre `Tickets`.
- [ ] `InternalNote` persiste `TicketLog.IsOnlyForCreatedAndAssigned = true`.
- [ ] `ExternalNote` persiste `TicketLog.IsOnlyForCreatedAndAssigned = false`.
- [ ] Devuelve `Response<TicketLogDto>` con `Summary`, `LogType`, `UserRegisteredName` y `CreatedAt` correctos.

### F9 — Visibilidad de logs

- [ ] Un log con `IsOnlyForCreatedAndAssigned = false` aparece en `GetTicketById` para cualquier usuario con acceso de lectura al ticket.
- [ ] Un log privado aparece para el creador del ticket (`CreatedByUserId`).
- [ ] Aparece para el usuario asignado (`AssignedToUserId`).
- [ ] Aparece para cualquier usuario con `IsSuperAdminTicket = true` activo en la empresa, sea o no el creador/asignado.
- [ ] **No** aparece para un usuario que no cumple ninguna de las tres condiciones anteriores, aunque tenga `CanRead` sobre `Tickets`.
- [ ] El `CASE tl.LogType` mapea `5` a `'Finalization'`.

### F10 — Controller

- [ ] `PUT /Tickets/{id}/reassign`, `PUT /Tickets/{id}/finish`, `POST /Tickets/{id}/notes` existen con el mapeo de errores de la sección Scope.
- [ ] `POST /Tickets/{id}/notes` devuelve 201.
- [ ] Un request sin `CompanyId` en el token devuelve **401** en los ocho endpoints del controller (los tres nuevos y los cinco preexistentes), no 400 en unos y 401 en otros.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test --filter "FullyQualifiedName~Tickets"` → 0 fallidos, ≥ 34 tests nuevos/modificados.
- [ ] `JOIN.Application` ≥ 90% line coverage (gate de CI).
- [ ] `CURL_REQUESTS.md` documenta los 3 endpoints nuevos y refleja el contrato reducido de `PUT /Tickets/{id}`.
- [ ] Ningún cambio toca `TicketUserCompany`, su coordinador, ni su controller (SPEC 34 queda intacta; esta spec solo la consume).

---

## Decisions taken and discarded

- **Un único comando `ReassignTicket` para primera asignación y reasignación** (elegido) vs. `AssignTicket` + `ReassignTicket` separados. El gateo (`IsSuperAdminTicket` del actor, `CanResolveTicket` del destino) es idéntico en ambos casos; separar los comandos solo duplicaría el handler sin aportar semántica distinta.
- **Rechazo con `403` vía `Response.Error` + `StatusCode(403, ...)` en el controller** (elegido) vs. modelarlo como un `400` de negocio más. Hay precedente exacto en el repo (`RolesController.cs:274`) para un rechazo de autorización que depende de datos en tiempo de ejecución (no de un claim estático de `[Authorize(Roles=...)]`). Usar 403 es semánticamente correcto (RFC 7235, ya invocado en `CLAUDE.md` para el otro mecanismo de autorización del proyecto) y mantiene la distinción 401/403/400 coherente con el resto del sistema.
- **`FinishTicket` acepta `IsSuperAdminTicket` como bypass de `CanFinishTicket`** (elegido) vs. exigir el flag exacto. Un super admin de tickets que puede redistribuir cualquier ticket sin restricción sería una autoridad menor si no pudiera además cerrarlo; negarle esa capacidad habría sido una inconsistencia de diseño no pedida por el brief.
- **`AddTicketNote` sin gateo por flags de `TicketUserCompany`** (elegido) vs. exigir `CanResolveTicket`/`CanFinishTicket` para escribir una nota. El brief no lo pide, y el propio `[PermissionResource("Tickets")] CanUpdate` ya es la puerta de entrada. Lo que sí cambia con la nota es su visibilidad de lectura, no quién puede escribirla.
- **`IsOnlyForCreatedAndAssigned` derivado del `LogType`, no un parámetro independiente del comando** (elegido) vs. dejar que el cliente lo setee explícitamente. Evita el estado inconsistente "nota externa marcada como privada" o viceversa; la regla de negocio ("interna = privada, externa = visible") es fija y no debería ser una opción del formulario.
- **`FinishTicket` no permite reabrir** (elegido, `TICKET_ALREADY_FINISHED` bloquea todo re-`FinishTicket`) vs. permitir cambiar entre dos status finales distintos. Sin una spec de workflow que defina qué transiciones son válidas entre estados terminales, permitirlo sería inventar una política no pedida. Si negocio necesita "reabrir", es explícitamente una operación distinta (`ReopenTicket`) que amerita su propia spec y su propio gateo.
- **`TicketDtoAssembler` como refactor incluido en esta spec, no diferido** (elegido) vs. dejar la duplicación y agregar una tercera/cuarta copia. Con 4 handlers necesitando la misma proyección de ~40 líneas, el corte de "no es abstracción prematura" ya se cruzó; postergarlo habría dejado la duplicación en un estado peor que el actual (2 copias) en vez de mejor.
- **`GetTicketByIdQueryHandler` gana un `INNER JOIN` extra en el segundo `SELECT` en vez de resolver `CreatedByUserId`/`AssignedToUserId` en C# tras leer el primer `SELECT`** (elegido) vs. filtrar los logs en memoria después de traerlos todos. Filtrar en SQL evita traer por la red filas que el viewer no debe ver en absoluto — más allá de la elegancia, es la diferencia entre "el server nunca tuvo el dato en la respuesta" y "el server lo trajo y lo descartó", relevante para datos marcados explícitamente como privados.
- **`ReassignTicket` al mismo usuario no persiste** (elegido, ajustado durante la implementación) vs. el flujo original que pasaba igual por `UpdateAsync`/`SaveChangesAsync`. Con el flujo original el no-op devolvía `REASSIGN_FAILED` porque EF no afecta filas en una entidad sin cambios.
- **`AddTicketNoteDto.LogType` como `int`** (elegido, ajustado durante la implementación) vs. el enum `LogType`. Mantiene `JOIN.Application.DTO` libre de referencias a `JOIN.Domain`; el rango válido (2/3) lo garantiza el validator del comando.
- **`INVALID_LOG_TYPE` como defensa, no como contrato principal** (constatado durante la implementación): `ValidationBehavior` corre antes que el handler y lanza la excepción de validación, así que el cliente recibe `ValidationProblemDetails`. Se documenta así en `CURL_REQUESTS.md` en vez de forzar el código de negocio.
- **Sin invalidación de cache ni de `RoleManager`**: esta spec no toca roles ni permisos de plataforma, solo datos de `TicketUserCompany` ya gestionados por SPEC 34.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **Breaking change de contrato**: `UpdateTicketDto` pierde `AssignedToUserId`. Un frontend que todavía lo envíe no rompe (el campo desconocido se ignora en la deserialización), pero tampoco logra reasignar — puede interpretarse como un bug silencioso si el consumidor no migra a `PUT /Tickets/{id}/reassign`. | Documentar el cambio de contrato explícitamente en `CURL_REQUESTS.md` y en el changelog de la API. Es un cambio deliberado, no accidental — la spec lo señala en Scope, Decisions y Acceptance criteria. |
| **Empresas cuyo roster de `TicketUserCompany` quedó vacío** (ninguna fila, ver riesgos de SPEC 34) no pueden usar `ReassignTicket` ni `FinishTicket` en absoluto — todo actor recibe 403 porque nadie tiene ningún flag. | Comportamiento esperado y coherente con la meta ("acceso gateado por el roster"); mitigado en SPEC 34 con el seed de desarrollo. Para producción, sigue siendo tarea operativa poblar el roster antes de operar tickets. |
| **`TicketUserCompanyCapabilityResolver` usa `GetAllAsync()` en memoria**, igual que el resto del módulo — mismo patrón, mismo riesgo de escala ya aceptado en SPEC 34. | Sin cambios: es coherente con cómo ya opera todo el módulo de tickets. Optimización de query si el volumen lo exige, no un cambio de contrato. |
| **La corrección de visibilidad de logs es retroactiva**: logs `Reassignment`/`StatusChange`/`Creation` ya existentes en la base (creados antes de esta spec) tienen `IsOnlyForCreatedAndAssigned = false` porque `AddLog` siempre grabó `false` — no hay datos "privados" preexistentes que esta spec deba re-filtrar; el problema era 100% de lectura hacia adelante, no de datos corruptos hacia atrás. | No requiere migración de datos. Se documenta acá para que quede explícito por qué no hay un F de "backfill". |
| **`FinishTicket` y `ReassignTicket` no son atómicos entre sí** si dos requests concurrentes actúan sobre el mismo ticket (ej. alguien lo reasigna mientras otro lo finaliza). | Aceptado: mismo nivel de concurrencia optimista que ya tiene `UpdateTicket` hoy (sin locking explícito, `SaveChangesAsync` reporta 0 filas afectadas si hubo un conflicto real de EF). No se introduce un mecanismo de concurrencia nuevo en esta spec. |
| **Las validaciones de catálogo preexistentes no filtran por tenant** (defecto anterior a esta spec): `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` hacen `statusRepository.GetAsync(request.TicketStatusId)` (y lo mismo para complejidad, `TimeUnit`, persona, proyecto, área, ticket precedente) sin comparar `CompanyId` — se puede referenciar un catálogo de otra empresa. `FinishTicketCommandHandler` replica el patrón al cargar el `TicketStatus` destino. | No se corrige en esta spec para no ampliar el alcance, pero **sí** se corrige en SPEC 37 F5, donde la consecuencia deja de ser cosmética: ahí el `IsFinal` del status decide si el ticket puede cerrarse, y un `IsFinal` ajeno abriría un camino de cierre fuera del workflow de la empresa. Los handlers nuevos de esta spec (`FinishTicket`) quedan escritos de forma que agregar el filtro sea una sola condición más. |
| **Un `TicketStatus` marcado `IsFinal = true` podría no existir todavía para una empresa** que solo tiene el status "Open" sembrado, bloqueando `FinishTicket` por completo. | Es responsabilidad del catálogo `TicketStatusesController` (ya existente, fuera de esta spec) tener al menos un status terminal por empresa. No se agrega una validación cruzada nueva; el error `INVALID_TICKET_STATUS`/`TICKET_STATUS_NOT_FINAL` ya deja claro qué falta. |

---

## What is **not** in this spec

- Auto-asignación / self-service de tickets sin asignar.
- Reapertura de tickets finalizados.
- Workflow parametrizable de transiciones de status (SPEC 37).
- Notificaciones sobre reasignación, finalización o notas externas.
- Adjuntar documentos a un `TicketLog` (SPEC 36, que consume el `TicketLogsId` de los logs que esta spec crea).
- Edición o borrado de notas ya creadas.
- Endpoint paginado de logs independiente del detalle del ticket.
- Endpoint "mis capacidades" del usuario autenticado.
- Cualquier cambio a `TicketUserCompany`, su coordinador o su controller (permanecen como los dejó SPEC 34).

Cada uno, si llega, va en su propia spec.
