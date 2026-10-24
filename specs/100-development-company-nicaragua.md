# SPEC 100 — Empresa de desarrollo Nicaragua (`JOIN-NI-001`): todo el seed existente, base en córdoba y acceso de los usuarios actuales

> **Status:** Borrador
> **Depends on:** SPEC 19 y 28 (roles por empresa y membresía multiempresa), SPEC 43-post y 48-post (módulos, menú y permisos por empresa), SPEC 44-post (calendario), SPEC 45-post (cambio de empresa con `POST /workspaces/switch-company`), SPEC 54 y 55 (personas y clientes de ejemplo), **SPEC 56** (monedas por empresa), SPEC 57 (productos de ejemplo), SPEC 59–65 (comercial), SPEC 70–72 (adjuntos y correo). Rama de trabajo: `spec-100-development-company-nicaragua`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Crear en desarrollo una segunda empresa, **JOIN Nicaragua** (`JOIN-NI-001`), con moneda base **córdoba (NIO)** y **USD** como moneda paralela, que reciba **todo lo que hoy siembra `JOIN-001`** (módulos, menú, permisos, catálogos, tickets, calendario, personas, clientes, leads, productos, plantillas) y en la que **todo usuario con acceso a `JOIN-001`** tenga acceso con los mismos roles.
> **Origen:** decisión del usuario (2026-10-24): `JOIN-001` tiene base USD y no se puede cambiar (ya tiene montos); se elige "empresa nueva NI". Pedido: "crea una empresa nueva, pero habilítale todo lo de seed ya existente a esta nueva empresa y habilita a los usuarios existentes a esta nueva empresa, de modo que livingstone23@gmail se pueda mover en esa empresa sin problemas".
> **Historia:** 2026-10-24 — reescrita tras la revisión de diseño: `Rate` según SPEC 56 (0,027308, no 36,62), un solo asiento de tasa (fecha de hoy), reglas 7–8 (datos y roles de SPEC 81–98) diferidas, alcance de usuarios ampliado a todos los `UserCompanies` de `JOIN-001`, redondeo a múltiplo de 10, geografía repartida entre los 20 municipios de Nicaragua, refactor completo a perfil. El estado vuelve a `Borrador` porque cambian el alcance y la lógica: el usuario la re-aprueba antes de implementarla.

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Solo en `Development`**, idempotente (inserta lo que falte, no pisa cambios del usuario), en el mismo punto del arranque que el resto del seed de desarrollo.
2. **Moneda NIO global:** `SeedCurrenciesAsync` agrega el córdoba (`NIO`, "Córdoba nicaragüense", símbolo `C$`, 2 decimales) al catálogo global de monedas. Sirve para cualquier empresa.
3. **Empresa `JOIN-NI-001`** (`Common.Companies`, `TaxId = "JOIN-NI-001"` como clave del seed): nombre "JOIN Nicaragua", activa. Moneda base **NIO** (`Finance.CompanyCurrencies.IsBase`) y **USD** como moneda paralela, con **una sola** fila `ExchangeRate` de fecha de hoy y `Rate = 0,027308` (convención de SPEC 56: 1 USD = 0,027308 NIO). El factor `36,62` (≈ 1 / 0,027308) es el que convierte los montos de ejemplo de USD a NIO.
4. **Todo el seed existente de `JOIN-001` también para `JOIN-NI-001`.** Los métodos del seed que hoy reciben el id de `JOIN-001` (o lo buscan por `TaxId`) se ejecutan también con el id de `JOIN-NI-001`:
   - módulos de la empresa (`CompanyModules`: copia exacta de los de `JOIN-001`, incluido `Calendar`), permisos (`RoleSystemOptions`, copia exacta de los de `JOIN-001`);
   - catálogos por empresa (`SeedDefaultCatalogsForCompanyAsync`: mensajería, calendario, etapas de cliente, monedas, configuración de leads, roles de grupo, adjuntos, correo, tipos de plantilla) y los de Admin (áreas, proyectos, géneros, regímenes fiscales, industrias, rangos de ingreso);
   - tickets (estados, complejidades, valores por defecto, adjuntos, usuarios de tickets y tickets de ejemplo);
   - calendario (`CalendarCompany`, calendarios de usuarios y actividades de ejemplo);
   - personas (incluidas las 40 de búsqueda), direcciones y contactos, clientes, grupos de venta, leads, categorías y productos, plantillas de correo.

   Los montos de ejemplo que en `JOIN-001` están en USD se siembran en `JOIN-NI-001` **convertidos a NIO** con el factor del perfil y **redondeados al múltiplo de 10** más cercano. Las direcciones de ejemplo usan país `NI` y se reparten en round-robin entre los **20 municipios de Nicaragua** del catálogo, sobre las 30 personas con dirección.
5. **Acceso de los usuarios existentes:** en cada arranque de `Development` se recorren **todos** los `UserCompanies` de `JOIN-001` (no solo los 6 usuarios del seed: también los creados en runtime) y se crea el acceso en `JOIN-NI-001` con los **mismos roles** (`UserRoleCompanies`) y la misma configuración de tickets y calendario. La empresa por defecto de cada usuario **no cambia** (sigue `JOIN-001`); se pasa a la nueva con el selector de empresa (`POST /workspaces/switch-company`, SPEC 45-post). No se siembran `RoleCompany`: `JOIN-001` tampoco los tiene y no participan en la resolución de permisos (`PermissionService` lee `UserRoleCompanies` + `RoleSystemOptions`).
6. **Refactor del seed:** los métodos `SeedJoin…Async(Guid joinCompanyId)` pasan a ser `Seed…ForDevelopmentCompanyAsync(Guid companyId, DevelopmentCompanyProfile profile)`, con el perfil **obligatorio** (sin valor por defecto). El perfil indica la moneda base, las monedas paralelas y su tasa, el factor de conversión de los montos de ejemplo y la geografía (país y municipios). `JOIN-001` usa el perfil actual (USD) y `JOIN-NI-001` el de Nicaragua. Los nombres, códigos y datos de `JOIN-001` no cambian.
7. **Datos de SPEC 81–98 (diferida):** sus secciones "Seed de desarrollo (Nicaragua, datos simulados)" se siembran en `JOIN-NI-001` cuando cada una de esas specs se implemente. SPEC 100 solo entrega la infraestructura multiempresa que lo permite; no crea datos, opciones de menú ni permisos que hoy no existen.
8. **Roles de desarrollo "Vendedor" y "Cobranza" (diferida):** se crean y se habilitan en `JOIN-NI-001` con las specs que los definan (SPEC 85–98), no en SPEC 100.
9. **Caché de permisos:** al terminar, se invalida la caché de permisos de `JOIN-NI-001` y la de los usuarios copiados (`CompanyPermissionCacheInvalidator`), para que los usuarios vean el menú de inmediato.
10. **Menú y permisos siempre al día en las dos empresas:** `SeedMenuAndPermissionsAsync` hoy actualiza solo `JOIN-001` (busca `TaxId == "JOIN-001"` y guarda su checksum en `SeedStates`). Pasa a recorrer las empresas de desarrollo (`JOIN-001` y `JOIN-NI-001`), cada una con su propia fila en `SeedStates`, para que las opciones y permisos que agreguen las specs 81–98 lleguen también a `JOIN-NI-001`.
11. **Métodos con `TaxId` fijo:** los `Seed…DefaultsAsync` que filtran por `TaxId == "JOIN-001" || "PRIV-001"` (por ejemplo `SeedTicketAttachmentSettingsDefaultsAsync`) incluyen `JOIN-NI-001`. Al implementar se revisan todos los métodos del seeder que comparan contra `"JOIN-001"` y se listan en el PR.

---

## Scope

**In:**

1. `SeedCurrenciesAsync` con `NIO`.
2. `SeedDevelopmentCompaniesAsync`: crea `JOIN-001` y `JOIN-NI-001` y ejecuta, para cada empresa de desarrollo, los métodos de la regla 4 con su perfil.
3. Refactor de los métodos de seed de `JOIN-001` a métodos por empresa (regla 6), sin cambiar su resultado en `JOIN-001`.
4. Copia de accesos de usuarios y roles (regla 5) y de `RoleSystemOptions` y `CompanyModules` (regla 4).
5. `SeedMenuAndPermissionsAsync` recorre las empresas de desarrollo, cada una con su fila en `SeedStates` (regla 10).
6. Los `Seed…DefaultsAsync` con `TaxId` fijo incluyen `JOIN-NI-001` (regla 11).
7. Para bases existentes sin migraciones pendientes, el arranque en `Development` ejecuta este seed (mismo patrón que `SeedMenuAndPermissionsAsync`).

**Out of scope:**

- Crear `JOIN-NI-001` fuera de `Development`.
- Cambiar la moneda base de `JOIN-001`.
- Los datos, las opciones de menú y los permisos de SPEC 81–98 (reglas 7–8, diferidas).
- Los roles globales "Vendedor" y "Cobranza" (diferidos a SPEC 85–98).
- La siembra de `RoleCompany` (ninguna empresa tiene hoy esas filas y no afectan la resolución de permisos).
- Migraciones de esquema: no hay tablas nuevas (el perfil es código, no datos).

---

## Modelo de datos

No hay entidades, tablas ni migraciones nuevas. Lo único nuevo es un tipo de código:

**`src/3.Persistence/Seed/DevelopmentCompanyProfile.cs`**

```csharp
namespace JOIN.Persistence.Seed;

/// <summary>
/// Perfil de una empresa de desarrollo (SPEC 100): moneda base, monedas paralelas con su tasa
/// (convención de SPEC 56), factor de conversión de los montos de ejemplo y geografía.
/// </summary>
public sealed record DevelopmentCompanyProfile
{
    public required string TaxId { get; init; }
    public required string CompanyName { get; init; }
    public required string BaseCurrencyCode { get; init; }
    public required IReadOnlyList<ParallelCurrencySeed> ParallelCurrencies { get; init; }

    /// <summary>Unidades de la moneda base por 1 unidad de USD (los montos de ejemplo de JOIN-001).</summary>
    public required decimal ExampleAmountFactor { get; init; }
    public required string CountryIsoCode { get; init; }
    public required IReadOnlyList<string> MunicipalityNames { get; init; }
    public required string TimeZoneId { get; init; }
}

/// <summary>Moneda paralela de una empresa de desarrollo: 1 unidad = <paramref name="Rate"/> unidades de la base.</summary>
public sealed record ParallelCurrencySeed(string Code, decimal Rate);
```

**Perfiles** (mismo archivo o `DevelopmentCompanyProfiles.cs`):

| | `JOIN-001` | `JOIN-NI-001` |
| --- | --- | --- |
| `TaxId` / `CompanyName` | `JOIN-001` / "JOIN Software Group" | `JOIN-NI-001` / "JOIN Nicaragua" |
| `BaseCurrencyCode` | `USD` | `NIO` |
| `ParallelCurrencies` | DOP `0,016846`, EUR `1,08` | USD `0,027308` |
| `ExampleAmountFactor` | `1` | `36,62` |
| `CountryIsoCode` | `HN` | `NI` |
| `MunicipalityNames` | "San Pedro Sula" | los 20 municipios de Nicaragua |
| `TimeZoneId` | `America/Managua` | `America/Managua` |

`Rate` (convención de SPEC 56) y `ExampleAmountFactor` guardan el mismo dato en dos convenciones recíprocas; el perfil expone ambos para no repetir la aritmética en el código.

---

## Plan de implementación

Cada paso deja el sistema funcional.

1. **Perfil.** Nuevo `src/3.Persistence/Seed/DevelopmentCompanyProfile.cs` con el record, `ParallelCurrencySeed` y la factory `DevelopmentCompanyProfiles` (perfiles `Join001` y `Nicaragua`). Sin cambio de comportamiento.
2. **Moneda NIO global.** `SeedCurrenciesAsync` agrega `("NIO", "Córdoba nicaragüense", "C$", 2)`.
3. **Monedas por empresa.** `SeedCompanyCurrenciesForCompanyAsync(companyId)` recibe la moneda base del perfil: NIO para `JOIN-NI-001`, USD para el resto. `SeedJoinParallelCurrenciesAsync` pasa a `SeedParallelCurrenciesForDevelopmentCompanyAsync(companyId, profile)`: con base NIO siembra USD con `Rate = 0,027308` de fecha de hoy; con base USD conserva DOP `0,016846` y EUR `1,08` tal cual hoy.
4. **Empresa `JOIN-NI-001`.** `SeedDefaultCompanyAsync` pasa a `SeedDevelopmentCompaniesAsync`: crea `JOIN-001` y `JOIN-NI-001` (activa, `GcRecord = 0`) y devuelve la lista de perfiles de desarrollo.
5. **Refactor a perfil.** Cada `SeedJoin…Async(Guid joinCompanyId)` pasa a `Seed…ForDevelopmentCompanyAsync(Guid companyId, DevelopmentCompanyProfile profile)`; los que dependen de montos o geografía leen el factor y los municipios del perfil. Los catálogos **globales** (países, provincias, municipios, calles, canales de comunicación, tipos de identificación, tipos de contacto, módulos, opciones, roles, usuarios por defecto, estados, unidades de tiempo) no cambian ni se duplican por empresa.
6. **Montos convertidos.** Los precios de productos (`SeedJoinProductCatalogAsync`) y las líneas de lead en moneda paralela (`SeedJoinLeadsAsync`) se multiplican por `ExampleAmountFactor` y se redondean al múltiplo de 10 más cercano (`Math.Round(valor / 10m, 0, MidpointRounding.AwayFromZero) * 10m`) cuando el perfil no es el de `JOIN-001`. El lead en moneda paralela usa la primera moneda paralela del perfil (DOP en `JOIN-001`, USD en `JOIN-NI-001`).
7. **Geografía.** `SeedJoinPersonAddressesAndContactsAsync` pasa a `SeedPersonAddressesAndContactsForDevelopmentCompanyAsync(companyId, profile)`: país del perfil y round-robin sobre `profile.MunicipalityNames`; en `JOIN-001` sigue Honduras / San Pedro Sula sin cambios.
8. **Acceso de usuarios.** `SeedUserAccessAsync` pasa a `SeedUserAccessForDevelopmentCompanyAsync(companyId)`: para cada `UserCompanies` de `JOIN-001` (todos, incluidos los creados en runtime) crea la fila en `JOIN-NI-001` con `IsDefault = false` y copia sus `UserRoleCompanies` (mismo `RoleId`, `CompanyId = JOIN-NI-001`), su configuración de tickets (`TicketUserCompanies`) y su calendario (`CalendarUser`).
9. **Permisos y módulos.** `SeedRoleSystemOptionsAsync` se ejecuta por cada empresa de desarrollo y se copia el conjunto de `RoleSystemOptions` de `JOIN-001` a `JOIN-NI-001` (mismos roles y opciones, `CompanyId` destino). `CompanyModules` se copia exacto de `JOIN-001` (incluido `Calendar`).
10. **Menú y permisos.** `SeedMenuAndPermissionsAsync` recorre las empresas de desarrollo; cada una recibe su fila en `SeedStates` (mismo checksum, porque las definiciones estáticas en código no cambian).
11. **Filtros por `TaxId`.** `SeedTicketAttachmentSettingsDefaultsAsync` y los demás métodos que comparan contra `"JOIN-001"` o `"PRIV-001"` incluyen `"JOIN-NI-001"`. El PR lista todos los métodos revisados.
12. **Arranque.** `Program.cs`: en `Development`, con o sin migraciones pendientes, se ejecuta `SeedDevelopmentCompaniesAsync` (idempotente); sin migraciones pendientes también se ejecuta `SeedMenuAndPermissionsAsync` y `SeedJoinEmailTemplatesAsync` para **ambas** empresas (hoy solo `JOIN-001`).
13. **Caché.** Al terminar se invalida la caché de permisos de `JOIN-NI-001` y la de los usuarios copiados.
14. **Pruebas.** `tests/IntegrationTests/Persistence/DevelopmentCompanyNicaraguaSeedPostgreSqlTests.cs` (patrón de `CommunicationChannelSeedPostgreSqlTests`): empresa creada, base NIO, tasa 0,027308, accesos copiados, precios convertidos a múltiplos de 10, municipios de Nicaragua y idempotencia al correr el seed dos veces. Los handlers de la capa Application no cambian, así que la cobertura de `JOIN.Application.UnitTest` no se ve afectada.

---

## Criterios de aceptación

1. En una base nueva de desarrollo existen `JOIN-001` (base USD) y `JOIN-NI-001` (base NIO, USD paralela con `Rate = 0,027308` de fecha de hoy).
2. `livingstone23@gmail.com` lista sus empresas, ve `JOIN-NI-001`, cambia a ella (SPEC 45-post) y ve el mismo menú y permisos que en `JOIN-001` (tickets, calendario, clientes, leads, productos, administración, seguridad).
3. En `JOIN-NI-001` hay personas (incluidas las 40 de búsqueda), clientes, leads, grupos de venta, productos, tickets, calendario y plantillas de correo.
4. Los precios de los productos de `JOIN-NI-001` son múltiplos de 10 en NIO y equivalen al precio de `JOIN-001` multiplicado por `36,62` y redondeado a la decena.
5. Las direcciones de ejemplo de `JOIN-NI-001` usan el país `NI` y municipios de Nicaragua.
6. Todo `UserCompanies` de `JOIN-001` tiene su equivalente en `JOIN-NI-001` con los mismos roles; ningún `IsDefault` de `JOIN-001` cambia.
7. `JOIN-NI-001` tiene los mismos módulos que `JOIN-001`, incluido `Calendar`.
8. `SeedStates` tiene una fila por cada empresa de desarrollo, con su checksum.
9. Correr el arranque dos veces no duplica nada. Los datos de `JOIN-001` no cambian: mismos conteos, mismos precios en USD, mismo `IsDefault`, mismos permisos.
10. Integración: `DevelopmentCompanyNicaraguaSeedPostgreSqlTests` cubre la creación de la empresa, la moneda base NIO, la tasa 0,027308, la copia de accesos, los precios convertidos y la idempotencia.

---

## Decisiones

- **Empresa nueva en lugar de cambiar `JOIN-001`:** la base USD de `JOIN-001` ya tiene montos (`BASE_CURRENCY_IN_USE`). Una empresa nueva con base NIO evita conversiones artificiales en los ejemplos de Nicaragua.
- **Mismos métodos con perfil** en lugar de duplicar el seed: cualquier seed nuevo de desarrollo se escribe una vez y se aplica a las dos empresas. El perfil es obligatorio (sin default), para que nadie agregue un método que dependa de la moneda sin declarar de qué moneda depende.
- **`Rate = 0,027308` y no 36,62:** SPEC 56 define `Rate` como "1 unidad de la moneda paralela = `Rate` unidades de la base"; el seed de `JOIN-001` convierte con `precio / rate` (DOP `0,016846`). Guardar `36,62` invertiría la convención y rompería ese cálculo. El `36,62` queda como factor de conversión de los montos de ejemplo, en el perfil.
- **Un solo asiento de tasa (fecha de hoy)**, como ya hace `JOIN-001` con DOP y EUR. Una serie de 30 tasas daily daría historial, pero ninguna spec implementada lo consume hoy.
- **Reglas 7–8 diferidas:** SPEC 81–98 están en `Borrador` y sus bloques de seed de Nicaragua, sus opciones de menú y sus permisos no existen en código. SPEC 100 entrega la infraestructura multiempresa; cada spec futura siembra su propio bloque en `JOIN-NI-001`.
- **Sin `RoleCompany`:** el catálogo de roles por empresa no se siembra hoy para ninguna empresa y no participa en la resolución de permisos. `JOIN-NI-001` recibe los mismos permisos por `RoleSystemOptions` que `JOIN-001`.
- **Redondeo a múltiplo de 10:** los precios de ejemplo se leen como cifras de córdobas, no como conversiones exactas.
- **Geografía repartida en los 20 municipios:** el catálogo global ya tiene `NI` con 10 departamentos y 20 municipios; se usan todos, en round-robin sobre las 30 personas con dirección.
- **Estado devuelto a `Borrador`:** cambian el alcance y la lógica de la spec (tasa, reglas diferidas, alcance de usuarios), así que vuelve a `Borrador` con esta nota de `Historia` para que el usuario la re-apruebe.

---

## Riesgos

- **Regresión en `JOIN-001`:** el refactor toca unos 30 métodos de `DatabaseSeeder` (5269 líneas). Mitigación: `JOIN-001` conserva nombres, orden y datos; el criterio 9 exige que nada cambie en `JOIN-001`, y las pruebas de integración de semillas (`tests/IntegrationTests/Persistence`) verifican los conteos.
- **Idempotencia:** los métodos por empresa deben seguir siendo "missing rows only". Si un método por empresa inserta sin filtrar por `CompanyId` o por código, el segundo arranque duplica. El criterio 9 y la prueba de idempotencia lo cubren.
- **Permisos de `SeedMenuAndPermissionsAsync` con dos empresas:** `SeedState.CompanyId` es único, así que cada empresa tiene su fila; el checksum no cambia porque las definiciones siguen siendo estáticas en código.
- **Redondeo:** los precios de `JOIN-NI-001` no cuadran exactamente con `JOIN-001` × 36,62 por el múltiplo de 10. Es esperado y está en el criterio 4.
- **Acceso revocado:** si el usuario quita a alguien de `JOIN-NI-001` a mano, el próximo arranque de `Development` vuelve a darle acceso (el seed es de desarrollo y por diseño repone lo que falta). Riesgo aceptado.
- **Arranque lento:** el seed de una segunda empresa duplica el trabajo de `SeedAsync` en `Development`. Mitigación: los catálogos globales no se repiten; el `SeedState` por empresa evita repetir el pipeline de menú y permisos.
