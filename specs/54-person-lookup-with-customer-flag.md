# SPEC 54 — Personas: búsqueda única con marca de cliente, cliente sin usuario y datos semilla

> **Status:** Borrador
> **Depends on:** SPEC 38 (filtros globales), SPEC 39 (fork PostgreSQL), SPEC 40-post (índices únicos filtrados), SPEC 41-post (restauración), SPEC 43-post (permisos por ruta). Rama de trabajo: `spec-54-person-lookup-with-customer-flag`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-15
> **Objective:** Que el frontend busque en una sola llamada entre las personas y los clientes (`Admin.Customers`) de la empresa, con un único texto, y sepa por cada resultado si es cliente y qué tipo de identificación tiene. Para poder sembrar y validar clientes reales, un cliente deja de exigir un usuario de acceso.
> **Origen:** FE-27 (buscador "Cliente/Persona" del ticket) y la brecha de FE-22 (`GET /Persons` sin búsqueda única, `join_frontb/specs/README.md`).

---

## Contexto

- `Customer` (`Admin.Customers`) vincula una `Person` con un código de cliente (`C` + 9 dígitos, `CustomerCodeGenerator`) y una etapa (`PersonLifecycleStage`). Hoy además exige un `UserId` (FK a `Security.Users`), así que no se puede registrar un cliente que no tenga cuenta de acceso.
- El ticket guarda solo `PersonId` (`Messaging.Tickets`). Elegir un cliente en el ticket es elegir su persona: no hace falta una columna nueva en `Ticket`.
- El frontend busca hoy con `GET /Persons` por `firstName`, `lastName`, `commercialName` e `identificationNumber` en cuatro llamadas en paralelo y une los resultados. No puede saber cuáles son clientes: `GET /Customers` no filtra por documento ni devuelve identificación.
- El seed (`DatabaseSeeder`) solo crea el tipo de identificación `Cedula`, no siembra ningún `Customer` y todas las personas de `JOIN-001` usan cédula.

## Scope

**In:**

**A. Cliente sin usuario obligatorio**
1. `Customer.UserId` pasa a `Guid?` (FK opcional a `Security.Users`, `OnDelete` sin cambio). `Customer.Create` acepta `userId` nulo; si viene, sigue validando que no sea `Guid.Empty`.
2. Índice único `(CompanyId, PersonId, UserId)` → se reemplaza por `UX_Customers_Company_Person` sobre `(CompanyId, PersonId)` con `HasFilter("\"gcrecord\" = 0")`: una persona es cliente **una sola vez** por empresa, tenga o no usuario. Es lo que garantiza que la búsqueda no repita personas.
3. Migración `Spec54CustomerOptionalUser` (Npgsql, convención 7): `ALTER COLUMN userid DROP NOT NULL`, cambio de índice. Antes de crear el índice nuevo, la migración verifica que no haya duplicados `(companyid, personid)` vivos; si los hay, falla con un mensaje claro (no borra datos).
4. `CreateCustomerCommand.UserId` y `UpdateCustomer` aceptan `null`. El validador quita `NotEmpty` de `UserId`; si viene, el handler mantiene sus validaciones actuales (usuario existe, activo y miembro de la empresa).
5. El handler de alta rechaza una segunda alta de la misma persona en la empresa con `CUSTOMER_PERSON_ALREADY_EXISTS` (409), en lugar de comparar `PersonId` + `UserId`. La restauración (`RestoreCustomerCommandHandler`) aplica la misma regla y solo comprueba el borrado del usuario padre cuando `UserId` tiene valor.
6. `CustomerQuerySql`: `INNER JOIN security.users` → `LEFT JOIN`. `CustomerResponseDto.UserId` → `Guid?`, `UserEmail` → `string?`.

**B. Búsqueda única**
7. `GET /api/v1/Persons/lookup?term={texto}&limit={n}` en `PersonsController` (`[PermissionResource("Persons")]`, acción de lectura, igual que `GET /Persons`).
8. `limit`: default 20, mínimo 1, máximo 50 (fuera de rango se ajusta al borde, sin error).
9. Busca con `ILIKE '%' || @Term || '%'` (convención 6) en `firstname`, `middlename`, `lastname`, `secondlastname`, `commercialname`, `identificationnumber`, el nombre completo concatenado (`CONCAT_WS(' ', firstname, middlename, lastname, secondlastname)`, para que "Norma Osorio" encuentre a la persona) y `customercode`. El texto se recorta (`Trim`) y se escapan `%`, `_` y `\`.
10. `term` vacío o con menos de 2 caracteres tras el `Trim` → 400 `INVALID_SEARCH_TERM`.
11. Solo personas **activas y no borradas** de la empresa del token. El cliente solo cuenta si está activo (`isactive = TRUE`) y no borrado.
12. **Una fila por persona**, aunque coincida por varios campos o tenga un cliente borrado además del vivo. Se arma con un `LEFT JOIN` al cliente vivo de la persona (único por el índice del punto 2), sin `DISTINCT` sobre texto.
13. Orden: (1) coincidencia exacta de `identificationnumber` o `customercode` (sin distinguir mayúsculas), (2) clientes antes que personas, (3) nombre completo ascendente, (4) `id` (desempate estable).
14. Consulta Dapper en `GetPersonLookupQuery` (`UseCases/Admin/Persons/Queries/GetPersonLookup/`), con `LIMIT @Limit` (convención 6).

**C. Datos semilla (PostgreSQL)**
15. `SeedIdentificationTypesAsync` agrega `Pasaporte` y `RNC` al catálogo global, además de `Cedula` (no se renombra: el seed la busca por nombre). Devuelve un diccionario nombre → Id en vez de un único Guid; las personas existentes siguen con `Cedula`.
16. Nuevo `SeedJoinLookupPersonsAsync(joinCompanyId, idTypes)`: 40 personas nuevas en `JOIN-001` (lista de abajo). Idempotente por `(CompanyId, IdentificationNumber)`, igual que `SeedJoinPersonsAsync`.
17. Nuevo `SeedJoinCustomersAsync(joinCompanyId)`: 30 de esas 40 se registran como `Customer` **sin usuario** (`UserId = null`), código con `CustomerCodeGenerator` (`C000000001`…), etapa según la lista. Idempotente por `(CompanyId, PersonId)`. Dos de ellos se dejan desactivados (`Deactivate()`) para validar que salen como persona.
18. Ambos se llaman en el paso 7 de `SeedAsync`, después de `SeedJoinPersonAddressesAndContactsAsync` y antes de `SeedJoinTicketsAsync`. No tocan `SeedMenuAndPermissionsAsync` ni su checksum.
19. Fechas con `DateTime.UtcNow` (convención 5).

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
| `IsCustomer` | bool | Hay un `Customer` activo y no borrado de esa persona en la empresa |
| `CustomerId` | Guid? | Solo si `IsCustomer` |
| `CustomerCode` | string? | Solo si `IsCustomer` |
| `PersonLifecycleStage` | int? | Solo si `IsCustomer` |

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `INVALID_SEARCH_TERM` | 400 | `term` vacío o de menos de 2 caracteres |
| `COMPANY_REQUIRED` | 401 | El token no trae empresa |
| `CUSTOMER_PERSON_ALREADY_EXISTS` | 409 | Alta o restauración de un cliente cuya persona ya es cliente vivo en la empresa |

## Datos semilla: 40 personas de `JOIN-001`

Cédula con formato `NNN-NNNNNNN-N`, pasaporte con 8 dígitos o letra + 7 dígitos, RNC con formato `N-NN-NNNNN-N`. "Cliente" indica si se registra en `Admin.Customers` y con qué etapa; `—` = solo persona.

| # | Tipo | Nombre | Identificación | Cliente |
|---|---|---|---|---|
| 1 | Física | Livingstone Cano | Pasaporte `20548796` | Customer |
| 2 | Física | Norma Osorio | Cedula `103-2001035-1` | — |
| 3 | Física | Gabriela Cano Ferreira | Cedula `001-1789456-2` | Customer |
| 4 | Física | Mateo Cano Ruiz | Cedula `001-1823410-7` | — |
| 5 | Física | Valeria Osorio Peña | Cedula `402-2150987-3` | Prospect |
| 6 | Física | Andrés Felipe Quintero | Pasaporte `AB4417320` | Customer |
| 7 | Física | Daniela Castillo Mejía | Cedula `223-0098451-6` | Customer |
| 8 | Física | Rafael Antonio Guzmán | Cedula `031-0456123-9` | Customer |
| 9 | Física | Isabel Cristina Rosario | Cedula `047-1203389-4` | Lead |
| 10 | Física | Tomás Herrera Báez | Cedula `001-0871245-1` | Customer |
| 11 | Física | Lucía Fernández Almonte | Pasaporte `30981245` | Customer |
| 12 | Física | Joaquín Medina Paulino | Cedula `402-1987742-0` | Prospect |
| 13 | Física | Camila Santana Veras | Cedula `001-1567023-8` | Customer |
| 14 | Física | Esteban Marte Cabrera | Cedula `056-0034781-2` | — |
| 15 | Física | Paola Jiménez Tejada | Cedula `223-0145567-5` | — |
| 16 | Física | Héctor Manuel Polanco | Cedula `001-0654398-3` | FormerCustomer |
| 17 | Física | Mariana Batista Cruz | Pasaporte `C2245871` | Customer |
| 18 | Física | Ricardo Peña Lora | Cedula `031-0789012-6` | — |
| 19 | Física | Natalia Vásquez Ortiz | Cedula `402-2233198-1` | Customer |
| 20 | Física | Fernando Abreu Matos | Cedula `001-1345987-4` | Lead |
| 21 | Física | Sofía Encarnación Gil | Cedula `047-0987654-0` | Customer |
| 22 | Física | Alejandro Núñez Brito | Pasaporte `41237765` | — |
| 23 | Física | Verónica Taveras Sosa | Cedula `001-1902345-9` | Customer *(desactivado)* |
| 24 | Física | Manuel de Jesús Acosta | Cedula `034-0012876-3` | Customer |
| 25 | Física | Carolina Espinal Durán | Cedula `402-1765432-8` | Prospect |
| 26 | Física | Diego Rosario Familia | Cedula `001-0567812-2` | — |
| 27 | Física | Adriana Mercedes Lugo | Cedula `223-0198765-7` | Customer |
| 28 | Física | Iván Sánchez Holguín | Pasaporte `D7781203` | Customer |
| 29 | Física | Elena Rodríguez Cano | Cedula `001-1678901-5` | Customer *(desactivado)* |
| 30 | Física | Pablo Ortega Montás | Cedula `031-0567234-1` | — |
| 31 | Física | Rosa Amelia Fermín | Cedula `047-1456789-2` | — |
| 32 | Física | Samuel Díaz Mercado | Cedula `001-1234098-6` | — |
| 33 | Jurídica | Distribuidora Osorio & Asociados S.R.L. (rep. Norma Osorio) | RNC `1-31-45678-2` | Customer |
| 34 | Jurídica | Agroindustrial Cibao S.A. (rep. Tomás Herrera) | RNC `1-01-23456-7` | Customer |
| 35 | Jurídica | Clínica Santa Lucía S.R.L. (rep. Lucía Fernández) | RNC `1-30-98765-4` | Customer |
| 36 | Jurídica | Ferretería El Progreso S.R.L. (rep. Manuel Acosta) | RNC `1-31-11223-5` | Lead |
| 37 | Jurídica | Transporte Caribe Express S.A. (rep. Iván Sánchez) | RNC `1-01-77889-1` | Customer |
| 38 | Jurídica | Colegio Nuevo Horizonte S.R.L. (rep. Rosa Fermín) | RNC `1-30-44556-9` | Prospect |
| 39 | Jurídica | Hotel Playa Dorada S.A. (rep. Camila Santana) | RNC `1-01-99001-3` | Customer |
| 40 | Jurídica | Software Cano Tech S.R.L. (rep. Livingstone Cano) | RNC `1-31-66778-0` | Customer |

Resumen: 40 personas (32 físicas, 8 jurídicas; 26 cédulas, 6 pasaportes, 8 RNC) y 30 clientes (20 Customer + 2 Customer desactivados, 4 Prospect, 3 Lead, 1 FormerCustomer). Quedan 10 solo como persona.

Casos que la lista cubre a propósito:
- **Apellido compartido entre clientes y personas:** "cano" coincide con #1, #3, #29 (clientes; #29 desactivado), #4 (persona) y #40 (jurídica, por su nombre comercial y su representante). Cada uno sale una sola vez.
- **Coincidencia por varios campos:** #40 coincide por `lastname` y por `commercialname`; sale una vez.
- **Nombre completo:** "Norma Osorio" encuentra a #2 (persona) y a #33 (jurídica, por su representante).
- **Cliente desactivado:** #23 y #29 salen con `IsCustomer = false`.
- **Coincidencia exacta:** `20548796` devuelve a #1 primero; `C000000001` devuelve al cliente con ese código.

## Criterios de aceptación

1. Una persona que también es cliente activo aparece **una sola vez**, con `IsCustomer = true`, `CustomerCode` y `PersonLifecycleStage`.
2. Una persona que coincide por varios campos (nombre, apellido, nombre comercial, documento, código) aparece una sola vez.
3. Un cliente desactivado o borrado aparece como persona (`IsCustomer = false`).
4. Buscar por documento parcial, por nombre completo ("Norma Osorio") o por código de cliente devuelve la persona.
5. No devuelve personas de otra empresa ni personas inactivas o borradas.
6. Una sola consulta SQL, sin N+1 por cliente; `EXPLAIN` sin errores contra la base migrada (convención 10).
7. `POST /Customers` sin `userId` crea el cliente; un segundo alta de la misma persona responde 409 `CUSTOMER_PERSON_ALREADY_EXISTS`. `GET /Customers` lista clientes con y sin usuario (`userEmail` nulo).
8. La migración aplica sobre una base existente sin pérdida de datos y el seed corre dos veces seguidas sin duplicar personas, clientes ni tipos de identificación.
9. Tras el seed en una base vacía: 3 tipos de identificación, 40 personas nuevas en `JOIN-001` y 30 filas en `Admin.Customers`, 2 desactivadas.
10. Tests de integración (`CustomWebApplicationFactory`, `postgres:17`) para 1–5, 7 y `INVALID_SEARCH_TERM`.
11. Smoke con Docker (convención 12): `curl` de `GET /Persons/lookup?term=cano`, `?term=Norma Osorio` y `?term=20548796` con el token de `JOIN-001`.

## Decisiones

- **`GET /Persons/lookup` y no una ruta bajo `Tickets`:** sirve también a FE-22 ("Relacionado con") y cierra su brecha. Exige el mismo permiso que ya pide el buscador actual (`Persons`).
- **`UserId` opcional en vez de crear 30 usuarios:** un cliente es un registro de negocio; la cuenta de acceso es opcional (portal de clientes). El comentario del índice en `CustomerConfiguration` ya anticipaba un `UserId` nulo. Crear usuarios solo para sembrar clientes ensuciaría `Security.Users` y los roles.
- **Unicidad por `(CompanyId, PersonId)`:** con `UserId` nulo, el índice anterior permitiría dos clientes de la misma persona (PostgreSQL trata los `NULL` como distintos). Como una persona solo puede ser cliente una vez por empresa, la búsqueda no puede repetir filas.
- **`Customers` se queda en el esquema `Admin`:** los esquemas agrupan por módulo (`Admin`, `Messaging`, `Calendars`, `Security`, `Common`), y `Customer` depende de `Person`, que vive en `Admin`. Un esquema propio de una sola tabla obligaría a una migración de `SET SCHEMA`, a reescribir el SQL de Dapper (`admin.customers`) y no aporta aislamiento. Si en el futuro hay un módulo de clientes con varias tablas (contratos, segmentos, portal), se evalúa en su propia spec.
- **`Cedula` sin tilde:** el seed la busca por nombre; renombrarla rompería bases existentes. Se corrige, si se quiere, desde el CRUD de tipos de identificación.

## Riesgos

- Bases con clientes duplicados `(companyid, personid)` vivos: la migración falla a propósito. Se resuelven a mano antes de aplicarla.
- `ILIKE '%…%'` sobre seis columnas no usa índices B-tree. Con el volumen actual es aceptable; si crece, `pg_trgm` + índice GIN en otra spec.
