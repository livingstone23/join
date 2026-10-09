# SPEC 42-post — Valores iniciales del ticket desde `TicketCompanyDefaults`, con estado inicial editable

> **Status:** Borrador
> **Origen:** copia de SPEC 42 de `main` (estado allí al copiarla: Aprobado (implementado en rama `spec-42-ticket-create-default-status`)), adaptada a PostgreSQL para el fork `main_postgresql`. Se implementa **desde cero** en el fork, sin traer commits de `main` (decisión del usuario, 2026-10-09).
> **Rama de trabajo:** `spec-42-post-ticket-create-default-status`, creada desde `main_postgresql`; PR hacia `main_postgresql`, nunca hacia `main`.
> **Lectura en el fork:** dentro de esta spec, toda referencia a una spec de la serie copiada (40–49, 51, 99) se lee como su versión `-post` ("SPEC 41" = SPEC 41-post), y aplican las convenciones de `specs/README.md` → "Serie `-post`". Donde el texto copiado de `main` y la sección "Adaptación a PostgreSQL" difieren, prevalece esta última.
> **Depends on:** SPEC 41-post (bloqueo de borrado con `ActiveDependentsCheck`, que cubre la parte D). Relacionada con SPEC 35 (lifecycle), SPEC 47-post (ingesta por canal, `Pospuesto`), SPEC 37 (estados finales solo vía `FinishTicket`) y con `join_frontb/specs/17-tickets-nuevo.md` y `19-tickets-configuracion.md`.
> **Date:** 2026-10-09 (copia `-post`; original: 2026-09-28)
> **Objective:** Que al crear un ticket el estado sea opcional en el request: si el cliente lo envía, el backend valida que sea un estado activo, no final y de la misma empresa; si no lo envía, aplica `Messaging.TicketCompanyDefaults.TicketStatusDefaultId`.

---

## Adaptación a PostgreSQL (fork `main_postgresql`)

Esta spec no tiene SQL crudo, migraciones ni `HasFilter`: el resolvedor, el handler y las validaciones usan repositorios EF, así que el código es el mismo que en `main`. Lo que cambia en el fork:

| Tema | En `main` | En esta versión |
|---|---|---|
| F0 — verificación en QA | Consulta a `join_db_qa` (SQL Server) | **No aplica:** el fork todavía no tiene base de QA (SPEC 50 D). |
| Parte D (bloqueo de borrado del estado inicial) | Ya la cubría SPEC 41 | La cubre SPEC 41-post, que se implementa antes. |
| Tests de integración | `CreateTicketInitialStatusTests` sobre `CustomWebApplicationFactory` | `tests/IntegrationTests/Messaging/CreateTicketInitialStatusPostgreSqlTests.cs` sobre `PostgreSqlWebApplicationFactory`: los 5 escenarios de F5 más `POST /TicketCompanyDefaults` con estado final → `TICKET_STATUS_DEFAULT_INVALID`. |

Verificación con Docker (convención 12 del README): con la API contra `postgres:17` y la seed de desarrollo, `POST /Tickets` sin `ticketStatusId` → 201 con el estado inicial de `JOIN-001`; con un estado final → 400 `INVALID_TICKET_STATUS`.

---

## Por qué existe esta spec

Surgió al diseñar la pantalla "Nuevo ticket" del front (`join_frontb/SpecPropuestaTickets/PropuestaTickets_JOIN.html`).

**Decisión del 2026-09-28 (revisada el mismo día):** los valores de `Messaging.TicketCompanyDefaults` son **propuestas**. Al abrir el formulario de alta, estado, complejidad, unidad de tiempo, canal, proyecto y área vienen precargados con el default de la empresa, y el usuario que crea el ticket puede cambiar cualquiera de ellos. Por ejemplo, si el default de complejidad es "Media", puede crear el ticket como "Alta" o "Baja". Con el estado pasa lo mismo: se presenta en un select con los estados activos de la empresa, precargado con el estado inicial configurado.

Hoy el backend no soporta bien esta regla:

- `CreateTicketDto`/`CreateTicketCommand` exigen `TicketStatusId` (`CreateTicketCommandValidator`: `NotEqual(Guid.Empty)`). Un cliente que no lo envía (por ejemplo, la ingesta por WhatsApp o correo de SPEC 47, etapa posterior) no puede crear un ticket sin buscar el default por su cuenta.
- `CreateTicketCommandHandler` valida el estado con `statusRepository.GetAsync(request.TicketStatusId)` **sin filtrar por `CompanyId`, `IsActive` ni `IsFinal`**. Un cliente puede crear un ticket directamente en un estado final (sin log de finalización ni SLA confiable, SPEC 37), en un estado inactivo o en un estado de otra empresa.
- `TicketCompanyDefault.TicketStatusDefaultId` existe desde `InitialReset` con el comentario *"Default status to be assigned to new tickets if not specified"*, y ningún handler lo lee.

---

## Scope

**In:**

### A. Contrato de creación

- `CreateTicketDto` (`src/2.Application.DTO/Messaging/Tickets/`) y `CreateTicketCommand`: `TicketStatusId` pasa de `Guid` a **`Guid?`** (opcional).
- `CreateTicketCommandValidator`: la regla `NotEqual(Guid.Empty)` se reemplaza por `Must(v => !v.HasValue || v.Value != Guid.Empty)` con el mensaje "Ticket status id is invalid." (mismo patrón que `AssignedToUserId`, `PersonId`, etc.).
- `TicketsController.Create`: sin cambios de firma; el mapeo copia el campo opcional.
- El resto de los campos (complejidad, unidad de tiempo, canal, proyecto, área) **no cambia**: siguen siendo obligatorios u opcionales como hoy. El front los precarga desde `TicketCompanyDefaults` y el usuario puede cambiarlos.

### B. `TicketInitialStatusResolver`

- `src/2.Application/UseCases/Messaging/Tickets/TicketInitialStatusResolver.cs` (nuevo): clase DI (constructor primario, `IUnitOfWork`), mismo patrón que los coordinadores feature-local (`PersonAddressDefaultCoordinator`, `TicketStatusTransitionGuard` de SPEC 37).
- `Task<Response<Guid>> ResolveAsync(Guid companyId, Guid? requestedStatusId, CancellationToken ct)`:
  - **Con `requestedStatusId`:** carga ese estado. No existe, está borrado, `IsActive == false`, `CompanyId != companyId` o `IsFinal == true` → `Response.Error("INVALID_TICKET_STATUS")` (código que ya existe). Si es válido, lo devuelve.
  - **Sin `requestedStatusId`:** carga la fila activa de `TicketCompanyDefault` de la empresa. Sin fila, o con `TicketStatusDefaultId == null` → `Response.Error("TICKET_DEFAULT_STATUS_NOT_CONFIGURED")`. Si el estado configurado no existe, está borrado o inactivo, es de otra empresa o es final → `Response.Error("TICKET_DEFAULT_STATUS_INVALID")`. Si es válido, lo devuelve.
- Registrado `Scoped` en `2.Application/Common/ConfigureServices.cs`.
- **Las reglas de transición de SPEC 37 no aplican al crear.** Regulan cambios desde un estado de origen, y al crear no hay origen: cualquier estado activo y no final de la empresa es válido.

### C. `CreateTicketCommandHandler`

- Llama a `TicketInitialStatusResolver.ResolveAsync(currentUserService.CompanyId, request.TicketStatusId, ct)` después del guard de `COMPANY_REQUIRED`/`INVALID_COMPANY` y antes de las demás validaciones de FK. Si falla, devuelve ese error tal cual.
- Asigna `entity.TicketStatusId` con el valor resuelto.
- Se elimina la validación actual `INVALID_TICKET_STATUS` del handler (sin filtro de empresa, activo ni final): la reemplaza el resolvedor.
- El log `Creation` registra el estado resuelto como `NewStatusName`, igual que hoy.

### D. Configuración de defaults

- `CreateTicketCompanyDefaultCommandHandler` y `UpdateTicketCompanyDefaultCommandHandler`: si `TicketStatusDefaultId` tiene valor, validar que el estado exista, esté activo, sea de la misma empresa y **no** sea final → si no, `TICKET_STATUS_DEFAULT_INVALID`. Así la configuración nunca apunta a un estado que el resolvedor rechazaría.
- `DeleteTicketStatus` (existente): hoy devuelve `TICKET_STATUS_IN_USE` si hay tickets que lo usan. Se agrega el mismo bloqueo si el estado es el `TicketStatusDefaultId` de la empresa, con el detalle `"Es el estado inicial configurado en Valores por defecto"` en `errors` (regla de SPEC 41: no se borra un registro con dependencias activas).

### E. Ingesta por canal (SPEC 47, etapa posterior)

- `RegisterInboundTicketCommandHandler` (todavía no implementado) llama a `TicketInitialStatusResolver.ResolveAsync(companyId, null, ct)`: los tickets que entran por WhatsApp o correo quedan siempre en el estado inicial configurado. Se deja la nota en SPEC 99 al aprobar esta spec.

### F. Seed

- `SeedTicketCompanyDefaultsAsync` (`DatabaseSeeder.cs:1626`) ya siembra la fila de la empresa JOIN de desarrollo. Verificar que asigne `TicketStatusDefaultId` al estado con `IsInitial = true`; si no lo hace, completarlo.

### G. Documentación

- `CURL_REQUESTS.md` / `POSTMAN_CURL.txt`: marcar `ticketStatusId` como opcional en el ejemplo de `POST /Tickets`, agregar un ejemplo sin el campo y documentar los códigos nuevos.

**Out of scope:**

- Aplicar en el servidor los demás defaults (complejidad, unidad de tiempo, canal, proyecto, área) cuando el cliente no los envía. Hoy complejidad, unidad y canal son obligatorios en el request; si se quiere que el backend complete los que falten (útil para SPEC 99), es otra spec.
- Restringir por permiso quién puede elegir un estado distinto del inicial al crear.
- Cambiar `UpdateTicketDto`: el cambio de estado de un ticket existente sigue las reglas de SPEC 37.
- Formato del código (`UsePersonalizedCode`/`StartCode`/`CodeSequenceLength`): no cambia.

---

## Data model

Sin cambios de schema. Cambia el contrato de alta y se agrega el resolvedor:

```csharp
// src/2.Application.DTO/Messaging/Tickets/CreateTicketDto.cs  (y CreateTicketCommand)
public Guid? TicketStatusId { get; init; }   // antes: Guid, obligatorio

// src/2.Application/UseCases/Messaging/Tickets/TicketInitialStatusResolver.cs
public sealed class TicketInitialStatusResolver(IUnitOfWork unitOfWork)
{
    public async Task<Response<Guid>> ResolveAsync(Guid companyId, Guid? requestedStatusId, CancellationToken cancellationToken)
    {
        if (requestedStatusId is { } requested)
        {
            var chosen = await unitOfWork.GetRepository<TicketStatus>().GetAsync(requested);
            return IsUsableInitialStatus(chosen, companyId)
                ? Ok(chosen!.Id)
                : Response<Guid>.Error("INVALID_TICKET_STATUS",
                    ["The ticket status must be active, not final and belong to the current company."]);
        }

        var defaults = (await unitOfWork.GetRepository<TicketCompanyDefault>().GetAllAsync())
            .FirstOrDefault(d => d.CompanyId == companyId && d.GcRecord == 0);

        if (defaults?.TicketStatusDefaultId is not { } defaultId)
            return Response<Guid>.Error("TICKET_DEFAULT_STATUS_NOT_CONFIGURED",
                ["The company has no initial ticket status configured in TicketCompanyDefaults."]);

        var configured = await unitOfWork.GetRepository<TicketStatus>().GetAsync(defaultId);
        return IsUsableInitialStatus(configured, companyId)
            ? Ok(configured!.Id)
            : Response<Guid>.Error("TICKET_DEFAULT_STATUS_INVALID",
                ["The configured initial ticket status is missing, inactive, final or belongs to another company."]);
    }

    private static bool IsUsableInitialStatus(TicketStatus? status, Guid companyId) =>
        status is not null && status.GcRecord == 0 && status.IsActive && status.CompanyId == companyId && !status.IsFinal;

    private static Response<Guid> Ok(Guid id) => new() { IsSuccess = true, Data = id };
}
```

Con SPEC 38 aplicada, los filtros globales ya limitan estas lecturas a la empresa del token; el chequeo explícito de `CompanyId` se mantiene como defensa, igual que en el resto de handlers.

| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_TICKET_STATUS` | 400 | El cliente envió un estado inexistente, borrado, inactivo, final o de otra empresa (código existente, regla más estricta) |
| `TICKET_DEFAULT_STATUS_NOT_CONFIGURED` | 400 | El cliente no envió estado y la empresa no tiene estado inicial configurado |
| `TICKET_DEFAULT_STATUS_INVALID` | 400 | El cliente no envió estado y el configurado ya no es usable |
| `TICKET_STATUS_DEFAULT_INVALID` | 400 | Al guardar defaults con un estado que no cumple las reglas |

---

## Implementation plan

### F0 — Verificación previa

No aplica en el fork: no hay base de QA PostgreSQL todavía (SPEC 50). Cuando exista, la consulta equivalente es `SELECT d.companyid FROM messaging.ticketcompanydefaults d LEFT JOIN messaging.ticketstatuses s ON s.id = d.ticketstatusdefaultid AND s.companyid = d.companyid AND s.isactive = TRUE AND s.isfinal = FALSE AND s.gcrecord = 0 WHERE d.gcrecord = 0 AND s.id IS NULL;`.

### F1 — Resolvedor

1. Crear `TicketInitialStatusResolver` y registrarlo.
2. Unit tests (gate 90%):
   - Con estado enviado: válido; inexistente; borrado; inactivo; final; de otra empresa.
   - Sin estado enviado: sin fila de defaults; fila sin estado; estado configurado inexistente, borrado, inactivo, final o de otra empresa; válido.

### F2 — Alta

1. `TicketStatusId` opcional en `CreateTicketDto`, `CreateTicketCommand` y el validador.
2. Usar el resolvedor en `CreateTicketCommandHandler` y quitar la validación vieja.
3. Actualizar `CreateTicketCommandHandlerTests` y `CreateTicketCommandValidatorTests`: casos con estado enviado (válido y cada motivo de rechazo) y sin estado (default válido, sin configurar, configurado inválido).

### F3 — Configuración

1. Validación `TICKET_STATUS_DEFAULT_INVALID` en create/update de `TicketCompanyDefault`.
2. Bloqueo de borrado del estado configurado como inicial.
3. Tests de ambos.

### F4 — Seed y documentación

1. Verificar/completar `SeedTicketCompanyDefaultsAsync`.
2. `CURL_REQUESTS.md` / `POSTMAN_CURL.txt`.

### F5 — Verificación

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. Gate de cobertura de `JOIN.Application.UnitTest` ≥ 90%.
3. Integración (`tests/IntegrationTests`, clase `CreateTicketInitialStatusPostgreSqlTests`, filtro `~PostgreSql`):
   - `POST /Tickets` sin `ticketStatusId` → 201 con el estado inicial configurado.
   - `POST /Tickets` con otro estado activo no final de la empresa → 201 con ese estado.
   - `POST /Tickets` con el estado final → 400 `INVALID_TICKET_STATUS`.
   - `POST /Tickets` con un estado de otra empresa → 400 `INVALID_TICKET_STATUS`.
   - Fila de defaults sin estado y request sin estado → 400 `TICKET_DEFAULT_STATUS_NOT_CONFIGURED`.

---

## Acceptance criteria

- [ ] `TicketStatusId` es opcional en `CreateTicketDto` y `CreateTicketCommand`.
- [ ] Un ticket creado sin estado queda en el `TicketStatusDefaultId` de su empresa.
- [ ] Un ticket creado con un estado activo, no final y de la misma empresa queda en ese estado, aunque no sea el inicial configurado.
- [ ] Un estado final, inactivo, borrado o de otra empresa enviado por el cliente responde 400 `INVALID_TICKET_STATUS`.
- [ ] Sin estado enviado ni estado inicial configurado, el alta responde 400 `TICKET_DEFAULT_STATUS_NOT_CONFIGURED`; con un estado configurado no usable, 400 `TICKET_DEFAULT_STATUS_INVALID`.
- [ ] Las reglas de transición de SPEC 37 no intervienen en el alta.
- [ ] No se puede guardar un `TicketStatusDefaultId` que no cumpla las reglas (`TICKET_STATUS_DEFAULT_INVALID`).
- [ ] No se puede borrar el estado configurado como inicial.
- [ ] El seed de desarrollo deja la empresa JOIN con estado inicial configurado.
- [ ] Gate de cobertura ≥ 90% y suite de integración PostgreSQL (`~PostgreSql`) en verde.

---

## Decisions

- **Los defaults de `TicketCompanyDefaults` son propuestas editables, incluido el estado** (elegido el 2026-09-28; reemplaza la versión anterior de esta spec, que fijaba el estado en el servidor). El usuario que crea el ticket conoce el caso y puede empezar, por ejemplo, directamente en "En progreso".
- **Estado opcional en el request, con fallback al default** (elegido) en vez de obligatorio. La ingesta por canal (SPEC 47) y cualquier cliente sin pantalla crean tickets sin conocer los estados de la empresa.
- **Solo estados activos y no finales al crear** (elegido). Un ticket solo se cierra con `FinishTicket` (SPEC 37), que deja el log de finalización del que depende el SLA. Crear un ticket ya cerrado rompería esa garantía.
- **Las transiciones no aplican al crear** (elegido). Regulan el paso de un estado de origen a otro; al crear no hay origen. Aplicarlas obligaría a tratar el estado inicial configurado como origen implícito, algo que SPEC 37 no define.
- **Reutilizar `INVALID_TICKET_STATUS` para el estado enviado** (elegido) en vez de un código nuevo. El front ya lo mapea y el significado es el mismo; solo se endurece la regla.
- **Resolvedor compartido en vez de lógica en el handler** (elegido). SPEC 47 crea tickets por otra vía y debe aplicar exactamente las mismas reglas.
- **Descartado:** quitar `TicketStatusId` del DTO y forzar siempre el default (versión anterior de esta spec). No permite que el usuario elija el estado al crear.

---

## Lecciones de la implementación en `main` (2026-10-08, referencia)

En `main` esta spec ya se implementó; estas notas evitan repetir los mismos tropiezos al escribirla de nuevo en el fork:

- **D — bloqueo de borrado:** lo cubre SPEC 41-post (`DeleteTicketStatusCommandHandler` + `ActiveDependentsCheck`, `TICKET_STATUS_IN_USE` con `"Active ticket company defaults: 1"`). Con 41-post implementada antes (orden del README) no hay código nuevo en esa parte; el detalle conserva ese formato, no el texto en español de la sección D.
- **D — validación de defaults:** create/update de `TicketCompanyDefault` reutilizan `TicketInitialStatusResolver.IsUsableInitialStatus`; cualquier fallo del estado (inexistente incluido) responde `TICKET_STATUS_DEFAULT_INVALID`.
- **Mapper:** `TicketMapper.ToEntity(CreateTicketCommand)` ignora `TicketStatusId` (origen y destino); el handler asigna el estado resuelto **antes** de `AddLog`, así el log `Creation` guarda el estado resuelto.
- **F — seed:** `SeedTicketCompanyDefaultsAsync` ya usa el estado `IsInitial`; se endurece el filtro a `IsActive && !IsFinal`.
- **G:** `POSTMAN_CURL.txt` no existe; solo `CURL_REQUESTS.md`.
- **Tests:** `TicketInitialStatusResolverTests` (todos los motivos de rechazo, con y sin estado enviado), casos nuevos en `CreateTicketCommandHandlerTests`, `CreateTicketCommandValidatorTests` y en los handlers de `TicketCompanyDefaults`; integración con los 5 escenarios de F5 más `TICKET_STATUS_DEFAULT_INVALID`.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| Clientes existentes que envían un estado final o de otra empresa empiezan a recibir 400. | Es el objetivo: hoy esos tickets quedan en estados inconsistentes. Se documenta en `CURL_REQUESTS.md`. |
| Empresas sin estado inicial configurado. | Solo fallan los altas que no envían estado. F0 las detecta; el front deshabilita el alta y muestra un aviso con enlace a Configuración (`join_frontb/specs/17-tickets-nuevo.md`). |
| Se borra o desactiva el estado inicial después de configurarlo. | El borrado queda bloqueado (D); desactivarlo hace que los altas sin estado respondan `TICKET_DEFAULT_STATUS_INVALID`. |
