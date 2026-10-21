# SPEC 68 — Calendario: índice de actividades con filtros y permiso por usuario para ver las de otros

> **Status:** Implementado
> **Depends on:** SPEC 43-post (opciones de menú y rutas en inglés), SPEC 44-post (actividades, ocurrencias, `CalendarUserConfiguration`, recurso `CalendarOthers`), SPEC 48-post (bloqueo por módulo), SPEC 67 (vínculo Persona/Cliente coherente). Rama de trabajo: `spec-68-calendar-activity-index`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que el usuario consulte en una lista paginada y exportable las actividades del calendario (citas por defecto) con filtros, viendo solo las propias salvo que un administrador le permita ver las de otros usuarios de la empresa.
> **Revisión de implementación (2026-10-19):** la consulta toma las series por `RecurrenceEndUtc` (como "Mi calendario") y no solo cuando su primera ocurrencia cae en el rango; los filtros se aplican por ocurrencia, así que una ocurrencia modificada que no pasa un filtro no hace reaparecer la de la serie; `OccurrenceStartUtc/EndUtc` son el horario real; SuperAdmin exporta sin chequeo, como en el filtro global; el seed del menú vive en `DatabaseSeeder`. Ajuste editorial, mantiene el estado.
> **Origen:** pedido del usuario (2026-10-19): "un índice de Citas con tipos de filtros". Se divide con SPEC 67 (Persona/Cliente) y SPEC 69 (comentarios y documentos). Front: FE-43.

---

## Reglas de negocio (decisión del usuario, 2026-10-19)

1. **Todas las actividades, filtro por tipo.** El índice lista actividades de cualquier tipo; el front preselecciona el tipo "Cita" (`APPOINTMENT`).
2. **Por defecto, solo las propias** (actividades de calendarios cuyo dueño es el usuario).
3. **Ver las de otros es un permiso por usuario**, no por rol: `CalendarUserConfiguration.CanViewOthersActivities` (default `false`). Con `true`, el índice muestra las de todos los usuarios de la empresa.
4. El flag lo cambia un administrador en Calendario → Configuración → pestaña "Usuarios" (recurso `CalendarSettings`, `CanUpdate`). El usuario no puede cambiarse el suyo (`PUT /CalendarUserConfigurations/me` lo ignora).
5. **El Panel no cambia:** sigue con el permiso de rol `CalendarOthers` (BE-44-post). El flag solo rige el índice (y, en SPEC 69, quién ve comentarios y documentos).
6. **Una fila por ocurrencia:** las actividades periódicas se expanden dentro del rango, como "Mi calendario". Rango máximo **92 días**.
7. **Filtros:** desde/hasta (obligatorios), tipos, estados, usuarios dueños y calendarios (solo con el flag; sin él se ignoran), persona/cliente (`RequesterPersonId`, SPEC 67), canal de origen, texto libre (título, resumen, lugar) e incluir anuladas.
8. **Exportar** a Excel/PDF requiere `CanExport` y devuelve hasta 10 000 filas.
9. Abrir una actividad desde el índice muestra su detalle e historial aunque sea de otro usuario (con el flag), en solo lectura; editar o cambiar estado sigue los permisos de "Mi calendario" y del Panel.

## Scope

**In:**

**A. Dominio y persistencia**
1. `CalendarUserConfiguration.CanViewOthersActivities` (`bool`, default `false`).
2. Migración `Spec68CalendarActivityIndex`: la columna (default `false`). La opción de menú y sus permisos se siembran en `DatabaseSeeder` (`SystemOptionSeed` / `RoleSystemOptionSeed`), que corre después de la migración.

**B. Configuración por usuario**
3. `GET /CalendarUserConfigurations` y `GET /CalendarUserConfigurations/{userId}` devuelven `CanViewOthersActivities`; `PUT /CalendarUserConfigurations/{userId}` lo recibe. `PUT …/me` lo ignora (regla 4).

**C. Índice** — `CalendarActivityIndexController`, recurso `CalendarActivityIndex`
4. `GET /api/v1/CalendarActivityIndex?from&to&activityCompanyIds&statusCompanyIds&ownerUserIds&calendarIds&requesterPersonId&originChannelIds&search&includeCancelled&pageNumber&pageSize&export` → `Response<PagedResult<CalendarActivityIndexItemDto>>`. Con `export=true` exige `CanExport` (SuperAdmin pasa sin chequeo, como en `DynamicAuthorizationFilter`) y usa `pageSize = 10 000`. Las series entran por `RecurrenceEndUtc` y sus excepciones se leen siempre; todos los filtros se aplican a cada ocurrencia ya expandida.
5. `GET /api/v1/CalendarActivityIndex/{id}` y `/{id}/logs`: detalle e historial (mismos DTO que `CalendarEvents`), con la regla de visibilidad 2–3.
6. `GET /api/v1/CalendarActivityIndex/owners?search&limit`: usuarios de la empresa con calendario para el filtro "Dueño" (`limit` por defecto y máximo 50; solo con el flag; sin él devuelve solo al usuario actual).
7. `CalendarActivityIndexItemDto`: `EventId`, `ExceptionId?`, `OccurrenceStartUtc`, `OccurrenceEndUtc`, `IsAllDay`, `IsRecurring`, `Title`, `ActivityCompanyId`, `ActivityName`, `ActivityColor`, `StatusCompanyId`, `StatusName`, `OwnerUserId`, `OwnerName`, `CalendarName`, `RequesterPersonId?`, `RequesterPersonName?`, `CustomerCode?`, `OriginChannelCode`, `Location?`. `OccurrenceStartUtc/EndUtc` son el horario real de la ocurrencia (una modificada muestra el suyo); el detalle se abre con `ExceptionId ?? EventId`.

**D. Menú y permisos**
8. `SystemOption` `Activities` → ruta `/calendar/activities`, módulo `Calendar`, recurso `CalendarActivityIndex`, visible en menú. Seed por rol: los roles con `MyCalendar` reciben `CanRead`; `SuperAdmin`, `Manager` y `Supervisor` además `CanExport`. Admin y SuperAdminCompany por `PrivilegedRoleNames`.

**Out of scope:**
- Editar o cambiar estado desde la lista (se hace desde el detalle con los permisos actuales).
- Que el flag habilite el Panel o escribir en calendarios ajenos.
- Vistas guardadas de filtros (lo cubre `AppDataGrid` en el front, FE-00-02).

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `CALENDAR_RANGE_TOO_LARGE` | 400 | `to <= from` o rango mayor de 92 días (mismo código que el rango de `CalendarEvents`). |
| `CALENDAR_EVENT_NOT_FOUND` | 404 | La actividad no existe, es de otra empresa o es ajena y el usuario no tiene el flag. |

## Plan de implementación

1. Columna `CanViewOthersActivities`, migración y su lectura/escritura en `CalendarUserConfigurations` (regla 4); unit tests.
2. Servicio de visibilidad del índice (`CalendarActivityVisibility`: propio o flag) reutilizable por SPEC 69; unit tests.
3. Query del índice con Dapper: candidatas por rango y filtros, expansión de ocurrencias con el expansor existente, orden por inicio, paginación y exportación; unit tests.
4. Detalle, historial y dueños del índice.
5. Controlador, opción de menú y permisos por rol (seed + migración).
6. Integration tests y `EXPLAIN` del SQL nuevo.

## Criterios de aceptación

1. Un usuario sin el flag ve solo sus actividades aunque envíe `ownerUserIds` de otros.
2. Con el flag ve las de toda la empresa y puede filtrar por dueño y calendario; nunca ve otra empresa.
3. Una serie semanal en un rango de 30 días aparece una vez por ocurrencia, y una ocurrencia modificada muestra sus datos propios.
4. Cada filtro (tipo, estado, persona/cliente, canal, texto, anuladas) reduce el resultado según su regla; rango > 92 días responde 400.
5. `export=true` sin `CanExport` responde 403; con permiso devuelve hasta 10 000 filas.
6. `PUT /CalendarUserConfigurations/me` no cambia el flag; `PUT /CalendarUserConfigurations/{userId}` con `CalendarSettings.CanUpdate` sí.
7. El detalle de una actividad ajena responde 404 sin el flag y 200 con él.
8. Unit tests de visibilidad, filtros y paginación; integración para 1–7; `EXPLAIN` del SQL nuevo.

## Decisiones

- **Flag por usuario en `CalendarUserConfiguration`** en vez de permiso de rol (decisión del usuario): el administrador decide persona por persona; la pestaña "Usuarios" de la configuración ya lista a cada usuario con calendario, así que no hace falta una sección nueva.
- **El Panel conserva su permiso de rol:** cambiarlo obligaría a sembrar el flag para quien hoy usa el Panel.
- **Controlador propio** (`CalendarActivityIndex`) y no `CalendarActivities`: ese nombre ya es el catálogo de tipos de actividad (BE-44-post).
- **92 días** en vez de los 62 del Panel: un índice se usa para revisar un trimestre; la expansión se hace por página igual.

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Muchas series largas con el flag (toda la empresa) hacen pesada la expansión. | Rango máximo de 92 días, candidatas filtradas en SQL antes de expandir, y límite de exportación. |
| Un admin da el flag sin querer. | El flag se ve en la pestaña "Usuarios" y el cambio queda en la auditoría de la configuración. |

## Impacto en el frontend

- **FE-43** (Borrador): página "Actividades" (`/calendar/activities`) con `AppDataGrid`, filtros y exportación; columna/flag "Ve actividades de otros" en la pestaña "Usuarios" de FE-25.
