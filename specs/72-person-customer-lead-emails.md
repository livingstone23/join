# SPEC 72 — Enviar correos a personas, clientes y leads, y registrarlos en el historial del lead

> **Status:** Implementado
> **Depends on:** SPEC 49-post (almacenamiento, para leer adjuntos), SPEC 55 (clientes), SPEC 59 (tipo de contacto `EMAIL`), SPEC 60 (alcance de visibilidad de leads), **SPEC 61** (leads, contactos y línea de tiempo), SPEC 62 (documentos del lead), **SPEC 70** (documentos de producto), **SPEC 71** (plantillas y `IHtmlSanitizer`). Rama de trabajo: `spec-72-person-customer-lead-emails`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que un usuario envíe correos desde la persona, el cliente o el lead a una persona que tenga un correo válido registrado, y que cada envío quede en el historial de correos de la persona y, si se asocia a un lead abierto, en su línea de tiempo.
> **Origen:** pedido del usuario (2026-10-19). Las plantillas van aparte en SPEC 71. Front: FE-47.
> **Ajuste por SPEC 71 (2026-10-21):** el reemplazo de las variables que queden en el texto al enviar (regla 6) usa el contexto de `EmailTemplateContextReader` de SPEC 71 (el mismo que `render`): persona de la empresa, lead de esa persona y visible según SPEC 60, remitente y empresa. `{{Persona.Nombre}}` es la razón social para una persona jurídica y `{{Fecha.Hoy}}` usa la zona horaria de la empresa. Sus errores (`PERSON_NOT_FOUND`, `LEAD_NOT_FOUND`) son los de SPEC 71.
> **Ajuste en implementación (2026-10-21):** precisa cómo quedó el código donde el texto era ambiguo; no cambia el alcance. (1) La migración `Spec72PersonCustomerLeadEmails` crea tablas, columna e índices; la opción `EmailSettings`, sus permisos y la fila de `EmailCompanySettings` los siembra `DatabaseSeeder` (como SPEC 62, 69, 70 y 71); `SeedAsync` también siembra los límites de producto de SPEC 70, que faltaban. (2) Código en `UseCases/Common/Emails` (`PersonEmailSender`, `Commands/`, `Queries/`). (3) `toAddress` vacío usa el correo principal (regla 1). (4) Enviar responde **201** con `PersonEmailSentDto` (`id`, `status`, `sentAt`, `toAddress`, `leadId`, `leadInteractionId`, `providerMessageId`, `attachmentCount`); `EMAIL_SEND_FAILED` (502) lleva el mismo objeto con el id del registro `Failed`. (5) `POST /Leads/{id}/emails` ignora un `leadId` en el cuerpo: el lead es el de la ruta, así el front envía el mismo payload desde los tres puntos. (6) El historial de un cliente es el de su persona; la línea de tiempo suma `contactTypeCode` y `emailMessageId` a los contactos. (7) `GET /EmailCompanySettings` sin fila responde `EMAIL_SETTINGS_NOT_CONFIGURED` (el seed la crea). (8) El límite diario cuenta el día UTC. (9) Errores añadidos a la tabla: `PERSON_EMAIL_NOT_FOUND`, `EMAIL_SETTINGS_ALREADY_EXISTS`, `USER_REQUIRED`, `STORAGE_PROVIDER_NOT_CONFIGURED`, `FILE_NOT_FOUND_IN_STORAGE`. FE-47 se ajusta en el mismo cambio.

---

## Reglas de negocio (decisión del usuario, 2026-10-19)

1. **Destinatario con correo válido:** solo se envía a una persona con al menos un `PersonContact` vivo y activo de tipo `PrimaryEmail` o `AlternativeEmail` cuyo valor pase la validación de formato (`MailAddress` + FluentValidation `EmailAddress`). Por defecto se usa el `IsPrimary`; se puede elegir otro de sus correos válidos. El destino debe ser uno de esos correos, no una dirección libre.
2. **Tres puntos de envío:** el detalle de la persona, el del cliente (envía a su `PersonId`) y el del lead (envía a la persona del lead).
3. **Asociación al lead:**
   - Desde el lead, el envío queda asociado a ese lead.
   - Desde la persona o el cliente, el lead es **opcional**: el usuario elige uno de los leads abiertos de la persona que estén en su alcance (SPEC 60).
   - **Solo leads `Open`** (`LEAD_NOT_OPEN`), como los contactos de SPEC 61.
4. **Registro en el historial del lead:** si el envío se asocia a un lead y sale bien, se crea una `LeadInteraction` con el `ContactTypeCompany` de código `EMAIL` de la empresa, `Direction = Outbound`, `Outcome = Other`, `OccurredAt` = hora del envío y `Summary` = `"Correo: {asunto}"` + un extracto del texto plano (máx. 2000). Se actualiza `LastInteractionAt` del lead. Si el envío falla, no se crea la interacción.
5. **Siempre queda en el historial de correos de la persona** (`PersonEmailMessages`), con estado `Sent` o `Failed`, venga de donde venga.
6. **Contenido:** asunto (máx. 200), cuerpo HTML (máx. 50 000, saneado con `IHtmlSanitizer` de SPEC 71), hasta 5 CC con formato válido, y plantilla opcional (SPEC 71). Las variables `{{…}}` que queden en el texto se reemplazan al enviar con el mismo `IEmailTemplateRenderer`.
7. **Adjuntos solo de documentos existentes:**
   - Documentos vivos de productos de la empresa (SPEC 70).
   - Documentos vivos del lead asociado (SPEC 62).
   - No se suben archivos sueltos.
   - Máximo 10 adjuntos, con un tope total por empresa.
8. **Remitente:** `From` = el remitente de la empresa (`EmailCompanySettings.FromEmail`/`FromName`). Si está vacío, se usa el global (`SendGridOptions.FromEmail`/`FromName`). `Reply-To` = correo del usuario que envía, para que las respuestas le lleguen a él.
9. **Límites por empresa** (`EmailCompanySettings`): máximo de envíos por usuario por día (cuentan los `Sent` y los `Failed` del día UTC) y tamaño total de adjuntos. Seed: 50 por día y 15 MB.
10. **Visibilidad:** desde el lead se aplica `LeadAccessGuard` (SPEC 61); fuera de alcance responde `LEAD_NOT_FOUND`. Una persona inactiva no recibe correos (`PERSON_NOT_ACTIVE`).

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Admin`)
1. `PersonEmailMessage` (`BaseTenantEntity`): `PersonId`, `CustomerId?`, `LeadId?`, `LeadInteractionId?`, `EmailTemplateId?`, `Origin` (`Person = 1`, `Customer = 2`, `Lead = 3`), `ToAddress` (máx. 320), `CcAddresses?` (`text`, separadas por coma), `FromAddress`, `ReplyToAddress?`, `Subject`, `BodyHtml` (`text`), `Status` (`Sent = 1`, `Failed = 2`), `ProviderMessageId?` (máx. 200), `ErrorMessage?` (máx. 500), `SentByUserId`, `SentAt`. Índices: `(CompanyId, PersonId, SentAt)`, `(CompanyId, LeadId, SentAt)`, `(CompanyId, SentByUserId, SentAt)`.
2. `PersonEmailMessageAttachment` (`BaseTenantEntity`): `PersonEmailMessageId`, `SourceKind` (`ProductDocument = 1`, `LeadDocument = 2`), `SourceId`, `FileName`, `ContentType`, `SizeBytes`.
3. `EmailCompanySettings` (`BaseTenantEntity`, índice único filtrado por empresa): `FromEmail?`, `FromName?` (máx. 150), `MaxEmailsPerUserPerDay`, `MaxAttachmentsTotalBytes`.
4. `LeadInteraction` suma `PersonEmailMessageId?` para que la línea de tiempo enlace el correo.
5. Migración `Spec72PersonCustomerLeadEmails`: tablas, columna e índices. La opción de menú, los permisos y la fila de configuración los siembra `DatabaseSeeder` (D.15–D.17).

**B. Envío** (Application + Infrastructure)
6. `IEmailService` suma `Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)`. `SendEmailAsync` no cambia, así que sus 15 usos (autenticación, alertas, tickets) siguen igual.
   - `EmailMessage`: `To`, `Cc[]`, `FromEmail?`, `FromName?`, `ReplyTo?`, `Subject`, `HtmlBody`, `Attachments[]` con `FileName`, `ContentType` y `Content` (bytes).
   - `EmailSendResult`: `Success`, `ProviderMessageId?`, `Error?`.
   - `SendGridEmailAdapter` lo implementa; el `X-Message-Id` de la respuesta va a `ProviderMessageId`.
   - `CapturingEmailService` (integración) también lo implementa y guarda los mensajes para afirmarlos.
7. `PersonEmailSender` (servicio de Application que comparten los tres comandos):
   1. Resuelve el destino válido (regla 1).
   2. Valida el lead (regla 3) y el límite diario (regla 9).
   3. Carga los adjuntos desde su proveedor con `IFileStorageResolver` y valida el tope total.
   4. Sanea y renderiza el contenido (regla 6) y arma el remitente (regla 8).
   5. Envía.
   6. Guarda en un solo `SaveAsync` el `PersonEmailMessage` (`Sent` o `Failed`), sus adjuntos y, si salió bien y hay lead, la `LeadInteraction`, además de `LastInteractionAt`.
   El envío ocurre **fuera** de una transacción: los comandos no implementan `ITransactionalCommand`.
8. Si el proveedor falla, se guarda el registro `Failed` y se responde `EMAIL_SEND_FAILED` (502) con el id del registro.
9. `EMAIL_CONTACT_TYPE_NOT_CONFIGURED` si el lead necesita la interacción y la empresa no tiene un `ContactTypeCompany` activo con código `EMAIL`. Se valida **antes** de enviar.

**C. Endpoints**
10. Recurso `Persons`:
    - `GET /api/v1/Persons/{id}/email-addresses` → correos válidos con `IsPrimary` (`CanRead`). Vacío = no tiene correo válido.
    - `POST /api/v1/Persons/{id}/emails` (`[RequirePermission(PermissionFlags.CanExecute)]`). Cuerpo: `{ toAddress, cc[], subject, bodyHtml, emailTemplateId?, leadId?, attachments: [{ sourceKind, sourceId }] }`.
    - `GET /api/v1/Persons/{id}/emails` (paginado, `CanRead`): fecha, asunto, destino, estado, origen, lead, autor y cantidad de adjuntos.
    - `GET /api/v1/Persons/{id}/emails/{emailId}` (`CanRead`): detalle con el cuerpo y los adjuntos.
    - `GET /api/v1/Persons/{id}/emails/attachment-candidates?leadId=&search=` (`CanRead`): documentos de productos (búsqueda `ILIKE` por producto o nombre, máx. 50) y documentos del lead, si se manda.
11. Recurso `Customers`: `POST /api/v1/Customers/{id}/emails` (`CanExecute`) y `GET /api/v1/Customers/{id}/emails` (`CanRead`), que delegan en la persona del cliente con `Origin = Customer`.
12. Recurso `Leads`:
    - `POST /api/v1/Leads/{id}/emails` (`[RequirePermission(PermissionFlags.CanUpdate)]`, como los contactos), sin `leadId` en el cuerpo.
    - `GET /api/v1/Leads/{id}/emails` y `GET /api/v1/Leads/{id}/emails/{emailId}` (`CanRead`).
    - Los leads abiertos de la persona para el selector salen del `GET /Leads/duplicates?personId=` existente (SPEC 61).
13. `GET /Leads/{id}/timeline` (SPEC 61): los contactos con `PersonEmailMessageId` llevan `EmailMessageId` y el tipo `EMAIL`, para abrir el detalle.
14. Recurso `EmailSettings`: `GET/POST/PUT /api/v1/EmailCompanySettings`.

**D. Menú, permisos y seed**
15. `SystemOption` `EmailSettings` (`/administration/email-settings`, módulo `Administration`, `ControllerName` `EmailSettings`). SuperAdmin, Admin y Manager con todo.
16. `CanExecute` en las opciones `Persons` y `Customers` para los roles que tienen `CanUpdate` en ellas. Para los roles con `Leads` no hace falta nada nuevo, porque el envío desde el lead usa `CanUpdate`.
17. `SeedEmailCompanySettingsForCompanyAsync` (sin remitente propio; 50 por día; 15 MB): empresas nuevas, `SeedAsync` y arranque sin migraciones pendientes, como `LeadAttachmentSettings`.

**Out of scope:**
- Seguimiento de aperturas, clics y rebotes (webhooks de SendGrid).
- Recibir las respuestas en el historial (podría venir por la ingesta de SPEC 47-post en otra spec).
- Envío masivo (campañas) y programado.
- Subir archivos sueltos como adjuntos.
- Verificar el remitente de la empresa en SendGrid desde el sistema.
- Correos a direcciones que no estén registradas en la persona.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `PERSON_NOT_FOUND` / `CUSTOMER_NOT_FOUND` / `LEAD_NOT_FOUND` | 404 | De otra empresa, inexistente o, para el lead, fuera de alcance |
| `PERSON_NOT_ACTIVE` | 409 | La persona está inactiva |
| `PERSON_EMAIL_NOT_AVAILABLE` | 400 | La persona no tiene ningún correo válido |
| `INVALID_EMAIL_ADDRESS` | 400 | `toAddress` no es uno de sus correos válidos, o un CC no tiene formato válido |
| `LEAD_NOT_OPEN` | 409 | El lead asociado no está abierto |
| `INVALID_LEAD_PERSON` | 400 | El `leadId` elegido es de otra persona |
| `EMAIL_CONTACT_TYPE_NOT_CONFIGURED` | 400 | Hay lead y la empresa no tiene el tipo de contacto `EMAIL` activo |
| `EMAIL_TEMPLATE_NOT_FOUND` | 404 | Plantilla de otra empresa, inexistente o inactiva (SPEC 71) |
| `EMAIL_TEMPLATE_UNKNOWN_VARIABLE` | 400 | El texto usa una variable fuera de la lista |
| `EMAIL_ATTACHMENT_NOT_FOUND` | 404 | Documento de otra empresa, borrado, o documento de lead de otro lead |
| `EMAIL_ATTACHMENTS_TOO_LARGE` | 400 | Supera `MaxAttachmentsTotalBytes` o más de 10 adjuntos |
| `EMAIL_DAILY_LIMIT_REACHED` | 429 | El usuario llegó a `MaxEmailsPerUserPerDay` |
| `EMAIL_SETTINGS_NOT_CONFIGURED` | 400 | La empresa no tiene fila de configuración |
| `EMAIL_SEND_FAILED` | 502 | El proveedor rechazó el envío; el registro queda `Failed` (la respuesta trae su id) |
| `PERSON_EMAIL_NOT_FOUND` | 404 | Detalle de un correo de otra persona, otro lead, otra empresa o inexistente |
| `EMAIL_SETTINGS_ALREADY_EXISTS` | 409 | `POST /EmailCompanySettings` cuando la empresa ya tiene la fila |
| `USER_REQUIRED` | 401 | El token no trae el usuario (como en SPEC 61) |
| `STORAGE_PROVIDER_NOT_CONFIGURED` | 503 | El proveedor de almacenamiento de un adjunto no está configurado (SPEC 49-post) |
| `FILE_NOT_FOUND_IN_STORAGE` | 404 | La fila del documento existe pero el archivo ya no está en el almacenamiento |

## Implementation plan

1. Dominio, configuraciones EF, columna en `LeadInteraction` y migración. Build verde.
2. `EmailMessage`, `EmailSendResult` y `SendAsync` en `IEmailService`, `SendGridEmailAdapter` (con adjuntos, CC y Reply-To) y `CapturingEmailService`. Tests del adaptador con el cliente mockeado.
3. `EmailCompanySettings` (GET/POST/PUT, opción de menú, permisos y seed).
4. `PersonEmailSender` y `SendPersonEmailCommand`, con `GET email-addresses` y `attachment-candidates`. Tests unitarios de cada regla.
5. Envío desde el cliente y desde el lead (interacción, `LastInteractionAt`, `EmailMessageId` en la línea de tiempo).
6. Historial: lista y detalle por persona, cliente y lead.
7. Integración con `CapturingEmailService`, `EXPLAIN` del SQL nuevo y ajustes en `CURL_REQUESTS.md`.

## Criterios de aceptación

- [ ] Una persona sin correos válidos devuelve `email-addresses` vacío y el envío responde 400 `PERSON_EMAIL_NOT_AVAILABLE`.
- [ ] Un correo con formato inválido guardado en la persona no aparece en `email-addresses`.
- [ ] Enviar a una dirección que no es de la persona responde 400 `INVALID_EMAIL_ADDRESS`.
- [ ] Un envío desde el lead crea un `PersonEmailMessage` `Sent` y una `LeadInteraction` con tipo `EMAIL`, y actualiza `LastInteractionAt`. La línea de tiempo muestra el contacto con `EmailMessageId`.
- [ ] Un envío desde la persona sin `leadId` queda solo en el historial de la persona; con `leadId` de un lead abierto, también en el lead.
- [ ] Un envío asociado a un lead ganado, perdido o inactivo responde 409 `LEAD_NOT_OPEN` y no envía nada.
- [ ] Si el proveedor falla, se guarda `Failed`, no se crea la interacción y se responde 502 `EMAIL_SEND_FAILED`.
- [ ] El mensaje capturado lleva `Reply-To` = correo del usuario y `From` = el de la empresa, o el global si está vacío.
- [ ] `{{Persona.Nombre}}` en el asunto llega reemplazado al proveedor.
- [ ] Un documento de producto y uno del lead llegan como adjuntos; un documento de otro lead responde 404 `EMAIL_ATTACHMENT_NOT_FOUND`; superar el tope total responde 400.
- [ ] El envío número 51 del día de un usuario responde 429 `EMAIL_DAILY_LIMIT_REACHED`.
- [ ] Enviar desde la persona sin `CanExecute` sobre `Persons` responde 403.
- [ ] Los usos existentes de `SendEmailAsync` siguen funcionando (tests actuales en verde).
- [ ] Unit tests con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: el lead se elige al enviar desde la persona o el cliente**, y es opcional (usuario, 2026-10-19). No se registra en todos sus leads ni se pierde el envío: siempre queda en la persona.
- **Sí: solo leads abiertos** (usuario, 2026-10-19), igual que los contactos de SPEC 61.
- **Sí: adjuntos solo de documentos existentes** (usuario, 2026-10-19), de productos y del lead.
- **Sí: plantillas en spec aparte** (usuario, 2026-10-19): SPEC 71.
- **Sí: remitente de la empresa con `Reply-To` del usuario** (usuario, 2026-10-19). Las respuestas llegan a quien envió.
- **Sí: límites configurables por empresa** (usuario, 2026-10-19): 50 envíos por usuario por día y 15 MB.
- **Sí: método nuevo `SendAsync` en lugar de cambiar `SendEmailAsync`.** Los 15 usos actuales no cambian.
- **Sí: enviar fuera de una transacción y guardar después.** Una transacción abierta durante una llamada HTTP al proveedor bloquearía filas. Si se pierde el guardado tras enviar, el correo salió pero no quedó registrado (ver Riesgos).
- **Sí: el envío desde la persona y el cliente usa `CanExecute`**, como en SPEC 17: enviar no es crear ni editar la persona.
- **Sí: `Outcome = Other`** en la interacción. El resultado del contacto se conoce después; el usuario puede registrar otro contacto con el resultado.
- **No: dirección libre como destino.** El pedido dice "siempre que tengan registrado un correo válido".

## Riesgos

| Riesgo | Mitigación |
|---|---|
| El correo sale pero falla el guardado posterior. | Se registra en el log con `ProviderMessageId` (`LoggingBehavior`) y el handler reintenta el `SaveAsync` una vez. |
| El remitente de la empresa no está verificado en SendGrid y todo sale `Failed`. | El error del proveedor queda en `ErrorMessage`. La pantalla de configuración avisa que el remitente debe verificarse en SendGrid. Si el campo está vacío, se usa el global. |
| Adjuntos grandes desde la nube hacen lento el envío. | Tope total por empresa (15 MB por defecto) y máximo de 10 adjuntos. |

## Impacto en el frontend

- **FE-47** (Borrador):
  - Botón y modal "Enviar correo" en el detalle de la persona, del cliente y del lead.
  - Pestaña "Correos" en los tres detalles, ítem de correo en la línea de tiempo y pantalla Administración → Configuración de correo.
  - Usa `lookup` y `render` de SPEC 71 (FE-46) y los documentos de FE-45.
  - Ajusta FE-32 y FE-37.

## Lo que **no** entra en esta spec

- Aperturas, clics, rebotes y respuestas entrantes.
- Campañas y envíos programados.
- Archivos sueltos como adjuntos y destinos libres.
- Verificación del remitente desde el sistema.
