# SPEC 71 — Plantillas de correo por empresa, agrupadas por tipo, con variables

> **Status:** Borrador
> **Depends on:** SPEC 41-post (borrado lógico y restauración), SPEC 43-post (módulos, menú y rutas), SPEC 61 (leads, para las variables `Lead.*`). Rama de trabajo: `spec-71-email-templates-by-company`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-19
> **Objective:** Que cada empresa registre plantillas de correo (asunto y cuerpo con variables), agrupadas por un tipo de un catálogo propio, para usarlas al enviar correos a personas, clientes y leads (SPEC 72).
> **Origen:** pedido del usuario (2026-10-19) al definir el envío de correos de SPEC 72: "crea un spec para poder registrar plantillas por empresa y poder agrupar por un tipo". Front: FE-46.

---

## Reglas de negocio (decisión del usuario, 2026-10-19)

1. **Por empresa.** Cada empresa tiene sus plantillas y sus tipos. El nombre de la plantilla es único dentro de la empresa.
2. **Agrupadas por tipo:** catálogo por empresa `EmailTemplateTypes`, editable por el administrador (seed: Presentación, Propuesta, Seguimiento, Agradecimiento). Cada plantilla pertenece a un tipo.
3. **Variables de lista cerrada**, con la sintaxis `{{Grupo.Campo}}`:

   | Variable | Valor |
   |---|---|
   | `{{Persona.Nombre}}` | Nombre de la persona (o razón social) |
   | `{{Persona.NombreCompleto}}` | Nombres y apellidos |
   | `{{Persona.Correo}}` | Correo destino elegido |
   | `{{Cliente.Codigo}}` | Código del cliente (vacío si no es cliente) |
   | `{{Lead.Codigo}}` | Código del lead asociado (vacío sin lead) |
   | `{{Lead.Tema}}` | Tema del lead |
   | `{{Usuario.Nombre}}` | Nombre del usuario que envía |
   | `{{Usuario.Correo}}` | Correo del usuario que envía |
   | `{{Empresa.Nombre}}` | Nombre de la empresa |
   | `{{Fecha.Hoy}}` | Fecha de hoy en formato `dd/MM/yyyy` |

   Una variable que no está en la lista invalida la plantilla al guardar (`EMAIL_TEMPLATE_UNKNOWN_VARIABLE`, con la lista de las desconocidas). Al renderizar, una variable sin valor se reemplaza por texto vacío.
4. **La plantilla es un punto de partida:** el usuario la elige en el modal de envío (SPEC 72), se renderiza y él edita el texto antes de enviar.
5. **Activa o inactiva:** una plantilla inactiva no aparece en el selector, pero los correos ya enviados conservan la referencia. Borrado lógico y restauración (SuperAdmin), como SPEC 41-post.
6. **Cuerpo HTML saneado** al guardar (sin `script`, `iframe`, atributos `on*` ni URLs `javascript:`), con el mismo servicio que usa SPEC 72.

## Scope

**In:**

**A. Dominio y persistencia** (esquema `Admin`)
1. `EmailTemplateType` (`BaseTenantEntity`): `Code` (máx. 30, mayúsculas), `Name` (máx. 100), `SortOrder`, `IsActive`. Índice único filtrado `(CompanyId, Code)` con `"gcrecord" = 0`.
2. `EmailTemplate` (`BaseTenantEntity`): `EmailTemplateTypeId`, `Name` (máx. 150), `Subject` (máx. 200), `BodyHtml` (`text`, máx. 50 000 caracteres), `IsActive`. Índices: único filtrado `(CompanyId, Name)` con `"gcrecord" = 0`; `(CompanyId, EmailTemplateTypeId, IsActive)`.
3. Migración `Spec71EmailTemplatesByCompany`: tablas, índices, opciones de menú y permisos.

**B. Application**
4. `IEmailTemplateRenderer` (Application, implementación en Application, sin dependencias externas): `ExtractVariables(text)`, `Validate(subject, body)` y `Render(subject, body, EmailTemplateContext)`. `EmailTemplateContext` lleva persona, correo destino, cliente, lead, usuario y empresa. La lista de variables vive en una sola clase (`EmailTemplateVariables`).
5. `IHtmlSanitizer` (interfaz en Application, implementación en Infrastructure con `HtmlSanitizer`/Ganss.Xss): limpia `BodyHtml` al crear y editar.
6. CRUD de tipos con bloqueo por uso (`EMAIL_TEMPLATE_TYPE_IN_USE`, `"Active EmailTemplates: N"`).
7. CRUD de plantillas; queries con Dapper: lista paginada (filtros `search` con `ILIKE` sobre nombre y asunto, `emailTemplateTypeId`, `isActive`) y detalle.
8. `RenderEmailTemplateQuery` `{ templateId, personId, emailAddress?, leadId? }`: devuelve `Subject` y `BodyHtml` renderizados. Valida que la persona, el lead (de esa persona y visible según SPEC 60) y la plantilla activa sean de la empresa.

**C. Endpoints**
9. Recurso `EmailTemplates`: `GET /api/v1/EmailTemplates` (paginado), `GET /{id}`, `POST`, `PUT /{id}`, `DELETE /{id}`, `POST /{id}/restore`, `GET /api/v1/EmailTemplates/variables` (lista de variables con su descripción, para el editor).
10. Recurso `EmailTemplateTypes`: `GET/POST/PUT/DELETE /api/v1/EmailTemplateTypes` + `POST /{id}/restore`.
11. Para quien envía correos sin permiso de administrar plantillas (`[SkipDynamicAuthorization]`, solo autenticado y con empresa):
    - `GET /api/v1/EmailTemplates/lookup?emailTemplateTypeId=&search=` → plantillas activas agrupadas por tipo (id, nombre, tipo), máx. 50.
    - `POST /api/v1/EmailTemplates/{id}/render` → punto 8.
    El envío en sí sigue protegido por los permisos de SPEC 72.

**D. Menú, permisos y seed**
12. `SystemOption` `EmailTemplates` (`/administration/email-templates`) y `EmailTemplateTypes` (`/administration/email-template-types`), módulo `Administration`, con sus `ControllerName`. Permisos: SuperAdmin, Admin y Manager con todo; Supervisor solo lectura.
13. Seed por empresa de los cuatro tipos (`INTRO`, `PROPOSAL`, `FOLLOW_UP`, `THANKS`) en `ICompanyCatalogSeeder.SeedDefaultCatalogsForCompanyAsync`, en `SeedAsync` y para las empresas existentes en el arranque (idempotente). En desarrollo, `JOIN-001` siembra una plantilla por tipo.

**Out of scope:**
- Variables de productos (lista de productos del lead) y condicionales o bucles en la plantilla.
- Adjuntos predefinidos en la plantilla.
- Plantillas globales del SuperAdmin compartidas entre empresas.
- Plantillas para otros canales (WhatsApp, SMS).
- Versionado de plantillas.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `EMAIL_TEMPLATE_NOT_FOUND` | 404 | Plantilla de otra empresa, inexistente o (en `render`) inactiva |
| `EMAIL_TEMPLATE_TYPE_NOT_FOUND` | 404 | Tipo de otra empresa, inexistente o inactivo al asignarlo |
| `EMAIL_TEMPLATE_UNKNOWN_VARIABLE` | 400 | El asunto o el cuerpo usa una variable fuera de la lista |
| `EMAIL_TEMPLATE_TYPE_IN_USE` | 409 | Borrar un tipo con plantillas vivas |
| `PERSON_NOT_FOUND` / `LEAD_NOT_FOUND` | 404 | En `render`: persona o lead de otra empresa, inexistente o fuera de alcance |
| `DUPLICATE_KEY` | 409 | Nombre de plantilla o código de tipo repetido |

## Implementation plan

1. Dominio, configuraciones EF y migración. Build verde.
2. `EmailTemplateVariables` y `IEmailTemplateRenderer` con sus tests (extraer, validar, renderizar con valores vacíos).
3. `IHtmlSanitizer` en Infrastructure y su registro en `DependencyInjection`.
4. CRUD de tipos y de plantillas con controladores, opciones de menú, permisos y seed. Tests unitarios.
5. `lookup`, `variables` y `render`. Tests unitarios.
6. Pruebas de integración y `EXPLAIN` del SQL nuevo. Ajustar `CURL_REQUESTS.md`.

## Criterios de aceptación

- [ ] Una empresa nueva nace con los cuatro tipos de plantilla.
- [ ] Guardar una plantilla con `{{Persona.Apodo}}` responde 400 `EMAIL_TEMPLATE_UNKNOWN_VARIABLE` y nombra la variable.
- [ ] Guardar un cuerpo con `<script>` o `onclick` lo guarda sin ellos.
- [ ] `render` con una persona sin lead devuelve `{{Lead.Codigo}}` vacío y el resto reemplazado.
- [ ] `render` con un lead de otra persona responde 404 `LEAD_NOT_FOUND`.
- [ ] `lookup` solo devuelve plantillas activas de la empresa, agrupadas por tipo, y funciona para un usuario sin permiso `EmailTemplates`.
- [ ] Borrar un tipo con plantillas vivas responde 409 `EMAIL_TEMPLATE_TYPE_IN_USE`.
- [ ] Dos empresas pueden tener una plantilla con el mismo nombre; dentro de una empresa, no (409).
- [ ] Unit tests de renderer, handlers y validadores con la cobertura de CI ≥ 90 %; integración para los puntos anteriores; `EXPLAIN` de cada sentencia nueva.

## Decisiones

- **Sí: spec separada de SPEC 72** (usuario, 2026-10-19). Las plantillas se administran aparte y el envío funciona también sin ellas.
- **Sí: tipo como catálogo por empresa** (usuario, 2026-10-19).
- **Sí: variables de lista cerrada** (usuario, 2026-10-19). Un error de tipeo se detecta al guardar y no le llega al cliente.
- **Sí: esquema `Admin`.** Las plantillas sirven a personas, clientes y leads; no pertenecen solo al módulo de clientes.
- **Sí: `lookup` y `render` con `[SkipDynamicAuthorization]`**, como el buscador de productos del lead (SPEC 57): los vendedores usan plantillas sin administrarlas.
- **No: motor de plantillas (Scriban, Liquid).** Un reemplazo de una lista cerrada basta y no permite ejecutar lógica.

## Impacto en el frontend

- **FE-46** (Borrador): pantallas Administración → Plantillas de correo y Tipos de plantilla, con un editor que inserta variables. FE-47 usa `lookup` y `render` en el modal de envío.

## Lo que **no** entra en esta spec

- Variables de productos, condicionales y bucles.
- Adjuntos en la plantilla.
- Plantillas globales y de otros canales.
- Versionado.
