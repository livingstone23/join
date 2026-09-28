# SPEC 39 — Consolidación de query filters globales (soft-delete + tenant) en `ApplicationDbContext`

> **Status:** Borrador
> **Depends on:** SPEC 30 (`UserConnectionLog` ganó `GcRecord` ahí; esta spec cierra el lado EF que quedó pendiente). No depende de las specs 34-38 del módulo de tickets, pero las beneficia directamente: sus 5 entidades nuevas (`TicketUserCompany`, `TicketAttachmentSettings`, `TicketDocument`, `TicketInboundChannel`, `TicketStatusTransition`) heredan todas de `BaseTenantEntity` y quedan cubiertas automáticamente por el mecanismo genérico de esta spec, sin que ninguna de esas specs necesite agregar una línea a `ConfigureGlobalQueryFilters`.
> **Date:** 2026-09-28
> **Objective:** Reemplazar las ~45 líneas manuales de `ApplicationDbContext.ConfigureGlobalQueryFilters` por un mecanismo genérico basado en reflexión que derive el filtro de soft-delete (`GcRecord == 0`) y, cuando aplica, el filtro de tenant (`CompanyId == currentUserService.CompanyId`) directamente de la jerarquía de clases del dominio (`IAuditableEntity` / `BaseTenantEntity`), preservando exactamente el comportamiento actual — con una única corrección: alinear el filtro de `UserConnectionLog` con lo que SPEC 30 ya estableció a nivel Dapper.

---

## Por qué existe esta spec

Surgió durante la revisión del módulo de tickets (SPEC 34-38): cada una de esas specs necesita agregar entidades nuevas a `ApplicationDbContext.ConfigureGlobalQueryFilters`, un método de ~45 líneas mantenido a mano desde el inicio del proyecto, con una llamada `builder.Entity<T>().HasQueryFilter(...)` por entidad. Al auditar ese método contra las 44 entidades reales del dominio que implementan `IAuditableEntity` (directa o indirectamente vía `BaseAuditableEntity`/`BaseTenantEntity`), aparecieron dos problemas concretos que confirman que el mantenimiento manual ya no escala:

1. **`UserConnectionLog` tiene un comentario y una línea comentada desactualizados.** `ConfigureGlobalQueryFilters` dice hoy:
   ```csharp
   // Note: UserConnectionLog typically doesn't need Soft Delete as it's an audit trail,
   // but if it inherits from BaseAuditableEntity, add it here:
   // builder.Entity<UserConnectionLog>().HasQueryFilter(e => e.GcRecord == 0);
   ```
   Eso fue cierto cuando `UserConnectionLog : BaseEntity` (sin `GcRecord`, confirmado por SPEC 30). Pero SPEC 30 (**Status: Implementado**) migró la entidad a `BaseAuditableEntity`, le agregó la columna `GcRecord`, y **restauró el filtro `GcRecord = 0` en las 4 queries Dapper de `RoleUserSessionRepository`** que revocan sesiones — es decir, el sistema ya depende de `GcRecord` para distinguir una sesión activa de una revocada. El comentario en `ApplicationDbContext` nunca se actualizó: a nivel EF, hoy cualquier lectura de `UserConnectionLog` que no pase por ese repositorio Dapper específico ve sesiones revocadas igual que las activas.
2. **8 entidades que heredan `BaseTenantEntity` no tienen ningún filtro EF**, ni de soft-delete ni de tenant, simplemente porque nadie agregó la línea al agregarlas al dominio: `Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile`. Sus propios handlers (`CreateGenderCommandHandler`, `CreateCustomerCommandHandler`, etc.) sí setean y filtran `CompanyId` a mano — pero eso es una defensa que cada handler tiene que recordar reimplementar, no una garantía del framework. **Esta spec no las toca** (ver Out of scope) — se documentan acá porque son la prueba de que el patrón manual ya falló dos veces sin que nadie lo notara, y son la motivación real del cambio, pero cerrarlas es una decisión de alcance distinta (puede revelar datos cruzados entre tenants que hoy conviven sin conflicto de claves naturales) que merece su propia spec.

El objetivo de esta spec es angosto a propósito: **cero cambios de comportamiento salvo uno, documentado y justificado** (`UserConnectionLog`). Todo lo demás — incluida la fuga de las 8 entidades — queda fuera, para que este cambio sea revisable y reversible de forma aislada.

---

## Scope

**In:**

### A. Persistencia

- `src/3.Persistence/Contexts/ApplicationDbContext.cs`: reemplazar el cuerpo de `ConfigureGlobalQueryFilters` por un método basado en reflexión que itera `builder.Model.GetEntityTypes()` y construye el filtro por tipo:
  - Si el CLR type implementa `IAuditableEntity` → filtro base `GcRecord == 0`.
  - Si además es asignable a `BaseTenantEntity` → se agrega `&& CompanyId == _currentUserService.CompanyId`, salvo que el tipo esté en `TenantFilterExemptions`.
  - Dos `HashSet<Type>` privados y estáticos, documentados inline, reemplazan las decisiones que hoy están implícitas en qué línea existe o no existe:
    - `TenantFilterExemptions` — entidades `BaseTenantEntity` que **sí** llevan `GcRecord == 0` pero **no** `CompanyId == tenant`, porque necesitan lectura cross-tenant o porque su `CompanyId` no es fiable. Contenido exacto (idéntico al comportamiento actual): `UserCompany`, `RoleCompany`, `CompanyModule`, `Region`.
    - `SoftDeleteFilterExemptions` — entidades que implementan `IAuditableEntity` pero no llevan ningún filtro EF. Contenido exacto: `{ typeof(ApplicationUser) }` únicamente (ver Decisiones — es la única exención real que queda tras verificar las otras cinco candidatas).
- Ningún cambio de dominio: no se agrega ninguna interfaz ni marcador nuevo. El mecanismo se apoya enteramente en `IAuditableEntity`/`BaseTenantEntity`, que ya existen.

### B. Tests de integración

- `tests/IntegrationTests/Persistence/GlobalQueryFiltersIntegrationTests.cs` (nuevo): contra SQL Server real (Testcontainers, mismo patrón que `RoleUserSessionRepositoryIntegrationTests` de SPEC 30 — **no** unit tests con mocks, porque `HasQueryFilter` es un comportamiento de EF que un `IUnitOfWork` mockeado no ejercita en absoluto). Ver casos en Implementation plan / F3.

**Out of scope (para specs futuras):**

- **Cerrar la fuga de tenant en las 8 entidades sin filtro** (`Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile`). Agregarles el filtro es, con este mecanismo, tan simple como sacarlas de ninguna lista — pero es un cambio de comportamiento real (una consulta que hoy devuelve filas de otras empresas dejaría de hacerlo) que merece su propio análisis de impacto y su propio criterio de aceptación, no colarse como efecto colateral de un refactor "de prolijidad". Spec de seguimiento.
- **Decidir el filtro de `ApplicationUser`.** Se preserva el comportamiento actual (sin filtro) explícitamente. Filtrarlo cambiaría, por ejemplo, `CreatedByUserName`/`AssignedToUserName` de tickets viejos con un usuario ya dado de baja (pasarían de mostrar el nombre a `null`, porque `CreateTicketCommandHandler`/`UpdateTicketCommandHandler` resuelven esos nombres vía `_unitOfWork.GetRepository<ApplicationUser>().GetAsync(id)`) — una decisión de producto, no de esta spec.
- **Corregir que `Region` herede `BaseTenantEntity` por error** (su `CompanyId` nunca se setea — confirmado en `CreateRegionCommandHandler`, que nunca lo toca). El fix correcto es moverla a `BaseAuditableEntity` como sus hermanas `Country`/`Province`/`Municipality`, lo cual implica una migración de schema (columna `CompanyId` y su FK) — fuera del alcance de un cambio de query filters. Se documenta como riesgo conocido.
- **Marcadores de interfaz nuevos** (`ICrossTenantEntity`, `INotSoftDeletable`, etc.) para reemplazar las listas de excepción por convención declarada en el dominio. Se descartó — ver Decisiones.

---

## Data model

Sin cambios de dominio ni de schema. Solo el método en `ApplicationDbContext`:

```csharp
// src/3.Persistence/Contexts/ApplicationDbContext.cs

/// <summary>
/// Entity types that are excluded from the automatic tenant clause derived in
/// <see cref="ConfigureGlobalQueryFilters"/>, because their correct behavior deviates from
/// what BaseTenantEntity alone would imply. Each exclusion is deliberate and verified against
/// real call sites — this list must stay in sync with reality, not grow by convenience.
/// </summary>
private static readonly HashSet<Type> TenantFilterExemptions = new()
{
    // Cross-tenant by design: resolving which companies a user belongs to (e.g. during
    // login, before a CompanyId claim exists) requires seeing rows across all tenants.
    typeof(UserCompany),
    typeof(RoleCompany),

    // Cross-tenant by design: its own read model (GetCompanyModulesQueryHandler) is a
    // Dapper-based platform admin view with CompanyId as an *optional* filter, not a
    // per-tenant list.
    typeof(CompanyModule),

    // Inherits BaseTenantEntity but CompanyId is never set by any handler
    // (CreateRegionCommandHandler never touches it) — every row has CompanyId ==
    // Guid.Empty. Tenant-filtering this would return zero rows for every real tenant.
    // Looks like a modeling mistake (Country/Province/Municipality correctly use
    // BaseAuditableEntity instead) — fixing the base class is a schema migration,
    // out of scope here (see spec's Out of scope). Preserve today's behavior.
    typeof(Region)
};

/// <summary>
/// Entity types that implement <see cref="IAuditableEntity"/> but keep exactly today's
/// behavior of having no query filter at all. Verified, not assumed: ApplicationUser is the
/// only entity in this set as of this spec.
/// </summary>
private static readonly HashSet<Type> SoftDeleteFilterExemptions = new()
{
    // ApplicationUser can't inherit BaseAuditableEntity (single inheritance — it already
    // extends IdentityUser<Guid>) and has never had a query filter. Enabling it now would
    // be new functionality with a real, verified consequence: CreateTicketCommandHandler /
    // UpdateTicketCommandHandler resolve CreatedByUserName/AssignedToUserName via
    // GetRepository<ApplicationUser>().GetAsync(id); a soft-deleted user would start
    // resolving to null on old records. A product decision, deferred — not this spec's call.
    typeof(ApplicationUser)
};

/// <summary>
/// Applies global filters to all relevant entities.
/// </summary>
/// <remarks>
/// Derives the filter for every entity from its base class instead of a manually maintained
/// per-entity list: any type assignable to <see cref="IAuditableEntity"/> gets
/// <c>GcRecord == 0</c>, and any type additionally assignable to <see cref="BaseTenantEntity"/>
/// also gets <c>CompanyId == _currentUserService.CompanyId</c> — unless listed in
/// <see cref="SoftDeleteFilterExemptions"/> or <see cref="TenantFilterExemptions"/>
/// respectively. New tenant-scoped entities inherit the correct filter automatically the
/// moment they extend BaseTenantEntity, with no line to remember to add here.
/// </remarks>
private void ConfigureGlobalQueryFilters(ModelBuilder builder)
{
    foreach (var entityType in builder.Model.GetEntityTypes())
    {
        var clrType = entityType.ClrType;

        if (!typeof(IAuditableEntity).IsAssignableFrom(clrType) || SoftDeleteFilterExemptions.Contains(clrType))
        {
            continue;
        }

        var parameter = Expression.Parameter(clrType, "e");
        var gcRecordProperty = Expression.Property(parameter, nameof(IAuditableEntity.GcRecord));
        Expression filter = Expression.Equal(gcRecordProperty, Expression.Constant(BaseAuditableEntity.ActiveGcRecord));

        if (typeof(BaseTenantEntity).IsAssignableFrom(clrType) && !TenantFilterExemptions.Contains(clrType))
        {
            var companyIdProperty = Expression.Property(parameter, nameof(BaseTenantEntity.CompanyId));
            var currentCompanyId = Expression.Property(Expression.Constant(_currentUserService), nameof(ICurrentUserService.CompanyId));
            filter = Expression.AndAlso(filter, Expression.Equal(companyIdProperty, currentCompanyId));
        }

        entityType.SetQueryFilter(Expression.Lambda(filter, parameter));
    }
}
```

`using System.Linq.Expressions;` se agrega a los `using` del archivo.

### Mapa completo de equivalencia (verificación exhaustiva, no muestreo)

Las 44 entidades del dominio que implementan `IAuditableEntity` se dividen así — cada fila es el resultado exacto que el método genérico produce, comparado contra el comportamiento actual línea por línea:

| Categoría | Cantidad | Resultado con el método genérico | ¿Cambia vs. hoy? |
|---|---|---|---|
| `BaseTenantEntity`, ya filtradas por tenant hoy (`Person`, `Ticket`, `RoleSystemOption`, etc.) | 14 | `GcRecord == 0 && CompanyId == tenant` | No |
| `BaseTenantEntity`, exentas de tenant hoy (`UserCompany`, `RoleCompany`, `CompanyModule`, `Region`) | 4 | `GcRecord == 0` únicamente (van en `TenantFilterExemptions`) | No |
| `BaseTenantEntity`, sin ningún filtro hoy (`Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile`) | 8 | `GcRecord == 0 && CompanyId == tenant` (les tocaría por regla) | **Sí — por eso quedan explícitamente fuera de esta spec, ver más abajo** |
| `BaseAuditableEntity` (catálogos globales), ya filtradas hoy (`Company`, `Country`, `SystemOption`, etc.) | 13 | `GcRecord == 0` | No |
| `BaseAuditableEntity`, sin filtro hoy, con dedicados repos Dapper que nunca pasan por EF (`EmailOtpEnableCode`, `MfaLoginChallenge`, `PhoneVerificationCode`, `UserMfaRecoveryCode`) | 4 | `GcRecord == 0` (ya no exentas — ver Decisiones) | Formalmente sí, funcionalmente no (ver abajo) |
| `UserConnectionLog` (`BaseAuditableEntity` desde SPEC 30) | 1 | `GcRecord == 0` | **Sí, intencional — ver Decisiones** |
| `IAuditableEntity` directo, sin filtro hoy | 1 | Sin filtro (`ApplicationUser`, va en `SoftDeleteFilterExemptions`) | No |

**Fila crítica a validar en F3**: las 8 entidades sin filtro no pueden quedar cubiertas por el método genérico tal cual está escrito arriba — quedarían filtradas automáticamente por regla, lo cual **no** es el alcance de esta spec. Por eso el `TenantFilterExemptions` de la implementación real debe incluir también esas 8, hasta que la spec de seguimiento las cierre deliberadamente. Ver Implementation plan F1.3.

---

## Implementation plan

### F1 — Refactor de `ConfigureGlobalQueryFilters`

1. Agregar `using System.Linq.Expressions;` a `ApplicationDbContext.cs`.
2. Reemplazar el cuerpo de `ConfigureGlobalQueryFilters` por el método de la sección Data model.
3. **`TenantFilterExemptions` debe incluir, además de las 4 confirmadas, las 8 entidades sin filtro hoy** (`Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile`), con un comentario que remita a esta spec y a la de seguimiento — de lo contrario el refactor, sin querer, cerraría la fuga de tenant como efecto colateral, violando el alcance acordado ("solo el mecánico").
4. `SoftDeleteFilterExemptions = { typeof(ApplicationUser) }` — un único elemento.
5. `dotnet build -c Release` → 0 errores. **Verificar la firma exacta de `IMutableEntityType.SetQueryFilter` disponible en EF Core 10.0.3/10.0.4** (el proyecto usa versiones ligeramente distintas entre `3.Persistence`/`3.Infrastructure`) — si el compilador pide un `filterKey` (`SetQueryFilter(string?, LambdaExpression)` por los filtros nombrados introducidos en EF Core 9), ajustar a `entityType.SetQueryFilter(null, Expression.Lambda(filter, parameter))` o el overload que el build exija. No asumir la firma sin confirmar con el compilador.

### F2 — Alinear `UserConnectionLog`

1. Con F1 ya aplicado, `UserConnectionLog` recibe el filtro `GcRecord == 0` automáticamente (no está en ninguna lista de exención). No requiere código adicional — este paso es de **verificación**, no de implementación.
2. Confirmar que `RoleUserSessionRepository` (Dapper puro, SPEC 30) sigue funcionando sin cambios: el filtro nuevo de EF no lo afecta, porque Dapper nunca pasa por el pipeline de `IQueryable` de EF. Es protección adicional para cualquier lectura EF futura de `UserConnectionLog`, no un reemplazo del filtrado Dapper existente.
3. Actualizar el comentario que documenta esta entidad si queda alguno desactualizado en el archivo.

### F3 — Tests de integración

`tests/IntegrationTests/Persistence/GlobalQueryFiltersIntegrationTests.cs`, contra SQL Server real vía `CustomWebApplicationFactory` (mismo fixture que SPEC 06/30), mínimo 10 casos:

1. Una entidad tenant-scoped ya filtrada hoy (ej. `Person`): dos empresas, cada una con una fila; autenticado como tenant A, `context.Persons.ToList()` solo devuelve la fila de A.
2. La misma entidad: una fila soft-deleted de la propia empresa A no aparece.
3. `UserCompany`: filas de dos empresas distintas, ambas visibles sin importar el tenant autenticado (confirma la exención de tenant se preserva); una fila soft-deleted no aparece (confirma que el soft-delete sigue activo).
4. `Region`: una fila con `CompanyId = Guid.Empty` (como las crea hoy `CreateRegionCommandHandler`) es visible sin importar el tenant autenticado — **prueba explícita de que el método genérico no la filtra por tenant** (si este test falla, es la señal de que `Region` se cayó de `TenantFilterExemptions` por error).
5. `CompanyModule`: mismo patrón que `UserCompany` — visible cross-tenant, soft-delete respetado.
6. Una entidad catálogo global sin tenant (ej. `Company` o `SystemOption`): visible siempre, soft-delete respetado.
7. `ApplicationUser`: una fila soft-deleted (`GcRecord != 0`) sigue siendo devuelta por `context.Users.Find(id)` — confirma que la exención se mantiene.
8. **`UserConnectionLog` — el caso nuevo**: una fila soft-deleted (`GcRecord != 0`, simulando una sesión revocada) **no** aparece en `context.UserConnectionLogs.ToList()` — este es el único test de la spec que debe fallar contra el código *anterior* a F1/F2 y pasar después; es la prueba de que el cambio de comportamiento intencional ocurrió.
9. Las 8 entidades explícitamente diferidas (tomar 2 como muestra, ej. `Gender`, `Customer`): confirmar que **siguen sin filtro** — una fila de otra empresa es visible hoy y debe seguir siéndolo tras este refactor (si este test falla, el alcance se salió de lo acordado en F1.3).
10. Una de las 5 entidades nuevas de SPEC 34-38, si ya existe para cuando se corra esta spec (ej. `TicketUserCompany`): confirma que queda filtrada por tenant automáticamente, sin ninguna línea agregada a `ConfigureGlobalQueryFilters` — la prueba del beneficio central de la spec. Si SPEC 34 aún no está implementada, este caso se agrega cuando lo esté, no bloquea el cierre de esta spec.

### F3b — Verificación de que ningún test existente necesita modificarse

Auditoría exhaustiva (no muestreo) de las clases de test que tocan `ApplicationDbContext` directamente, hecha **antes** de implementar F1, para descartar de antemano que el refactor rompa algo que ya pasa hoy:

1. `grep` de `ApplicationDbContext` sobre `tests/` completo → 4 archivos: `PermissionServiceTests.cs` (unit, `JOIN.Application.UnitTest`), `RoleUserSessionRepositoryIntegrationTests.cs`, `MfaLoginChallengeTests.cs`, `AccountSessionsRevokeTests.cs`.
2. `PermissionServiceTests.cs` — construye un `ApplicationDbContext` con `UseInMemoryDatabase` y siembra `UserRoleCompany`, `SystemOption`, `RoleSystemOption`. Los tres mantienen exactamente el mismo filtro antes y después de esta spec (ninguno está en ninguna de las dos listas de exención, ni cambia de categoría). **Sin impacto.**
3. `RoleUserSessionRepositoryIntegrationTests.cs` y `AccountSessionsRevokeTests.cs` — tocan `UserConnectionLog`/`UserRefreshToken` exclusivamente vía `.Add(...)` + `SaveChangesAsync()` (inserts) o mutando una entidad ya trackeada + `SaveChangesAsync()` (updates, ej. `CleanupAsync`). Los query filters de EF solo interceptan lecturas (`Where`/`FirstOrDefault`/`ToList`/etc.), nunca inserts ni updates sobre una entidad ya trackeada por el `ChangeTracker`. Toda lectura posterior a una escritura, en ambos archivos, pasa por `IRoleUserSessionRepository` (Dapper puro, SPEC 30) o por llamadas HTTP a la API — ninguna hace `db.UserConnectionLogs.Where(...)` ni equivalente. **Sin impacto**, confirmado con `grep -n "\.UserConnectionLogs\b" tests/` filtrando los `.Add(`.
4. `MfaLoginChallengeTests.cs` — único archivo de test con una lectura EF real y directa sobre una de las entidades que gana el filtro por primera vez (`db.MfaLoginChallenges.Where(c => c.UserId == user!.Id && c.GcRecord == 0)...`). El propio test ya incluye `GcRecord == 0` en su `Where` — el filtro global nuevo queda como una condición redundante (`AND GcRecord == 0` sobre una cláusula que ya lo exige), no cambia el resultado. **Sin impacto.**
5. `grep` de `.PhoneVerificationCodes`/`.UserMfaRecoveryCodes`/`.EmailOtpEnableCodes` sobre `tests/` completo → cero resultados fuera de las declaraciones de `DbSet`. Ningún test lee estas 3 entidades vía EF. **Sin impacto.**
6. Las 8 entidades diferidas (`Customer`, `Gender`, etc.) y las 4 exentas de tenant (`UserCompany`, `RoleCompany`, `CompanyModule`, `Region`) no cambian de comportamiento por construcción (están explícitamente preservadas en las listas de exención) — no requieren auditoría de tests, el diseño ya las protege.

**Conclusión de F3b: ninguna clase de test existente requiere modificación.** Los únicos tests nuevos son los de F3 (`GlobalQueryFiltersIntegrationTests.cs`). Si al implementar aparece algún otro test que lea una de las entidades afectadas vía EF sin este análisis haberlo detectado, es señal de que esta auditoría quedó incompleta y debe repetirse antes de mergear, no ignorarse.

### F4 — Verificación final

1. `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
2. `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj --filter "FullyQualifiedName~GlobalQueryFilters"` → 0 fallidos.
3. **No** correr esto contra el gate de cobertura de `JOIN.Application.UnitTest` — este cambio vive en `3.Persistence`, fuera del alcance de ese gate por diseño de `CLAUDE.md`. La prueba de que el cambio es correcto son los tests de integración de F3, no un porcentaje de cobertura.
4. Smoke manual: iniciar sesión con dos usuarios de dos empresas de desarrollo distintas, confirmar que ninguno ve datos de tickets/personas de la otra empresa (regresión general, no específica de esta spec).
5. Smoke manual: revocar una sesión propia (`DELETE /api/v1/account/sessions/{id}`, flujo de SPEC 26/30) y confirmar que el endpoint sigue devolviendo 200 sin 500 — valida que F2 no rompió el camino Dapper existente.

---

## Acceptance criteria

### F1 — Refactor

- [ ] `ConfigureGlobalQueryFilters` deriva el filtro de cada entidad por reflexión, sin una llamada `HasQueryFilter` explícita por tipo.
- [ ] `TenantFilterExemptions` contiene exactamente 12 entradas: las 4 confirmadas (`UserCompany`, `RoleCompany`, `CompanyModule`, `Region`) más las 8 diferidas explícitamente (`Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile`), cada una con su comentario justificando por qué está ahí.
- [ ] `SoftDeleteFilterExemptions` contiene exactamente 1 entrada: `ApplicationUser`.
- [ ] `dotnet build -c Release` → 0 errores.

### F2 — `UserConnectionLog`

- [ ] `UserConnectionLog` recibe `GcRecord == 0` a nivel EF por primera vez.
- [ ] `RoleUserSessionRepository` (Dapper, SPEC 30) no cambia su comportamiento — sigue filtrando `GcRecord` en sus propias queries, ajeno al nuevo filtro EF.

### F3 — Tests

- [ ] Los 10 casos de `GlobalQueryFiltersIntegrationTests` pasan contra SQL Server real (Testcontainers).
- [ ] El caso de `UserConnectionLog` (#8) es el único que representa un cambio de comportamiento real; todos los demás confirman que nada más cambió.
- [ ] El caso de `Region` (#4) prueba explícitamente que una fila con `CompanyId = Guid.Empty` sigue siendo visible cross-tenant — protección contra que alguien saque `Region` de `TenantFilterExemptions` sin darse cuenta de la consecuencia.
- [ ] Ninguna clase de test existente (`PermissionServiceTests`, `RoleUserSessionRepositoryIntegrationTests`, `MfaLoginChallengeTests`, `AccountSessionsRevokeTests`, ni el resto de la suite) requiere modificación — verificado en F3b antes de implementar F1, y confirmado de nuevo al final corriendo la suite completa sin editar ningún test preexistente.

### General

- [ ] `dotnet build -c Release` → 0 errores, 0 warnings nuevos.
- [ ] `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj` → 0 fallidos.
- [ ] Ninguna de las 8 entidades diferidas cambia de comportamiento.
- [ ] `ApplicationUser` no cambia de comportamiento.
- [ ] Toda entidad `BaseTenantEntity` nueva que se agregue de acá en adelante (empezando por las 5 de SPEC 34-38) queda filtrada por tenant sin necesidad de tocar `ApplicationDbContext`.

---

## Decisions taken and discarded

- **Reflexión sobre `IAuditableEntity`/`BaseTenantEntity` en vez de mantener la lista manual** (elegido). La lista manual ya falló dos veces de forma silenciosa (8 entidades sin ningún filtro, `UserConnectionLog` con un comentario desactualizado) sin que nada lo señalara — no hay ningún test que falle cuando alguien agrega una entidad `BaseTenantEntity` nueva y se olvida la línea. La reflexión hace que "olvidarse" deje de ser posible: toda entidad nueva hereda el filtro correcto por construcción, y las excepciones reales quedan en dos listas cortas, auditables de un vistazo.
- **`HashSet<Type>` explícito en vez de una interfaz marcadora nueva** (`ICrossTenantEntity`, `INotSoftDeletable`) (elegido). Una interfaz sería más "declarativa" (la excepción vive junto a la entidad, no en un archivo aparte), pero exige tocar el dominio de 5 entidades existentes (`UserCompany`, `RoleCompany`, `CompanyModule`, `Region`, `ApplicationUser`) por un beneficio marginal frente a una lista de 13 líneas comentadas en un solo lugar. Con 44 entidades totales y solo 5 excepciones reales, el archivo único es más fácil de auditar que rastrear qué entidades implementan una interfaz opcional.
- **Alcance limitado al refactor mecánico, sin cerrar la fuga de las 8 entidades** (elegido, ver Scope). Cerrar la fuga es casi gratis con este mecanismo (sacar 8 nombres de una lista), pero es un cambio de comportamiento de producción real — una query que hoy cruza tenants dejaría de hacerlo, lo cual podría exponer, por ejemplo, que dos empresas definieron un `Gender`/`Industry` con el mismo nombre y hoy conviven sin conflicto porque nunca se filtró por tenant en una unicidad cruzada. Separar ambos cambios permite revisar y revertir cada uno de forma independiente.
- **`UserConnectionLog` se corrige (deja de estar exenta), no se preserva "por las dudas"** (elegido). El comentario que la excluía predata la migración de SPEC 30 a `BaseAuditableEntity` y ya no describe la realidad — dejarlo como estaba sería perpetuar código muerto en vez de corregirlo, y el propio sistema (`RoleUserSessionRepository`) ya prueba que `GcRecord` es semánticamente significativo para esta entidad.
- **Los 4 repositorios de MFA/OTP dejan de estar exentos** (revertido respecto de una propuesta anterior en esta misma conversación). Verificado leyendo el código: los cuatro (`EmailOtpEnableCodeRepository`, `MfaLoginChallengeRepository`, `PhoneVerificationCodeRepository`, `UserMfaRecoveryCodeRepository`) son 100% Dapper, nunca pasan por `_unitOfWork.GetRepository<T>()`. El filtro EF no tiene ningún efecto sobre su comportamiento real hoy — excluirlos "por las dudas" no protegía nada concreto, y dejarlos filtrados por la regla genérica es más consistente y sirve de defensa adicional si alguna vez se agrega una lectura EF de estas entidades.
- **`ApplicationUser` se preserva sin filtro, sin decidir su destino final acá** (elegido). Es la única exención con una consecuencia funcional verificada y real (nombres de usuario en tickets viejos), a diferencia de las otras cinco candidatas descartadas arriba, que no tenían ningún efecto real. Se trata como una decisión de producto pendiente, no como parte de este refactor.

---

## Identified risks

| Riesgo | Mitigación |
|--------|------------|
| **La firma exacta de `IMutableEntityType.SetQueryFilter` puede diferir de la asumida en EF Core 10** (la versión introdujo filtros nombrados en EF Core 9, con overloads `SetQueryFilter(string?, LambdaExpression?)`). | F1.5 exige confirmar contra el compilador real, no asumir de memoria. Si el overload sin `filterKey` no existe, usar `SetQueryFilter(null, lambda)`. |
| **`Region.CompanyId` nunca se setea — si alguien saca `Region` de `TenantFilterExemptions` sin saberlo, toda consulta de `Region` devuelve cero filas para cualquier tenant real**, rompiendo formularios de dirección en toda la aplicación. | El comentario inline en el código explica la causa exacta. El test F3.4 falla de inmediato si esto ocurre — no queda como una regresión silenciosa. |
| **Query filters son un concern de `3.Persistence`, fuera del gate de cobertura de `JOIN.Application.UnitTest` que exige `CLAUDE.md`.** Un CI verde no prueba nada sobre este cambio. | F3 exige tests de integración contra SQL Server real como la prueba de corrección, no el porcentaje de cobertura. F4.3 lo deja explícito para que nadie confunda "CI verde" con "este cambio está probado". |
| **Habilitar el filtro de `UserConnectionLog` por primera vez a nivel EF** podría afectar alguna lectura EF de esta entidad no descubierta por el grep de esta spec (solo se verificó `RoleUserSessionRepository`). | Es el único cambio de comportamiento intencional de la spec, documentado explícitamente. Si aparece una regresión, el rollback es acotado: mover `UserConnectionLog` a `SoftDeleteFilterExemptions`. |
| **Las 8 entidades diferidas siguen sin protección de tenant a nivel EF** — el riesgo que motivó esta spec no se cierra acá, solo se documenta y se acota. | Es la decisión de alcance explícita de esta spec (ver Decisiones). Queda como el primer ítem de la spec de seguimiento. |

---

## What is **not** in this spec

- Cerrar el filtro de tenant en `Customer`, `Gender`, `Industry`, `TaxRegime`, `IncomeRange`, `PersonBusinessProfile`, `PersonEmployment`, `PersonFinancialProfile` — spec de seguimiento.
- Decidir si `ApplicationUser` debe filtrarse — decisión de producto diferida.
- Corregir que `Region` herede `BaseTenantEntity` por error — requiere migración de schema, spec aparte si se prioriza.
- Interfaces marcadoras en el dominio (`ICrossTenantEntity`, etc.).
- La rutina de notificación por inactividad de tickets — **SPEC 40**, tema no relacionado.

Cada uno, si llega, va en su propia spec.
