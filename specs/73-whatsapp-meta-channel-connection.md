# SPEC 73 — WhatsApp (Meta Cloud API): esquema `Channel_Meta`, conexión por empresa, webhook firmado, perfil del negocio y módulo

> **Status:** Borrador
> **Depends on:** SPEC 05 (resiliencia HTTP), SPEC 00-post (catálogo de canales, código `WHATSAPP`), SPEC 43-post (módulos y rutas en inglés), SPEC 48-post (bloqueo por módulo activo), SPEC 49-post (`IFileStorageResolver`, para la foto del perfil). Rama de trabajo: `spec-73-whatsapp-meta-channel-connection`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que cada empresa conecte su propio número de WhatsApp a través de su propia App de Meta, que JOIN reciba y valide los webhooks de esa App, y que el administrador gestione el perfil de WhatsApp Business desde JOIN.
> **Origen:** diseño del canal WhatsApp con el usuario (2026-10-18 y 2026-10-19). Propuesta visual: `join_frontb/SpecPropuestaWhatsApp/PropuestaWhatsApp_JOIN.html` (Configuración › Conexión con Meta y Perfil del negocio). Es la primera de la serie BE-73 a BE-80. Front: FE-48.

---

## Serie WhatsApp (BE-73 a BE-80)

| Spec | Contenido |
|---|---|
| **73** | Esquema, conexión por empresa, secretos cifrados, webhook firmado, perfil del negocio, módulo y menú |
| 74 | Conversaciones, mensajes, estados, marca de identificación, John Doe, personas pendientes, archivos |
| 75 | Agentes y coordinadores, disponibilidad, Cola, En espera, notificaciones, envío de mensajes, SignalR |
| 76 | Plantillas vinculadas con Meta y respuestas rápidas |
| 77 | Etapas del canal, pipeline y homologación con Leads y Clientes |
| 78 | Índice de conversaciones |
| 79 | Automatizaciones básicas |
| 80 | Asistente IA vía n8n |

Orden de implementación: 73 → 74 → 75 → 76 → (77, 78, 79 en cualquier orden) → 80.

## Reglas de negocio (decisiones del usuario, 2026-10-18)

1. **Un número por empresa, con su propia App de Meta.** Cada empresa registra su App ID, App Secret, WABA ID, Phone Number ID y el token de un System User.
2. **La configuración vive en una tabla** (`Channel_Meta.WhatsAppChannels`). Los secretos (App Secret, access token, PIN de dos pasos) se guardan **cifrados**, nunca vuelven por la API y no aparecen en logs.
3. **Vencimiento del token:**
   - Se espera un token sin vencimiento (System User).
   - Al guardarlo, JOIN consulta `GET /debug_token`. Si Meta informa `expires_at`, se guarda en `AccessTokenExpiresAt`; si no, queda nulo (no vence).
   - Si Meta no lo informa y el administrador conoce la fecha, puede escribirla.
   - Con fecha, se avisa al administrador 15, 7 y 1 día antes. Al vencer, el canal pasa a `TokenExpired` sin perder conversaciones, y el administrador registra un token nuevo.
4. **Webhook por empresa:** cada canal tiene una URL propia `POST /api/v1/channels/meta/whatsapp/{webhookToken}`. El token de la ruta identifica la empresa y su App Secret, con el que se valida la firma.
5. **Firma obligatoria:** toda entrega se valida con `X-Hub-Signature-256` (HMAC-SHA256 del cuerpo crudo con el App Secret de esa empresa) **antes** de leer el cuerpo. Si no coincide: 401, sin guardar nada más que un contador.
6. **Respuesta inmediata:** la entrega válida se guarda cruda en `WebhookEvents` (`jsonb`) y se responde 200 enseguida. El procesamiento (BE-74) ocurre en segundo plano, porque Meta reintenta si la respuesta tarda.
7. **Reemplaza a Twilio para WhatsApp.** La ingesta de tickets por Twilio (SPEC 47-post) no se borra y queda para el futuro. Una empresa con canal Meta conectado no usa la ruta de Twilio para WhatsApp.
8. **Esquema `Channel_Meta`** (físico `channel_meta`) para todas las tablas de este canal.
9. **Solo en `main_postgresql`.**
10. **Perfil de WhatsApp Business:** el administrador lee y actualiza desde JOIN la información, la descripción, el correo, la dirección, la categoría, los sitios web y la foto.
    - El nombre visible (display name) se muestra con su estado de aprobación.
    - Su cambio pasa por la revisión de Meta y se solicita en WhatsApp Manager (enlace desde la pantalla).

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Channel_Meta`)
1. `WhatsAppChannel` (`BaseTenantEntity`, índice único filtrado `(CompanyId) WHERE gcrecord = 0`):
   - **Identificación del número y la App:** `AppId` (máx. 50), `WabaId` (máx. 50), `PhoneNumberId` (máx. 50, único global filtrado), `DisplayPhoneNumber` (máx. 30).
   - **Secretos cifrados:** `EncryptedAppSecret`, `EncryptedAccessToken`, `EncryptedTwoStepPin?` (`text`).
   - **Token:** `AccessTokenExpiresAt?`, `AccessTokenCheckedAt?`, `AccessTokenScopes?` (`jsonb`).
   - **Webhook:** `VerifyToken` (máx. 100), `WebhookToken` (máx. 64, único global).
   - **Estado del canal:** `Status` (`Draft = 1`, `Connected = 2`, `Disconnected = 3`, `TokenExpired = 4`), `QualityRating?` (máx. 20), `MessagingLimitTier?` (máx. 30), `ConnectedAt?`.
   - **Avisos de vencimiento:** `LastExpiryWarningDays?` (15, 7 o 1).
2. `BusinessProfile` (`BaseTenantEntity`, 1:1 con el canal):
   - `About?` (máx. 139), `Description?` (máx. 512), `Email?` (máx. 128), `Address?` (máx. 256), `Vertical?` (máx. 50), `Websites` (`jsonb`, hasta 2).
   - Foto: `PhotoPath?` + `PhotoStorageProvider?` (`StorageProviderKind`).
   - Nombre visible: `DisplayName?`, `DisplayNameStatus?` (máx. 30).
   - `SyncedAt?`.
3. `WebhookEvent` (`BaseTenantEntity`): `WhatsAppChannelId`, `ReceivedAt`, `Payload` (`jsonb`), `ProcessedAt?`, `Attempts`, `Error?` (máx. 2000). Índice `(ProcessedAt, ReceivedAt)` filtrado `processedat IS NULL` para el procesador.
4. Migración `Spec73WhatsAppMetaChannel`: esquema, tablas, índices, módulo, opción de menú y permisos.

**B. Cifrado de secretos** (Infrastructure)
5. `ISecretProtector` (Application) con `Protect(string)` / `Unprotect(string)`, implementado con ASP.NET Core Data Protection (propósito `JOIN.ChannelMeta.Secrets`). Es la primera pieza de cifrado del proyecto: `CalendarExternalConnection.EncryptedRefreshToken` (SPEC 46-post) puede adoptarla en su spec de acciones.
6. El llavero de Data Protection debe sobrevivir a contenedores recreados (ver Preguntas abiertas, punto 1). En Development se guarda en disco (`keys/`, ignorado por git).

**C. Cliente de Meta** (Infrastructure)
7. `IMetaGraphClient` sobre `HttpClient` con la resiliencia de SPEC 05. Versión de Graph API configurable (`MetaGraph:Version`, valor inicial `v23.0`) y base `https://graph.facebook.com`. Métodos de esta spec:
   - `DebugTokenAsync`;
   - `GetPhoneNumberAsync`, que devuelve el número visible, la calidad, el límite de mensajes y el nombre visible con su estado;
   - `RegisterPhoneNumberAsync` (PIN) y `SubscribeAppToWabaAsync`;
   - `GetBusinessProfileAsync`, `UpdateBusinessProfileAsync` y `UploadProfilePhotoAsync` (carga reanudable).
8. Los errores de Meta (`error.code`, `error.message`, `fbtrace_id`) se traducen a `META_API_ERROR` (502) con el código de Meta en `errors`.

**D. Webhook** (WebApi, `[AllowAnonymous]`)
9. `GET /api/v1/channels/meta/whatsapp/{webhookToken}`: verificación de Meta. Si `hub.mode = subscribe` y `hub.verify_token` coincide con el del canal, responde `hub.challenge` en texto plano (200); si no, 403.
10. `POST /api/v1/channels/meta/whatsapp/{webhookToken}`:
    1. Lee el cuerpo crudo (`EnableBuffering`) y busca el canal por `WebhookToken`; si no existe, 404.
    2. Valida la firma (regla 5) y guarda el `WebhookEvent`.
    3. Responde 200.
    4. Avisa al procesador por un canal en memoria. El procesador es `IWhatsAppWebhookProcessor` (BE-74); en esta spec, una implementación nula que marca el evento como procesado.
11. Tamaño máximo del cuerpo: 1 MB. Una ruta con canal `Draft` o `Disconnected` responde 200 sin procesar (Meta no debe reintentar), y el evento se guarda con `Error = "CHANNEL_NOT_CONNECTED"`.

**E. Endpoints de administración** (recurso `WhatsAppSettings`)
12. `GET /api/v1/WhatsAppChannel`: configuración sin secretos (los secretos se informan como `hasAppSecret`, `hasAccessToken`, `hasTwoStepPin` y los últimos 4 caracteres).
13. `POST /api/v1/WhatsAppChannel` / `PUT /api/v1/WhatsAppChannel`: alta y cambio. Los secretos solo se mandan para reemplazarlos. Al guardar un token se ejecuta la regla 3.
14. `POST /api/v1/WhatsAppChannel/test` (`CanExecute`):
    - valida el token (`debug_token`, permisos `whatsapp_business_messaging` y `whatsapp_business_management`);
    - lee el número, la calidad y el límite;
    - revisa que la WABA esté suscrita a la App.

    Devuelve un resumen por chequeo. Si todo pasa, `Status = Connected` y `ConnectedAt`.
15. `POST /api/v1/WhatsAppChannel/register-number` y `POST /api/v1/WhatsAppChannel/subscribe-app` (`CanExecute`).
16. `POST /api/v1/WhatsAppChannel/rotate-webhook-token` (`CanExecute`): genera un `WebhookToken` nuevo. La URL anterior deja de funcionar; la pantalla avisa que hay que pegar la nueva en la App de Meta.
17. `GET /api/v1/WhatsAppChannel/profile`, `PUT /api/v1/WhatsAppChannel/profile` y `POST /api/v1/WhatsAppChannel/profile/photo` (multipart; JPG o PNG cuadrada, mínimo 192×192, máximo 5 MB). La foto se guarda en el storage (`channel-meta/{companyId}/profile/…`) y se sube a Meta. `POST /api/v1/WhatsAppChannel/profile/refresh` lee el perfil desde Meta.

**F. Vencimiento del token**
18. `WhatsAppTokenExpiryMonitor` (`BackgroundService`, una vez al día):
    - Por cada canal con `AccessTokenExpiresAt`, registra el aviso del umbral alcanzado (15, 7 o 1 día) en `LastExpiryWarningDays` y lo envía por correo a los usuarios con `CanUpdate` en `WhatsAppSettings` (`IEmailService`). Desde BE-75 también llega como notificación en la app.
    - Al vencer, pasa el canal a `TokenExpired`.
    - Es el primer proceso en segundo plano del fork. Si antes se adopta Hangfire (pendiente de SPEC 44), se migra a un job recurrente.

**G. Módulo, menú y permisos**
19. `SystemModule` `WhatsApp` ("WhatsApp channel module", `fa-brands fa-whatsapp`, `IsBase: false`), activable por empresa (SPEC 48-post).
20. `SystemOption` `WhatsAppMenu` (padre, sin ruta) y `WhatsAppSettings` (`/whatsapp/settings`, `ControllerName` `WhatsAppSettings`, con `CanExecute`). Las demás opciones del módulo las siembra la spec que las usa:
    - `WhatsAppInbox` y `WhatsAppAgents` (BE-75);
    - `WhatsAppTemplates` (BE-76);
    - `WhatsAppPipeline` (BE-77);
    - `WhatsAppConversations` (BE-78);
    - `WhatsAppAutomations` (BE-79).
21. Permisos sembrados: SuperAdmin, Admin y SuperAdminCompany con todo en `WhatsAppSettings`; Manager con lectura.

**Out of scope:**
- Varios números por empresa (multi-WABA).
- Embedded Signup de Meta (alta guiada con login de Facebook): la conexión es manual con los datos de la App.
- Cambiar el nombre visible por API (se hace en WhatsApp Manager).
- Llamadas de voz de WhatsApp.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `WHATSAPP_CHANNEL_NOT_FOUND` | 404 | La empresa no tiene canal |
| `WHATSAPP_CHANNEL_ALREADY_EXISTS` | 409 | Segundo canal para la misma empresa |
| `WHATSAPP_PHONE_NUMBER_IN_USE` | 409 | El `PhoneNumberId` ya está en otra empresa |
| `WHATSAPP_TOKEN_INVALID` | 400 | `debug_token` dice que el token no es válido o le faltan permisos |
| `WHATSAPP_PROFILE_PHOTO_INVALID` | 400 | Formato, tamaño o dimensiones fuera de lo permitido |
| `META_API_ERROR` | 502 | Meta respondió con error; el código de Meta va en `errors` |
| `WEBHOOK_SIGNATURE_INVALID` | 401 | Firma ausente o distinta (solo en el webhook) |

## Implementation plan

1. Esquema `Channel_Meta`, entidades, configuraciones EF y migración. Build verde.
2. `ISecretProtector` con Data Protection y su persistencia del llavero según la decisión del punto 1 de Preguntas abiertas.
3. `IMetaGraphClient` con tests contra un `HttpMessageHandler` falso.
4. Webhook GET/POST con validación de firma, `WebhookEvents` y el procesador nulo. Tests de firma válida, inválida y canal desconectado.
5. Endpoints de configuración, prueba, registro, suscripción y rotación.
6. Perfil del negocio y foto.
7. `WhatsAppTokenExpiryMonitor`.
8. Módulo, menú, permisos e integración (`CustomWebApplicationFactory` con Meta simulado). Smoke real con una App de prueba de Meta y la URL pública del webhook.

## Criterios de aceptación

- [ ] `GET /WhatsAppChannel` nunca devuelve un secreto; en la base los tres secretos están cifrados.
- [ ] Un POST al webhook con firma inválida responde 401 y no crea `WebhookEvent`.
- [ ] Un POST con firma válida responde 200 en menos de 500 ms y deja el evento crudo en `WebhookEvents`.
- [ ] El GET de verificación con el `verify_token` correcto devuelve el `hub.challenge`; con otro, 403.
- [ ] Guardar un token cuyo `debug_token` trae `expires_at` llena `AccessTokenExpiresAt`; uno sin vencimiento lo deja nulo.
- [ ] Un canal cuyo token vence hoy pasa a `TokenExpired` en la siguiente pasada del monitor y se envía el aviso.
- [ ] `test` informa cada chequeo por separado y deja el canal `Connected` solo si todos pasan.
- [ ] Rotar el token de la URL hace que la URL anterior responda 404.
- [ ] El perfil actualizado en JOIN se refleja en Meta (smoke) y la foto queda en el storage con su ruta.
- [ ] Con el módulo WhatsApp inactivo, los endpoints de administración responden según SPEC 48-post; el webhook sigue aceptando y guarda los eventos con `CHANNEL_NOT_CONNECTED`.
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: URL de webhook propia por empresa** (usuario, 2026-10-18). Con una App por empresa, la URL es la que dice qué App Secret valida la firma.
- **Sí: configuración en tabla con secretos cifrados** (usuario, 2026-10-18).
- **Sí: token sin vencimiento, pero si Meta informa una fecha se guarda y se avisa** (usuario, 2026-10-18).
- **Sí: la entrega se guarda cruda y se procesa en segundo plano.** Responder rápido evita los reintentos de Meta, y el evento crudo permite reprocesar si algo falla.
- **Sí: el webhook acepta entregas aunque el módulo esté inactivo,** para que Meta no desactive la suscripción; solo se guardan.
- **No: Embedded Signup.** La conexión manual es la forma más rápida de tener el canal funcionando (usuario, 2026-10-18: "práctico y rápido").

## Preguntas abiertas

1. **Llavero de Data Protection en AWS:** propuesta, guardarlo en S3 (o en la base) protegido con una llave de **AWS KMS**. Alternativa: guardar los secretos en **AWS Secrets Manager** y dejar en la tabla solo su referencia. Hace falta la decisión antes de implementar el paso 2.
2. **Nombres del módulo y del menú:** el módulo se llama `WhatsApp` y las opciones `WhatsAppInbox`, `WhatsAppConversations`, `WhatsAppPipeline`, `WhatsAppTemplates`, `WhatsAppAutomations`, `WhatsAppAgents` y `WhatsAppSettings`, con rutas `/whatsapp/...`. ¿Conforme?

## Riesgos

| Riesgo | Mitigación |
|---|---|
| Se pierde el llavero de Data Protection y los secretos dejan de poder leerse. | Llavero persistente (pregunta 1). Si igual se pierde, el canal pasa a `Disconnected` y el administrador vuelve a cargar los secretos; las conversaciones no se pierden. |
| Meta cambia la versión de Graph API. | Versión configurable y probada en el smoke; los cambios se revisan en cada subida de versión. |
| Alguien descubre la URL del webhook. | Sin la firma con el App Secret no se acepta nada; la URL se puede rotar. |

## Impacto en el frontend

- **FE-48** (Borrador): base del módulo WhatsApp y Configuración › Conexión con Meta y Perfil del negocio.
