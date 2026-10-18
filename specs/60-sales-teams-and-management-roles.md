# SPEC 60 — Grupos de trabajo: varios por usuario, grupo principal, vigencia y roles de gestión con alcance

> **Status:** Implementado
> **Depends on:** SPEC 28 (membresía de usuarios en empresas, `UserCompanies`), SPEC 43-post (módulos, menú y rutas), **SPEC 59** (parametrización comercial). Rama de trabajo: `spec-60-sales-teams-and-management-roles`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-16
> **Objective:** Que cada empresa forme grupos de trabajo con usuarios del sistema, asigne a cada integrante un rol de gestión (líder, coordinador, supervisor, integrante) que define qué leads puede ver, y conserve la historia de quién estuvo en qué grupo y cuándo, para medir resultados por grupo y período.
> **Origen:** diseño del módulo de clientes (2026-10-16, vista "Grupos de trabajo" y "Parametrización → Roles de gestión").

---

## Reglas de negocio (decisión del usuario, 2026-10-16)

1. **Grupos del módulo de clientes** (esquema `Customer`), por empresa.
2. **Un usuario puede estar en varios grupos** a la vez (por ejemplo, un supervisor). Una de sus membresías vigentes es su **grupo principal**: es el que se propone al crear un lead.
3. **Vigencia:** cada membresía tiene `ValidFrom` y `ValidTo`. Una baja no borra: cierra la membresía con fecha. Los reportes por período usan el grupo guardado en el lead (SPEC 61), no las membresías.
4. **Roles de gestión por empresa** con un **alcance** que dice qué leads ve quien tiene el rol: `Own` (solo los suyos), `Team` (los de su grupo) o `Company` (los de cualquier usuario de la empresa). Cada empresa ajusta el alcance de cada rol. Ejemplo: integrante → `Own`; coordinador y líder → `Team`; supervisor → `Company`.
5. **Alcance efectivo** de un usuario = la unión de lo que le da cada membresía vigente; sus propios leads siempre los ve. Quien no está en ningún grupo ve solo los suyos. El SuperAdmin ve todo.
6. **Un líder vigente por grupo** (rol con `IsLeader`).
7. **Los leads inactivos los ve toda la empresa** (SPEC 61); el alcance aplica a los leads abiertos, ganados y perdidos.

## Scope

**In:**

**A. Entidades (esquema `Customer`)**
1. `TeamRoles`: `Code`, `Name`, `LeadScope` (`Own = 1`, `Team = 2`, `Company = 3`), `IsLeader`, `CanReassign`, `SortOrder`, `IsActive`. Únicos filtrados `(CompanyId, Code)` y `(CompanyId, Name)`.
2. `SalesTeams`: `Name`, `Description?`, `IsActive`. Único filtrado `(CompanyId, Name)`.
3. `SalesTeamMembers`: `SalesTeamId`, `UserId`, `TeamRoleId`, `IsPrimary`, `ValidFrom` (`date`), `ValidTo` (`date?`). Vigente = `ValidTo IS NULL OR ValidTo >= hoy`. Único filtrado `(SalesTeamId, UserId)` con `"validto" IS NULL AND "gcrecord" = 0` (una membresía abierta por usuario y grupo); único filtrado `(CompanyId, UserId)` con `"isprimary" = TRUE AND "validto" IS NULL AND "gcrecord" = 0` (un principal vigente por usuario).

**B. Reglas en Application**
4. `SalesTeamMemberPrimaryCoordinator`: la primera membresía vigente de un usuario queda como principal; marcar otra como principal desmarca la anterior; al cerrar la principal, pasa a principal la membresía vigente más antigua que le quede (si tiene).
5. `SalesTeamLeaderCoordinator`: exactamente un líder vigente por grupo con integrantes; asignar el rol de líder a otro integrante quita el rol al anterior (pasa a `MEMBER`), en la misma transacción.
6. El usuario debe ser miembro activo de la empresa (`UserCompanies`, SPEC 28); si deja la empresa, sus membresías vigentes se cierran con la fecha del día (en el mismo comando que lo desvincula).
7. `ILeadAccessScopeResolver.ResolveAsync(companyId, userId)` → `LeadAccessScope { IsCompanyWide, TeamIds[], CanReassignTeamIds[], UserId }`, calculado con las membresías vigentes y el alcance de cada rol (regla 5). Lo usan SPEC 61 (bandeja, detalle, reasignar) y SPEC 64 (reportes). Lectura con Dapper.
8. Un rol en uso por alguna membresía vigente no se borra (`TEAM_ROLE_IN_USE`); un grupo con membresías vigentes no se borra (`SALES_TEAM_IN_USE`), se desactiva. Un grupo inactivo no se elige en leads nuevos.

**C. Endpoints**
9. `GET/POST/PUT/DELETE /api/v1/SalesTeams` + `restore`. `GET /{id}` incluye integrantes vigentes e históricos.
10. `GET /api/v1/SalesTeams/{id}/members`, `POST` (usuario, rol, `ValidFrom`, `IsPrimary`), `PUT /{memberId}` (rol, `IsPrimary`), `POST /{memberId}/close` (`ValidTo`, por defecto hoy). Sin `DELETE`.
11. `GET/POST/PUT/DELETE /api/v1/TeamRoles` + `restore`, bajo el recurso `LeadSettings` (se editan en la pantalla de parametrización, SPEC 59).
12. `GET /api/v1/SalesTeams/me` → membresías vigentes del usuario del token (grupo, rol, principal, alcance), bajo el permiso `Leads` (lo necesita el alta de leads). Sin parámetros: siempre el usuario y la empresa del token.

**D. Menú, permisos y seed**
13. `SystemOption` `SalesTeams` (`/customers/sales-teams`, módulo `Customers`). Permisos: SuperAdmin, Admin y Manager con todo; Supervisor lectura.
14. Seed por empresa de roles (empresas existentes en el arranque y nuevas desde `ICompanyCatalogSeeder.SeedDefaultCatalogsForCompanyAsync`): `SUPERVISOR` "Supervisor comercial" (Company, reasigna), `LEADER` (Team, líder, reasigna), `COORDINATOR` (Team), `MEMBER` (Own). En desarrollo, `JOIN-001` siembra los tres grupos de la propuesta con sus integrantes (incluido un supervisor en dos grupos).

**Out of scope:**

- Metas o cuotas por grupo o usuario.
- Grupos para otros módulos (tickets ya tiene `TicketUserCompany`; si otro módulo los necesita, se evalúa en su spec).
- Aprobaciones dentro del grupo.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `SALES_TEAM_NOT_FOUND` / `TEAM_ROLE_NOT_FOUND` / `SALES_TEAM_MEMBER_NOT_FOUND` | 404 | Registro de otra empresa o inexistente |
| `SALES_TEAM_USER_NOT_IN_COMPANY` | 400 | El usuario no es miembro activo de la empresa |
| `SALES_TEAM_MEMBER_DATES_INVALID` | 400 | `ValidTo` anterior a `ValidFrom` |
| `SALES_TEAM_IN_USE` / `TEAM_ROLE_IN_USE` | 409 | Borrar un grupo o rol con membresías vigentes |
| `DUPLICATE_KEY` | 409 | Segunda membresía abierta del usuario en el mismo grupo, o nombre repetido |

## Criterios de aceptación

1. Un usuario puede tener membresías vigentes en dos grupos; solo una es principal, y marcar la otra cambia el principal en la misma transacción.
2. Cerrar la membresía principal pasa el principal a la vigente más antigua; si no queda ninguna, el usuario queda sin grupo.
3. Un grupo tiene un solo líder vigente; nombrar otro líder pasa al anterior a integrante.
4. `ILeadAccessScopeResolver`: integrante → solo su id; líder de A → grupo A; supervisor (alcance `Company`) en A y B → toda la empresa; integrante en A y líder en B → grupo B más sus propios leads; usuario sin grupos → solo los suyos.
5. Cambiar el alcance de un rol en la empresa cambia lo que resuelve el servicio en la siguiente llamada.
6. Una baja nunca borra filas: el grupo muestra a los integrantes históricos con su `ValidTo`.
7. `GET /SalesTeams/me` devuelve solo las membresías vigentes del usuario del token.
8. Unit tests de coordinadores y del resolver; integración para 1–7; `EXPLAIN` del SQL nuevo.

## Registro de implementación (2026-10-17)

Rama `spec-60-sales-teams-and-management-roles` (desde `spec-59-commercial-settings-catalogs`, para encadenar las migraciones).

- **Dominio y persistencia:**
  - `JOIN.Domain.Customers.Teams`: `TeamRole` (usa el catálogo común de SPEC 59), `SalesTeam`, `SalesTeamMember` y `LeadScope`.
  - Índices `UX_SalesTeamMembers_Team_User_Open` (`"validto" IS NULL AND "gcrecord" = 0`) y `UX_SalesTeamMembers_Company_User_Primary` (`"isprimary" = TRUE AND "validto" IS NULL AND "gcrecord" = 0`), más los únicos de nombre y código.
  - Migración `Spec60SalesTeamsAndManagementRoles`: solo crea tablas e índices.
- **Application:**
  - `SalesTeamMemberPrimaryCoordinator`: la primera membresía abierta queda como principal; marcar otra desmarca la anterior (se guarda primero para no chocar con el índice, dentro de la transacción); al cerrar la principal pasa a la abierta más antigua. `IsPrimary = false` sobre la principal se ignora: el principal se mueve marcando otra.
  - `SalesTeamLeaderCoordinator`: nombrar líder a un integrante pasa al anterior al rol `MEMBER` (o, si no existe, al primer rol activo que no es de líder).
  - `ISalesTeamMembershipCloser`: `RemoveUserCompanyCommandHandler` (SPEC 28) cierra con la fecha del día las membresías abiertas del usuario en esa empresa, en la misma transacción (regla 6).
  - `ILeadAccessScopeResolver` (Dapper): une el alcance de las membresías vigentes (`ValidFrom <= hoy` y `ValidTo` nulo o `>= hoy`); devuelve `IsCompanyWide`, `TeamIds`, `CanReassignTeamIds`, `CanReassignCompanyWide` y los helpers `Covers` / `CanReassign`. El SuperAdmin lo resuelve quien lo usa (SPEC 61).
  - Bloqueos `TEAM_ROLE_IN_USE` y `SALES_TEAM_IN_USE` con detalle `"Active SalesTeamMembers: N"` (membresías vigentes).
- **WebApi:**
  - `SalesTeamsController` (recurso `SalesTeams`), con `/{id}/members`, `PUT /{memberId}` y `POST /{memberId}/close` (`CanUpdate`, cuerpo opcional).
  - `GET /SalesTeams/me` con `[PermissionResource("Leads")]`: hasta que SPEC 61 siembre la opción `Leads`, solo el SuperAdmin lo pasa.
  - `TeamRolesController` (recurso `LeadSettings`).
  - `CommercialResponseResults` reemplaza al mapeo de SPEC 59 y sirve a los tres specs.
- **Seed:**
  - Opción `SalesTeams` (`/customers/sales-teams`); permisos para SuperAdmin y Manager, y solo lectura para Supervisor.
  - Roles por empresa (`SUPERVISOR` "Supervisor comercial", `LEADER`, `COORDINATOR`, `MEMBER`).
  - En `JOIN-001`, los tres grupos de la propuesta (Equipo Capital, Equipo Norte, Cuentas Corporativas) con los usuarios sembrados: `supervisor@join.com` como supervisor comercial en Capital (principal) y Norte. No se crean usuarios nuevos para los nombres de la propuesta.
- **Verificación:**
  - Build Release sin errores.
  - Unit tests: 3346 OK, cobertura de líneas 94,34 %.
  - Integración: 296/296 OK (8 nuevas: roles sembrados, principal en una transacción y traspaso al cerrar, un líder por grupo, resolver con los cinco casos y cambio de alcance, `/me`, cierre al desvincular, permisos y seed de `JOIN-001`, `EXPLAIN`).

## Decisiones

- **Grupos solo del módulo de clientes** (decisión del usuario).
- **Varios grupos con uno principal** (decisión del usuario): permite supervisores en varios grupos sin ambigüedad al registrar.
- **El alcance vive en el rol, no en el usuario** (decisión del usuario): la empresa ajusta qué ve cada rol y todos los que lo tienen cambian a la vez.
- **Rol de gestión ≠ rol de seguridad:** el rol de gestión `SUPERVISOR` (catálogo `TeamRoles`, alcance de leads dentro del módulo) no es el rol de seguridad `Supervisor` de Identity (permisos por pantalla, SPEC 17). Un usuario puede tener uno sin el otro; la pantalla los muestra con nombres distintos ("Supervisor comercial" para el de gestión) para no confundirlos.
- **Atribución por el grupo guardado en el lead:** evita contar un cliente dos veces cuando el usuario está en varios grupos.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Un supervisor con alcance `Company` ve leads de grupos que no le corresponden. | Es el comportamiento pedido; cada empresa puede bajar el alcance del rol a `Team`. |
| Membresías vigentes de usuarios que dejaron la empresa. | Regla 6: se cierran al desvincularlo. |

## Impacto en el frontend

- Specs FE (todas en Borrador, `join_frontb/specs`): **FE-33 (roles de gestión) y FE-34 (grupos)**. Base común del módulo: FE-29.
- Pantalla nueva Clientes → Grupos de trabajo y pestaña "Roles de gestión" en Parametrización.
