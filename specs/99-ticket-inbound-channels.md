# SPEC 37 — Canales internos `WEB` y `APP` en el catálogo de canales

> **Status:** Borrador
> **Depends on:** Ninguna.
> **Related:** SPEC 45 (el calendario registra como origen `WEB` las actividades creadas desde la aplicación), SPEC 48 (ingesta de tickets por WhatsApp y correo, etapa posterior — contenido que antes estaba en esta spec).
> **Date:** 2026-10-01 (reescrita; la versión original del 2026-09-25 se movió a SPEC 48)
> **Objective:** Agregar al catálogo `Common.CommunicationChannels` los canales internos `Web` (`WEB`) y `App` (`APP`), para que un ticket o una actividad creados desde la propia aplicación queden registrados con su origen real, y usar `WEB` como canal por defecto de los tickets en la seed de desarrollo.

> **Historia (2026-10-01):** esta spec se llamaba "Ingesta multicanal: WhatsApp y correo entrante crean tickets" y estaba **Aprobada**. Por decisión del usuario, la creación automática de tickets desde canales externos (WhatsApp **y** correo) queda para una etapa posterior, junto con los agentes de canal. Todo ese diseño se movió sin cambios de fondo a **SPEC 48**. Aquí queda solo la parte que se necesita ya. Por el cambio de alcance, vuelve a **Borrador**.

---

## Por qué existe esta spec

El catálogo de canales sembrado hoy (`SeedCommunicationChannelsAsync`) tiene `SendGrid`, `Telegram`, `Twilio` y `WhatsApp`: todos son proveedores de mensajería externa. No existe ninguna fila que represente **la propia aplicación**.

Consecuencias actuales:

- `Ticket.ChannelId` es obligatorio. Un ticket creado por un usuario desde JOIN queda etiquetado con un proveedor que no intervino; en la seed de desarrollo, `TicketCompanyDefault.ChannelDefaultId` apunta a `WHATSAPP`. Cualquier reporte de "tickets por canal" queda falseado.
- SPEC 45 (calendario) necesita `WEB` como `OriginChannelId` de las actividades creadas desde la aplicación.

Decisiones del usuario (2026-10-01) que acotan esta spec:

- Por ahora, **los tickets los crea un usuario** con acceso al módulo y permisos, desde JOIN (`POST /Tickets`). Puede crearlos a nombre de otra persona (`PersonId`, ya soportado).
- WhatsApp y correo como canales de **ingesta automática** se configuran después de concluir los módulos de Tickets y Calendar (SPEC 48, SPEC 46).

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
- Las búsquedas son siempre por `Code`, nunca por `Name` (el comentario de `CommunicationChannel.Code` lo establece: *"Internal code to facilitate logic in the Application layer"*).

### B. Constantes

- `src/1.Domain/Common/CommunicationChannelCodes.cs` (nuevo): `public static class CommunicationChannelCodes { public const string Web = "WEB"; public const string App = "APP"; public const string WhatsApp = "WHATSAPP"; public const string SendGrid = "SENDGRID"; public const string Telegram = "TELEGRAM"; public const string Twilio = "TWILIO"; }`. La seed de esta spec, SPEC 45 y SPEC 48 usan estas constantes en vez de literales.

### C. Tests

- Prueba de integración del seeder: sobre base limpia y sobre una base ya sembrada, quedan seis canales activos (`SendGrid`, `Telegram`, `Twilio`, `WhatsApp`, `Web`, `App`) sin duplicados, y el `TicketCompanyDefault` de `JOIN-001` apunta a `WEB`.

**Out of scope:**

- **Ingesta automática de tickets por WhatsApp y correo** (`TicketInboundChannel`, webhooks, `ExternalMessageId`) → **SPEC 48**, etapa posterior.
- **Reasignar el canal de tickets existentes.** Los tickets creados antes de esta spec conservan su `ChannelId`; no hay forma confiable de saber cuáles nacieron en la aplicación. Los reportes por canal son confiables desde el despliegue de esta spec.
- **Corregir `Provider = "Meta"` de la fila `WhatsApp`.** La seed es idempotente por `Name` y no actualiza filas existentes; se revisa en SPEC 48, que es donde el transporte real importa.
- **Índice único de `Ticket.Code`.** Ya es `(CompanyId, Code)` en el código. **No** se filtra por `GcRecord`: `TicketCodeGenerator` cuenta también los tickets borrados al calcular la secuencia y nunca reutiliza un código, así que mantener los borrados en el índice garantiza que restaurar un ticket (SPEC 42) no choque con otro.
- Cambios en el frontend: el formulario de nuevo ticket ya precarga el canal desde `TicketCompanyDefault` (SPEC 43 / front 17).

---

## Implementation plan

### F1 — Constantes y seed
1. Crear `CommunicationChannelCodes`.
2. Agregar `Web` y `App` a `SeedCommunicationChannelsAsync`.
3. Cambiar la búsqueda del canal por defecto de la seed de `TicketCompanyDefault` a `WEB` primero.

### F2 — Tests y verificación
1. Prueba de integración del seeder (sección C).
2. `dotnet build` sin warnings nuevos; `dotnet test` en verde.
3. Arrancar en Development contra una base existente y comprobar en `GET /CommunicationChannels` los seis canales, y en `GET /TicketCompanyDefaults` que el canal por defecto es `Web`. Detener la API al terminar.

---

## Acceptance criteria

- [ ] `Common.CommunicationChannels` tiene `Web` (`WEB`, `Internal`) y `App` (`APP`, `Internal`) activos, sin duplicar los existentes, en base limpia y en base ya sembrada.
- [ ] El `TicketCompanyDefault` de desarrollo usa `WEB` como canal por defecto.
- [ ] Ningún código nuevo compara canales por `Name`; se usan las constantes de `CommunicationChannelCodes`.
- [ ] Ningún ticket existente cambia de canal.

---

## Decisions taken and discarded

- **Reducir la 37 y mover la ingesta a SPEC 48** (decisión del usuario, 2026-10-01) vs posponer la 37 completa. Los canales internos se necesitan ya (SPEC 45 y la etiqueta correcta de los tickets creados desde la aplicación); la ingesta externa no.
- **Sembrar `App` aunque hoy no se use** (elegido): completa los cuatro canales que pidió el brief original (WhatsApp, web operativa, correo, app) con una línea idempotente.
- **Sin filtrar por `GcRecord` el índice de `Ticket.Code`** (elegido, corrige la versión original de esta spec): ver Out of scope.

---

## Identified risks

| Riesgo | Mitigación |
|---|---|
| Una base donde alguien creó a mano un canal llamado "Web" con otro `Code`: la seed no inserta la fila (es idempotente por `Name`) y la búsqueda por `Code = "WEB"` no la encuentra. | La seed registra un warning si existe un canal con `Name = "Web"` y `Code` distinto de `WEB`. La corrección es manual desde el CRUD de canales. |
