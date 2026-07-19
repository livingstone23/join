# SPEC 08 — Versionamiento de API y Documentación Scalar

> **Status:** Implementado
> **Depends on:** Ninguna (aislamiento explícito respecto a SPEC 07 — no se modifica configuración de contenedores Docker)
> **Date:** 2026-07-14
> **Objective:** Introducir versionamiento explícito de API (Asp.Versioning) con prefijo `api/v{version:apiVersion}/...` en los 33 controladores existentes, eliminar las 3 rutas duplicadas sin versionar (`api/admin/...`), y exponer un selector de versiones en Scalar generado dinámicamente vía `IApiVersionDescriptionProvider`.

---

## Scope

**In:**

- Paquetes NuGet `Asp.Versioning.Http`, `Asp.Versioning.Mvc`, `Asp.Versioning.Mvc.ApiExplorer` agregados a `src/4.Services.WebApi/JOIN.Services.WebApi.csproj`.
- `Program.cs`: `AddApiVersioning(options => { DefaultApiVersion = new ApiVersion(1,0); AssumeDefaultVersionWhenUnspecified = true; ReportApiVersions = true; })` encadenado con `.AddApiExplorer(options => { GroupNameFormat = "'v'VVV"; SubstituteApiVersionInUrl = true; })`.
- `Program.cs`: reemplazar el registro plano de OpenAPI/Scalar por un setup version-aware acorde al patrón oficial de `Asp.Versioning` + `Microsoft.AspNetCore.OpenApi` de .NET 9+: registrar un documento OpenAPI por cada versión soportada con `builder.Services.AddOpenApi("vN")` (nombre derivado de `GroupNameFormat = "'v'VVV"`; hoy solo `v1`, lista a extender manualmente al agregar nuevas versiones), y tras `builder.Build()` llamar `app.MapOpenApi()` (route default `/openapi/{documentName}.json` resuelve por nombre de documento) más `app.DescribeApiVersions()` para alimentar `MapScalarApiReference(options => options.AddDocument(...))`. `WithDocumentPerVersion()` (mencionado en la wiki de Asp.Versioning) **no existe** en los paquetes `Asp.Versioning.* 10.0.0` que tenemos — se descarta. **Tampoco** se enumera `IApiVersionDescriptionProvider` vía `BuildServiceProvider()` temprano porque choca con el freeze del bootstrap logger de Serilog (`InvalidOperationException: The logger is already frozen`) — verificado empíricamente.
- `Program.cs`: el `MapGet("/", ...)` de redirección agrega `.ExcludeFromDescription()` para no contaminar los documentos OpenAPI (es Minimal API, no controller — `[ApiExplorerSettings]` no aplica aquí).
- **Los 33 controladores** existentes reciben `[ApiVersion("1.0")]`, y sus atributos `[Route]` cambian de string literal (`api/v1/[controller]`, `api/v1/auth`, `api/v1/account`, `api/v1/workspaces`) a `api/v{version:apiVersion}/[controller]` (o el equivalente con nombre fijo, ej. `api/v{version:apiVersion}/auth`).
- Los 3 controladores con ruta dual hoy (`MunicipalitiesController`, `IdentificationTypesController`, `ProvincesController`): su ruta estándar se versiona igual que el resto (`api/v{version:apiVersion}/[controller]`), y su ruta `api/admin/[controller]` se **elimina y reemplaza** por `api/v{version:apiVersion}/admin/[controller]` — quedan con dos rutas, ambas versionadas, ninguna sin versionar.

**Out of scope (para specs futuros):**

- Crear una v2 real de cualquier endpoint — la infraestructura queda lista para soportarla, pero no se agrega ningún endpoint v2 en este spec.
- Políticas de deprecación (`Deprecated`, headers `Sunset`) para retiro futuro de versiones.
- Modificar lógica de negocio de handlers/Commands/Queries — solo atributos de ruta y versión a nivel de controlador.
- Modificar configuración de contenedores Docker/`docker-compose.yml` (SPEC 07) — aislamiento explícito.
- Modificar el archivo de SPEC 06 (`specs/06-*.md`, aún Draft, sin implementar) — solo se deja la nota de que su ruta de ejemplo deberá ajustarse a la real cuando se implemente.
- Cambios en `appsettings.json`/`appsettings.Development.json`/`appsettings.Production.json`.
- Actualizar clientes/frontend que consuman la API para usar las nuevas rutas versionadas — fuera del alcance de este spec de backend.

---

## Data model

Este spec no introduce clases de dominio — los artefactos son atributos de versionamiento y configuración de Program.cs:

```csharp
// Program.cs — registro de servicios
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddEndpointsApiExplorer();

// One document per supported ApiVersion; name = GroupNameFormat output ("v1", "v2", ...).
// Today only v1.0 is supported; extend this list when new [ApiVersion] attributes are added.
builder.Services.AddOpenApi("v1");

// Program.cs — pipeline (Development)
app.MapOpenApi(); // default route: /openapi/{documentName}.json — auto-handles all docs

app.MapScalarApiReference(options =>
{
    var descriptions = app.DescribeApiVersions(); // post-build, no early BuildServiceProvider
    for (var i = 0; i < descriptions.Count; i++)
    {
        var description = descriptions[i];
        var isDefault = i == descriptions.Count - 1;
        options.AddDocument(description.GroupName, description.GroupName, isDefault: isDefault);
    }
});

app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
```

```csharp
// Controller estándar — antes / después (ej. PersonsController)
[ApiController]
[Route("api/v1/[controller]")]                         // antes
// ---
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]       // después
```

```csharp
// Controller con nombre fijo — antes / después (ej. AuthController)
[ApiController]
[Route("api/v1/auth")]                                  // antes
// ---
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]                // después
```

```csharp
// Los 3 controladores de ruta dual — antes / después (ej. ProvincesController)
[ApiController]
[Route("api/v1/[controller]")]
[Route("api/admin/[controller]")]                        // antes
// ---
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Route("api/v{version:apiVersion}/admin/[controller]")]  // después
```

Conventions:

- `{version:apiVersion}` con `ApiVersion(1, 0)` resuelve en runtime a `v1` en la URL — no `v1.0` — por el formato default de `Asp.Versioning`; se valida explícitamente en el plan de implementación para descartar regresión en rutas existentes.
- `GroupNameFormat = "'v'VVV"` controla el nombre de grupo del API Explorer/OpenAPI (`v1`), independiente del formato de sustitución en la URL.
- Los 3 controladores de ruta dual terminan con **dos** atributos `[Route]`, ambos versionados — cero rutas sin versionar en toda la superficie de la API.

---

## Implementation plan

1. Agregar los 3 paquetes NuGet (`Asp.Versioning.Http`, `Asp.Versioning.Mvc`, `Asp.Versioning.Mvc.ApiExplorer`) a `JOIN.Services.WebApi.csproj`. Build sin errores (nada los usa todavía).
2. Configurar `AddApiVersioning(...).AddApiExplorer(...)` en `Program.cs`. Build sin errores; runtime aún no cambia rutas (controladores siguen con `api/v1` literal).
3. Reemplazar el registro plano de `AddOpenApi()`/`MapOpenApi()`/`MapScalarApiReference()` por el setup version-aware definitivo: `builder.Services.AddOpenApi("v1")` (lista explícita — extensible al agregar nuevas versiones), `app.MapOpenApi()` (route default `/openapi/{documentName}.json` cubre todos los docs registrados), `MapScalarApiReference` con `app.DescribeApiVersions()` para poblar el selector. Agregar `.ExcludeFromDescription()` al `MapGet("/", ...)`. Build sin errores.
4. Refactorizar los 33 controladores: agregar `[ApiVersion("1.0")]` y cambiar sus atributos `[Route]` al patrón con `{version:apiVersion}`, incluyendo los 3 controladores de ruta dual (eliminar la ruta `api/admin/[controller]` sin versionar, agregar `api/v{version:apiVersion}/admin/[controller]`). Build completo de `4.Services.WebApi` sin errores.
5. Verificación funcional manual: correr la app, confirmar que `GET /api/v1/persons` (o cualquier endpoint estándar equivalente) responde exactamente igual que antes del cambio (mismo status code, mismo payload) — sin regresión de URL real pese al cambio de sintaxis del atributo `[Route]`.
6. Verificación funcional manual: confirmar que `GET /api/admin/provinces` (ruta vieja, ahora eliminada) responde `404`, y que `GET /api/v1/admin/provinces` responde correctamente — valida la migración de las 3 rutas duales.
7. Verificación funcional manual: abrir `/scalar/v1`, confirmar que aparece el selector de versión mostrando `v1`, y que el documento `/openapi/v1.json` **no** incluye el endpoint raíz `/` (confirma `.ExcludeFromDescription()`).
8. Verificación funcional manual: inspeccionar los headers de respuesta de cualquier request exitoso (ej. `curl -i`) y confirmar la presencia del header `api-supported-versions` (confirma `ReportApiVersions = true`).

---

## Acceptance criteria

- [x] `JOIN.Services.WebApi.csproj` referencia `Asp.Versioning.Http`, `Asp.Versioning.Mvc`, `Asp.Versioning.Mvc.ApiExplorer`.
- [x] `Program.cs` configura `AddApiVersioning` con `DefaultApiVersion = new ApiVersion(1,0)`, `AssumeDefaultVersionWhenUnspecified = true`, `ReportApiVersions = true`.
- [x] `AddApiExplorer` configura `GroupNameFormat = "'v'VVV"` y `SubstituteApiVersionInUrl = true`.
- [x] Los documentos OpenAPI se registran versionados (`AddOpenApi("v1")`); lista explícita a extender al agregar nuevas `[ApiVersion]`. Ver detalle en Decisiones — `WithDocumentPerVersion()` no se compila en `Asp.Versioning.* 10.0.0` y `BuildServiceProvider()` temprano rompe Serilog freeze.
- [x] El `MapGet("/", ...)` de redirección tiene `.ExcludeFromDescription()`.
- [x] Los 36 controladores tienen `[ApiVersion("1.0")]` (spec original mencionaba 33; codebase creció — patrón aplicado a todos para satisfacer "cero `[Route]` con `v1` hardcodeado").
- [x] Los 36 controladores usan `[Route("api/v{version:apiVersion}/...")]` — cero atributos `[Route]` con `v1` hardcodeado como string literal.
- [x] `MunicipalitiesController`, `IdentificationTypesController`, `ProvincesController` ya no tienen ningún `[Route]` sin versionar (`api/admin/[controller]` eliminado).
- [x] Esos mismos 3 controladores exponen `api/v{version:apiVersion}/admin/[controller]` además de su ruta estándar versionada.
- [x] `GET /api/v1/{cualquier-recurso}` responde con el mismo status/payload que antes del cambio (sin regresión) — verificado con `GET /api/v1/Provinces` → `200` + token de SuperAdmin.
- [x] `GET /api/admin/provinces` (ruta vieja) responde `404` — verificado.
- [x] `GET /api/v1/admin/provinces` responde correctamente — verificado → `200`.
- [x] `/scalar/v1` muestra el selector de versión con `v1` disponible — verificado, UI responde `200`.
- [x] `/openapi/v1.json` no incluye el endpoint raíz `/` — verificado (`paths["/"]` ausente; 105 paths totales, todos `/api/v1/...`).
- [x] Las respuestas HTTP incluyen el header `api-supported-versions` — verificado (`api-supported-versions: 1.0` en `/api/v1/Users/login` y `/api/v1/Provinces`).
- [x] No se modifica lógica de negocio de ningún handler/Command/Query.
- [x] No se modifica `Dockerfile`, `docker-compose.yml`, `.dockerignore`, `.env.example` (SPEC 07).
- [x] La solución compila con 0 errores en `4.Services.WebApi`.

> Verificación manual realizada el 2026-07-19 contra `mcr.microsoft.com/mssql/server:2022-latest` local + DB `join_db_qa`. App respondiendo en `http://localhost:5267`.

---

## Decisiones

- **Sí:** eliminar por completo las rutas sin versionar `api/admin/[controller]` en los 3 controladores afectados, reemplazándolas por `api/v{version:apiVersion}/admin/[controller]`. Unifica toda la superficie de la API bajo un único estándar estricto — cero excepciones, cero rutas huérfanas sin versión. Es un breaking change consciente y aceptado para cualquier cliente que use esas 3 rutas hoy.

- **Sí:** registrar documentos OpenAPI versionados con `AddOpenApi("vN")` explícito por versión soportada. Hoy solo `v1`. El nombre se deriva del `GroupNameFormat = "'v'VVV"` del API Explorer; para agregar v2 al sistema basta con (a) decorar controllers con `[ApiVersion("2.0")]`, (b) añadir `builder.Services.AddOpenApi("v2")` en Program.cs, (c) el `app.DescribeApiVersions()` post-build ya enumera automáticamente la nueva versión y la alimenta al selector de Scalar sin más cambios. Patrón elegido tras verificar empíricamente que (i) `WithDocumentPerVersion()` (mencionado en wiki Asp.Versioning) **no se compila** con los paquetes `Asp.Versioning.* 10.0.0` actuales, y (ii) `BuildServiceProvider()` temprano para iterar `IApiVersionDescriptionProvider` rompe el bootstrap logger de Serilog en este repo (`InvalidOperationException: The logger is already frozen`).

- **Sí:** usar `.ExcludeFromDescription()` para el `MapGet("/", ...)` raíz, no `[ApiExplorerSettings(IgnoreApi = true)]`. Es Minimal API, no una acción de controller — el atributo no aplica a este tipo de endpoint; `.ExcludeFromDescription()` es el mecanismo correcto y único para excluirlo de los documentos OpenAPI.

- **No:** crear una v2 real de ningún endpoint. La infraestructura queda lista, pero agregar una versión nueva sin necesidad de negocio sería trabajo especulativo — se hace cuando exista un caso real de breaking change de API.

- **No:** políticas de deprecación/Sunset headers. No hay ninguna versión que deprecar todavía (solo existe 1.0) — prematuro.

- **No:** tocar `Dockerfile`/`docker-compose.yml`/`.env.example` de SPEC 07, ni la lógica interna de ningún handler. Aislamiento explícito — este spec es puramente de superficie de API (rutas + documentación), no de infraestructura de contenedores ni de negocio.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Refactor mecánico de 33 archivos — riesgo de olvidar un controlador o dejar un `[Route]` con `v1` hardcodeado por error humano. | Criterio de aceptación explícito y verificable por grep ("cero atributos `[Route]` con `v1` hardcodeado") — fácil de auditar exhaustivamente antes de dar el spec por completo. |
| Eliminar las 3 rutas `api/admin/[controller]` sin versionar es un breaking change real para cualquier cliente (frontend u otro consumidor) que las use hoy. | Aceptado explícitamente como decisión — unificación de la superficie de API bajo un único estándar prevalece sobre compatibilidad con esas 3 rutas puntuales. Se recomienda comunicar el cambio al equipo de frontend antes de mergear. |
| El formato exacto de sustitución de `{version:apiVersion}` en la URL (`v1` vs `v1.0`) no se verifica en este documento con 100% de certeza — depende del comportamiento default de `Asp.Versioning`. Si resolviera distinto a `v1`, sería un breaking change no intencional en las 33 rutas de golpe. | Paso de verificación explícito en el plan de implementación (paso 5) antes de considerar el spec completo — si el formato no coincide, se ajusta el route constraint o el formato de versión antes de mergear. |
| SPEC 06 (aún Draft, sin implementar) tiene un ejemplo ilustrativo con una ruta de auth que no refleja el esquema versionado final de este spec. | Ya señalado como nota en este documento; se corrige naturalmente cuando SPEC 06 se implemente, usando la ruta real vigente en ese momento. |

---

## Lo que **no** está en este spec

- Endpoints v2 reales.
- Políticas de deprecación / Sunset headers.
- Cambios a lógica de negocio de handlers/Commands/Queries.
- Cambios a `Dockerfile`, `docker-compose.yml`, `.dockerignore`, `.env.example` (SPEC 07).
- Actualización de clientes/frontend consumidores de la API.
- Modificaciones a `specs/06-*.md` u otro spec previo.

Cada uno, si se necesita, va en su propio spec.
