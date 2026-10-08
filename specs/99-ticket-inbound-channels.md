# SPEC 99 — Canales internos `WEB` y `APP` en el catálogo de canales

> **Status:** Implementado
> **Depends on:** Ninguna.
> **Related:** SPEC 44 (el calendario registra como origen `WEB` las actividades creadas desde la aplicación), SPEC 47 (ingesta de tickets por WhatsApp y correo, etapa posterior — contenido que antes estaba en esta spec).
> **Date:** 2026-10-01 (reescrita; la versión original del 2026-09-25 se movió a SPEC 47)
> **Objective:** Agregar al catálogo `Common.CommunicationChannels` los canales internos `Web` (`WEB`) y `App` (`APP`), para que un ticket o una actividad creados desde la propia aplicación queden registrados con su origen real, y usar `WEB` como canal por defecto de los tickets en la seed de desarrollo.

> **Historia (2026-10-08):** al revisarla contra el código, el usuario decidió: (1) `WEB` y `APP` se agregan también con una **migración de datos** idempotente, porque la siembra completa solo corre cuando hay migraciones pendientes y una base existente (QA) nunca los recibiría; (2) los tickets demo que siembre `SeedJoinTicketsAsync` de aquí en adelante usan `WEB`; (3) si `WEB` existe pero está borrado (SPEC 41), la siembra registra un warning y el SuperAdmin lo restaura con `POST /CommunicationChannels/{id}/restore`. Lo relacionado con WhatsApp sigue en SPEC 47 (etapa posterior). Sigue en **Borrador** hasta que el usuario la apruebe.

> **Numeración (2026-10-05):** esta spec fue la **SPEC 37** hasta el 2026-10-05; se renumeró a 99 por decisión del usuario (ver `specs/README.md`).

> **Historia (2026-10-01):** esta spec se llamaba "Ingesta multicanal: WhatsApp y correo entrante crean tickets" y estaba **Aprobada**. Por decisión del usuario, la creación automática de tickets desde canales externos (WhatsApp **y** correo) queda para una etapa posterior, junto con los agentes de canal. Todo ese diseño se movió sin cambios de fondo a **SPEC 47**. Aquí queda solo la parte que se necesita ya. Por el cambio de alcance, vuelve a **Borrador**.

---

## Por qué existe esta spec

El catálogo de canales sembrado hoy (`SeedCommunicationChannelsAsync`) tiene `SendGrid`, `Telegram`, `Twilio` y `WhatsApp`: todos son proveedores de mensajería externa. No existe ninguna fila que represente **la propia aplicación**.

Consecuencias actuales:

- `Ticket.ChannelId` es obligatorio. Un ticket creado por un usuario desde JOIN queda etiquetado con un proveedor que no intervino; en la seed de desarrollo, `TicketCompanyDefault.ChannelDefaultId` apunta a `WHATSAPP`. Cualquier reporte de "tickets por canal" queda falseado.
- SPEC 44 (calendario) necesita `WEB` como `OriginChannelId` de las actividades creadas desde la aplicación.

Decisiones del usuario (2026-10-01) que acotan esta spec:

- Por ahora, **los tickets los crea un usuario** con acceso al módulo y permisos, desde JOIN (`POST /Tickets`). Puede crearlos a nombre de otra persona (`PersonId`, ya soportado).
- WhatsApp y correo como canales de **ingesta automática** se configuran después de concluir los módulos de Tickets y Calendar (SPEC 47, SPEC 45).

Un usuario que registra a mano un ticket por una persona que llamó o escribió por WhatsApp puede seguir eligiendo `WhatsApp` como canal: el catálogo describe **por dónde llegó la solicitud**, no quién la tecleó. `WEB` es el valor por defecto cuando la solicitud nace en la propia aplicación.

---

## Scope

**In:**

### A. Seed (`src/3.Persistence/Seed/DatabaseSeeder.cs`)

- `SeedCommunicationChannelsAsync`: dos filas nuevas, idempotentes por `Name` como las existentes:

```csharp
new() { Name = "Web", Provider = "Internal", Code = "WEB", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
new() { Name = "App", Provider = "Internal", Code = "APP", IsActive = true, Created = now, CreatedBy = "System_Seeder", GcRecord = 0 },
```

- `Web` = la aplicación web de JOIN (la consola de gestión). `App` = una aplicación propia futura (móvil o portal con login); se siembra ya para que el catálogo esté completo, aunque hoy nada la use.
- Seed de `TicketCompanyDefault` de desarrollo: `ChannelDefaultId` pasa a buscar primero `Code == "WEB"` y, solo si no existe, el comportamiento actual (`WHATSAPP` y luego el primero disponible). Como esa seed ya actualiza la fila existente (`existing.ChannelDefaultId = ...`), las bases de desarrollo quedan con `WEB`.
- Seed de tickets demo (`SeedJoinTicketsAsync`): los tickets que siembre a partir de esta spec usan `WEB` (con el mismo fallback). Los tickets ya sembrados no cambian (decisión 2026-10-08).
- Si existe un canal con `Code = "WEB"` (o `Name = "Web"`) borrado (`GcRecord > 0`), la siembra no lo inserta ni lo reactiva: registra un warning indicando que se restaure con `POST /CommunicationChannels/{id}/restore` (SPEC 41, solo SuperAdmin) y usa el fallback.
- Las búsquedas son siempre por `Code`, nunca por `Name` (el comentario de `CommunicationChannel.Code` lo establece: *"Internal code to facilitate logic in the Application layer"*).

### A2. Migración de datos (decisión 2026-10-08)

- Hoy `Program.cs` corre la siembra completa (`SeedAsync`, que incluye `SeedCommunicationChannelsAsync`) **solo cuando hay migraciones pendientes**; en Development sin migraciones pendientes solo corre `SeedMenuAndPermissionsAsync` (roles, usuarios, opciones y permisos). Esta spec no agrega columnas, así que sin una migración propia `WEB` y `APP` no llegarían a una base que ya existe.
- Migración `Spec99InternalCommunicationChannels`: inserta `Web` (`WEB`, `Internal`) y `App` (`APP`, `Internal`) con `migrationBuilder.Sql(...)`, cada uno solo si no existe una fila con ese `Name` **o** ese `Code` (incluidas las borradas: el índice único de `Name` no está filtrado por `GcRecord`). `Down()` borra solo las filas que insertó (`CreatedBy = 'Spec99_Migration'`) y que ningún ticket o configuración referencia.
- Como la migración queda pendiente, el primer arranque también corre la siembra completa: la de `TicketCompanyDefault` de `JOIN-001` pasa a `WEB` (la seed ya sobreescribe esa fila, comportamiento previo).
- SQL de SQL Server; el port a PostgreSQL entra en SPEC 50 (lista de la sección F de esa spec).

### B. Constantes

- `src/1.Domain/Common/CommunicationChannelCodes.cs` (nuevo): `public static class CommunicationChannelCodes { public const string Web = "WEB"; public const string App = "APP"; public const string WhatsApp = "WHATSAPP"; public const string SendGrid = "SENDGRID"; public const string Telegram = "TELEGRAM"; public const string Twilio = "TWILIO"; }`. La seed de esta spec, SPEC 44 y SPEC 47 usan estas constantes en vez de literales.

### C. Tests

- Prueba de integración del seeder: sobre base limpia (la que crea `CustomWebApplicationFactory`) y volviendo a ejecutar `SeedAsync` sobre la base ya sembrada, quedan seis canales activos (`SendGrid`, `Telegram`, `Twilio`, `WhatsApp`, `Web`, `App`) sin duplicados, y el `TicketCompanyDefault` de `JOIN-001` apunta a `WEB`. Hoy no existe ningún test del seeder; este es el primero.
- Prueba de integración: con `WEB` borrado, una nueva ejecución de la siembra no lo reactiva ni falla, y el canal por defecto cae al fallback.

**Out of scope:**

- **Ingesta automática de tickets por WhatsApp y correo** (`TicketInboundChannel`, webhooks, `ExternalMessageId`) → **SPEC 47**, etapa posterior.
- **Reasignar el canal de tickets existentes.** Los tickets creados antes de esta spec conservan su `ChannelId`; no hay forma confiable de saber cuáles nacieron en la aplicación. Los reportes por canal son confiables desde el despliegue de esta spec.
- **Corregir `Provider = "Meta"` de la fila `WhatsApp`.** La seed es idempotente por `Name` y no actualiza filas existentes; se revisa en SPEC 47, que es donde el transporte real importa.
- **Índice único de `Ticket.Code`.** Ya es `(CompanyId, Code)` en el código. **No** se filtra por `GcRecord`: `TicketCodeGenerator` cuenta también los tickets borrados al calcular la secuencia y nunca reutiliza un código, así que mantener los borrados en el índice garantiza que restaurar un ticket (SPEC 41) no choque con otro.
- Cambios en el frontend: el formulario de nuevo ticket ya precarga el canal desde `TicketCompanyDefault` (SPEC 42 / front 17).

---

## Implementation plan

### F1 — Constantes, seed y migración
1. Crear `CommunicationChannelCodes` y reemplazar los literales de código de canal del seeder (`"WHATSAPP"`) por las constantes.
2. Agregar `Web` y `App` a `SeedCommunicationChannelsAsync`.
3. Cambiar la búsqueda del canal por defecto de la seed de `TicketCompanyDefault` y de `SeedJoinTicketsAsync` a `WEB` primero; warning si `WEB` está borrado.
4. Migración de datos `Spec99InternalCommunicationChannels` (sección A2). La base de QA es compartida: avisar antes de levantar la API contra ella.

### F2 — Tests y verificación
1. Prueba de integración del seeder (sección C).
2. `dotnet build` sin warnings nuevos; `dotnet test` en verde.
3. Arrancar en Development contra una base existente (la migración de datos queda pendiente y se aplica) y comprobar en `GET /CommunicationChannels` los seis canales, y en `GET /TicketCompanyDefaults` que el canal por defecto es `Web`. Detener la API al terminar.

---

## Acceptance criteria

- [ ] `Common.CommunicationChannels` tiene `Web` (`WEB`, `Internal`) y `App` (`APP`, `Internal`) activos, sin duplicar los existentes, en base limpia y en base ya sembrada.
- [ ] El `TicketCompanyDefault` de desarrollo usa `WEB` como canal por defecto.
- [ ] Ningún código nuevo compara canales por `Name`; se usan las constantes de `CommunicationChannelCodes`.
- [ ] Ningún ticket existente cambia de canal; los tickets demo nuevos de la siembra usan `WEB`.
- [ ] Una base existente sin otras migraciones pendientes recibe `WEB` y `APP` al aplicar `Spec99InternalCommunicationChannels`.
- [ ] Un `WEB` borrado no se reactiva desde la siembra: queda un warning en el log.

---

## Decisiones de implementación (Ajuste por SPEC 99, 2026-10-08)

- `ResolveDefaultTicketChannelAsync` (seeder) concentra la elección del canal de la siembra de tickets: `WEB` activo → `WHATSAPP` → el canal activo más antiguo. La usan la siembra de `TicketCompanyDefault` y `SeedJoinTicketsAsync`.
- `SeedCommunicationChannelsAsync` registra un warning cuando un canal sembrado existe con el mismo `Name` y otro `Code` (riesgo de la tabla de abajo); no lo corrige.
- `SeedJoinTicketsAsync`: el log de creación que se agrega a un ticket demo ya existente sin log nombra el canal solo si es el del ticket (los tickets anteriores conservan su canal).
- Migración `Spec99InternalCommunicationChannels`: sin cambios de modelo; `INSERT` condicionado por `Name` o `Code`, `CreatedBy = 'Spec99_Migration'`. El test de integración comprueba que en una base limpia las filas `WEB`/`APP` las inserta la migración (antes de la siembra).
- Tests: `tests/IntegrationTests/Persistence/CommunicationChannelSeedTests.cs` (base limpia + segunda siembra; `WEB` borrado → no se reactiva y el canal por defecto cae a `WHATSAPP`).

## Decisions taken and discarded

- **Reducir la 99 y mover la ingesta a SPEC 47** (decisión del usuario, 2026-10-01) vs posponer la 99 completa. Los canales internos se necesitan ya (SPEC 44 y la etiqueta correcta de los tickets creados desde la aplicación); la ingesta externa no.
- **Sembrar `App` aunque hoy no se use** (elegido): completa los cuatro canales que pidió el brief original (WhatsApp, web operativa, correo, app) con una línea idempotente.
- **Sin filtrar por `GcRecord` el índice de `Ticket.Code`** (elegido, corrige la versión original de esta spec): ver Out of scope.

---

## Identified risks

| Riesgo | Mitigación |
|---|---|
| Una base donde alguien creó a mano un canal llamado "Web" con otro `Code`: la seed no inserta la fila (es idempotente por `Name`) y la búsqueda por `Code = "WEB"` no la encuentra. | La seed registra un warning si existe un canal con `Name = "Web"` y `Code` distinto de `WEB`. La corrección es manual desde el CRUD de canales. La migración tampoco inserta (chequea `Name` o `Code`). |
| `WEB` borrado por un usuario (SPEC 41): la seed no lo reactiva y el canal por defecto cae a `WHATSAPP` o al primero disponible. | Warning en el log; el SuperAdmin lo restaura con `POST /CommunicationChannels/{id}/restore` (decisión 2026-10-08). |
| La migración de datos usa SQL de SQL Server. | Se convierte al portar a `main_postgresql` (SPEC 50). |
