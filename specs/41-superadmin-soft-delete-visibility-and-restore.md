# SPEC 42 — Visibilidad de registros borrados y restauración para SuperAdmin

> **Status:** Borrador
> **Depends on:** SPEC 39 (query filters completos — define qué es "de empresa" y qué es "global"), SPEC 41 (índices únicos filtrados — la unicidad debe aplicar solo a activos para poder restaurar sin colisiones).
> **Date:** 2026-09-28
> **Objective:** Permitir que el rol Identity `SuperAdmin` vea registros activos **y** borrados (`GcRecord > 0`) de todas las entidades, y que pueda restaurar un registro borrado (`GcRecord → 0`), sin abrir ninguna fuga de datos entre empresas para el resto de los roles. Se implementa por etapas.

---

## Por qué existe esta spec

Hoy ningún rol puede ver ni recuperar un registro borrado:

- Los filtros globales de EF (SPEC 39) y todas las queries Dapper excluyen `GcRecord > 0` sin excepción, incluso para el SuperAdmin (por ejemplo `GetUsersWithRolesQueryHandler`, `UserManagementReportQueryHelper`: `WHERE u.GcRecord = 0`).
- Los métodos `Reactivate()` que existen (`Person`, `Gender`, `Industry`, `Customer`, etc.) cambian `IsActive`, **no** `GcRecord`. "Inactivo" (visible, marcado como no activo) y "borrado" (oculto) son conceptos distintos, y el segundo no tiene vuelta atrás.
- `GenericRepository.GetIncludingDeletedAsync` (`src/3.Persistence/Repositories/GenericRepository.cs:58`) ya existe, pero ignora **todos** los filtros — también el de tenant. Usarlo sin controles es una fuga.

---

## Reglas de visibilidad (invariantes del sistema)

| Rol | Datos de empresa | Datos globales | Borrados |
|---|---|---|---|
| Cualquier rol excepto `SuperAdmin` | Solo su `CompanyId` del token | Todos los activos | Nunca |
| `SuperAdmin` (rol Identity) | **Una empresa por request**: su empresa activa, u otra si la pide explícitamente (`TenantResolver`). Única excepción: el catálogo de empresas, donde ve todas. | Todos | Solo si lo pide explícitamente (`IncludeDeleted = true`) |

- "Global" significa exactamente las entidades sin `CompanyId` que SPEC 39 clasifica como catálogo global: `Company`, `Country`, `Province`, `Municipality`, `StreetType`, `CommunicationChannel`, `IdentificationType`, `EntityStatus`, `SystemModule`, `SystemOption`. Nada más es visible entre empresas.
- `IncludeDeleted` se **ignora silenciosamente** para cualquier rol que no sea `SuperAdmin` (mismo criterio que `TenantResolver` con `CompanyId`): el request no puede ampliar lo que el token permite.
- El rol `SuperAdminCompany` (administrador de una empresa) **no** es `SuperAdmin`: no ve borrados ni otras empresas.
- Mostrar borrados es opt-in por request, no un comportamiento por defecto del SuperAdmin: así los selectores de uso diario ("asignar ticket a…", "elegir género") nunca muestran registros borrados, ni siquiera a él.

---

## Diseño

### A. Dominio

- `BaseAuditableEntity.Restore()` → `GcRecord = ActiveGcRecord`. Idempotente.
- `ApplicationUser.Restore()` y `ApplicationRole.Restore()` equivalentes (no heredan `BaseAuditableEntity`: ya extienden `IdentityUser<Guid>`/`IdentityRole<Guid>`).
- `BaseAuditableEntity.IsDeleted` (propiedad calculada, no mapeada) → `GcRecord != ActiveGcRecord`.

### B. Application — piezas comunes (`2.Application/Common`)

- **`SoftDeleteVisibility.IncludeDeleted(ICurrentUserService, bool? requested)`** — estático, mismo estilo que `TenantResolver`: devuelve `true` solo si `requested == true && currentUserService.IsInRole("SuperAdmin")`.
- **`SoftDeleteRestorer`** — clase DI (constructor primario) con la lógica común de restauración, para que cada handler no la reimplemente:
  1. Carga con `GetIncludingDeletedAsync(id)`.
  2. Si no existe → `Response.Error("NOT_FOUND")`.
  3. **Tenant:** si la entidad es `BaseTenantEntity`, compara `entity.CompanyId` con `TenantResolver.Resolve(currentUser, request.CompanyId)`; si no coincide → `NOT_FOUND` (no `FORBIDDEN`, para no confirmar la existencia de un registro de otra empresa).
  4. Si ya está activa → `Response.Error("NOT_DELETED")`.
  5. **Padre borrado:** ejecuta el chequeo que le pasa el handler (por ejemplo, un `PersonContact` no se restaura si su `Person` está borrada) → `PARENT_DELETED`.
  6. **Duplicado activo:** ejecuta el chequeo que le pasa el handler contra la misma clave natural del índice único filtrado de SPEC 41 → `ACTIVE_DUPLICATE_EXISTS` (error de negocio, nunca un 500 por violación de índice).
  7. `entity.Restore()` + `SaveChangesAsync`.

### C. Application — por entidad (convención CQRS)

Por cada entidad restaurable, en `UseCases/<Area>/<Feature>/Commands/Restore<Entity>/`:

- `Restore<Entity>Command(Guid Id, Guid? CompanyId)` : `IRequest<Response<Guid>>`, `ITransactionalCommand<...>`.
- `Restore<Entity>CommandValidator` — `Id` no vacío.
- `Restore<Entity>CommandHandler` — guard `CompanyId == Guid.Empty`, guard `IsInRole("SuperAdmin")`, y delega en `SoftDeleteRestorer` pasándole los chequeos de padre y de duplicado propios de la entidad.

Queries de listado y por id de cada entidad:

- Parámetro opcional `IncludeDeleted` (bool, por defecto `false`).
- `WHERE (@IncludeDeleted = 1 OR x.GcRecord = 0)`, con `@IncludeDeleted` calculado por `SoftDeleteVisibility`, nunca tomado directo del request.
- Los JOINs a tablas relacionadas mantienen `GcRecord = 0` (un registro borrado muestra sus relaciones activas; no se mezclan borrados de varias tablas).
- DTO: se agregan `IsDeleted` (bool) y `DeletedOn` (`DateOnly?`, derivado del sello `yyyyMMdd` de `GcRecord`).

### D. WebApi

- `POST /api/v1/<resource>/{id}/restore` en el controller existente de cada entidad, con `[Authorize(Roles = "SuperAdmin")]` (mismo patrón que `CompaniesController`). **Decisión (2026-09-28):** basta con el rol; no se exige un flag de permiso adicional por recurso.
- `GET` de listado y por id aceptan `?includeDeleted=true`.

### E. Modelo de borrado: solo lógico, sin cascada hacia arriba

Verificado en el código (2026-09-28):

- **No hay borrado físico.** Ningún caso de uso llama a `GenericRepository.DeleteAsync` (el único método que hace `Remove()`), no hay `DELETE` en SQL crudo ni llamadas a `UserManager.DeleteAsync`/`RoleManager.DeleteAsync`. Las FK con `DeleteBehavior.Cascade` (usuario → membresías, tokens, códigos MFA; ticket → `TicketLog`) solo se disparan con un borrado físico, que nunca ocurre.
- **Borrar un hijo nunca afecta al padre.** `DeletePersonAddressCommandHandler` marca solo la dirección; si era la default, `PersonAddressDefaultCoordinator.PromoteNextDefaultAsync` promueve otra, y si no queda ninguna simplemente no hay default: se puede borrar la última dirección y la persona sigue activa. Igual para contactos (`PersonContactPrimaryCoordinator`).
- **Borrar una persona sí borra (lógicamente) algunos hijos.** `DeletePersonCommandHandler` marca la persona y sus direcciones y contactos activos. **No** marca `Customer`, `PersonEmployment`, `PersonBusinessProfile` ni `PersonFinancialProfile`, y sus queries no cruzan con `Admin.Persons`: quedan activos y visibles apuntando a una persona borrada. Con la regla de abajo, este comportamiento cambia.

**Decisión (2026-09-28): un padre con hijos activos no se puede borrar.** El único borrado válido en el sistema es el lógico (`GcRecord`). Si el padre tiene hijos activos, el borrado falla con un error de negocio que lista qué registros activos tiene; el usuario debe borrar primero los hijos y recién entonces puede borrar el padre. No hay borrado en cascada ni confirmación que lo fuerce.

- Código de error: el patrón existente `<ENTIDAD>_IN_USE` (por ejemplo `PERSON_IN_USE`), con el detalle de los hijos activos en `errors` (por ejemplo `"3 direcciones activas"`, `"2 contactos activos"`, `"1 empleo activo"`).
- **`DeletePerson` deja de borrar direcciones y contactos junto con la persona.** Direcciones, contactos, cliente, empleos, perfil de negocio y perfil financiero activos bloquean el borrado de la persona. Es un cambio de comportamiento respecto de hoy: los tests existentes de `DeletePersonCommandHandler` que esperan el borrado conjunto se actualizan como parte de la Etapa 2.
- Borrar un hijo sigue sin afectar al padre (sin cambios).

Este patrón ya existe en la mayoría de los catálogos: `DeleteGender`, `DeleteIndustry`, `DeleteRegion`, `DeleteTaxRegime`, `DeleteIncomeRange`, `DeleteProject`, `DeleteTicketStatus` y `DeleteProvince` devuelven `<ENTIDAD>_IN_USE` si el registro está referenciado. **No** lo tienen hoy: `DeleteArea`, `DeleteCountry`, `DeleteCompany` y `DeletePerson`. Esta spec lo extiende a todo padre con hijos: cada etapa, antes de implementar, inventaría las relaciones padre → hijo de sus entidades y alinea sus handlers `Delete<Entidad>`. `DeleteCompany` (solo SuperAdmin, SPEC 39) queda bloqueado mientras la empresa tenga membresías o registros activos.

Reglas para esta spec:

- Se elimina `DeleteAsync` de `IGenericRepository`/`GenericRepository` (borrado físico sin uso), para que ningún caso de uso futuro pueda introducirlo.
- **Restaurar es siempre individual.** Restaurar una `Person` restaura solo la persona; sus direcciones y contactos se restauran uno por uno. Una persona restaurada sin direcciones ni contactos activos es un estado válido (igual que borrar todas sus direcciones).
- **Referencias a catálogos borrados:** se permite restaurar, como hoy. El dato referenciado se muestra vacío.

### F. Entidades que nunca se restauran

Registros de seguridad y auditoría: restaurarlos reabriría sesiones, códigos o tokens revocados, o alteraría el historial.

`UserConnectionLog`, `UserRefreshToken`, `EmailOtpEnableCode`, `MfaLoginChallenge`, `PhoneVerificationCode`, `UserMfaRecoveryCode`, `TicketLog`.

No tienen endpoint de restauración. Ver sus registros borrados queda fuera de esta spec.

---

## Etapas

Cada etapa es un PR independiente, con sus propios tests. Solo se restauran entidades que hoy tienen un caso de uso de borrado (F0 confirma el inventario).

### Etapa 1 — Infraestructura común + catálogos

- Piezas A, B y el test de guarda de visibilidad (ver Tests).
- Catálogos de empresa: `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `Region`, `Area`, `Project`, `TicketStatus`, `TicketComplexity`, `TimeUnit`.
- Catálogos globales: `Country`, `Province`, `Municipality`, `StreetType`, `CommunicationChannel`, `IdentificationType`, `EntityStatus`, `SystemModule`, `SystemOption`.

Son las entidades más simples: sin padre (salvo `Province → Country`, `Municipality → Province`, `Region → Country`), y la validación de duplicado activo es la clave natural del índice.

### Etapa 2 — Personas

`Person`, `PersonAddress`, `PersonContact`, `PersonEmployment`, `PersonBusinessProfile`, `PersonFinancialProfile`, `Customer`.

- Hijos (`PersonAddress`, `PersonContact`, perfiles) → `PARENT_DELETED` si la `Person` está borrada.
- Restaurar una `Person` restaura solo la persona (sección E); direcciones y contactos se restauran individualmente.
- Invariantes existentes: al restaurar una dirección/contacto/empleo/perfil marcado como default, principal o actual, el coordinador correspondiente (`PersonAddressDefaultCoordinator`, `PersonEmploymentCurrentCoordinator`, `PersonBusinessProfileActiveCoordinator`, `PersonFinancialProfileCurrentCoordinator`) se aplica igual que en Create/Update: si ya hay otro default activo, el restaurado vuelve **sin** la marca.

### Etapa 3 — Seguridad y empresas

`ApplicationUser`, `ApplicationRole`, `Company`, `CompanyModule`, `UserCompany`, `RoleCompany`, `UserRoleCompany`, `RoleSystemOption`, `UserCommunicationChannel`, `UserPerson`.

- Restaurar un usuario **no** restaura sus membresías (`UserCompany`, `UserRoleCompany`) ni sus sesiones: cada una se restaura explícitamente.
- Restaurar un usuario queda registrado en el log de eventos de seguridad (`SecurityEventLog`).
- Invalidar la caché de permisos (`permissions:v2:{companyId}:{userId}`) al restaurar `UserRoleCompany`, `RoleCompany` o `RoleSystemOption`.

### Etapa 4 — Tickets

`Ticket`, `TicketNotification`, `TicketCompanyDefault`, y las entidades de SPEC 34-38 que ya estén implementadas.

- `TicketLog` no se restaura (sección E).
- Restaurar un `Ticket` genera una entrada en `TicketLog` (auditoría del ticket).

---

## Tests

### Test de guarda de visibilidad (Etapa 1)

Test de integración parametrizado sobre todos los endpoints `GET` de listado:

- Sembrar un registro activo y uno borrado en la empresa A, y uno activo en la empresa B.
- Autenticado como **Manager de A** con `?includeDeleted=true` → solo ve el activo de A (confirma que el parámetro se ignora y que no hay fuga hacia B).
- Autenticado como **SuperAdminCompany de A** con `?includeDeleted=true` → igual que el Manager.
- Autenticado como **SuperAdmin** (empresa activa A) con `?includeDeleted=true` → ve el activo y el borrado de A, no el de B.
- Autenticado como **SuperAdmin** con `?includeDeleted=true&companyId=B` → ve el de B.

Este test también cubre el hueco que SPEC 39 dejó abierto: una query Dapper nueva sin `CompanyId = @TenantId` falla aquí. Cada etapa agrega sus endpoints a la parametrización.

### Tests por entidad (cada etapa)

- Unit tests (gate de 90%) de cada `Restore<Entity>CommandHandler`: no encontrado, otra empresa → `NOT_FOUND`, no borrado → `NOT_DELETED`, padre borrado → `PARENT_DELETED`, duplicado activo → `ACTIVE_DUPLICATE_EXISTS`, no SuperAdmin → error, éxito.
- Unit tests de `SoftDeleteVisibility` y `SoftDeleteRestorer`.
- Integración: `POST /{id}/restore` como Manager → 403; como SuperAdmin → 200 y el registro vuelve a aparecer en el listado normal.

---

## Acceptance criteria

- [ ] Ningún rol distinto de `SuperAdmin` puede ver registros borrados ni datos de otra empresa, aunque envíe `includeDeleted=true` o `companyId`.
- [ ] `SuperAdmin` ve activos y borrados con `includeDeleted=true`, dentro de la empresa resuelta por `TenantResolver`.
- [ ] `SuperAdmin` puede restaurar cualquier entidad de las etapas implementadas; las de la sección E no son restaurables.
- [ ] Restaurar nunca produce un 500 por índice único: el duplicado activo se detecta antes y devuelve `ACTIVE_DUPLICATE_EXISTS`.
- [ ] No se puede restaurar un hijo cuyo padre está borrado.
- [ ] No se puede borrar un padre con hijos activos: el error `<ENTIDAD>_IN_USE` lista los hijos activos. `DeletePerson` ya no borra direcciones ni contactos.
- [ ] No existe ningún borrado físico en el código (`DeleteAsync` eliminado de `IGenericRepository`).
- [ ] Los invariantes de default/principal/actual se mantienen al restaurar.
- [ ] El test de guarda de visibilidad cubre todos los endpoints de listado de las etapas implementadas.
- [ ] Gate de cobertura ≥ 90% y suite de integración en verde en cada etapa.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| `GetIncludingDeletedAsync` ignora el filtro de tenant. | Solo se usa dentro de `SoftDeleteRestorer`, que valida `CompanyId` antes de tocar el registro. |
| Un handler nuevo toma `IncludeDeleted` directo del request. | Convención: siempre pasa por `SoftDeleteVisibility`. El test de guarda de visibilidad lo detecta. |
| Restaurar un registro cuyas referencias (por ejemplo, el `Gender` de una `Person`) siguen borradas. | Se permite; el dato referenciado se muestra vacío como hoy (decisión 2026-09-28). |
| Volumen: ~40 entidades × (command + handler + validator + tests + endpoint). | Etapas independientes; `SoftDeleteRestorer` concentra la lógica común. |

---

## Preguntas abiertas

Todas resueltas el 2026-09-28:

- Una empresa por request, salvo el catálogo de empresas.
- Restauración individual, sin cascada.
- Basta con el rol `SuperAdmin` para restaurar.
- Se permite restaurar con referencias a catálogos borrados.
- No hay vista "solo borrados"; solo `includeDeleted=true`.
- Un padre con hijos activos no se puede borrar: error `<ENTIDAD>_IN_USE` con el detalle de los hijos. Direcciones y contactos también bloquean el borrado de una persona.
