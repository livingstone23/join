# Specs del backend (BE)

Índice de todas las specs de este repositorio. Se actualiza **en el mismo cambio** que crea una spec o le cambia el estado, la etapa o las dependencias.

Las specs del frontend viven en `join_frontb/specs` y tienen su propia numeración. Para evitar ambigüedad, entre repositorios se citan con prefijo: **BE-43** (esta carpeta) y **FE-15** (frontend). Dentro de una misma carpeta basta "SPEC 43".

---

## Reglas

### Numeración
- Una spec nueva toma el **siguiente número libre** (hoy: **51**).
- Los números **no se reutilizan ni se reordenan**. Si una spec se divide, la parte que sale toma un número nuevo y ambas lo dicen en su encabezado (ejemplo: SPEC 99 → SPEC 47).
- **Excepción autorizada (2026-10-05):** por decisión del usuario se renumeró una sola vez: la 37 (canales `WEB`/`APP`) pasó a **99** y las 38–49 bajaron un número (38→37 … 49→48). Las referencias se actualizaron en ambos repositorios. Una spec citada con su número anterior en un commit o conversación previa a esa fecha debe traducirse con esta tabla.
- El nombre del archivo es `NN-tema-en-ingles.md` y el título empieza con `# SPEC NN — `.

### Estados
| Estado | Significado | Quién lo asigna |
|---|---|---|
| `Borrador` | Redactada, pendiente de revisión. Toda spec nueva nace así. | Quien la redacta |
| `Aprobado` | Revisada y lista para implementar. | **Solo el usuario** |
| `Implementado` | El código está en `main` y cumple los criterios de aceptación. | Al cerrar la implementación, con confirmación del usuario |
| `Pospuesto` | Diseño guardado para una etapa posterior. No se implementa hasta que el usuario lo retome; al retomarla se revisa contra el estado actual y vuelve a `Borrador`. | El usuario decide la etapa |

### Cambios sobre una spec existente
- **Cambia el alcance o la lógica** (se agregan o quitan funcionalidades, cambian reglas de negocio): vuelve a `Borrador` y el encabezado lleva una nota **"Historia"** que explica qué cambió y por qué.
- **Ajuste editorial o de consistencia** (nombres, rutas, referencias a otra spec): conserva su estado y el encabezado lleva una nota **"Ajuste por SPEC XX (fecha)"**.
- Una decisión que afecta a specs ya escritas se refleja en **todas** ellas en el mismo cambio, incluidas las del frontend (FE) cuando tocan rutas, permisos o contratos de API.

### Antes de implementar
- Revisar las specs posteriores que la citan (`grep "SPEC NN" specs/`) y sus notas de ajuste, historia o etapa. La spec más reciente prevalece.
- Confirmar que todas sus dependencias están `Implementado` o se implementan en el mismo cambio.

---

## Índice

| Nº | Título | Estado | Depende de (según su encabezado) |
|---|---|---|---|
| 01 | TransactionBehavior para Commands en MediatR | Implementado | — |
| 02 | PerformanceBehavior para medición de latencia en MediatR | Implementado | 01 |
| 03 | UnhandledExceptionBehavior y LoggingBehavior | Implementado | 01, 02 |
| 04 | Serilog como motor de logging definitivo | Implementado | 01, 02, 03 |
| 05 | Resiliencia HTTP (Polly v8) y CommandTimeout de EF Core | Implementado | — |
| 06 | Infraestructura base de pruebas de integración | Implementado | 01 |
| 07 | Contenedorización de la API y entorno local mínimo (Docker) | Implementado | — |
| 08 | Versionamiento de API y documentación Scalar | Implementado | 07 |
| 09 | Restaurar el build de CI | Implementado | 01, 06 |
| 10 | Corregir fallas runtime post-SPEC 09 (Person) | Implementado | 01, 06, 09 |
| 11 | Cerrar las fallas restantes de CompanyModules (test-only) | Implementado | 06, 09, 10 |
| 12 | Permisos CanExport y CanExecute en RoleSystemOption | Implementado | — |
| 13 | Corrección de warnings de build y runtime (NuGet + EF Core) | Implementado | — |
| 14 | Saltar el reseed de menú/permisos cuando no es necesario | Implementado | — |
| 15 | Fix de IdentityModel en el collector de HealthChecks.UI | Implementado | 13 |
| 16 | CanExport y CanExecute en SystemOption | Implementado | 12 |
| 17 | `PermissionResource` opcional y override de flags por acción | Implementado | 12, 16 |
| 18 | CRUD de Roles con /detailed y soft delete | Implementado | 17 |
| 19 | Junction `RoleCompany` con CRUD restringido a SuperAdminCompany | Implementado | 17, 18 |
| 20 | Preview de usuarios afectados por rol | Implementado | 17, 18, 19 |
| 21 | SystemOption DTO completeness + ModuleName/ParentName | Aprobado | 16, 18 |
| 22 | RoleSystemOption DTO completeness | Implementado | 12, 16, 17, 21 |
| 23 | RoleSystemOption: tenant desde el JWT en PUT/DELETE | Implementado | 17, 22 |
| 24 | Roles hardening | Implementado | 18, 19, 22 |
| 25 | `RoleSystemOptions` bulk upsert + matrix | Implementado | 18, 22, 23, 24 |
| 26 | `account/*` self-management | Implementado | 18, 22, 23, 25 |
| 27 | Alta administrada de usuarios (invite), activación y reseteo forzado | Aprobado | 17, 23, 25 |
| 28 | Membresía multi-empresa, permisos efectivos y reporte de usuarios | Implementado | 17, 23, 25, 27 |
| 29 | Bitácora de seguridad | Implementado | 17, 23, 25, 27, 28 |
| 30 | Paridad de soft-delete en `UserConnectionLogs` | Implementado | 06, 26 |
| 31 | MFA configuration runbook | Implementado | 26 |
| 32 | Desafío de login MFA (Email OTP + TOTP) | Implementado | 03, 26, 30 |
| 33 | Centralización de `PaginationSettings` | Implementado | — |
| 34 | `TicketUserCompany`: roster de agentes de tickets | Implementado | — |
| 35 | Acciones de ciclo de vida del ticket | Implementado | 34 |
| 36 | Adjuntos de ticket: parámetros, `TicketDocuments` y storage | Implementado | 35 |
| 37 | Transiciones de status parametrizables y SLA | Implementado | 34, 35, 36 |
| 38 | Consolidación de query filters globales | Implementado | 30 |
| 39 | Fork `main_postgresql` | Implementado (en `main_postgresql`) | — |
| 40 | Índices únicos filtrados por soft-delete | Implementado | — |
| 41 | Visibilidad de registros borrados y restauración para SuperAdmin | Aprobado | 38, 40 |
| 42 | Valores iniciales del ticket desde `TicketCompanyDefaults` | Borrador | — |
| 43 | Módulos por empresa: módulos base, menú filtrado y rutas en inglés | Borrador | — |
| 44 | Módulo Calendario: parametrización, calendarios y actividades | Borrador | 99, 43, 48 |
| 45 | Calendario: ingreso por canales (agente) y personas pendientes | **Pospuesto** | 43, 44, 48 |
| 46 | Calendario: estructura para sincronizar con Google Calendar | Borrador | 44 |
| 47 | Ingesta de tickets por WhatsApp y correo | **Pospuesto** | 34, 35, 36, 99, 42, 43 |
| 48 | Bloqueo de APIs y menú por módulo activo de la empresa | Borrador | 43 |
| 49 | Almacenamiento de adjuntos intercambiable: local, Azure Blob, S3 y Cloudflare R2 | Borrador | 36 |
| 50 | `main_postgresql`: port de 40–49 y pendientes para llevar QA a PostgreSQL (archivo en `main_postgresql`) | Borrador | 39, 40–49 |
| 99 | Canales internos `WEB` y `APP` en el catálogo de canales | Borrador | — |

---

## Etapa actual: concluir Tickets y Calendar

Orden sugerido de implementación (cada uno requiere `Aprobado`):

1. **99** — canales `WEB`/`APP` (lo necesitan 42 y 44).
2. **43** — módulos base, opciones conectadas a su módulo, rutas en inglés.
3. **48** — bloqueo por módulo e interruptor `Modules:EnforceCompanyModules`.
4. **42** — estado inicial del ticket.
5. **37** — flujo de estados y SLA, ya aprobada.
6. **44** — calendario base.
7. **46** — estructura de Google Calendar.

En paralelo, sin dependencias con lo anterior: **38 → 40 → 41** (query filters, índices filtrados, restauración) y **39** (fork PostgreSQL, `Implementado` en `main_postgresql`).

**Cierre de etapa — 50:** las specs 40–49 se implementan en `main` (SQL Server). La **50** sale de `main_postgresql`, hace un único `git merge main`, convierte a PostgreSQL todo lo de 40–49 (migración incremental Npgsql, `HasFilter`, SQL) y cierra los pendientes de QA; su PR va a `main_postgresql`, nunca a `main`. Los archivos de las specs 39 y 50 vigentes viven en `main_postgresql`; mientras tanto, el SQL nuevo de 40–49 se escribe portable (ver `CLAUDE.md`) para reducir el port.

**Etapa posterior** (`Pospuesto`): **45** (agente de canales del calendario) y **47** (tickets por WhatsApp y correo), después de concluir Tickets y Calendar.

---

## Pendientes abiertos

Decisiones o verificaciones que no tienen spec propia todavía:

| Origen | Pendiente |
|---|---|
| 43 (A3) | Método para poblar `RoleSystemOptions` de una empresa nueva. Hoy solo `JOIN-001` tiene permisos sembrados; las demás se configuran a mano. |
| 43 (A6) | Asignación automática de módulos base a empresas nuevas y existentes. |
| 45 | `DynamicAuthorizationFilter` usa el claim `CompanyId` del JWT, no `X-Company-Id`: definir cómo opera un agente en varias empresas. |
| 45 | Proceso de limpieza de personas con pendiente `Discarded`. |
| 44 | Recordatorios y tareas automáticas (Hangfire): avisos, cierre de actividades vencidas, barridos de estados. |
| 46 | Acciones de sincronización con Google (OAuth, envío, webhook, worker). |
| 44 | Mejoras diferidas del calendario: invitados, calendarios privados, turnos nocturnos, permisos de medio día. |
| 41 | Las entidades del calendario (44, 45, 46) no existen todavía: entran en la Etapa 4 de la 41 (o en su propia spec) cuando se implementen. |
| 41 | Commands de `Area` y `Project` (crear/editar/borrar) usan `X-Company-Id` sin validarlo contra el token: alinearlos con `TenantResolver` (las queries ya se alinearon en la Etapa 1). |
| 41 | Índices únicos de los catálogos globales (`Country.IsoCode`, `StreetType.Name`, `SystemModule.Name`…) no están filtrados por `GcRecord`: un borrado impide crear otro con la misma clave. Decidir si se filtran (patrón SPEC 40). |
| 38 (F0) | Ejecutar `docs/migrations/spec-38-data-audit.sql` contra la base de desarrollo y pegar los conteos en el PR; si hay filas afectadas, decidir si se reasignan. |
| 38 (D) | Smoke manual: un `SuperAdminCompany` solo ve su empresa y sus módulos, y no puede borrar empresas ni modificar módulos; validar con `join_frontb`. |
| Proceso | Script de verificación de consistencia de specs (referencias inexistentes, nombres retirados, dependencias de specs pospuestas, specs fuera del índice). |
