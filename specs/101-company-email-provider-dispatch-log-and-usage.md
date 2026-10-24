# SPEC 101 — Correo por empresa: cuenta de SendGrid propia o de la plataforma, salida única con registro de cada envío, cuotas y reportes de uso por módulo

> **Status:** Borrador
> **Depends on:** SPEC 43-post (menú y permisos), SPEC 72 (`EmailCompanySettings`, `PersonEmailSender`, `IEmailService.SendAsync`), SPEC 41-post (borrado lógico). Rama de trabajo: `spec-101-company-email-provider`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Que cada empresa de JOIN envíe sus correos con **su propia cuenta de SendGrid** (o con la de la plataforma si no tiene), que **todo correo** del sistema salga por **un único punto** que lo registra (empresa, módulo, función, usuario, resultado), que la plataforma controle **cuántos correos envía cada empresa** con cuotas, y que haya **reportes por período, módulo y función** con un resumen de uso. Los módulos futuros (documentos, alertas, WhatsApp, tickets…) quedan medidos sin trabajo extra.
> **Ajuste (2026-10-24, pedido del usuario):** cada correo lleva **nuestro id** (`DispatchId`) enviado a SendGrid como argumento propio y guarda el **id de SendGrid**, para que una spec futura reciba el webhook de eventos y conozca el estado de cada correo (regla 10).
> **Origen:** pedido del usuario (2026-10-24): "centralizar la configuración de SendGrid por empresa, porque cada API que se genere será orientada a una empresa que se cree en JOIN, y controlar cuántos correos envía cada empresa… generar reportes por período de los correos que se envíen, por módulos o secciones, para hacer un agrupamiento y un resumen de cómo se utiliza, con el fin de que los próximos módulos igual se puedan medir".
> **Etapa:** el usuario la pide **antes de arrancar** la serie 81–100. SPEC 84 (envío de documentos), 85 (correo de alertas) y 87, 94 y 98 envían correos y deben usar la salida única de esta spec.

---

## Estado actual (revisión del código, 2026-10-24)

- **Una sola cuenta de SendGrid para toda la plataforma:** `SendGrid:ApiKey`, `FromEmail` y `FromName` en la configuración (`SendGridOptions`). `Email:Provider = Log` en desarrollo.
- `EmailCompanySettings` (`Admin`, SPEC 72) guarda por empresa el remitente (`FromEmail`, `FromName`), el máximo de envíos por usuario por día y el tamaño total de adjuntos. **No** guarda credenciales.
- `IEmailService` tiene dos métodos: `SendEmailAsync(to, subject, html)` (sin empresa ni registro) y `SendAsync(EmailMessage)` (SPEC 72).
- **Puntos que envían hoy** y si dejan rastro:

  | Punto | Empresa conocida | Registro |
  |---|---|---|
  | `PersonEmailSender` (personas, clientes, leads; SPEC 72) | Sí | `PersonEmailMessages` (con cuerpo) |
  | `ForgotPasswordCommandHandler` | No (antes de iniciar sesión) | Ninguno |
  | `SetupPassword` / `InviteUserCommandHandler` | Sí (la empresa que invita) | Ninguno |
  | `ForceUserPasswordResetCommandHandler` | Sí | Ninguno |
  | `RequestEmailChangeCommandHandler` | No (cuenta del usuario) | Ninguno |
  | `EmailOtpSendCodeCommandHandler` | No | Ninguno |
  | `SendMfaChallengeCommandHandler` | No | Ninguno |
  | `HealthCheckEmailPublisher` | No (sistema) | Ninguno |

- No hay cifrado de secretos configurado (ASP.NET Core Data Protection no está registrado).

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Cuenta de envío por empresa** (`EmailCompanySettings` suma `ProviderMode`):
   - `Platform = 1` (por defecto): usa la cuenta de SendGrid de JOIN (configuración global).
   - `Own = 2`: usa la **API key de SendGrid de la empresa**. La key se guarda **cifrada** (regla 9), nunca se devuelve por la API (solo los últimos 4 caracteres) y se **verifica** con un envío de prueba antes de activarse (`VerifiedAt`). Si una key propia falla por autenticación (401/403), el envío queda `Failed` con el motivo y se marca `VerificationError`; **no** cae en silencio a la cuenta de la plataforma, salvo que la empresa active `FallbackToPlatform`.
   - El remitente sigue siendo el de la empresa (`FromEmail`, `FromName`) o, sin él, el global. Con `Own`, el remitente debe estar verificado en la cuenta de SendGrid de la empresa (lo comprueba el envío de prueba).
2. **Salida única de correo** (`IEmailDispatcher`, Application): **todo** correo del sistema pasa por ella. Recibe el mensaje y su **clasificación**:
   - `CompanyId?` (null solo para correos de sistema o de cuenta sin empresa);
   - `Module` (catálogo fijo, regla 3) y `Feature` (código de la función, regla 3);
   - `UserId?` (quien lo dispara), `SourceKind?` y `SourceId?` (entidad de origen, p. ej. el documento o la alerta).

   El despachador: resuelve la cuenta (regla 1) y el remitente, aplica las **cuotas** (regla 5), envía con el adaptador del proveedor, y **registra el intento** en `EmailDispatchLogs` (regla 4), haya salido bien, mal o bloqueado. `IEmailService` queda como adaptador del proveedor, interno de Infrastructure: ningún handler lo usa directamente (`SendEmailAsync` se retira de los handlers).
3. **Módulos y funciones** (estándar, para agrupar y medir):

   | `Module` | `Feature` (ejemplos) |
   |---|---|
   | `Security = 1` | `Security.ForgotPassword`, `Security.MfaChallenge`, `Security.EmailOtp`, `Security.EmailChange`, `Security.ForcePasswordReset` |
   | `Users = 2` | `Users.Invitation`, `Users.SetupPassword` |
   | `Customers = 3` | `Customers.PersonEmail`, `Customers.CustomerEmail`, `Customers.LeadEmail` (SPEC 72) |
   | `CommercialDocuments = 4` | `Documents.Send` (SPEC 84) |
   | `Alerts = 5` | `Alerts.<AlertType>` (SPEC 85) |
   | `Tickets = 6` | reservado |
   | `Calendar = 7` | reservado |
   | `WhatsApp = 8` | reservado (SPEC 73–80) |
   | `System = 99` | `System.HealthCheck` |

   `Module` es un enum (agrupación del reporte). `Feature` es un código de texto con prefijo del módulo (máx. 100), definido como constante en cada spec que envía correo. **Regla para specs futuras:** toda spec que envíe correo declara su `Module` y sus `Feature` y usa `IEmailDispatcher` (se agrega a las convenciones de `specs/README.md` y a `CLAUDE.md`).
4. **Registro central** (`EmailDispatchLogs`, solo inserción): un registro por intento con empresa, módulo, función, usuario, origen, cuenta usada (`Platform`/`Own`/`Log`), destinatario principal, número de destinatarios (para, CC), asunto (máx. 200), tamaño total y número de adjuntos, estado (`Sent = 1`, `Failed = 2`, `Blocked = 3`), id del mensaje del proveedor, error (máx. 500), fecha y duración. **No guarda el cuerpo** (los módulos que necesitan el cuerpo lo guardan en su tabla, como `PersonEmailMessages`). Los correos de seguridad registran el destinatario **enmascarado** (`j***@dominio.com`) y sin asunto.
5. **Cuotas por empresa** (las fija la **plataforma**, SuperAdmin): `MonthlyQuota?` y `DailyQuota?` por empresa (vacías = sin límite), con `QuotaAction` (`Block = 1` o `WarnOnly = 2`) y `WarningPercent` (por defecto 80).
   - Cuentan los envíos `Sent` y `Failed` de la empresa en el día o mes (zona horaria de la empresa, o UTC sin ella).
   - Al superar la cuota con `Block`, el envío queda `Blocked` (`EMAIL_QUOTA_EXCEEDED`) y no sale.
   - **Excepción:** los módulos `Security`, `Users` y `System` **nunca se bloquean** (recuperar la contraseña o entrar con MFA no puede depender de la cuota), pero sí cuentan.
   - Al cruzar `WarningPercent` y al llegar al 100 % se envía **una sola vez por período** un aviso a la plataforma (correo a `Email:PlatformAlertsTo`) y a `QuotaAlertEmail` de la empresa (si lo tiene). Cuando exista SPEC 85, el aviso también entra como alerta.
   - El límite por usuario por día de SPEC 72 se mantiene y se aplica antes de la cuota de empresa.
6. **Reportes y resumen de uso** (consultas con Dapper sobre `EmailDispatchLogs`):
   - **Resumen** del período: total, enviados, fallidos, bloqueados, % de éxito, uso de la cuota (barra), cuenta usada.
   - **Por módulo y función**, **por día** (tendencia), **por usuario** y **por estado**; los errores más frecuentes.
   - **Detalle** paginado con filtros (fechas, módulo, función, estado, usuario, cuenta) y exportación CSV.
   - **Vista de plataforma** (solo SuperAdmin): todas las empresas, uso frente a cuota, cuenta usada y sus fallos, y edición de cuotas.
   - Período máximo de consulta: 24 meses.
7. **Correos sin empresa** (`ForgotPassword`, OTP, MFA, cambio de correo, health check): salen siempre por la cuenta de la plataforma y se registran con `CompanyId = null` (visibles solo en la vista de plataforma).
8. **Retención:** los registros se conservan `Email:DispatchLogRetentionMonths` (por defecto 24). La purga es un proceso programado (Hangfire, `PROCESSES.md`).
9. **Cifrado de secretos:** `ISecretProtector` (Application) con implementación sobre **ASP.NET Core Data Protection** en Infrastructure, con el anillo de claves **persistido en la base** (`Security.DataProtectionKeys`) para que funcione con varias instancias y reinicios. Propósito `JOIN.EmailProvider.ApiKey`. Lo podrán usar otros secretos por empresa (por ejemplo, el token de Meta de SPEC 73 o Google de SPEC 46-post).
10. **Doble identificador, preparado para el webhook de SendGrid:**
   - **Nuestro id:** el `Id` (`Guid`) de `EmailDispatchLogs` se genera **antes** de enviar y se manda a SendGrid en `custom_args` (`join_dispatch_id`, `join_company_id`, `join_module`). SendGrid devuelve esos argumentos en **cada evento** del webhook, sin depender de su id.
   - **Id de SendGrid:** el encabezado `X-Message-Id` de la respuesta se guarda en `ProviderMessageId` en **todos** los envíos (hoy solo lo capturan los de SPEC 72). Los eventos del webhook traen `sg_message_id`, que empieza con ese valor; sirve como segunda vía de correlación.
   - **Un registro por destinatario:** SendGrid emite eventos por cada dirección (para y CC). `EmailDispatchRecipients` guarda cada destinatario del envío (dirección, tipo para/CC, enmascarada en seguridad), para que el webhook futuro actualice el estado de cada uno.
   - **Estado de entrega reservado:** `EmailDispatchRecipients` y `EmailDispatchLogs` suman `DeliveryStatus` (`Unknown = 0` hasta que exista el webhook; luego `Processed`, `Delivered`, `Deferred`, `Bounced`, `Dropped`, `Opened`, `Clicked`, `SpamReport`, `Unsubscribed`) y `DeliveryStatusAt?`. Esta spec solo los crea en `Unknown`.
   - **Límites de lo que se puede saber** (de la documentación de SendGrid): se informa procesado, entregado al servidor del destinatario, diferido, rebotado, descartado, abierto (si el cliente de correo carga imágenes; Apple Mail y otros lo distorsionan), clic, spam y baja. **No existe un evento de "eliminado"**: SendGrid no puede saber si el destinatario borró el correo.
   - **Webhook por cuenta:** con cuenta propia (`Own`), cada empresa configura en su SendGrid la URL del webhook de JOIN; la spec del webhook guardará por empresa su clave de verificación de firma (cifrada con `ISecretProtector`).
11. **Seguridad de la configuración:** cambiar la cuenta o la API key exige `EmailSettings` con `CanUpdate`; el envío de prueba, `CanExecute`. Cada cambio queda en `EmailProviderSettingsEvents` (quién, cuándo, qué cambió; nunca la key).

## Scope

**In:**

**A. Esquema**
1. `Admin.EmailCompanySettings` (SPEC 72) suma: `ProviderMode`, `ApiKeyEncrypted?` (`text`), `ApiKeyLast4?`, `VerifiedAt?`, `VerificationError?` (máx. 500), `FallbackToPlatform` (por defecto `false`), `MonthlyQuota?`, `DailyQuota?`, `QuotaAction`, `WarningPercent`, `QuotaAlertEmail?` (máx. 320), `LastQuotaWarningPeriod?` (para no repetir avisos).
2. `Admin.EmailDispatchLogs` (solo inserción, `CompanyId?`): `Module`, `Feature`, `UserId?`, `SourceKind?` (máx. 50), `SourceId?`, `ProviderAccount` (`Platform = 1`, `Own = 2`, `Log = 3`), `ToAddress` (máx. 320), `RecipientCount`, `Subject?`, `AttachmentCount`, `TotalBytes`, `Status`, `ProviderMessageId?` (máx. 200), `Error?`, `OccurredAt`, `DurationMs`, `DeliveryStatus` (`Unknown = 0`), `DeliveryStatusAt?`. `Id` generado antes del envío (regla 10). Índice `(ProviderMessageId)` filtrado `"providermessageid" IS NOT NULL`.
3. `Admin.EmailDispatchRecipients` (solo inserción salvo el estado de entrega): `DispatchLogId`, `Address` (máx. 320, enmascarada en `Security`), `RecipientType` (`To = 1`, `Cc = 2`), `DeliveryStatus` (`Unknown = 0`), `DeliveryStatusAt?`. Índice `(DispatchLogId)`. Índices `(CompanyId, OccurredAt)`, `(CompanyId, Module, OccurredAt)`, `(Status, OccurredAt)`.
4. `Admin.EmailProviderSettingsEvents` (solo inserción): `CompanyId`, `EventType`, `UserId`, `OccurredAt`, `Details?` (`jsonb`, sin secretos).
5. `Security.DataProtectionKeys` (tabla estándar de `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`).

**B. Aplicación e infraestructura**
6. `IEmailDispatcher.DispatchAsync(EmailDispatchRequest)` → `EmailDispatchResult` (`Sent`/`Failed`/`Blocked`, id del proveedor, error). `EmailDispatchRequest` = `EmailMessage` (SPEC 72) + clasificación de la regla 2.
7. `EmailAccountResolver`: devuelve la cuenta de la empresa (key descifrada) o la de la plataforma. Caché por empresa de 5 minutos, invalidada al cambiar la configuración.
8. `SendGridEmailAdapter` recibe la key por envío (cliente por key, sin crear un `HttpClient` por envío), agrega `custom_args` con `join_dispatch_id`, `join_company_id` y `join_module`, y devuelve siempre el `X-Message-Id`. `LoggingEmailAdapter` también registra en `EmailDispatchLogs` con `ProviderAccount = Log`.
9. **Migración de los puntos existentes** al despachador con su `Module`/`Feature`: los ocho de la tabla "Estado actual". `PersonEmailSender` sigue guardando `PersonEmailMessages` y además pasa por el despachador (sin doble envío).
10. `EmailQuotaGuard`: cuenta el período con `SELECT … FOR UPDATE` sobre la fila de configuración de la empresa para que envíos simultáneos no superen la cuota.

**C. Endpoints**
11. Empresa (recurso `EmailSettings`): `GET/PUT /api/v1/EmailCompanySettings` (SPEC 72) suma `providerMode`, `apiKey` (solo escritura), `apiKeyLast4`, `verifiedAt`, `verificationError`, `fallbackToPlatform`, `quotaAlertEmail` y, en lectura, las cuotas y el uso del período. `POST /api/v1/EmailCompanySettings/test` `{ to }` (`CanExecute`): envío de prueba con la cuenta configurada. `DELETE /api/v1/EmailCompanySettings/api-key` (vuelve a `Platform`).
12. Uso de la empresa (recurso `EmailUsage`): `GET /api/v1/EmailUsage/summary?from=&to=`, `GET /by-module`, `GET /by-day`, `GET /by-user`, `GET /errors`, `GET /logs` (paginado), `GET /export` (`CanExport`).
13. Plataforma (solo SuperAdmin, recurso `PlatformEmailUsage`): `GET /api/v1/PlatformEmailUsage/companies?from=&to=` (uso, cuota, cuenta, fallos por empresa), `GET /companies/{companyId}/…` (mismos reportes de la empresa), `PUT /companies/{companyId}/quota` `{ monthlyQuota?, dailyQuota?, quotaAction, warningPercent }`, `GET /system` (correos sin empresa).

**D. Menú y seed**
14. Administración: `EmailSettings` (existente, SPEC 72) suma la sección de cuenta; `EmailUsage` → `/administration/email-usage`. Seguridad: `PlatformEmailUsage` → `/security/email-usage` (solo SuperAdmin). Permisos: `EmailUsage` para SuperAdmin, Admin y Manager.
15. Seed: todas las empresas en `Platform`, sin cuotas. En desarrollo, `JOIN-001` y `JOIN-NI-001` (SPEC 100) con cuota mensual de ejemplo 5.000 y `WarnOnly`, y registros de ejemplo del último mes por módulo.

**Out of scope:**
- Recibir el webhook de eventos de SendGrid (entregado, rebotado, abierto…) y mostrar el estado: spec futura. Esta spec deja listos nuestro id en `custom_args`, el id de SendGrid, los destinatarios y el campo `DeliveryStatus`.
- Otros proveedores (SMTP, Amazon SES): el modelo los admite agregando un `ProviderType`; esta spec solo implementa SendGrid.
- Facturar el uso a las empresas.

## Implementation plan

Orden pensado para que cada paso compile y tenga sus tests antes del siguiente. Leer antes las notas "Ajuste" del encabezado y las de SPEC 72, 84 y 85 (convención de correo saliente en `specs/README.md`).

1. **Cifrado de secretos.** Paquete `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`; `ApplicationDbContext` implementa `IDataProtectionKeyContext` (tabla `Security.DataProtectionKeys`); `AddDataProtection().PersistKeysToDbContext<ApplicationDbContext>().SetApplicationName("JOIN")` en `3.Infrastructure/DependencyInjection.cs`. `ISecretProtector` (Application) y `DataProtectionSecretProtector` (Infrastructure) con el propósito `JOIN.EmailProvider.ApiKey`.
2. **Esquema y migración** `Spec101CompanyEmailProvider`: columnas nuevas de `EmailCompanySettings`, `EmailDispatchLogs`, `EmailDispatchRecipients`, `EmailProviderSettingsEvents` y `DataProtectionKeys`. Enums `EmailModule`, `EmailProviderMode`, `EmailProviderAccount`, `EmailDispatchStatus`, `EmailDeliveryStatus`, `EmailRecipientType`, `EmailQuotaAction` en `1.Domain`. Constantes de `Feature` en `2.Application/Common/Email/EmailFeatures.cs`.
3. **Adaptador.** `SendGridEmailAdapter.SendAsync` recibe la key y los `custom_args` (`join_dispatch_id`, `join_company_id`, `join_module`) y devuelve siempre `X-Message-Id`. Un `SendGridClient` por key cacheado (no un `HttpClient` por envío). `LoggingEmailAdapter` devuelve un id simulado (`log-<guid>`).
4. **Despachador.** `IEmailDispatcher` / `EmailDispatcher` (Application): genera el `Id`, resuelve la cuenta (`EmailAccountResolver`), aplica `EmailQuotaGuard` (con `SELECT … FOR UPDATE` sobre la fila de configuración), envía, guarda `EmailDispatchLogs` + `EmailDispatchRecipients` y, si corresponde, dispara el aviso de cuota (una vez por período, `LastQuotaWarningPeriod`).
5. **Migrar los ocho puntos** de la tabla "Estado actual" a `IEmailDispatcher` con su `Module` y `Feature`. `IEmailService.SendEmailAsync` deja de usarse en Application (queda solo para el adaptador o se elimina). `PersonEmailSender` guarda `PersonEmailMessages` como hoy, con el `ProviderMessageId` que devuelve el despachador.
6. **Endpoints** de configuración (cuenta, key, prueba), uso de la empresa y vista de plataforma (Dapper para los reportes).
7. **Menú, permisos y seed** (`EmailUsage`, `PlatformEmailUsage`; seed de desarrollo).
8. **Documentación:** agregar a `CLAUDE.md` la regla "todo correo sale por `IEmailDispatcher` con `Module` y `Feature`"; nota "Ajuste por SPEC 101" ya presente en SPEC 72, 84 y 85.

**Tests** (regla `/spec-impl`: solo los nuevos y los de clases tocadas):
- Unitarios nuevos: `EmailDispatcherTests`, `EmailAccountResolverTests`, `EmailQuotaGuardTests`, `DataProtectionSecretProtectorTests`, validadores y handlers de configuración y reportes, masking de destinatarios.
- Existentes a correr: los de cada handler migrado del paso 5 (`ForgotPasswordCommandHandlerTests`, `InviteUserCommandHandlerTests`, `SendMfaChallengeCommandHandlerTests`, `EmailOtpSendCodeCommandHandlerTests`, `ForceUserPasswordResetCommandHandlerTests`, `SendPersonEmailCommandHandlerTests` (cubre `PersonEmailSender`). `RequestEmailChangeCommandHandler` no tiene tests hoy: se crea `RequestEmailChangeCommandHandlerTests`.
- Integración: envío con `Log` registrado, cuotas con bloqueo y excepción de seguridad, reportes por permiso, descifrado tras recrear el host.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `EMAIL_QUOTA_EXCEEDED` | 409 | Cuota diaria o mensual superada con `Block` (nunca en `Security`, `Users`, `System`) |
| `EMAIL_PROVIDER_NOT_VERIFIED` | 409 | `ProviderMode = Own` sin una key verificada |
| `EMAIL_PROVIDER_TEST_FAILED` | 400 | El envío de prueba falló (devuelve el motivo del proveedor) |
| `EMAIL_API_KEY_REQUIRED` | 400 | `Own` sin key |
| `INVALID_PERIOD` | 400 | Rango inválido o mayor a 24 meses |

## Criterios de aceptación

1. Con `JOIN-NI-001` en `Own` y su key verificada, un correo de lead sale con la cuenta de la empresa y queda en `EmailDispatchLogs` con `ProviderAccount = Own`, módulo `Customers` y función `Customers.LeadEmail`.
2. La key nunca aparece en respuestas, logs ni eventos; `GET` devuelve solo los últimos 4 caracteres.
3. Una key inválida hace fallar el envío de prueba con el motivo, y no se activa `Own`.
4. Con cuota diaria 10 y `Block`, el undécimo correo de leads del día queda `Blocked`; un "olvidé mi contraseña" de un usuario de esa empresa sale igual.
5. Al cruzar el 80 % de la cuota se envía un solo aviso en el período.
6. El reporte de la empresa agrupa el mes por módulo y función, y el total cuadra con el detalle exportado.
7. Un Admin de empresa no ve otras empresas; el SuperAdmin ve todas y los correos de sistema.
8. Dos envíos simultáneos con un correo de cuota restante: uno sale y el otro queda `Blocked`.
9. Un envío registra su `Id` antes de salir, SendGrid lo recibe en `custom_args` (verificable con el registro del adaptador en pruebas) y `ProviderMessageId` queda guardado en todos los módulos, también en seguridad y usuarios. Un correo con 1 destinatario y 2 CC deja 3 filas en `EmailDispatchRecipients`.
10. Tras reiniciar la API (y con dos instancias), las keys cifradas se siguen descifrando (anillo de claves en la base).
11. Unitarios: resolución de cuenta, cuotas y excepciones por módulo, enmascarado, clasificación de cada punto migrado, protector de secretos. Integración: flujo de envío con `Log`, cuotas, reportes y permisos.

## Decisiones

- **Una salida única con clasificación obligatoria:** medir por módulo solo funciona si nada puede enviar por fuera. Por eso los handlers dejan de usar `IEmailService`.
- **Cuotas en manos de la plataforma:** la empresa configura su cuenta, pero no puede subirse su propia cuota.
- **Seguridad nunca se bloquea:** perder el acceso por una cuota de marketing sería peor que exceder la cuota.
- **Sin cuerpo en el registro central:** basta para medir y auditar, y evita guardar datos personales repetidos.

## Riesgos

- Una empresa con `Own` mal configurada deja de enviar. Mitigación: verificación previa, error visible en su configuración y `FallbackToPlatform` opcional.
- Data Protection sin el anillo persistido haría ilegibles las keys tras un reinicio. Mitigación: persistencia en la base desde esta spec (criterio 9).

## Seed de desarrollo

Solo en `Development`: `JOIN-001` y `JOIN-NI-001` en `Platform` con cuota de ejemplo y registros simulados del último mes (unos 300, repartidos entre `Customers`, `Security` y `Users`, con algunos `Failed`). En desarrollo el proveedor es `Log`, así que ningún correo sale realmente.
