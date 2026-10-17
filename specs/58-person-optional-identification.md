# SPEC 58 — Personas: identificación opcional

> **Status:** Aprobado
> **Depends on:** SPEC 38 (filtros globales), SPEC 40-post (índices únicos filtrados), SPEC 54 (búsqueda única de personas), SPEC 55 (activación de clientes). Rama de trabajo: `spec-58-person-optional-identification`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Que una persona se pueda registrar sin tipo ni número de identificación (un lead suele llegar solo con nombre, teléfono o correo), sin perder la garantía de que una identificación no se repite dentro de la empresa ni de que todo cliente tiene identificación.
> **Origen:** diseño del módulo de clientes (2026-10-16): el lead se registra en `Admin.Persons` y la identificación se exige al convertirlo en cliente.

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **Identificación opcional** en `Person`: `IdentificationTypeId` e `IdentificationNumber` pueden venir vacíos.
2. **Los dos o ninguno:** si llega uno, el otro es obligatorio.
3. **Unicidad cuando hay número:** el índice único `(CompanyId, IdentificationTypeId, IdentificationNumber)` aplica solo a filas con número.
4. **Todo cliente tiene identificación:** `ICustomerActivationService.EnsureActiveAsync` (SPEC 55) rechaza crear o activar el cliente de una persona sin identificación. SPEC 59 permite a cada empresa volver opcional esta regla (`RequireIdentificationForCustomer`, por defecto `true`); hasta entonces es obligatoria.

## Contexto

- `PersonConfiguration`: `IdentificationTypeId` e `IdentificationNumber` son `IsRequired()`; el índice único `(CompanyId, IdentificationTypeId, IdentificationNumber)` cubre todas las filas vivas.
- `CreatePersonCommandValidator` / `UpdatePersonCommandValidator` los exigen.
- Las queries de personas, el lookup de SPEC 54, el detalle de cliente y el ingreso por canal (`IntakeRequesterResolver`, SPEC 45-post) unen con `Admin.IdentificationTypes` con `INNER JOIN`.

## Scope

**In:**

1. Dominio: `Person.IdentificationTypeId` pasa a `Guid?` e `IdentificationNumber` a `string?`.
2. Migración `Spec58PersonOptionalIdentification` (Npgsql): columnas nulas; el índice único pasa a filtro `"identificationnumber" IS NOT NULL AND "gcrecord" = 0` (convención 2, el filtro `IS NOT NULL` se escribe porque Npgsql no lo agrega solo). Los datos existentes no cambian.
3. Validadores de alta y edición: regla 2; número normalizado (`Trim`). Mensaje: `"IdentificationTypeId and IdentificationNumber must be provided together."`.
4. DTOs y queries: `PersonDto`, `PersonListItemDto`, `PersonLookupItemDto` (SPEC 54) y el DTO de cliente con identificación nula; `INNER JOIN` con `Admin.IdentificationTypes` pasa a `LEFT JOIN` en todas las queries que la leen (lista y detalle de personas, lookup, clientes, personas pendientes, ingreso por canal).
5. `EnsureActiveAsync` (SPEC 55): si la persona no tiene identificación, responde `PERSON_IDENTIFICATION_REQUIRED` (regla 4). `POST /Customers` y `activate` heredan la regla.
6. `IntakeRequesterResolver` (SPEC 45-post) no cambia su búsqueda por documento: sigue buscando solo cuando el canal trae número.

**Out of scope:**

- Detección de personas duplicadas sin identificación (por teléfono o correo): el buscador de SPEC 54 ya busca por esos datos; avisar duplicados queda para otra spec si hace falta.
- Mover `Person` al esquema `Customer` (sigue en `Admin`, pendiente de SPEC 55).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| (validación) | 400 | Llega tipo sin número o número sin tipo |
| `PERSON_IDENTIFICATION_REQUIRED` | 400 | Crear o activar el cliente de una persona sin identificación (mientras la empresa lo exija) |
| `DUPLICATE_KEY` | 409 | Misma identificación viva en la empresa (índice único) |

## Criterios de aceptación

1. `POST /Persons` sin identificación crea la persona; con solo tipo o solo número responde 400.
2. Dos personas sin identificación conviven en la misma empresa; dos con la misma identificación viva, no (409).
3. `GET /Persons`, `GET /Persons/{id}`, `GET /Persons/lookup` y `GET /Customers/{id}` devuelven personas sin identificación (no las pierden por el `JOIN`).
4. `POST /Customers` para una persona sin identificación responde 400 `PERSON_IDENTIFICATION_REQUIRED`; tras completarla, 201.
5. La migración no modifica ninguna fila existente.
6. Unit tests de validadores y del servicio de activación; integración para 1–4; `EXPLAIN` de las queries que cambian de `INNER` a `LEFT JOIN`.

## Decisiones

- **Opcional en la persona, obligatoria en el cliente** (decisión del usuario): registrar rápido no debe frenarse, pero un cliente sí necesita identificación (facturación futura).
- **Sin tipo "Sin identificar":** sembrar un tipo provisional ensucia los datos y rompe la unicidad; se descartó (decisión del usuario).

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Más personas duplicadas al no tener documento. | El alta rápida de SPEC 61 busca primero con el lookup (nombre, teléfono, correo) y muestra coincidencias. |
| Algún `INNER JOIN` olvidado oculta personas. | Criterio 3 y búsqueda de `INNER JOIN admin.identificationtypes` en `src` como parte de la revisión. |

## Impacto en el frontend

- Specs FE (todas en Borrador, `join_frontb/specs`): **FE-29 (ajuste de Personas) y FE-27**. Base común del módulo: FE-29.
- FE-27 (buscador cliente o persona) y el formulario de personas: la identificación puede venir vacía. Se deja la nota "Ajuste por BE-58" en FE-27.
