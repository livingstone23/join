# SPEC 59 — Parametrización comercial: etapas, orígenes, motivos, vinculación, tipos de contacto y reglas por empresa

> **Status:** Borrador
> **Depends on:** SPEC 41-post (borrado lógico y bloqueo por uso), SPEC 43-post (módulos, menú y rutas), SPEC 44-post (patrón maestro global + versión por empresa de `CalendarActivity`), SPEC 99-post (`CommunicationChannel.Code`). Rama de trabajo: `spec-59-commercial-settings-catalogs`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Que cada empresa configure cómo trabaja sus leads: las etapas del embudo con su probabilidad, los orígenes ("razones de registro"), los motivos para ganar, perder o dejar inactivo, los tipos de vinculación esperada, los tipos de contacto y las reglas del módulo (duplicados, identificación del cliente, código).
> **Origen:** diseño del módulo de clientes (2026-10-16, vista "Parametrización"). Los roles de gestión se definen en SPEC 60 y los límites de adjuntos en SPEC 62.

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **Todo por empresa.** Cada catálogo es de la empresa (`BaseTenantEntity`), salvo el maestro de tipos de contacto.
2. **Etapas del lead** con probabilidad (0–100) y orden. Exactamente **una** etapa `Initial`, una `Won` y una `Lost` por empresa; el resto son `Open`. `Won` y `Lost` son de sistema: se renombran, no se borran ni se desactivan. Las etapas se recorren en orden de `SortOrder` (la regla de avance la aplica SPEC 61).
3. **Motivos** en un solo catálogo con su uso: `Won`, `Lost` o `Inactive`. Obligatorios al ganar, perder o inactivar.
4. **Tipo de vinculación** = la relación que se espera con la persona (posible cliente, proveedor, socio, distribuidor, referidor). Cada tipo declara qué pasa al ganar (`OnWin`): `Customer` (crea o confirma el cliente, SPEC 63) o `None` (solo registro).
5. **Tipos de contacto con maestro global y versión por empresa** (mismo patrón que `CalendarActivity` / `CalendarActivityCompany`): el maestro lo siembra el sistema; cada empresa activa, renombra y ordena los suyos, no inventa códigos.
6. **Reglas por empresa** (`LeadCompanySettings`, una fila por empresa):
   - `DuplicateOpenLeadPolicy`: `Warn` (por defecto), `Block` o `Allow`. Avisar y no bloquear es el valor por defecto porque a la misma persona se le pueden ofrecer productos distintos (decisión del usuario).
   - `RequireIdentificationForCustomer`: por defecto `true` (SPEC 58).
   - `CodePrefix` (por defecto `LD-`) y `CodeSequenceLength` (por defecto 6) para el código del lead.
7. **Pendiente futuro** (decisión del usuario): inactivar leads automáticamente tras N días sin contacto, con una tarea programada por módulo. Esta spec **no** agrega el campo; queda en "Pendientes abiertos" del README.

## Scope

**In:**

**A. Entidades (esquema `Customer`)**
1. `LeadStages`: `Code` (normalizado como `CustomerStage.NormalizeCode`), `Name`, `Probability` (0–100), `SortOrder`, `Kind` (`Initial = 1`, `Open = 2`, `Won = 3`, `Lost = 4`), `IsActive`. Únicos filtrados `(CompanyId, Code)` y `(CompanyId, Name)`; únicos filtrados `(CompanyId)` con `"kind" = 1 AND "gcrecord" = 0` y lo mismo para `3` y `4` (una sola inicial, ganada y perdida vivas).
2. `LeadSources`: `Code`, `Name`, `Description?`, `SortOrder`, `IsActive`.
3. `LeadReasons`: `Code`, `Name`, `UsedFor` (`Won = 1`, `Lost = 2`, `Inactive = 3`), `SortOrder`, `IsActive`.
4. `RelationshipTypes`: `Code`, `Name`, `OnWin` (`None = 0`, `Customer = 1`), `SortOrder`, `IsActive`.
5. `ContactTypes` (global, `BaseAuditableEntity`): `Code`, `Name`, `Icon`, `CommunicationChannelCode?` (se compara por código, SPEC 99), `SchedulesCalendarEvent`, `AsksDuration`, `SortOrder`, `IsActive`.
6. `ContactTypeCompanies` (`BaseTenantEntity`): `ContactTypeId`, `Name`, `SortOrder`, `IsActive`. Único filtrado `(CompanyId, ContactTypeId)`.
7. `LeadCompanySettings` (`BaseTenantEntity`): campos de la regla 6. Único filtrado `(CompanyId)`.

**B. Reglas en Application**
8. `LeadStageKindCoordinator`: impide quedarse sin etapa inicial, ganada o perdida; impide desactivar o borrar las de sistema; el código de una de sistema no cambia.
9. Bloqueo por uso (formato de SPEC 41-post): una etapa, origen, motivo, vinculación o tipo de contacto en uso por algún lead no se borra (`<ENTIDAD>_IN_USE`, lo verifica SPEC 61 al existir `Leads`). Desactivar sí se puede: lo inactivo no se elige en altas nuevas.
10. `ILeadCompanySettingsProvider.GetAsync(companyId)`: si la empresa no tiene fila devuelve los valores por defecto (no falla).

**C. Endpoints** (todos bajo el recurso `LeadSettings`, `[PermissionResource("LeadSettings")]`, una sola opción de menú)
11. `GET/POST/PUT/DELETE /api/v1/LeadStages` + `restore`; `GET/POST/PUT/DELETE /api/v1/LeadSources` + `restore`; `GET/POST/PUT/DELETE /api/v1/LeadReasons` (filtro `usedFor`) + `restore`; `GET/POST/PUT/DELETE /api/v1/RelationshipTypes` + `restore`.
12. `GET /api/v1/ContactTypeCompanies` (con datos del maestro), `PUT /{id}` (nombre, orden, activo). Las filas por empresa se crean con el seed; no hay `POST` ni `DELETE` (la empresa desactiva).
13. `GET/POST/PUT/DELETE /api/v1/ContactTypes` (maestro global) solo `SuperAdmin`. Al crear un tipo maestro se agrega la fila inactiva a todas las empresas.
14. `GET /api/v1/LeadCompanySettings` (fila actual o valores por defecto) y `PUT /api/v1/LeadCompanySettings` (crea o actualiza la fila de la empresa del token).
15. Lecturas para quien trabaja leads sin permiso de parametrización: `GET /api/v1/Leads/catalogs` (SPEC 61) devuelve los catálogos activos bajo el permiso `Leads`.

**D. Menú, permisos y seed**
16. `SystemOption` `LeadSettings` (`/customers/lead-settings`, módulo `Customers`). Permisos: SuperAdmin, Admin y Manager con todo; Supervisor solo lectura.
17. Seed por empresa (`SeedLeadSettingsForCompanyAsync`, idempotente; empresas existentes y nuevas con `POST /Companies`):
    - Etapas: `NEW` Nuevo 10 % (Initial), `CONTACTED` Contactado 25 %, `QUALIFIED` Calificado 40 %, `PROPOSAL` Propuesta 60 %, `NEGOTIATION` Negociación 80 %, `WON` Ganado 100 % (Won), `LOST` Perdido 0 % (Lost).
    - Orígenes: `REFERRAL`, `WEB`, `EVENT`, `WA_IN`, `COLD`, `EMAIL_CAMP`, `PENDING` (persona pendiente, SPEC 65), `MIGRATION` (leads creados por la migración de SPEC 63).
    - Motivos: ganado `BEST_PRICE`, `REFERRAL_TRUST`, `FEATURES`; perdido `HIGH_PRICE`, `COMPETITOR`, `NO_BUDGET`; inactivo `POSTPONED`, `NO_ANSWER`, `NO_CAPACITY`.
    - Vinculación: `PROSPECT_CUSTOMER` y `DISTRIBUTOR` (`OnWin = Customer`); `SUPPLIER`, `PARTNER`, `REFERRER` (`OnWin = None`).
    - Tipos de contacto (maestro): `CALL`, `ONLINE_MEETING` (canal `WEB`), `VISIT`, `EMAIL` (canal `SENDGRID`), `SMS`, `WHATSAPP` (canal `WHATSAPP`), `DEMO`; activos para todas las empresas.
    - `LeadCompanySettings` con los valores por defecto.

**Out of scope:**

- Roles de gestión y grupos (SPEC 60), límites de adjuntos (SPEC 62).
- Inactivación automática (regla 7).
- Etapas distintas por tipo de vinculación (un solo embudo por empresa).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `LEAD_STAGE_KIND_REQUIRED` | 400 | La empresa quedaría sin etapa inicial, ganada o perdida |
| `LEAD_STAGE_SYSTEM` | 400 | Borrar, desactivar o cambiar el código de una etapa `Won`/`Lost` |
| `<ENTIDAD>_NOT_FOUND` | 404 | Registro de otra empresa o inexistente (`LEAD_STAGE`, `LEAD_SOURCE`, `LEAD_REASON`, `RELATIONSHIP_TYPE`, `CONTACT_TYPE`) |
| `<ENTIDAD>_IN_USE` | 409 | Borrar un registro usado por algún lead (con detalle `"Active Leads: N"`) |
| `DUPLICATE_KEY` | 409 | Código o nombre repetido en la empresa, o segunda etapa inicial/ganada/perdida viva |

## Criterios de aceptación

1. Toda empresa existente y toda empresa nueva tiene el seed completo; correr el seed dos veces no duplica.
2. No se puede dejar una empresa sin etapa inicial, ganada o perdida, ni tener dos vivas de cada una (también con requests concurrentes).
3. Las etapas `Won`/`Lost` se renombran pero no se borran, desactivan ni cambian de código.
4. `GET /LeadCompanySettings` de una empresa sin fila devuelve `Warn`, `true`, `LD-` y `6`; `PUT` crea la fila.
5. Un tipo maestro nuevo aparece inactivo en todas las empresas; la empresa lo activa y renombra sin afectar a otras.
6. `LeadReasons?usedFor=Inactive` devuelve solo motivos de inactivación.
7. Sin permiso `LeadSettings` responde 403; los catálogos de una empresa no se ven desde otra.
8. Unit tests de handlers, validadores y coordinador; integración para 1–7; `EXPLAIN` del SQL nuevo.

## Decisiones

- **Una sola opción de menú y un solo recurso de permiso** (`LeadSettings`) para todos los catálogos: misma idea que `[PermissionResource("Persons")]` en los subrecursos de personas; evita llenar el menú y la matriz de permisos con siete opciones.
- **Motivos en un solo catálogo con `UsedFor`:** la pantalla es una y los reportes agrupan igual.
- **Maestro + empresa para tipos de contacto** (valor por defecto aceptado): el código sirve para integrar con canales y calendario (SPEC 65) sin depender del nombre que ponga cada empresa.
- **Duplicados: avisar por defecto, configurable** (decisión del usuario).

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Una empresa reordena etapas y rompe el avance de leads en curso. | El avance (SPEC 61) se calcula siempre con el orden vigente; un lead en una etapa que pasó a otro lugar sigue avanzando a la siguiente según el nuevo orden. |

## Impacto en el frontend

- Pantalla nueva Clientes → Parametrización, con pestañas por catálogo y reglas. Requiere una spec FE nueva.
