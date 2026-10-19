# SPEC 76 — WhatsApp: plantillas vinculadas con Meta y respuestas rápidas

> **Status:** Borrador
> **Depends on:** **SPEC 73** (canal, cliente de Meta, webhook), **SPEC 74** (mensajes), **SPEC 75** (envío y ventana de 24 h). Rama de trabajo: `spec-76-whatsapp-templates-quick-replies`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Crear, enviar a aprobación y sincronizar las plantillas de mensaje de Meta desde JOIN, vinculadas en ambos lados por su id, y usarlas al responder, sobre todo con la ventana de 24 h cerrada. Además, respuestas rápidas para el agente.
> **Origen:** diseño del canal WhatsApp (2026-10-18). Propuesta visual: `join_frontb/SpecPropuestaWhatsApp/PropuestaWhatsApp_JOIN.html` (Plantillas, modal "Enviar plantilla", Configuración › Respuestas rápidas). Front: FE-51.

---

## Reglas de negocio (decisiones del usuario, 2026-10-18)

1. **Vinculadas en ambos lados:**
   - Una plantilla creada en JOIN se envía a Meta (`POST /{waba-id}/message_templates`) y guarda el `MetaTemplateId` que devuelve.
   - Una creada en WhatsApp Manager se importa al sincronizar (`GET /{waba-id}/message_templates`), con `Origin = Meta`.
   - La clave de cruce es `MetaTemplateId`; sin él, nombre + idioma.
2. **Aprobación de Meta:**
   - Solo las plantillas `APPROVED` se pueden enviar.
   - El estado llega por el webhook `message_template_status_update` (BE-73/74) y por la sincronización manual.
   - Un rechazo guarda el motivo de Meta.
3. **Categorías y componentes:**
   - Categorías `UTILITY`, `MARKETING` y `AUTHENTICATION`.
   - Componentes: encabezado (texto, imagen, video o documento), cuerpo con `{{n}}`, pie y hasta 10 botones (respuesta rápida, URL o llamada).
   - Las reglas de formato de Meta se validan **antes** de enviar: nombre en minúsculas con `_`, variables consecutivas, ni al inicio ni al final del cuerpo, y un ejemplo por variable.
4. **Variables con origen:** cada `{{n}}` declara de dónde sale su valor al enviar. El agente puede editar los valores antes de enviar. Orígenes posibles:
   - persona: primer nombre, nombre completo o identificación;
   - agente: nombre;
   - conversación: código;
   - lead: código;
   - actividad: fecha y hora;
   - texto fijo.
5. **Editar una plantilla aprobada** se hace en Meta (`POST /{template-id}`) y vuelve a revisión. Borrar se hace en Meta y en JOIN queda soft delete.
6. **Respuestas rápidas:**
   - atajos por empresa (`/precio`) con un texto;
   - el agente las usa escribiendo `/` en la bandeja;
   - no pasan por Meta, así que solo sirven con la ventana abierta.

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Channel_Meta`)
1. `Template` (`BaseTenantEntity`, único `(CompanyId, Name, Language)` filtrado):
   - **Identidad:** `MetaTemplateId?` (máx. 50), `Name` (máx. 512), `Language` (máx. 10), `Category`.
   - **Estado en Meta:** `Status` (`Draft`, `Pending`, `Approved`, `Rejected`, `Paused`, `Disabled`), `RejectedReason?`.
   - **Componentes:**
     - encabezado: `HeaderType?` y `HeaderText?`;
     - encabezado con archivo: `HeaderMediaPath?` + `HeaderMediaProvider?` y `HeaderMediaHandle?` (el identificador de la carga reanudable);
     - `Body` (máx. 1024), `Footer?` (máx. 60), `Buttons` (`jsonb`).
   - **Origen y sincronización:** `Origin` (`Join = 1`, `Meta = 2`), `LastSyncedAt?`.
2. `TemplateVariable`: `TemplateId`, `Position`, `Source` (enum de la regla 4), `FixedText?`, `Sample` (máx. 200).
3. `QuickReply` (`BaseTenantEntity`, único `(CompanyId, Shortcut)` filtrado): `Shortcut` (máx. 30, empieza con `/`), `Text` (máx. 1024), `IsActive`.
4. `Message.TemplateId` (BE-74) pasa a tener FK.
5. Migración `Spec76WhatsAppTemplates`, con menú y permisos.

**B. Cliente de Meta**
6. `IMetaGraphClient` suma `CreateTemplateAsync`, `EditTemplateAsync`, `DeleteTemplateAsync`, `ListTemplatesAsync` (con paginación de Meta) y `UploadResumableAsync`, que se usa para el ejemplo del encabezado con archivo.
7. El procesador de BE-74 atiende el campo `message_template_status_update` y actualiza `Status` y `RejectedReason`. Además publica `TemplateStatusChanged`.

**C. Endpoints** (recurso `WhatsAppTemplates`)
8. `GET /api/v1/WhatsAppTemplates` (filtros: estado, categoría, idioma, origen) y `GET /{id}`.
9. `POST /api/v1/WhatsAppTemplates` (borrador), `PUT /{id}` (borrador o edición) y `DELETE /{id}`.
10. `POST /api/v1/WhatsAppTemplates/{id}/submit` (`CanExecute`): valida la regla 3, sube el archivo de ejemplo si hay y envía a Meta; queda `Pending` con su `MetaTemplateId`.
11. `POST /api/v1/WhatsAppTemplates/sync` (`CanExecute`): importa las nuevas, actualiza los estados y marca las que ya no están en Meta (`Disabled`). Devuelve el resumen: importadas, cambiadas y sin cambio.
12. `POST /api/v1/WhatsAppTemplates/{id}/render` `{ conversationId }`: valores sugeridos de las variables según su origen y la vista previa.
13. Envío: `POST /api/v1/WhatsAppConversations/{id}/messages` (BE-75) acepta `type = template` con `{ templateId, parameters[] }`.
    - Solo plantillas `Approved`; si no, `TEMPLATE_NOT_APPROVED`.
    - Con la ventana cerrada abre una conversación nueva (BE-75, regla 8).
14. Recurso `WhatsAppSettings`: `GET/POST/PUT/DELETE /api/v1/WhatsAppQuickReplies`.
15. `SystemOption` `WhatsAppTemplates` (`/whatsapp/templates`, módulo `WhatsApp`, con `CanExecute`). Permisos: Admin, SuperAdminCompany y Coordinador con todo; los demás roles de la bandeja con lectura, para poder elegir la plantilla al enviar.

**Out of scope:**
- Plantillas de autenticación con OTP generado por JOIN.
- Plantillas de carrusel y de Flows de Meta.
- Envío masivo de plantillas (campañas de marketing).
- Variables con nombre (`{{nombre}}`): se usan las posicionales.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `TEMPLATE_NOT_FOUND` | 404 | De otra empresa o inexistente |
| `TEMPLATE_NAME_INVALID` | 400 | No cumple minúsculas y `_`, o pasa de 512 |
| `TEMPLATE_VARIABLES_INVALID` | 400 | Variables no consecutivas, al inicio o al final, o sin ejemplo |
| `TEMPLATE_ALREADY_EXISTS` | 409 | Mismo nombre e idioma en la empresa |
| `TEMPLATE_NOT_APPROVED` | 409 | Se intenta enviar una que no está `Approved` |
| `TEMPLATE_PARAMETERS_MISMATCH` | 400 | Cantidad de parámetros distinta a la de variables |
| `QUICK_REPLY_SHORTCUT_INVALID` / `QUICK_REPLY_ALREADY_EXISTS` | 400 / 409 | Atajo sin `/` o repetido |
| `META_API_ERROR` | 502 | Meta rechazó la operación |

## Implementation plan

1. Entidades, migración, menú y permisos.
2. Validador de formato de Meta, con tests de cada regla.
3. Crear, enviar a revisión, editar y borrar contra Meta; sincronización.
4. Webhook de estado de plantillas.
5. `render` y envío de plantilla (con ventana cerrada → conversación nueva).
6. Respuestas rápidas.
7. Integración y smoke: crear una plantilla de utilidad, esperar la aprobación y enviarla a un teléfono real.

## Criterios de aceptación

- [ ] Una plantilla creada en JOIN y enviada a revisión queda `Pending` con `MetaTemplateId`; el webhook de aprobación la deja `Approved`.
- [ ] Sincronizar importa una plantilla creada en WhatsApp Manager con `Origin = Meta` y no duplica las existentes.
- [ ] Un cuerpo que termina en `{{1}}` responde 400 `TEMPLATE_VARIABLES_INVALID` sin llamar a Meta.
- [ ] Enviar una plantilla `Pending` responde 409 `TEMPLATE_NOT_APPROVED`.
- [ ] `render` con una conversación de Livingstone Cano sugiere "Livingstone" para "Persona · primer nombre".
- [ ] Una plantilla enviada con la ventana cerrada crea una conversación nueva con el siguiente código del día.
- [ ] Una respuesta rápida sin `/` responde 400.
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: vincular las plantillas en ambos lados, con la aprobación de Meta** (usuario, 2026-10-18).
- **Sí: validar el formato antes de enviar a Meta**, para no gastar revisiones en rechazos previsibles.
- **Sí: las variables declaran su origen**, para que el agente no tenga que escribir los datos que JOIN ya conoce.
- **No: las respuestas rápidas no se mandan a Meta.** Son texto libre y solo valen con la ventana abierta.

## Impacto en el frontend

- **FE-51** (Borrador): pantalla de plantillas con editor y vista previa, modal "Enviar plantilla" y respuestas rápidas.
