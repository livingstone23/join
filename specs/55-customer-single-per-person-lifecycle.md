# SPEC 55 — Clientes: uno por persona y empresa, solo activo o inactivo, historial de eventos y catálogo de etapas por empresa

> **Status:** Borrador
> **Depends on:** SPEC 38 (filtros globales), SPEC 39 (fork PostgreSQL), SPEC 40-post (índices únicos), SPEC 41-post (borrado y restauración, que esta spec retira para `Customers`), SPEC 43-post (permisos por ruta), SPEC 45-post (personas pendientes que se vinculan a un cliente). Rama de trabajo: `spec-55-customer-single-per-person-lifecycle`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-15
> **Historia (2026-10-15, decisión del usuario):** se agrega la regla 7 (persona inactiva → cliente inactivo; al reactivar la persona, el cliente **no** se reactiva solo) y se confirma la regla 6 (etapa independiente del estado). Borrar una persona que tiene cliente queda bloqueado para siempre (el bloqueo de SPEC 41-post ya existe y el cliente nunca se borra).
> **Historia (2026-10-15, decisión del usuario):** la etapa deja de ser el enum `PersonLifecycleStage`: pasa a un **catálogo por empresa** (`Admin.CustomerStages`, parte F) para que cada empresa defina sus etapas. `Customer.PersonLifecycleStage` se reemplaza por `Customer.CustomerStageId` y el enum se elimina. La regla 6 (etapa independiente del estado) se mantiene.
> **Origen:** sale de SPEC 54 (2026-10-15, decisión del usuario): la parte "un cliente por persona" de SPEC 54 creció a una regla de vida completa del cliente y se separa aquí. SPEC 54 depende de esta.
> **Objective:** Que un cliente exista **una sola vez** por persona y empresa, que nunca se borre ni se duplique, que solo cambie entre activo e inactivo, que cualquier evento pueda activarlo o confirmarlo como activo sin crear filas nuevas y que quede registrado qué evento lo hizo.

---

## Reglas de negocio (decisión del usuario, 2026-10-15)

1. **Un cliente por `(CompanyId, PersonId)`.** El `CustomerId` es la identidad permanente de esa persona como cliente de esa empresa. No existen dos filas para la misma persona en la misma empresa, ni vivas ni borradas.
2. **Solo dos estados: activo o inactivo** (`IsActive`, con `ActivatedAt` / `DeactivatedAt` del último cambio). Un cliente no se borra: se desactiva.
3. **Activación idempotente.** Uno o más eventos pueden activar o confirmar al cliente: si no existe, se crea activo; si está inactivo, se reactiva; si ya está activo, se confirma (sin cambiar `ActivatedAt`). Nunca se crea una segunda fila. Mismo `CustomerId` y mismo `CustomerCode` de por vida.
4. **Historial.** Cada creación, activación, confirmación y desactivación deja una fila en `Admin.CustomerStatusHistory` con el origen del evento.
5. **`UserId` = quien registró el cliente** (SPEC 54, Historia). No cambia al reactivar; quien hizo cada cambio queda en el historial.
6. **Etapa independiente del estado** (decisión del usuario). La etapa (`CustomerStageId`, catálogo por empresa, regla 8) es el embudo comercial; `IsActive` es el estado operativo. No se sincronizan: activar o desactivar nunca cambia la etapa, y pueden no coincidir (un cliente inactivo en la etapa "Cliente" es válido). La etapa se fija al crear (la que indique el evento o, si no indica, la etapa por defecto de la empresa) y se cambia con `PUT`.
7. **La persona manda sobre el cliente** (decisión del usuario):
   - Persona desactivada → su cliente se desactiva en la misma transacción (historial `Deactivated`, origen `PersonDeactivated`, `SourceReferenceId` = `PersonId`). Si ya estaba inactivo, no cambia nada.
   - Persona reactivada → el cliente **no** se reactiva solo: queda inactivo hasta que alguien lo active a mano o lo active un evento.
   - No se puede activar ni confirmar el cliente de una persona inactiva (`INVALID_PERSON`).
   - Una persona con cliente no se puede borrar (bloqueo de `PersonCascadeCoordinator`, SPEC 41-post); como el cliente nunca se borra, solo se puede desactivar.

8. **Etapas por empresa** (decisión del usuario). Cada empresa administra su propio catálogo de etapas (`Admin.CustomerStages`): crea, renombra, ordena, desactiva y define cuál es la etapa por defecto. Toda empresa tiene **exactamente una** etapa por defecto, activa. Una etapa inactiva no se puede asignar a clientes nuevos ni en un cambio de etapa, pero los clientes que ya la tienen la conservan. Una etapa en uso por algún cliente, o la etapa por defecto, no se puede borrar.

## Contexto (estado actual)

- La etapa hoy es el enum `PersonLifecycleStage` (`Lead = 1` "En contacto", `Prospect = 2` "En proceso", `Customer = 3` "cliente", `FormerCustomer = 4` "ClienteInactivo"), igual para todas las empresas. Solo lo usan `Customer`, su DTO, el filtro de `GET /Customers` y los comandos de alta y edición.
- `Customer` ya tiene `IsActive`, `ActivatedAt`, `DeactivatedAt`, `Reactivate()` y `Deactivate()` (idempotentes) y `PUT /Customers/{id}` cambia etapa y estado.
- Contradicen las reglas: el índice único `(CompanyId, PersonId, UserId)` con filtro `"gcrecord" = 0`; el alta, que solo rechaza `PersonId` + `UserId` repetidos (`CUSTOMER_LINK_EXISTS`) y toma `UserId` del cuerpo; `DELETE /Customers/{id}` (borrado lógico) y `POST /Customers/{id}/restore` (SPEC 41-post), que permiten borrar y volver a crear la misma persona como fila nueva.
- **Eventos que crean un cliente hoy: solo `POST /Customers`** (alta manual, `CreateCustomerCommandHandler`). Ningún otro flujo crea clientes: las personas pendientes (SPEC 45-post) solo se vinculan a un cliente que ya existe, y crear una persona o un ticket no crea cliente. No hay una operación que un evento pueda usar para activar un cliente; tampoco historial.
- Desactivar una persona (`PUT /Persons/{id}` con `IsActive = false`, `UpdatePersonCommandHandler`) no toca a su cliente; `Person.Deactivate()` solo deja un comentario sugiriendo un evento `PersonDeactivated`.
- El frontend solo lee `GET /Customers` y `GET /Customers/{id}` (vínculos del calendario, FE-22): retirar `DELETE`/`restore` y cambiar el alta no lo rompe.

## Scope

**In:**

**A. Unicidad**
1. Índice único `UX_Customers_Company_Person` sobre `(CompanyId, PersonId)` **sin filtro** (cubre también filas con `gcrecord <> 0`). Reemplaza al índice `(CompanyId, PersonId, UserId)`. `UX_Customers_Company_CustomerCode` se mantiene.
2. Migración `Spec55CustomerSinglePerPerson` (Npgsql, convención 7), en este orden:
   1. Si hay más de una fila por `(companyid, personid)` (sumando vivas y borradas), la migración **falla** con un mensaje que lista los pares; no fusiona ni borra datos. Se resuelven a mano antes de aplicarla.
   2. Las filas borradas (`gcrecord <> 0`) que quedan solas para su persona pasan a `gcrecord = 0`, `isactive = FALSE`, `deactivatedat = COALESCE(deactivatedat, NOW())`: un cliente "borrado" pasa a ser un cliente inactivo.
   3. Cambio de índice y creación de `admin.customerstatushistory` (punto C).
   4. Clientes activos cuya persona está inactiva pasan a `isactive = FALSE`, `deactivatedat = NOW()` (regla 7).
   5. Una fila de historial `Created` (origen `Migration`) por cliente existente, con `occurredat = activatedat` y `userid` = el `userid` del cliente; más una `Deactivated` (`occurredat = deactivatedat`, origen `Migration`) si está inactivo.

**B. Activación idempotente**
3. Servicio de aplicación `ICustomerActivationService` (`src/2.Application/Services/Admin/`):
   - `EnsureActiveAsync(companyId, personId, source, sourceReferenceId?, notes?, userId, customerStageId?, ct)` (la etapa solo se usa al crear; sin valor, la etapa por defecto de la empresa) → devuelve el cliente y si fue `Created`, `Activated` o `Confirmed`.
   - `DeactivateAsync(companyId, customerId, source, sourceReferenceId?, notes?, userId, ct)` → `Deactivated`, o sin cambios si ya estaba inactivo (no escribe historial en ese caso).
   - Valida que la persona exista, esté activa y no borrada en la empresa (`INVALID_PERSON`).
   - Concurrencia: `pg_advisory_xact_lock` sobre `(companyId, personId)` (convención 8) antes de leer, para que dos eventos simultáneos no intenten crear la misma fila; si aun así salta la `23505` de `UX_Customers_Company_Person`, se relee y se trata como `Confirmed`/`Activated`.
   - Corre dentro de la transacción del comando que lo usa (`ITransactionalCommand`).
4. `POST /Customers` (`CreateCustomerCommand`) pasa a usar `EnsureActiveAsync` con origen `Manual`:
   - Persona nueva como cliente → 201 con el cliente.
   - Persona que ya es cliente → **200** con el cliente existente (mismo `Id` y `CustomerCode`), reactivado si estaba inactivo. `Message` indica `Activated` o `Confirmed`.
   - `UserId` deja de venir en el cuerpo: es el usuario autenticado (`ICurrentUserService`) y solo se usa al crear. El campo `userId` del request se ignora si llega (compatibilidad) y se retira de la documentación.
   - Se elimina el código `CUSTOMER_LINK_EXISTS`.
5. `PUT /Customers/{id}` solo cambia `CustomerStageId` (etapa activa de la empresa; si no, `INVALID_CUSTOMER_STAGE`). `IsActive` sale del request (si llega, se ignora).
6. `POST /Customers/{id}/activate` y `POST /Customers/{id}/deactivate`, cuerpo opcional `{ "notes": "..." }` (máx. 500), origen `Manual`. Responden 200 con el cliente; si ya estaba en ese estado, 200 sin cambios (`activate` sobre uno activo registra `Confirmed`; `deactivate` sobre uno inactivo no registra nada). Mismo permiso de actualización que `PUT` (`[PermissionResource("Customers")]`).

**C. Historial**
7. Entidad `CustomerStatusHistory : BaseTenantEntity` → `Admin.CustomerStatusHistory`:

   | Columna | Tipo | Nota |
   |---|---|---|
   | `Id` | uuid | |
   | `CompanyId` | uuid | Tenant |
   | `CustomerId` | uuid | FK a `Customers`, `Restrict` |
   | `Action` | int | `CustomerStatusAction`: `Created = 1`, `Activated = 2`, `Confirmed = 3`, `Deactivated = 4` |
   | `Source` | int | `CustomerStatusSource`: `Manual = 1`, `Seed = 2`, `Migration = 3`, `PersonPendingConfirmation = 4`, `PersonDeactivated = 5` (se agregan valores con cada evento nuevo) |
   | `SourceReferenceId` | uuid? | Id del registro que originó el evento (p. ej. la `PersonPendingConfirmation`) |
   | `Notes` | varchar(500)? | |
   | `UserId` | uuid | FK a `Security.Users`: quien hizo el cambio |
   | `OccurredAt` | timestamp with time zone | UTC |

   Índice `IX_CustomerStatusHistory_Customer_OccurredAt` sobre `(CustomerId, OccurredAt DESC)`. Solo se inserta: sin `PUT`/`DELETE`.
8. `GET /Customers/{id}/status-history?pageNumber=&pageSize=` → `Response<PagedResult<CustomerStatusHistoryDto>>` ordenado por `OccurredAt` descendente, con `ActionName`, `SourceName` y `UserEmail`. Permiso de lectura de `Customers`.
9. `CustomerResponseDto` agrega `LastStatusAction`, `LastStatusSource` y `LastStatusAt` (última fila del historial) para mostrar el porqué del estado sin otra llamada.

**D. Sin borrado**
10. Se retiran `DELETE /Customers/{id}` (`DeleteCustomerCommand`) y `POST /Customers/{id}/restore` (`RestoreCustomerCommand`) con sus handlers, validadores y tests. Las rutas quedan sin acción (404/405). Las filas de `RoleSystemOptions` no cambian: el permiso `Delete` del recurso `Customers` queda sin uso.
11. `Customer` deja documentado en su XML que no se borra (el `GcRecord` heredado de `BaseTenantEntity` se mantiene en 0 siempre; los filtros globales no cambian).

**E. Eventos conectados en esta spec**
12. `LinkCustomerToPersonPendingConfirmationCommand` (SPEC 45-post) llama a `EnsureActiveAsync` sobre el cliente vinculado con origen `PersonPendingConfirmation` y `SourceReferenceId` = la pendiente: vincular confirma al cliente como activo (lo reactiva si estaba inactivo). *(Supuesto a confirmar por el usuario.)*
13. `UpdatePersonCommandHandler`: cuando la persona pasa de activa a inactiva, llama a `ICustomerActivationService.DeactivateAsync` sobre su cliente (si tiene) con origen `PersonDeactivated` (regla 7). Al pasar de inactiva a activa no hace nada con el cliente. Cualquier otro flujo que desactive personas usa el mismo servicio.
14. Otros eventos (alta de ticket, actividad de calendario, ingreso por canal…) **no** se conectan en esta spec: cada uno agrega su valor de `CustomerStatusSource` y su llamada en la spec que lo pida.

**F. Catálogo de etapas por empresa**
15. Entidad `CustomerStage : BaseTenantEntity` → `Admin.CustomerStages` (mismo patrón que `Gender` / `TicketStatus`):

    | Columna | Tipo | Nota |
    |---|---|---|
    | `Id` | uuid | |
    | `CompanyId` | uuid | Tenant |
    | `Code` | varchar(20) | Mayúsculas, sin espacios; único por empresa entre vivas (`UX_CustomerStages_Company_Code`, `"gcrecord" = 0`) |
    | `Name` | varchar(100) | Único por empresa entre vivas (`UX_CustomerStages_Company_Name`) |
    | `Description` | varchar(250)? | |
    | `SortOrder` | int | Orden en listas y en el embudo |
    | `IsDefault` | boolean | Una por empresa: `UX_CustomerStages_Company_Default` sobre `CompanyId` con `"isdefault" = TRUE AND "gcrecord" = 0` |
    | `IsActive` | boolean | |

16. `Customer.PersonLifecycleStage` → `Customer.CustomerStageId` (uuid, FK `Restrict` a `CustomerStages`, obligatoria) con navegación `CustomerStage`. `Customer.UpdateLifecycle` → `ChangeStage(Guid customerStageId)`. Se elimina el enum `PersonLifecycleStage` (`src/1.Domain/Enums/PersonLifecycleStage.cs`) y el código `INVALID_LIFECYCLE_STAGE`. Índice `IX_Customers_Company_Stage` reemplaza a `(CompanyId, PersonLifecycleStage)`.
17. **Etapas iniciales** de cada empresa (las mismas cuatro de hoy, ya como datos editables):

    | `Code` | `Name` | `SortOrder` | `IsDefault` | Valor anterior del enum |
    |---|---|---|---|---|
    | `LEAD` | En contacto | 1 | no | `Lead = 1` |
    | `PROSPECT` | En proceso | 2 | no | `Prospect = 2` |
    | `CUSTOMER` | Cliente | 3 | **sí** | `Customer = 3` |
    | `FORMER` | Cliente anterior | 4 | no | `FormerCustomer = 4` |

    Se crean: en la migración, para cada empresa existente; en `DatabaseSeeder` (`SeedCustomerStagesForCompanyAsync`, idempotente por `(CompanyId, Code)`) para `JOIN-001` y la empresa privada, antes de sembrar clientes; y en `ICompanyCatalogSeeder.SeedDefaultCatalogsForCompanyAsync`, para que toda empresa nueva nazca con su catálogo.
18. API `CustomerStagesController` (`[PermissionResource("CustomerStages")]`):
    - `GET /CustomerStages?includeInactive=&includeDeleted=` → lista de la empresa ordenada por `SortOrder`, `Name` (sin paginar: es un catálogo corto). `includeDeleted` solo SuperAdmin (SPEC 41-post).
    - `GET /CustomerStages/{id}`.
    - `POST /CustomerStages` y `PUT /CustomerStages/{id}`: `Code`, `Name`, `Description`, `SortOrder`, `IsDefault`, `IsActive`. Marcar `IsDefault = true` desmarca la anterior en la misma transacción. No se puede desmarcar la etapa por defecto sin marcar otra (`CUSTOMER_STAGE_DEFAULT_REQUIRED`) ni desactivarla (`CUSTOMER_STAGE_DEFAULT_INACTIVE`).
    - `DELETE /CustomerStages/{id}`: borrado lógico. Bloqueado si algún cliente la usa (`CUSTOMER_STAGE_IN_USE`, con la cantidad) o si es la etapa por defecto (`CUSTOMER_STAGE_DEFAULT_REQUIRED`).
    - `POST /CustomerStages/{id}/restore`: SuperAdmin, mismo patrón de SPEC 41-post (falla si choca con `Code`/`Name` vivos, o si al restaurarla habría dos por defecto: vuelve sin `IsDefault`).
    - `CustomerStageDto`: `Id`, `Code`, `Name`, `Description`, `SortOrder`, `IsDefault`, `IsActive`, `CustomersCount`, campos de `SoftDeletableDto`.
19. **Menú y permisos** (seed de `SeedMenuAndPermissionsAsync`, cambia el checksum): `SystemOption` `CustomerStages`, ruta `/customers/customer-stages`, módulo `Customers`, padre `CustomersMenu`, recurso `CustomerStages`, visible en el menú, con las cuatro acciones. `RoleSystemOptions`: SuperAdmin, Admin y Manager con las mismas banderas que tienen sobre `Customers`.
20. **Clientes con la etapa:** `CreateCustomerCommand.CustomerStageId` (opcional: sin valor, la etapa por defecto); `UpdateCustomerCommand.CustomerStageId` (obligatoria); ambos validan que la etapa sea de la empresa, viva y activa (`INVALID_CUSTOMER_STAGE`). `CustomerResponseDto`: `PersonLifecycleStage`/`PersonLifecycleStageName` → `CustomerStageId`, `CustomerStageCode`, `CustomerStageName`. `GET /Customers`: el filtro `personLifecycleStage` → `customerStageId`.
21. **Migración de datos** (dentro de `Spec55CustomerSinglePerPerson`, antes del paso de historial): crear `admin.customerstages`; insertar las cuatro etapas para cada empresa de `common.companies` (`gen_random_uuid()`, `NOW()`); agregar `customerstageid` nula; `UPDATE` de cada cliente según su `personlifecyclestage` (1→`LEAD`, 2→`PROSPECT`, 3→`CUSTOMER`, 4→`FORMER` de su empresa); `SET NOT NULL`; crear la FK y el índice; borrar `personlifecyclestage`.

**Out:**
- Cambios en `Ticket`, en el calendario o en la búsqueda (SPEC 54).
- Sincronizar la etapa con el estado (regla 6).
- Historial de cambios de etapa (el historial de esta spec es solo de estado).
- Etapas globales compartidas entre empresas: cada empresa tiene las suyas (regla 8).
- Reactivar el cliente al reactivar la persona (regla 7: nunca automático).
- UI del historial en el frontend.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_PERSON` | 400 | La persona no existe, está inactiva o borrada en la empresa (incluye `activate` del cliente de una persona inactiva) |
| `INVALID_CUSTOMER_STAGE` | 400 | La etapa no existe en la empresa, está borrada o inactiva |
| `CUSTOMER_STAGE_DEFAULT_REQUIRED` | 400 | Se intenta dejar la empresa sin etapa por defecto (desmarcar sin marcar otra, o borrar la etapa por defecto) |
| `CUSTOMER_STAGE_DEFAULT_INACTIVE` | 400 | Se intenta desactivar la etapa por defecto o marcar como defecto una inactiva |
| `CUSTOMER_STAGE_IN_USE` | 409 | Borrar una etapa que usa algún cliente |
| `CUSTOMER_STAGE_NOT_FOUND` | 404 | Etapa inexistente o de otra empresa |
| `CUSTOMER_NOT_FOUND` | 404 | `activate` / `deactivate` / `status-history` / `PUT` de un cliente de otra empresa o inexistente |
| `COMPANY_REQUIRED` / `USER_REQUIRED` | 401 | El token no trae empresa o usuario |

## Criterios de aceptación

1. Dos `POST /Customers` de la misma persona (aunque los hagan usuarios distintos) dejan **una** fila: el primero responde 201 (`Created`), el segundo 200 (`Confirmed`) con el mismo `Id` y `CustomerCode`.
2. `POST /Customers` de una persona con cliente inactivo lo reactiva (200, `Activated`), sin fila nueva y con el mismo `CustomerCode`.
3. Diez llamadas concurrentes a `EnsureActiveAsync` para la misma persona dejan una sola fila de cliente, una fila `Created` y nueve `Confirmed` en el historial.
4. `deactivate` y luego `activate` dejan `IsActive` como corresponde, actualizan `DeactivatedAt` / `ActivatedAt` y registran `Deactivated` y `Activated`.
5. `DELETE /Customers/{id}` y `POST /Customers/{id}/restore` ya no existen.
6. `GET /Customers/{id}/status-history` lista los eventos en orden descendente con origen y usuario.
7. Vincular una persona pendiente a un cliente inactivo lo reactiva y registra `Activated` con origen `PersonPendingConfirmation`.
8. La migración, sobre una base con un cliente borrado y sin duplicados, lo deja inactivo y vivo; con duplicados `(companyid, personid)`, falla sin tocar datos.
9. `INSERT` directo de una segunda fila `(companyid, personid)` en la base falla con `23505`.
10. Desactivar una persona con cliente activo deja al cliente inactivo con historial `Deactivated` / `PersonDeactivated`; reactivar la persona deja al cliente inactivo; `activate` del cliente mientras la persona está inactiva responde 400 `INVALID_PERSON`.
11. La migración desactiva los clientes activos cuya persona está inactiva.
12. Tras la migración, cada empresa existente tiene las cuatro etapas iniciales, cada cliente conserva su etapa equivalente y la columna `personlifecyclestage` ya no existe.
13. Una empresa creada con `POST /Companies` nace con las cuatro etapas, `CUSTOMER` por defecto.
14. Dos empresas pueden tener etapas con el mismo `Code` o `Name`; dentro de una empresa, no (409 `DUPLICATE_KEY`).
15. Marcar otra etapa como defecto desmarca la anterior; nunca hay dos ni ninguna por empresa.
16. `POST /Customers` sin `customerStageId` asigna la etapa por defecto; con una etapa inactiva o de otra empresa responde 400 `INVALID_CUSTOMER_STAGE`; desactivar una etapa no cambia a los clientes que la tienen.
17. Borrar una etapa en uso responde 409 `CUSTOMER_STAGE_IN_USE`; borrar una sin uso y no por defecto funciona, y el SuperAdmin la puede restaurar.
18. La opción `/customers/customer-stages` aparece en el menú de Clientes para SuperAdmin, Admin y Manager.
19. Tests de integración (`CustomWebApplicationFactory`, `postgres:17`) para 1–8, 10, 11 y 12–17; `EXPLAIN` del SQL nuevo (convención 10).

## Decisiones

- **Sin borrado** (decisión del usuario): un cliente es la relación permanente entre una persona y la empresa; lo que cambia es su estado. Así el índice puede cubrir todas las filas y la regla "nunca dos veces" la garantiza la base, no solo el código.
- **Alta idempotente** (decisión del usuario): el alta manual y los eventos se comportan igual; no hay que saber si ya existía para "activar".
- **Historial en tabla propia** (decisión del usuario): responde por qué un cliente está activo o inactivo y qué eventos lo confirmaron; `ActivatedAt`/`DeactivatedAt` siguen como resumen del último cambio.
- **`UserId` del token en el alta:** si `UserId` es quien registra, no puede venir del cuerpo (se podría registrar a nombre de otro).
- **El esquema no cambia** (`Admin`), por lo mismo que en SPEC 54.
- **Persona → cliente en una sola dirección** (decisión del usuario): desactivar la persona desactiva al cliente, pero reactivarla no lo reactiva, porque el cliente pudo estar inactivo por otra razón. El historial dice por qué está inactivo.
- **Etapa y estado separados** (decisión del usuario): la etapa sigue el embudo comercial (puede incluir etapas como "En contacto" o "En proceso", que todavía no compran); el estado dice si la relación está vigente.
- **Catálogo por empresa en vez de enum** (decisión del usuario): cada empresa tiene su propio embudo. Las cuatro etapas iniciales conservan el significado del enum para no perder datos, y desde ahí cada empresa las edita.
- **Etapa por defecto obligatoria:** permite que el alta idempotente y los eventos creen clientes sin conocer el catálogo de la empresa.
- **Etapas con borrado lógico** (a diferencia del cliente): son un catálogo como `Gender` o `TicketStatus`; el bloqueo por uso evita dejar clientes sin etapa.

## Riesgos

- Bases de desarrollo con clientes duplicados: la migración falla a propósito y hay que resolverlos a mano.
- Clientes que estaban borrados reaparecen como inactivos en `GET /Customers`: es lo que pide la regla, pero cambia lo que ve el usuario.
