# SPEC 100 — Empresa de desarrollo Nicaragua (`JOIN-NI-001`): todo el seed existente, base en córdobas y acceso de los usuarios actuales

> **Status:** Borrador
> **Depends on:** SPEC 38 (empresas y acceso de usuarios), SPEC 43-post y 48-post (módulos, menú y permisos por empresa), SPEC 44-post (calendario), SPEC 45-post (cambio de empresa con `POST /workspaces/switch-company`), SPEC 54 y 55 (personas y clientes de ejemplo), **SPEC 56** (monedas por empresa), SPEC 57 (productos de ejemplo), SPEC 59–65 (comercial), SPEC 70–72 (adjuntos y correo). Rama de trabajo: `spec-100-development-company-nicaragua`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Crear en desarrollo una segunda empresa, **JOIN Nicaragua** (`JOIN-NI-001`), con moneda base **córdoba (NIO)**, que reciba **todo lo que hoy siembra `JOIN-001`** (módulos, menú, permisos, catálogos, tickets, calendario, personas, clientes, leads, productos, plantillas) y en la que **todos los usuarios existentes** tengan acceso con los mismos roles, para que `livingstone23@gmail.com` cambie a ella y trabaje sin problemas. Es la empresa donde se siembran los datos simulados de Nicaragua de SPEC 81–98.
> **Origen:** decisión del usuario (2026-10-24): `JOIN-001` tiene base USD y no se puede cambiar (ya tiene montos); se elige "empresa nueva NI". Pedido: "crea una empresa nueva, pero habilítale todo lo de seed ya existente a esta nueva empresa y habilita a los usuarios existentes a esta nueva empresa, de modo que livingstone23@gmail se pueda mover en esa empresa sin problemas".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Solo en `Development`**, idempotente (inserta lo que falte, no pisa cambios del usuario), en el mismo punto del arranque que el resto del seed de desarrollo.
2. **Moneda NIO global:** `SeedCurrenciesAsync` agrega el córdoba (`NIO`, "Córdoba nicaragüense", símbolo `C$`, 2 decimales) al catálogo global de monedas. Sirve para cualquier empresa.
3. **Empresa `JOIN-NI-001`** (`Common.Companies`, `TaxId = "JOIN-NI-001"` como clave del seed): nombre "JOIN Nicaragua", activa. Moneda base **NIO**. Moneda paralela **USD** con tasas de los últimos 30 días simuladas alrededor de 36,62 (convención de SPEC 56: 1 USD = `Rate` NIO).
4. **Todo el seed existente de `JOIN-001` también para `JOIN-NI-001`.** Los métodos del seed que hoy reciben el id de `JOIN-001` (o lo buscan por `TaxId`) se ejecutan también con el id de `JOIN-NI-001`:
   - módulos de la empresa (`CompanyModules`: todos los que tiene `JOIN-001`, incluido Calendario), roles por empresa (`RoleCompanies`) y permisos (`RoleSystemOptions`, copia exacta de los de `JOIN-001`);
   - catálogos por empresa (`SeedDefaultCatalogsForCompanyAsync`: mensajería, calendario, etapas de cliente, monedas, configuración de leads, roles de grupo, adjuntos, correo, tipos de plantilla) y los de Admin (áreas, proyectos, géneros, regímenes fiscales, industrias, rangos de ingreso);
   - tickets (estados, complejidades, valores por defecto, adjuntos, usuarios de tickets y tickets de ejemplo);
   - calendario (módulo, calendarios de usuarios y actividades de ejemplo);
   - personas (incluidas las 40 de búsqueda), direcciones y contactos, clientes, grupos de venta, leads, categorías y productos, plantillas de correo.

   Las personas de ejemplo usan ciudades de Nicaragua cuando el catálogo geográfico las tiene. Los precios y montos de ejemplo que en `JOIN-001` están en USD se siembran en `JOIN-NI-001` **convertidos a NIO** con la tasa del seed y redondeados a la decena.
5. **Acceso de los usuarios existentes:** cada usuario con acceso a `JOIN-001` (`UserCompanies`) recibe acceso a `JOIN-NI-001` con **los mismos roles** (`UserRoleCompanies`) y la misma configuración de tickets y calendario. Incluye a `livingstone23@gmail.com`. La empresa por defecto de cada usuario **no cambia** (sigue `JOIN-001`); se pasa a la nueva con el selector de empresa (`POST /workspaces/switch-company`, SPEC 45-post).
6. **Refactor del seed:** los métodos `SeedJoin…Async(Guid joinCompanyId)` pasan a ser `Seed…ForDevelopmentCompanyAsync(Guid companyId, DevelopmentCompanyProfile profile)`. El perfil indica la moneda base, la tasa de conversión de montos de ejemplo y la ciudad por defecto. `JOIN-001` usa el perfil actual (USD) y `JOIN-NI-001` el de Nicaragua. Los nombres, códigos y datos de `JOIN-001` no cambian.
7. **Datos de SPEC 81–98:** todas sus secciones "Seed de desarrollo (Nicaragua, datos simulados)" se aplican a `JOIN-NI-001`, no a `JOIN-001`.
8. **Roles de desarrollo** en `JOIN-NI-001`: "Vendedor" (ventas, punto de venta, paneles propios) y "Cobranza" (pagos, panel de crédito y cuentas por cobrar), con sus permisos de SPEC 85–98. Se asignan a dos usuarios de prueba sembrados para mostrar el alcance propio frente al total.
9. **Caché de permisos:** al terminar, se invalida la caché de permisos de `JOIN-NI-001` (`CompanyPermissionCacheInvalidator`), para que los usuarios vean el menú de inmediato.

## Scope

**In:**
1. `SeedCurrenciesAsync` con `NIO`.
2. `SeedDevelopmentCompaniesAsync`: crea `JOIN-NI-001` y ejecuta, para cada empresa de desarrollo, los métodos de la regla 4 con su perfil.
3. Refactor de los métodos de seed de `JOIN-001` a métodos por empresa (regla 6), sin cambiar su resultado en `JOIN-001`.
4. Copia de accesos de usuarios y roles (regla 5) y de `RoleSystemOptions` y `CompanyModules` (regla 4).
5. Para bases existentes sin migraciones pendientes, el arranque en `Development` ejecuta este seed (mismo patrón que `SeedMenuAndPermissionsAsync`).

**Out of scope:**
- Crear `JOIN-NI-001` fuera de `Development`.
- Cambiar la moneda base de `JOIN-001`.

## Criterios de aceptación

1. En una base nueva de desarrollo existen `JOIN-001` (base USD) y `JOIN-NI-001` (base NIO, USD paralela con tasas).
2. `livingstone23@gmail.com` lista sus empresas, ve `JOIN-NI-001`, cambia a ella y ve el mismo menú y permisos que en `JOIN-001` (tickets, calendario, clientes, leads, productos, administración, seguridad).
3. En `JOIN-NI-001` hay personas, clientes, leads, productos (con precios en NIO), tickets y actividades de calendario de ejemplo.
4. Correr el arranque dos veces no duplica nada. Los datos de `JOIN-001` no cambian.
5. Integración: el seed de desarrollo con las dos empresas y el cambio de empresa de un usuario sembrado.

## Decisiones

- **Empresa nueva en lugar de cambiar `JOIN-001`:** la base USD de `JOIN-001` ya tiene montos (`BASE_CURRENCY_IN_USE`). Una empresa nueva con base NIO evita conversiones artificiales en los ejemplos de Nicaragua.
- **Mismos métodos con perfil** en lugar de duplicar el seed: cualquier seed nuevo de desarrollo se escribe una vez y se aplica a las dos empresas.
