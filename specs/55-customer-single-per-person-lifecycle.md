# SPEC 55 — Clientes: uno por persona y empresa, solo activo o inactivo, con historial de eventos

> **Status:** Borrador
> **Depends on:** SPEC 38 (filtros globales), SPEC 39 (fork PostgreSQL), SPEC 40-post (índices únicos), SPEC 41-post (borrado y restauración, que esta spec retira para `Customers`), SPEC 43-post (permisos por ruta), SPEC 45-post (personas pendientes que se vinculan a un cliente). Rama de trabajo: `spec-55-customer-single-per-person-lifecycle`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-15
> **Origen:** sale de SPEC 54 (2026-10-15, decisión del usuario): la parte "un cliente por persona" de SPEC 54 creció a una regla de vida completa del cliente y se separa aquí. SPEC 54 depende de esta.
> **Objective:** Que un cliente exista **una sola vez** por persona y empresa, que nunca se borre ni se duplique, que solo cambie entre activo e inactivo, que cualquier evento pueda activarlo o confirmarlo como activo sin crear filas nuevas y que quede registrado qué evento lo hizo.

---

## Reglas de negocio (decisión del usuario, 2026-10-15)

1. **Un cliente por `(CompanyId, PersonId)`.** El `CustomerId` es la identidad permanente de esa persona como cliente de esa empresa. No existen dos filas para la misma persona en la misma empresa, ni vivas ni borradas.
2. **Solo dos estados: activo o inactivo** (`IsActive`, con `ActivatedAt` / `DeactivatedAt` del último cambio). Un cliente no se borra: se desactiva.
3. **Activación idempotente.** Uno o más eventos pueden activar o confirmar al cliente: si no existe, se crea activo; si está inactivo, se reactiva; si ya está activo, se confirma (sin cambiar `ActivatedAt`). Nunca se crea una segunda fila. Mismo `CustomerId` y mismo `CustomerCode` de por vida.
4. **Historial.** Cada creación, activación, confirmación y desactivación deja una fila en `Admin.CustomerStatusHistory` con el origen del evento.
5. **`UserId` = quien registró el cliente** (SPEC 54, Historia). No cambia al reactivar; quien hizo cada cambio queda en el historial.
6. **Etapa independiente del estado.** `PersonLifecycleStage` (Lead, Prospect, Customer, FormerCustomer) es la etapa comercial y no se sincroniza sola con `IsActive`: desactivar no pone `FormerCustomer` ni activar pone `Customer`. Solo se fija al crear (valor del evento o `Customer` por defecto) y se cambia con `PUT`. *(Supuesto a confirmar por el usuario.)*

## Contexto (estado actual)

- `Customer` ya tiene `IsActive`, `ActivatedAt`, `DeactivatedAt`, `Reactivate()` y `Deactivate()` (idempotentes) y `PUT /Customers/{id}` cambia etapa y estado.
- Contradicen las reglas: el índice único `(CompanyId, PersonId, UserId)` con filtro `"gcrecord" = 0`; el alta, que solo rechaza `PersonId` + `UserId` repetidos (`CUSTOMER_LINK_EXISTS`) y toma `UserId` del cuerpo; `DELETE /Customers/{id}` (borrado lógico) y `POST /Customers/{id}/restore` (SPEC 41-post), que permiten borrar y volver a crear la misma persona como fila nueva.
- No hay una operación que un evento pueda usar para activar un cliente; tampoco historial.
- El frontend solo lee `GET /Customers` y `GET /Customers/{id}` (vínculos del calendario, FE-22): retirar `DELETE`/`restore` y cambiar el alta no lo rompe.

## Scope

**In:**

**A. Unicidad**
1. Índice único `UX_Customers_Company_Person` sobre `(CompanyId, PersonId)` **sin filtro** (cubre también filas con `gcrecord <> 0`). Reemplaza al índice `(CompanyId, PersonId, UserId)`. `UX_Customers_Company_CustomerCode` se mantiene.
2. Migración `Spec55CustomerSinglePerPerson` (Npgsql, convención 7), en este orden:
   1. Si hay más de una fila por `(companyid, personid)` (sumando vivas y borradas), la migración **falla** con un mensaje que lista los pares; no fusiona ni borra datos. Se resuelven a mano antes de aplicarla.
   2. Las filas borradas (`gcrecord <> 0`) que quedan solas para su persona pasan a `gcrecord = 0`, `isactive = FALSE`, `deactivatedat = COALESCE(deactivatedat, NOW())`: un cliente "borrado" pasa a ser un cliente inactivo.
   3. Cambio de índice y creación de `admin.customerstatushistory` (punto C).
   4. Una fila de historial `Created` (origen `Migration`) por cliente existente, con `occurredat = activatedat` y `userid` = el `userid` del cliente; más una `Deactivated` (`occurredat = deactivatedat`) si está inactivo.

**B. Activación idempotente**
3. Servicio de aplicación `ICustomerActivationService` (`src/2.Application/Services/Admin/`):
   - `EnsureActiveAsync(companyId, personId, source, sourceReferenceId?, notes?, userId, lifecycleStage?, ct)` → devuelve el cliente y si fue `Created`, `Activated` o `Confirmed`.
   - `DeactivateAsync(companyId, customerId, source, sourceReferenceId?, notes?, userId, ct)` → `Deactivated`, o sin cambios si ya estaba inactivo (no escribe historial en ese caso).
   - Valida que la persona exista, esté activa y no borrada en la empresa (`INVALID_PERSON`).
   - Concurrencia: `pg_advisory_xact_lock` sobre `(companyId, personId)` (convención 8) antes de leer, para que dos eventos simultáneos no intenten crear la misma fila; si aun así salta la `23505` de `UX_Customers_Company_Person`, se relee y se trata como `Confirmed`/`Activated`.
   - Corre dentro de la transacción del comando que lo usa (`ITransactionalCommand`).
4. `POST /Customers` (`CreateCustomerCommand`) pasa a usar `EnsureActiveAsync` con origen `Manual`:
   - Persona nueva como cliente → 201 con el cliente.
   - Persona que ya es cliente → **200** con el cliente existente (mismo `Id` y `CustomerCode`), reactivado si estaba inactivo. `Message` indica `Activated` o `Confirmed`.
   - `UserId` deja de venir en el cuerpo: es el usuario autenticado (`ICurrentUserService`) y solo se usa al crear. El campo `userId` del request se ignora si llega (compatibilidad) y se retira de la documentación.
   - Se elimina el código `CUSTOMER_LINK_EXISTS`.
5. `PUT /Customers/{id}` solo cambia `PersonLifecycleStage`. `IsActive` sale del request (si llega, se ignora).
6. `POST /Customers/{id}/activate` y `POST /Customers/{id}/deactivate`, cuerpo opcional `{ "notes": "..." }` (máx. 500), origen `Manual`. Responden 200 con el cliente; si ya estaba en ese estado, 200 sin cambios (`activate` sobre uno activo registra `Confirmed`; `deactivate` sobre uno inactivo no registra nada). Mismo permiso de actualización que `PUT` (`[PermissionResource("Customers")]`).

**C. Historial**
7. Entidad `CustomerStatusHistory : BaseTenantEntity` → `Admin.CustomerStatusHistory`:

   | Columna | Tipo | Nota |
   |---|---|---|
   | `Id` | uuid | |
   | `CompanyId` | uuid | Tenant |
   | `CustomerId` | uuid | FK a `Customers`, `Restrict` |
   | `Action` | int | `CustomerStatusAction`: `Created = 1`, `Activated = 2`, `Confirmed = 3`, `Deactivated = 4` |
   | `Source` | int | `CustomerStatusSource`: `Manual = 1`, `Seed = 2`, `Migration = 3`, `PersonPendingConfirmation = 4` (se agregan valores con cada evento nuevo) |
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

**E. Eventos que activan hoy**
12. `LinkCustomerToPersonPendingConfirmationCommand` (SPEC 45-post) llama a `EnsureActiveAsync` sobre el cliente vinculado con origen `PersonPendingConfirmation` y `SourceReferenceId` = la pendiente: vincular confirma al cliente como activo (lo reactiva si estaba inactivo). *(Supuesto a confirmar por el usuario.)*
13. Otros eventos (alta de ticket, actividad de calendario, ingreso por canal…) **no** se conectan en esta spec: cada uno agrega su valor de `CustomerStatusSource` y su llamada en la spec que lo pida.

**Out:**
- Cambios en `Ticket`, en el calendario o en la búsqueda (SPEC 54).
- Sincronizar `PersonLifecycleStage` con el estado (regla 6).
- Qué pasa con el cliente cuando se borra la `Person` (hoy no hay cascada; queda como pendiente abierto).
- UI del historial en el frontend.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_PERSON` | 400 | La persona no existe, está inactiva o borrada en la empresa |
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
10. Tests de integración (`CustomWebApplicationFactory`, `postgres:17`) para 1–8; `EXPLAIN` del SQL nuevo (convención 10).

## Decisiones

- **Sin borrado** (decisión del usuario): un cliente es la relación permanente entre una persona y la empresa; lo que cambia es su estado. Así el índice puede cubrir todas las filas y la regla "nunca dos veces" la garantiza la base, no solo el código.
- **Alta idempotente** (decisión del usuario): el alta manual y los eventos se comportan igual; no hay que saber si ya existía para "activar".
- **Historial en tabla propia** (decisión del usuario): responde por qué un cliente está activo o inactivo y qué eventos lo confirmaron; `ActivatedAt`/`DeactivatedAt` siguen como resumen del último cambio.
- **`UserId` del token en el alta:** si `UserId` es quien registra, no puede venir del cuerpo (se podría registrar a nombre de otro).
- **El esquema no cambia** (`Admin`), por lo mismo que en SPEC 54.

## Riesgos

- Bases de desarrollo con clientes duplicados: la migración falla a propósito y hay que resolverlos a mano.
- Clientes que estaban borrados reaparecen como inactivos en `GET /Customers`: es lo que pide la regla, pero cambia lo que ve el usuario.
