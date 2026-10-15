# SPEC 54 — Personas: búsqueda única con marca de cliente y datos semilla

> **Status:** Borrador
> **Depends on:** SPEC 38 (filtros globales), SPEC 39 (fork PostgreSQL), SPEC 43-post (permisos por ruta), **SPEC 55** (un cliente por persona y empresa, activo/inactivo, historial; se implementa antes o en el mismo cambio). Rama de trabajo: `spec-54-person-lookup-with-customer-flag`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-15
> **Historia (2026-10-15, decisión del usuario):** `Customer.UserId` no es la cuenta de acceso del cliente sino **el usuario que registró el cliente**. Se descarta volverlo opcional: sigue obligatorio, el seed usa el usuario `livingstone23@gmail.com`. El comentario XML de `Customer` y de `Customer.UserId` se corrigió en `main_postgresql` con este significado.
> **Historia (2026-10-15, decisión del usuario):** un cliente existe una sola vez por persona y empresa, no se borra y solo pasa entre activo e inactivo, activado o confirmado por eventos con historial. Esa regla sale de esta spec a **SPEC 55**, que reemplaza a la antigua parte A (índice, migración y alta). La etapa del cliente pasa de enum a catálogo por empresa (SPEC 55, parte F): el contrato trae `CustomerStageId`/`CustomerStageCode`/`CustomerStageName` y el seed asigna etapas por código. Aquí cambian también: el lookup distingue cliente activo de inactivo (`IsCustomerActive`; un cliente inactivo sigue siendo cliente) y el seed registra el historial con origen `Seed`.
> **Objective:** Que el frontend busque en una sola llamada entre las personas y los clientes (`Admin.Customers`) de la empresa, con un único texto, y sepa por cada resultado si es cliente y qué tipo de identificación tiene. Que no repite personas lo garantiza SPEC 55 (un cliente por persona y empresa).
> **Origen:** FE-27 (buscador "Cliente/Persona" del ticket) y la brecha de FE-22 (`GET /Persons` sin búsqueda única, `join_frontb/specs/README.md`).

---

## Contexto

- `Customer` (`Admin.Customers`) vincula una `Person` con un código de cliente (`C` + 9 dígitos, `CustomerCodeGenerator`) y una etapa del catálogo de su empresa (`CustomerStageId`, SPEC 55). Su `UserId` (FK obligatoria a `Security.Users`) es **el usuario que registró el cliente**, no una cuenta de acceso del cliente.
- SPEC 55 deja un único cliente por `(CompanyId, PersonId)`, sin borrado, con estado activo o inactivo e historial en `Admin.CustomerStatusHistory`.
- El ticket guarda solo `PersonId` (`Messaging.Tickets`). Elegir un cliente en el ticket es elegir su persona: no hace falta una columna nueva en `Ticket`.
- El frontend busca hoy con `GET /Persons` por `firstName`, `lastName`, `commercialName` e `identificationNumber` en cuatro llamadas en paralelo y une los resultados. No puede saber cuáles son clientes: `GET /Customers` no filtra por documento ni devuelve identificación.
- El seed (`DatabaseSeeder`) solo crea el tipo de identificación `Cedula`, no siembra ningún `Customer` y todas las personas de `JOIN-001` usan cédula.

## Scope

**In:**

**A. Búsqueda única**
1. `GET /api/v1/Persons/lookup?term={texto}&limit={n}` en `PersonsController` (`[PermissionResource("Persons")]`, acción de lectura, igual que `GET /Persons`).
2. `limit`: default 20, mínimo 1, máximo 50 (fuera de rango se ajusta al borde, sin error).
3. Busca con `ILIKE '%' || @Term || '%'` (convención 6) en `firstname`, `middlename`, `lastname`, `secondlastname`, `commercialname`, `identificationnumber`, el nombre completo concatenado (`CONCAT_WS(' ', firstname, middlename, lastname, secondlastname)`, para que "Norma Osorio" encuentre a la persona) y `customercode`. El texto se recorta (`Trim`) y se escapan `%`, `_` y `\`.
4. `term` vacío o con menos de 2 caracteres tras el `Trim` → 400 `INVALID_SEARCH_TERM`.
5. Solo personas **activas y no borradas** de la empresa del token. Si la persona tiene cliente (activo o inactivo), la fila trae sus datos; `IsCustomerActive` dice su estado.
6. **Una fila por persona**, aunque coincida por varios campos. Se arma con un `LEFT JOIN` al cliente de la persona (único por `UX_Customers_Company_Person`, SPEC 55), sin `DISTINCT` sobre texto.
7. Orden: (1) coincidencia exacta de `identificationnumber` o `customercode` (sin distinguir mayúsculas), (2) clientes activos, luego clientes inactivos, luego personas, (3) nombre completo ascendente, (4) `id` (desempate estable).
8. Consulta Dapper en `GetPersonLookupQuery` (`UseCases/Admin/Persons/Queries/GetPersonLookup/`), con `LIMIT @Limit` (convención 6).

**B. Datos semilla (PostgreSQL)**
9. `SeedIdentificationTypesAsync` agrega `Pasaporte` y `RNC` al catálogo global, además de `Cedula` (no se renombra: el seed la busca por nombre). Devuelve un diccionario nombre → Id en vez de un único Guid; las personas existentes siguen con `Cedula`.
10. Nuevo `SeedJoinLookupPersonsAsync(joinCompanyId, idTypes)`: 40 personas nuevas en `JOIN-001` (lista de abajo). Idempotente por `(CompanyId, IdentificationNumber)`, igual que `SeedJoinPersonsAsync`.
11. Nuevo `SeedJoinCustomersAsync(joinCompanyId)`: 30 de esas 40 se registran como `Customer` con `UserId` = el usuario `livingstone23@gmail.com` (quien los registra; ya existe en `GetDefaultUserSeeds`, se busca por correo con `IgnoreQueryFilters`; si no existe, se registra un warning y no se siembran clientes), código con `CustomerCodeGenerator` (`C000000001`…), etapa según la lista, por `Code` del catálogo de `JOIN-001` (`LEAD`, `PROSPECT`, `CUSTOMER`, `FORMER`, sembrado por SPEC 55 antes que los clientes). Idempotente por `(CompanyId, PersonId)`. Cada cliente nuevo registra en `Admin.CustomerStatusHistory` (SPEC 55) una fila `Created` con origen `Seed`. Dos se dejan inactivos (`Deactivate()` + fila `Deactivated`, origen `Seed`) para validar que salen como `(cli inactivo)`. El seed escribe directo en el contexto (no usa `ICustomerActivationService`, que vive en Application).
12. Ambos se llaman en el paso 7 de `SeedAsync`, después de `SeedJoinPersonAddressesAndContactsAsync` y antes de `SeedJoinTicketsAsync`. No tocan `SeedMenuAndPermissionsAsync` ni su checksum.
13. Fechas con `DateTime.UtcNow` (convención 5).

**Out:**
- Columna nueva en `Ticket` o cambios en los endpoints de tickets.
- Mover `Customers` a un esquema propio (ver Decisiones).
- Paginación del lookup (es un autocompletado: solo `limit`).
- Búsqueda por correo o teléfono.
- Contactos y direcciones para las 40 personas nuevas.

## Contrato

`GET /api/v1/Persons/lookup?term=cano&limit=20` → `Response<IReadOnlyList<PersonLookupItemDto>>`

| Campo | Tipo | Nota |
|---|---|---|
| `PersonId` | Guid | Lo que el ticket guarda |
| `PersonType` | int | `PersonType` |
| `FullName` | string | `CONCAT_WS(' ', firstname, middlename, lastname, secondlastname)`; para persona jurídica, `CommercialName` si existe |
| `CommercialName` | string? | |
| `IdentificationTypeId` | Guid | |
| `IdentificationTypeName` | string? | Nombre del catálogo (`Cedula`, `Pasaporte`, `RNC`) |
| `IdentificationNumber` | string | |
| `IsCustomer` | bool | La persona tiene `Customer` en la empresa (activo o inactivo) |
| `IsCustomerActive` | bool | `IsCustomer` y el cliente está activo; `false` si no es cliente |
| `CustomerId` | Guid? | Solo si `IsCustomer` |
| `CustomerCode` | string? | Solo si `IsCustomer` |
| `CustomerStageId` | Guid? | Solo si `IsCustomer` (catálogo por empresa, SPEC 55) |
| `CustomerStageCode` | string? | Solo si `IsCustomer` |
| `CustomerStageName` | string? | Solo si `IsCustomer` |

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_SEARCH_TERM` | 400 | `term` vacío o de menos de 2 caracteres |
| `COMPANY_REQUIRED` | 401 | El token no trae empresa |

## Datos semilla: 40 personas de `JOIN-001`

Cédula con formato `NNN-NNNNNNN-N`, pasaporte con 8 dígitos o letra + 7 dígitos, RNC con formato `N-NN-NNNNN-N`. "Cliente" indica si se registra en `Admin.Customers` y el `Code` de su etapa; `—` = solo persona.

| # | Tipo | Nombre | Identificación | Cliente |
|---|---|---|---|---|
| 1 | Física | Livingstone Cano | Pasaporte `20548796` | CUSTOMER |
| 2 | Física | Norma Osorio | Cedula `103-2001035-1` | — |
| 3 | Física | Gabriela Cano Ferreira | Cedula `001-1789456-2` | CUSTOMER |
| 4 | Física | Mateo Cano Ruiz | Cedula `001-1823410-7` | — |
| 5 | Física | Valeria Osorio Peña | Cedula `402-2150987-3` | PROSPECT |
| 6 | Física | Andrés Felipe Quintero | Pasaporte `AB4417320` | CUSTOMER |
| 7 | Física | Daniela Castillo Mejía | Cedula `223-0098451-6` | CUSTOMER |
| 8 | Física | Rafael Antonio Guzmán | Cedula `031-0456123-9` | CUSTOMER |
| 9 | Física | Isabel Cristina Rosario | Cedula `047-1203389-4` | LEAD |
| 10 | Física | Tomás Herrera Báez | Cedula `001-0871245-1` | CUSTOMER |
| 11 | Física | Lucía Fernández Almonte | Pasaporte `30981245` | CUSTOMER |
| 12 | Física | Joaquín Medina Paulino | Cedula `402-1987742-0` | PROSPECT |
| 13 | Física | Camila Santana Veras | Cedula `001-1567023-8` | CUSTOMER |
| 14 | Física | Esteban Marte Cabrera | Cedula `056-0034781-2` | — |
| 15 | Física | Paola Jiménez Tejada | Cedula `223-0145567-5` | — |
| 16 | Física | Héctor Manuel Polanco | Cedula `001-0654398-3` | FORMER |
| 17 | Física | Mariana Batista Cruz | Pasaporte `C2245871` | CUSTOMER |
| 18 | Física | Ricardo Peña Lora | Cedula `031-0789012-6` | — |
| 19 | Física | Natalia Vásquez Ortiz | Cedula `402-2233198-1` | CUSTOMER |
| 20 | Física | Fernando Abreu Matos | Cedula `001-1345987-4` | LEAD |
| 21 | Física | Sofía Encarnación Gil | Cedula `047-0987654-0` | CUSTOMER |
| 22 | Física | Alejandro Núñez Brito | Pasaporte `41237765` | — |
| 23 | Física | Verónica Taveras Sosa | Cedula `001-1902345-9` | CUSTOMER *(inactivo)* |
| 24 | Física | Manuel de Jesús Acosta | Cedula `034-0012876-3` | CUSTOMER |
| 25 | Física | Carolina Espinal Durán | Cedula `402-1765432-8` | PROSPECT |
| 26 | Física | Diego Rosario Familia | Cedula `001-0567812-2` | — |
| 27 | Física | Adriana Mercedes Lugo | Cedula `223-0198765-7` | CUSTOMER |
| 28 | Física | Iván Sánchez Holguín | Pasaporte `D7781203` | CUSTOMER |
| 29 | Física | Elena Rodríguez Cano | Cedula `001-1678901-5` | CUSTOMER *(inactivo)* |
| 30 | Física | Pablo Ortega Montás | Cedula `031-0567234-1` | — |
| 31 | Física | Rosa Amelia Fermín | Cedula `047-1456789-2` | — |
| 32 | Física | Samuel Díaz Mercado | Cedula `001-1234098-6` | — |
| 33 | Jurídica | Distribuidora Osorio & Asociados S.R.L. (rep. Norma Osorio) | RNC `1-31-45678-2` | CUSTOMER |
| 34 | Jurídica | Agroindustrial Cibao S.A. (rep. Tomás Herrera) | RNC `1-01-23456-7` | CUSTOMER |
| 35 | Jurídica | Clínica Santa Lucía S.R.L. (rep. Lucía Fernández) | RNC `1-30-98765-4` | CUSTOMER |
| 36 | Jurídica | Ferretería El Progreso S.R.L. (rep. Manuel Acosta) | RNC `1-31-11223-5` | LEAD |
| 37 | Jurídica | Transporte Caribe Express S.A. (rep. Iván Sánchez) | RNC `1-01-77889-1` | CUSTOMER |
| 38 | Jurídica | Colegio Nuevo Horizonte S.R.L. (rep. Rosa Fermín) | RNC `1-30-44556-9` | PROSPECT |
| 39 | Jurídica | Hotel Playa Dorada S.A. (rep. Camila Santana) | RNC `1-01-99001-3` | CUSTOMER |
| 40 | Jurídica | Software Cano Tech S.R.L. (rep. Livingstone Cano) | RNC `1-31-66778-0` | CUSTOMER |

Resumen: 40 personas (32 físicas, 8 jurídicas; 26 cédulas, 6 pasaportes, 8 RNC) y 30 clientes (20 `CUSTOMER` + 2 `CUSTOMER` inactivos, 4 `PROSPECT`, 3 `LEAD`, 1 `FORMER`). Quedan 10 solo como persona.

Casos que la lista cubre a propósito:
- **Apellido compartido entre clientes y personas:** "cano" coincide con #1, #3, #29 (clientes; #29 inactivo), #4 (persona) y #40 (jurídica, por su nombre comercial y su representante). Cada uno sale una sola vez.
- **Coincidencia por varios campos:** #40 coincide por `lastname` y por `commercialname`; sale una vez.
- **Nombre completo:** "Norma Osorio" encuentra a #2 (persona) y a #33 (jurídica, por su representante).
- **Cliente inactivo:** #23 y #29 salen con `IsCustomer = true` e `IsCustomerActive = false`, después de los clientes activos.
- **Coincidencia exacta:** `20548796` devuelve a #1 primero; `C000000001` devuelve al cliente con ese código.

## Criterios de aceptación

1. Una persona que también es cliente aparece **una sola vez**, con `IsCustomer = true`, `IsCustomerActive` según su estado, `CustomerCode` y su etapa (`CustomerStageId`, `CustomerStageCode`, `CustomerStageName`).
2. Una persona que coincide por varios campos (nombre, apellido, nombre comercial, documento, código) aparece una sola vez.
3. Un cliente inactivo aparece como cliente (`IsCustomer = true`, `IsCustomerActive = false`) y se ordena después de los activos.
4. Buscar por documento parcial, por nombre completo ("Norma Osorio") o por código de cliente devuelve la persona.
5. No devuelve personas de otra empresa ni personas inactivas o borradas.
6. Una sola consulta SQL, sin N+1 por cliente; `EXPLAIN` sin errores contra la base migrada (convención 10).
7. Una persona sin cliente sale con `IsCustomer = false`, `IsCustomerActive = false` y `CustomerId`/`CustomerCode` y los campos de etapa nulos.
8. La migración aplica sobre una base existente sin pérdida de datos y el seed corre dos veces seguidas sin duplicar personas, clientes ni tipos de identificación.
9. Tras el seed en una base vacía: 3 tipos de identificación, 40 personas nuevas en `JOIN-001` y 30 filas en `Admin.Customers` (2 inactivas), todas con `UserId` del usuario `livingstone23@gmail.com`, y 32 filas en `Admin.CustomerStatusHistory` (30 `Created` + 2 `Deactivated`, origen `Seed`).
10. Tests de integración (`CustomWebApplicationFactory`, `postgres:17`) para 1–5, 7 y `INVALID_SEARCH_TERM`.
11. Smoke con Docker (convención 12): `curl` de `GET /Persons/lookup?term=cano`, `?term=Norma Osorio` y `?term=20548796` con el token de `JOIN-001`.

## Decisiones

- **`GET /Persons/lookup` y no una ruta bajo `Tickets`:** sirve también a FE-22 ("Relacionado con") y cierra su brecha. Exige el mismo permiso que ya pide el buscador actual (`Persons`).
- **`UserId` = usuario que registró el cliente** (decisión del usuario, 2026-10-15): no es una cuenta de acceso del cliente, así que no hay que crear usuarios para sembrar clientes ni volverlo opcional.
- **Unicidad, estado e historial del cliente en SPEC 55:** aquí solo se leen. Un cliente inactivo sigue siendo cliente (decisión del usuario, 2026-10-15) y el frontend lo marca `(cli inactivo)`.
- **`Customers` se queda en el esquema `Admin`:** los esquemas agrupan por módulo (`Admin`, `Messaging`, `Calendars`, `Security`, `Common`), y `Customer` depende de `Person`, que vive en `Admin`. Un esquema propio de una sola tabla obligaría a una migración de `SET SCHEMA`, a reescribir el SQL de Dapper (`admin.customers`) y no aporta aislamiento. Si en el futuro hay un módulo de clientes con varias tablas (contratos, segmentos, portal), se evalúa en su propia spec.
- **`Cedula` sin tilde:** el seed la busca por nombre; renombrarla rompería bases existentes. Se corrige, si se quiere, desde el CRUD de tipos de identificación.

## Riesgos

- Bases con clientes duplicados `(companyid, personid)` vivos: la migración falla a propósito. Se resuelven a mano antes de aplicarla.
- `ILIKE '%…%'` sobre seis columnas no usa índices B-tree. Con el volumen actual es aceptable; si crece, `pg_trgm` + índice GIN en otra spec.
