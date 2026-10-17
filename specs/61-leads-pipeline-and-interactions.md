# SPEC 61 — Leads: alta, bandeja, detalle, etapas en orden, contactos, comentarios, productos ofrecidos, duplicados, inactivar y reactivar

> **Status:** Aprobado
> **Depends on:** SPEC 54 (búsqueda única de personas), SPEC 55 (clientes), **SPEC 56** (monedas y tasas), **SPEC 57** (productos), **SPEC 58** (identificación opcional), **SPEC 59** (parametrización comercial), **SPEC 60** (grupos y alcance). Rama de trabajo: `spec-61-leads-pipeline-and-interactions`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Registrar prospectos (leads) sobre una persona, llevarlos por las etapas del embudo de la empresa, registrar cada contacto con comentarios y productos ofrecidos, y permitir dejarlos inactivos para que otro usuario los retome. Ganar y perder quedan en SPEC 63; documentos en SPEC 62.
> **Origen:** diseño del módulo de clientes (2026-10-16, vistas "Leads", "Nuevo lead" y "Detalle").

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **El lead es su propia entidad** (`Customer.Leads`) y siempre apunta a una persona (`Admin.Persons`). El cliente (`Customer.Customers`) solo se crea al ganar (SPEC 63).
2. **Estados:** `Open`, `Inactive`, `Won`, `Lost`. Esta spec maneja `Open` e `Inactive`; `Won` y `Lost` los pone SPEC 63.
3. **Etapas en orden:** un lead abierto avanza **a la siguiente etapa activa** según `SortOrder` (no salta) y puede volver a cualquier etapa anterior. `Won` y `Lost` no se alcanzan con el cambio de etapa: tienen sus acciones (SPEC 63), y "Ganar" funciona desde cualquier etapa abierta.
4. **Dueño y grupo:** el dueño es por defecto quien registra. El grupo se guarda en el lead: si el dueño tiene una sola membresía vigente, esa; si tiene varias, la que se elija (por defecto la principal); si no tiene, el lead queda sin grupo. Cada lead cuenta para un solo grupo.
5. **Inactivar:** un usuario con alcance sobre el lead (el dueño, o un rol que lo cubra, SPEC 60) lo marca inactivo con un motivo de uso `Inactive`. El lead conserva etapa, contactos y productos.
6. **Los leads inactivos los ve y los puede reactivar cualquier usuario de la empresa** con permiso de actualización sobre `Leads`. Quien lo reactiva **pasa a ser el dueño** y el lead toma **su grupo de ese momento**; el dueño y el grupo anteriores quedan en el historial de dueños. Si se gana después, cuenta para el nuevo dueño y su grupo.
7. **Duplicados según la empresa** (`LeadCompanySettings.DuplicateOpenLeadPolicy`, SPEC 59): al crear un lead para una persona con otro lead abierto, `Warn` (por defecto) crea y devuelve los leads abiertos encontrados; `Block` responde 409; `Allow` no dice nada.
8. **Moneda:** cada lead tiene moneda (activa de la empresa, por defecto la base), la tasa del día al registrar o editar montos y el monto en moneda base (SPEC 56). La moneda solo se cambia mientras el lead no tenga productos.
9. **Valor estimado:** con productos, es la suma de las líneas no rechazadas (en la moneda del lead); sin productos, se escribe a mano.
10. **Visibilidad** (SPEC 60): cada usuario ve los leads que cubre su alcance, más todos los inactivos. Fuera de alcance, el detalle responde 404.
11. **Persona inactiva:** su lead no acepta contactos nuevos ni reactivación hasta que la persona se reactive.

## Scope

**In:**

**A. Entidades (esquema `Customer`)**
1. `Leads`: `Code` (único por empresa), `PersonId`, `Topic` (máx. 200), `OwnerUserId`, `SalesTeamId?`, `LeadStageId`, `LeadSourceId`, `RelationshipTypeId`, `ReferredByPersonId?`, `Status` (`Open = 1`, `Inactive = 2`, `Won = 3`, `Lost = 4`), `LeadReasonId?` (motivo del último cierre o inactivación), `StatusNotes?`, `CurrencyId`, `ExchangeRate` (`numeric(18,6)`), `ExchangeRateDate`, `EstimatedValue` (`numeric(18,2)`), `EstimatedValueBase` (`numeric(18,2)`), `EstimatedCloseDate?` (`date`), `InactivatedAt?`, `ClosedAt?`, `CustomerId?` (lo llena SPEC 63 o la migración), `ProjectId?`, `AreaId?`, `LastInteractionAt?` (desnormalizado para ordenar la bandeja). Índices: único filtrado `(CompanyId, Code)`; `(CompanyId, Status, LeadStageId)`; `(CompanyId, OwnerUserId, Status)`; `(CompanyId, SalesTeamId, Status)`; `(CompanyId, PersonId, Status)`.
2. `LeadStageHistory`: `LeadId`, `FromStageId?`, `ToStageId`, `UserId`, `ChangedAt`. Una fila al crear (sin `FromStageId`) y una por cambio.
3. `LeadOwnershipHistory`: `LeadId`, `FromUserId?`, `ToUserId`, `FromTeamId?`, `ToTeamId?`, `Cause` (`Created = 1`, `Reassigned = 2`, `Reactivated = 3`), `UserId`, `At`, `Notes?`.
4. `LeadStatusHistory`: `LeadId`, `FromStatus?`, `ToStatus`, `LeadReasonId?`, `UserId`, `At`, `Notes?` (inactivar, reactivar; SPEC 63 agrega ganar, perder y reabrir).
5. `LeadInteractions`: `LeadId`, `ContactTypeCompanyId`, `Direction` (`Outbound = 1`, `Inbound = 2`), `OccurredAt`, `DurationMinutes?` (obligatorio si el tipo `AsksDuration`), `UserId`, `Outcome` (`Interested = 1`, `ProposalRequested = 2`, `NoAnswer = 3`, `NotInterested = 4`, `Other = 99`), `Summary` (máx. 2000). Índice `(CompanyId, LeadId, OccurredAt)`.
6. `LeadComments`: `LeadId`, `LeadInteractionId?`, `Body` (máx. 2000), `UserId`. Solo su autor los edita o borra (borrado lógico).
7. `LeadProducts`: `LeadId`, `ProductId`, `Quantity` (`numeric(18,2)`, `> 0`), `UnitPrice` (`numeric(18,2)`, en la moneda del lead), `DiscountPercent` (0–100), `LineTotal` (calculado al guardar), `Status` (`Offered = 1`, `Accepted = 2`, `Rejected = 3`), `OfferedInInteractionId?`.

**B. Reglas en Application**
8. `LeadCodeGenerator` (como `ICustomerCodeGenerator`): `CodePrefix` + secuencia por empresa de `CodeSequenceLength` dígitos, con `pg_advisory_xact_lock` (convención 8).
9. Alta (`CreateLeadCommand`, `ITransactionalCommand`): exactamente uno de `PersonId` o `NewPerson` (`PersonType`, nombres o razón social, `Phone?`, `Email?`, identificación opcional — crea la persona y su contacto principal en la misma transacción, reutilizando la lógica de `CreatePerson`); persona activa; origen, vinculación, moneda activos; etapa = la inicial de la empresa; dueño activo en la empresa; grupo según la regla 4; duplicados según la regla 7; historiales de etapa, dueño (`Created`) y estado iniciales; `ProductLines` opcionales y `FirstInteraction` opcional.
10. `LeadStageAdvanceCoordinator`: la etapa destino debe estar activa, ser `Initial` u `Open`, y ser la **siguiente** activa por `SortOrder` o una anterior (regla 3). Solo leads `Open`.
11. `LeadValueCalculator`: recalcula `EstimatedValue` desde las líneas no rechazadas, resuelve la tasa del día con `IExchangeRateResolver` y guarda `ExchangeRate`, `ExchangeRateDate` y `EstimatedValueBase` (con `IMoneyConverter`) cada vez que cambian líneas, moneda o valor manual.
12. `LeadAccessGuard`: aplica `ILeadAccessScopeResolver` (SPEC 60) a lecturas y acciones. Inactivos: visibles para todos. Reasignar exige un rol con `CanReassign` sobre el grupo del lead (o alcance `Company` con `CanReassign`), o SuperAdmin.
13. `LeadsAmountSource : IBaseCurrencyAmountSource` (SPEC 56): la empresa tiene montos si algún lead vivo tiene `EstimatedValue > 0`.
14. Bloqueos por uso de SPEC 57 y 59: `PRODUCT_IN_USE`, `LEAD_STAGE_IN_USE`, `LEAD_SOURCE_IN_USE`, `LEAD_REASON_IN_USE`, `RELATIONSHIP_TYPE_IN_USE`, `CONTACT_TYPE_IN_USE` se implementan aquí, con `Leads`, `LeadInteractions` y `LeadProducts`.
15. Persona: una persona con leads vivos no se puede borrar (se suma a `PersonCascadeCoordinator`, SPEC 41-post, con `"Active Leads: N"`).

**C. Endpoints** (recurso `Leads`, `[PermissionResource("Leads")]`)
16. `GET /api/v1/Leads` (paginado, Dapper): filtros `status` (por defecto `Open`), `leadStageId`, `ownerUserId`, `salesTeamId`, `leadSourceId`, `relationshipTypeId`, `search` (`ILIKE` sobre código, tema y nombre de la persona), `fromDate`/`toDate` (alta). Orden por `LastInteractionAt` desc. Trae etapa con probabilidad, dueño, grupo, origen, moneda, valor y valor base, último contacto (tipo y fecha).
17. `GET /api/v1/Leads/board?salesTeamId=&ownerUserId=` → por etapa abierta: cantidad, suma en moneda base y las 20 primeras tarjetas (vista por etapa).
18. `GET /api/v1/Leads/{id}` (detalle con persona, contacto principal, historial resumido y totales).
19. `GET /api/v1/Leads/{id}/timeline` → contactos, comentarios, cambios de etapa, de dueño y de estado, en orden descendente (unión con Dapper).
20. `GET /api/v1/Leads/duplicates?personId=` → leads abiertos de la persona (para avisar antes de crear).
21. `GET /api/v1/Leads/catalogs` → etapas, orígenes, motivos, vinculaciones, tipos de contacto y monedas activos de la empresa (sin permiso de parametrización).
22. `GET /api/v1/Leads/products-lookup?search=` → productos activos (SPEC 57) bajo el permiso `Leads`.
23. `POST /api/v1/Leads` (201) — alta (punto 9). Respuesta con el lead y `DuplicateOpenLeads` (vacío si no hubo).
24. `PUT /api/v1/Leads/{id}` — tema, vinculación, origen, referido, cierre estimado, proyecto, área, moneda (regla 8), valor manual (regla 9). Solo `Open`.
25. `PUT /api/v1/Leads/{id}/stage` `{ leadStageId }` — punto 10.
26. `POST /api/v1/Leads/{id}/reassign` `{ newOwnerUserId, notes? }` — punto 12; el grupo del lead no cambia. Historial `Reassigned`.
27. `POST /api/v1/Leads/{id}/inactivate` `{ leadReasonId, notes? }` — regla 5 (motivo `Inactive`). Historial de estado.
28. `POST /api/v1/Leads/{id}/reactivate` `{ salesTeamId?, notes? }` — regla 6. Cualquier usuario con `CanUpdate` sobre `Leads`. Historial de dueño (`Reactivated`) y de estado.
29. `POST /api/v1/Leads/{id}/interactions` (`[RequirePermission(PermissionFlags.CanUpdate)]`, como las notas de ticket), `PUT /{interactionId}` (solo su autor, 24 h), `GET`. Solo leads `Open`; actualiza `LastInteractionAt`.
30. `POST/PUT/DELETE /api/v1/Leads/{id}/comments` (`CanUpdate`; editar y borrar solo el autor).
31. `POST /api/v1/Leads/{id}/products`, `PUT /{lineId}` (cantidad, precio, descuento, estado), `DELETE /{lineId}`. Precio por defecto = `ReferencePrice` del producto convertido de moneda base a la del lead con la tasa del lead. Producto activo. Solo leads `Open`.

**D. Menú, permisos y seed**
32. `SystemOption` `Leads` (`/customers/leads`, módulo `Customers`), con `CanRead`, `CanCreate`, `CanUpdate`, `CanDelete` (borrar comentarios y líneas). Permisos sembrados: SuperAdmin, Admin, Manager y Supervisor con todo; UsuarioSimple lectura, alta y actualización (un integrante registra sus propios leads; lo que ve lo limita su alcance, SPEC 60). `Agent` sin acceso.
33. Seed de desarrollo en `JOIN-001`: los leads de ejemplo de la propuesta (abiertos, inactivos, uno en USD), con contactos y productos.

**Out of scope:**

- Ganar, perder, reabrir y conversión a cliente (SPEC 63); documentos (SPEC 62); reportes (SPEC 64); calendario y personas pendientes (SPEC 65).
- Varias personas por lead con su rol (descartado por ahora: vinculación = relación esperada).
- Inactivación automática por días sin contacto (pendiente futuro, SPEC 59 regla 7).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `LEAD_NOT_FOUND` | 404 | Lead de otra empresa, inexistente o fuera del alcance del usuario |
| `LEAD_PERSON_REQUIRED` | 400 | Llegan `PersonId` y `NewPerson` a la vez, o ninguno |
| `INVALID_PERSON` | 400 | Persona inexistente, borrada o inactiva |
| `LEAD_DUPLICATE_OPEN` | 409 | La persona ya tiene un lead abierto y la empresa tiene `Block` |
| `INVALID_LEAD_STAGE` / `INVALID_LEAD_SOURCE` / `INVALID_RELATIONSHIP_TYPE` / `INVALID_LEAD_REASON` / `INVALID_CONTACT_TYPE` | 400 | Registro inactivo, de otra empresa o de uso incorrecto (p. ej. motivo que no es `Inactive` al inactivar) |
| `LEAD_STAGE_SKIP_NOT_ALLOWED` | 400 | La etapa destino no es la siguiente ni una anterior |
| `LEAD_NOT_OPEN` | 409 | Acción que exige un lead abierto (editar, cambiar etapa, contacto, producto, inactivar) |
| `LEAD_NOT_INACTIVE` | 409 | Reactivar un lead que no está inactivo |
| `LEAD_ACTION_FORBIDDEN` | 403 | Reasignar sin `CanReassign` sobre el grupo, inactivar sin alcance, editar el comentario o contacto de otro |
| `INVALID_OWNER` / `INVALID_SALES_TEAM` | 400 | Dueño que no es miembro activo de la empresa; grupo inactivo o en el que el dueño no está vigente |
| `LEAD_CURRENCY_LOCKED` | 409 | Cambiar la moneda de un lead con productos |
| `INVALID_CURRENCY` / `EXCHANGE_RATE_NOT_FOUND` | 400 | Moneda inactiva o sin tasa (SPEC 56) |
| `INVALID_PRODUCT` | 400 | Producto inactivo o de otra empresa |
| `LEAD_PERSON_INACTIVE` | 409 | Contacto nuevo o reactivación con la persona inactiva |
| `COMPANY_REQUIRED` / `USER_REQUIRED` | 401 | El token no trae empresa o usuario |

## Criterios de aceptación

1. Crear un lead con persona existente o con `NewPerson` (sin identificación) deja el lead en la etapa inicial, `Open`, con código `LD-000001` en una empresa nueva y los tres historiales iniciales.
2. Dueño con un grupo → ese grupo; con dos → el elegido, o el principal si no se elige; sin grupos → lead sin grupo.
3. Con `Warn`, crear un segundo lead para la misma persona responde 201 con `DuplicateOpenLeads`; con `Block`, 409; con `Allow`, 201 sin lista.
4. Desde `Contactado` se puede ir a `Calificado` o volver a `Nuevo`, pero no saltar a `Propuesta` (400); reordenar las etapas cambia cuál es la siguiente.
5. Integrante ve solo sus leads; líder ve los de su grupo; supervisor (`Company`) ve todos; todos ven los inactivos. Un lead fuera de alcance responde 404 en el detalle.
6. Inactivar exige motivo `Inactive` y alcance sobre el lead. Otro usuario lo reactiva: pasa a ser el dueño, el lead toma su grupo y el historial de dueños registra al anterior.
7. Sin `CanReassign` sobre el grupo, reasignar responde 403; con él, cambia el dueño y no el grupo.
8. Agregar líneas recalcula el valor en la moneda del lead y en moneda base con la tasa del día; una línea rechazada no suma; con productos, cambiar la moneda responde 409.
9. Un lead en USD guarda `ExchangeRate`, `ExchangeRateDate` y `EstimatedValueBase` = valor × tasa redondeado a 2 decimales.
10. Contacto con un tipo que pide duración y sin duración → 400; contacto en un lead inactivo → 409; el contacto actualiza `LastInteractionAt`.
11. `timeline` devuelve contactos, comentarios y cambios de etapa, dueño y estado en orden descendente.
12. Borrar un producto, etapa, origen, motivo, vinculación o tipo de contacto usado por un lead responde 409 con `"Active Leads: N"` (o `LeadProducts`/`LeadInteractions`); borrar una persona con leads, 409.
13. Con leads con monto, cambiar la moneda base responde 409 `BASE_CURRENCY_IN_USE`.
14. Unit tests de handlers, validadores, coordinadores, guard y calculadora; integración (`CustomWebApplicationFactory`, `postgres:17`) para 1–13; `EXPLAIN` de la bandeja, el tablero y la línea de tiempo.

## Registro de implementación (2026-10-17)

Rama `spec-61-leads-pipeline-and-interactions` (desde `spec-60-sales-teams-and-management-roles`, para encadenar las migraciones).

- **Dominio y persistencia:**
  - `Lead`, `LeadStageHistory`, `LeadOwnershipHistory`, `LeadStatusHistory`, `LeadInteraction`, `LeadComment` y `LeadProduct` (con `LineTotal` redondeado a 2 decimales), más sus enums, en el esquema `customer`.
  - Índices del punto 1 (`UX_Leads_Company_Code` filtrado y los cuatro de bandeja, tablero, dueño y persona) y los de las tablas hijas.
  - Migración `Spec61LeadsPipelineAndInteractions`: solo crea tablas e índices.
  - `IUnitOfWork.AcquireLeadCodeLockAsync` (`pg_advisory_xact_lock`) para la secuencia del código.
- **Application:**
  - `LeadCodeGenerator`: prefijo y largo de `LeadCompanySettings`. Un prefijo nuevo empieza su propia secuencia.
  - `LeadAccessGuard`: SuperAdmin ve todo; si no, aplica el alcance de SPEC 60 más los inactivos. Lo usan los comandos y el filtro SQL de las lecturas (`ANY(@ScopeTeamIds)`).
  - `LeadStageAdvanceCoordinator`: siguiente etapa activa (`Initial`/`Open`) por `SortOrder` o una anterior; `Won` y `Lost` no se alcanzan.
  - `LeadValueCalculator`: suma de líneas no rechazadas o valor manual, tasa del día y `EstimatedValueBase` con `IMoneyConverter`. La tasa solo se vuelve a tomar cuando cambian los montos (líneas, moneda o valor manual).
  - `LeadOwnerRules`: dueño activo en la empresa y grupo de la regla 4 (solo cuentan grupos activos con membresía vigente).
  - Alta con `PersonId` o `NewPerson`. La persona nueva se crea con el handler de `CreatePerson` (mismo validador: una persona física necesita género) y su correo y teléfono como contactos.
  - Duplicados según la política de la empresa.
  - `LeadsAmountSource` bloquea el cambio de moneda base.
  - Bloqueos por uso: `PRODUCT_IN_USE` (`"Active LeadProducts: N"`); `LEAD_STAGE/SOURCE/REASON_IN_USE` y `RELATIONSHIP_TYPE_IN_USE` (`"Active Leads: N"`); `CONTACT_TYPE_IN_USE` (`"Active LeadInteractions: N"`, en el maestro); y la persona con leads (como dueña o referidora) en `PersonCascadeCoordinator`.
- **WebApi:**
  - `LeadsController` con todos los endpoints de la sección C. Contactos, comentarios, líneas, reasignar, inactivar y reactivar usan `CanUpdate`; borrar comentarios y líneas usa `CanDelete`.
  - `GET /Leads?status=0` lista todos los estados.
  - `GET /Leads/{id}/interactions` devuelve los contactos del lead.
- **Seed:**
  - Opción `Leads` (`/customers/leads`). Permisos: SuperAdmin, Manager y Supervisor con todo; UsuarioSimple con lectura, alta y actualización; `Agent` sin acceso.
  - En `JOIN-001`, seis leads de ejemplo sobre las personas, usuarios, grupos y productos sembrados: cinco abiertos en distintas etapas (uno en USD) y uno inactivo, con su contacto, líneas e historiales.
- **Notas para la revisión:**
  - Contacto sin duración cuando el tipo la pide: `400 LEAD_INTERACTION_DURATION_REQUIRED` (la spec pide 400 sin nombrar el código).
  - Proyecto o área inexistente: `400 INVALID_REFERENCE`.
  - Contacto, comentario o línea inexistente: `LEAD_INTERACTION_NOT_FOUND`, `LEAD_COMMENT_NOT_FOUND` y `LEAD_PRODUCT_NOT_FOUND` (404).
  - Reasignar exige un lead abierto (`LEAD_NOT_OPEN`).
  - Comentar se permite en cualquier estado si el lead es visible.
- **Verificación:**
  - Build Release sin errores.
  - Unit tests: 3376 OK, cobertura de líneas 94,36 %.
  - Integración: 307/307 OK. Son 11 nuevas (criterios 1–13 y `EXPLAIN` de bandeja, tablero y línea de tiempo).

## Decisiones

- **Etapas en orden con cierre anticipado** (decisión del usuario): ordena el trabajo sin impedir cerrar cuando el cliente ya decidió.
- **El inactivo es de toda la empresa y cambia de dueño al reactivarse** (decisión del usuario): un lead abandonado no queda bloqueado en manos de alguien que no lo trabaja.
- **Reasignar no cambia el grupo; reactivar sí** (decisión del usuario para reactivar): reasignar es redistribuir trabajo dentro del grupo; reactivar es que otro equipo retoma un lead abandonado.
- **Tasa del día guardada en el lead** (decisión del usuario): el embudo no cambia de valor cada vez que se mueve una tasa.
- **Productos tratados = líneas ofrecidas con su contacto** (`OfferedInInteractionId`): una sola tabla para lo ofrecido, sin tabla aparte de "productos mencionados".

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Bandeja lenta con muchos leads y el filtro de alcance. | Índices del punto 1 y `EXPLAIN` (criterio 14); el alcance se resuelve una vez por request. |
| Personas duplicadas por el alta rápida. | El formulario busca primero con `GET /Persons/lookup` y muestra coincidencias. |

## Impacto en el frontend

- Specs FE (todas en Borrador, `join_frontb/specs`): **FE-35, FE-36 y FE-37**. Base común del módulo: FE-29.
- Pantallas nuevas Clientes → Leads (tabla y por etapa), Nuevo lead y Detalle (línea de tiempo, productos, modales de contacto, inactivar y reactivar).
