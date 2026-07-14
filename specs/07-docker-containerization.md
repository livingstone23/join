# SPEC 07 — Contenedorización de la API y entorno local mínimo (Docker)

> **Status:** Draft
> **Depends on:** Ninguna
> **Date:** 2026-07-14
> **Objective:** Contenedorizar la API de JOIN con un Dockerfile multi-stage sobre imágenes Alpine de .NET 10, y levantar un entorno local mínimo vía docker-compose (solo el servicio de la API) que inyecta credenciales desde un `.env` gitignored apuntando a la base de datos externa ya existente.

---

## Scope

**In:**

- `Dockerfile` en la raíz del repo (build context necesita ver todos los proyectos `src/*` referenciados por la API — no solo `4.Services.WebApi`). Multi-stage:
  - `build`: `mcr.microsoft.com/dotnet/sdk:10.0-alpine` — restore + build de `src/4.Services.WebApi/JOIN.Services.WebApi.csproj`.
  - `publish`: mismo stage o siguiente — `dotnet publish -c Release -o /app/publish`.
  - `final`: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` — copia `/app/publish`, define `ENTRYPOINT`.
- `.dockerignore` en la raíz: excluye `**/bin/`, `**/obj/`, `.git/`, `tests/`, `.github/`, `specs/`, `.env`, `docker-compose.yml`, `*.user`, archivos de IDE (`.vscode/`, `.idea/`).
- `docker-compose.yml` en la raíz: **un único servicio** `api` — build desde el `Dockerfile` local, `env_file: .env`, mapeo de puerto explícito.
- `.env.example` committeado (plantilla con placeholders, sin valores reales) documentando las 3 claves requeridas: `ConnectionStrings__DefaultConnection`, `Jwt__Key`, `SendGrid__ApiKey`.
- `.env` real (con valores reales) agregado a `.gitignore` — nunca se commitea.
- El resto de configuración no sensible (`Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpirationMinutes`, etc.) permanece en `appsettings.json`/`appsettings.Development.json` sin cambios — solo los 3 valores realmente secretos se inyectan por variable de entorno, sobreescribiendo la jerarquía estándar de configuración de .NET (env vars > appsettings.json).

**Out of scope (para specs futuros):**

- Servicio de SQL Server en el compose — decisión explícita del usuario; la API se conecta a la base de datos externa vía túnel de Cloudflare, ya en ejecución.
- Redis, RabbitMQ, MailServer/MailHog — YAGNI, ya descartados explícitamente.
- `healthcheck`/`depends_on` de base de datos en el compose — no aplica al no haber servicio de BD local; si el servidor externo está caído, se espera que la API falle al arrancar.
- Pipeline de CI/CD para build/push de la imagen a un registro (Docker Hub, ACR, ECR) — este spec es infraestructura local, no despliegue.
- Manifiestos de Kubernetes/Azure/AWS — mencionado como motivación futura para elegir Alpine, pero no se crean artefactos de despliegue en este spec.
- Prometheus/monitoreo — planificado a futuro en el servidor externo del usuario, no forma parte de este spec.
- Modificar `Program.cs`, `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, la resiliencia HTTP/EF Core, o los documentos `specs/01-*.md` a `specs/06-*.md`.

---

## Data model

Este spec no introduce clases C# — los artefactos son archivos de infraestructura de build/despliegue:

```dockerfile
# Dockerfile (raíz del repo, forma)
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY src/1.Domain/JOIN.Domain.csproj src/1.Domain/
COPY src/2.Application.DTO/JOIN.Application.DTO.csproj src/2.Application.DTO/
COPY src/2.Application/JOIN.Application.csproj src/2.Application/
COPY src/3.Infrastructure/JOIN.Infrastructure.csproj src/3.Infrastructure/
COPY src/3.Persistence/JOIN.Persistence.csproj src/3.Persistence/
COPY src/4.Services.WebApi/JOIN.Services.WebApi.csproj src/4.Services.WebApi/
RUN dotnet restore src/4.Services.WebApi/JOIN.Services.WebApi.csproj
COPY src/ src/
RUN dotnet publish src/4.Services.WebApi/JOIN.Services.WebApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "JOIN.Services.WebApi.dll"]
```

```yaml
# docker-compose.yml (raíz del repo)
services:
  api:
    build:
      context: .
      dockerfile: Dockerfile
    env_file:
      - .env
    ports:
      - "8080:8080"
```

```bash
# .env.example (raíz del repo, committeado — sin valores reales)
ConnectionStrings__DefaultConnection=Server=TU_HOST_O_TUNEL,1433;Database=join_db;User Id=SA;Password=TU_PASSWORD;TrustServerCertificate=True;
Jwt__Key=TU_CLAVE_JWT_DE_AL_MENOS_32_CARACTERES
SendGrid__ApiKey=TU_API_KEY_DE_SENDGRID
```

Conventions:

- Restore por capas copiando primero solo los `.csproj` (aprovecha cache de Docker layers — si no cambian dependencias, `dotnet restore` no se re-ejecuta en rebuilds).
- El COPY de `.csproj` incluye **todos** los proyectos `src/*` referenciados transitivamente por `JOIN.Services.WebApi.csproj` (Domain, Application.DTO, Application, Infrastructure, Persistence) — necesario porque `dotnet restore`/`publish` resuelve `ProjectReference`.
- Puerto `8080` es el default de escucha HTTP de las imágenes `aspnet` de Microsoft desde .NET 8+ (usuario no-root, puerto no privilegiado) — no requiere `ASPNETCORE_URLS` explícito salvo que se quiera cambiar.
- `.env` real nunca se commitea; `.env.example` sí, como documentación de las claves requeridas.

---

## Implementation plan

1. Crear `.dockerignore` en la raíz con las exclusiones definidas (`**/bin/`, `**/obj/`, `.git/`, `tests/`, `.github/`, `specs/`, `.env`, `docker-compose.yml`, `*.user`, `.vscode/`, `.idea/`). No afecta el build local de `dotnet` existente.
2. Crear `Dockerfile` multi-stage en la raíz. Verificación: `docker build -t join-api .` construye la imagen sin errores.
3. Crear `.env.example` en la raíz con las 3 claves documentadas (`ConnectionStrings__DefaultConnection`, `Jwt__Key`, `SendGrid__ApiKey`) con placeholders, sin valores reales. Agregar la línea `.env` a `.gitignore` (agregar, no reemplazar el archivo si ya existe contenido).
4. Crear `.env` local (paso manual del desarrollador, no generado por el spec) con los valores reales: connection string al túnel de Cloudflare, `Jwt:Key` real, API Key real de SendGrid.
5. Crear `docker-compose.yml` en la raíz con el único servicio `api` (build local, `env_file: .env`, mapeo de puerto). Verificación: `docker-compose config` valida la sintaxis sin errores.
6. Verificación funcional manual: `docker-compose up --build`; confirmar que la imagen construye, el contenedor arranca, se conecta exitosamente a la BD externa vía el connection string inyectado desde `.env`, y `GET http://localhost:8080/health/ready` responde `Healthy`.
7. Verificación funcional manual (fallo esperado): con una connection string inválida temporal en `.env`, confirmar que el contenedor falla al arrancar con un error claro en logs (no queda colgado silenciosamente) — comportamiento esperado dado que no hay `EnableRetryOnFailure` (SPEC 05). Revertir el `.env` de prueba al terminar.

---

## Acceptance criteria

- [ ] `Dockerfile` existe en la raíz, multi-stage (`build`, `publish`/mismo stage, `final`).
- [ ] Los stages `build` y `final` usan imágenes Alpine oficiales de Microsoft para .NET 10 (`sdk:10.0-alpine`, `aspnet:10.0-alpine`).
- [ ] `docker build -t join-api .` construye la imagen sin errores.
- [ ] `.dockerignore` existe en la raíz y excluye `bin/`, `obj/`, `.git/`, `tests/`, `.env`.
- [ ] `docker-compose.yml` existe en la raíz y define **únicamente** el servicio `api` — sin servicio de SQL Server, Redis, RabbitMQ ni MailServer.
- [ ] El servicio `api` usa `env_file: .env` para inyectar `ConnectionStrings__DefaultConnection`, `Jwt__Key` y `SendGrid__ApiKey`.
- [ ] `.env.example` existe en la raíz, committeado, con las 3 claves documentadas y sin valores reales.
- [ ] `.env` está en `.gitignore` y nunca se commitea.
- [ ] `docker-compose up --build` levanta el contenedor de la API exitosamente y este se conecta a la base de datos externa vía el connection string inyectado.
- [ ] `GET http://localhost:8080/health/ready` responde `Healthy` con el contenedor corriendo.
- [ ] Con una connection string inválida en `.env`, el contenedor falla al arrancar con error visible en logs (comportamiento esperado, sin retry).
- [ ] `appsettings.json`, `appsettings.Development.json`, `appsettings.Production.json` permanecen sin modificaciones — solo las 3 claves secretas se sobreescriben vía variable de entorno.
- [ ] No se modifica `Program.cs`, `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, la resiliencia HTTP/EF Core, ni los documentos `specs/01-*.md` a `specs/06-*.md`.

---

## Decisiones

- **Sí:** imagen base Alpine para ambos stages (`sdk:10.0-alpine`, `aspnet:10.0-alpine`) en vez de Chiseled. Mantiene shell disponible para debug (`docker exec -it ... sh`), bajo peso, y consistencia con despliegues futuros a Azure/AWS. Chiseled quitaría demasiada visibilidad en esta etapa del proyecto.

- **Sí:** el `docker-compose.yml` contiene **únicamente** el servicio de la API — sin SQL Server local. La base de datos ya existe en un servidor externo en la red local del usuario, accesible vía túnel de Cloudflare, que además manejará monitoreo (Prometheus) a futuro. Levantar un SQL Server local duplicaría infraestructura innecesariamente y rompería la paridad con el entorno real; la API simplemente recibe el connection string externo por variable de entorno.

- **Sí:** secretos (`ConnectionStrings__DefaultConnection`, `Jwt__Key`, `SendGrid__ApiKey`) inyectados vía `.env` gitignored + `env_file` en compose, usando la convención de jerarquía de configuración de .NET (doble guion bajo = separador de sección). Elimina credenciales duras del control de versiones — hoy `appsettings.json` tiene una connection string real committeada, y este spec no perpetúa esa práctica para el flujo de Docker.

- **No:** MailServer/MailHog en el compose. El objetivo es contenedorización limpia de la API, no refactorizar el adaptador de email. El envío real ya usa la API HTTP de SendGrid (SPEC 05); introducir un SMTP local exigiría un adaptador paralelo y lógica condicional de DI — fuera de alcance, YAGNI estricto.

- **No:** `healthcheck`/`depends_on` de base de datos en el compose. Al no existir un servicio de BD local, no hay condición de carrera que mitigar — si el servidor externo está caído, es correcto y esperado que la API falle al arrancar (comportamiento consistente con la ausencia de `EnableRetryOnFailure` decidida en SPEC 05).

- **No:** persistencia con volumen nombrado para datos de BD. No aplica — no hay contenedor de base de datos en este spec.

- **No:** pipeline de CI/CD para build/push de la imagen, manifiestos de Kubernetes/Azure/AWS, o integración de monitoreo. Mencionados como contexto/motivación futura por el usuario, pero no forman parte del alcance de este spec — infraestructura local únicamente.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Sin `healthcheck`/`depends_on`, si el túnel de Cloudflare hacia el servidor externo tiene latencia alta (no caído, solo lento), `MigrateAsync()` al arranque podría hacer timeout y el contenedor fallaría a arrancar por una condición transitoria de red, no por indisponibilidad real de la BD. | Aceptado como comportamiento esperado (decisión explícita, consistente con la ausencia de `EnableRetryOnFailure` de SPEC 05). Si se vuelve un problema recurrente, agregar retry es candidato a spec futuro. |
| Onboarding: un desarrollador nuevo que clona el repo y corre `docker-compose up --build` sin haber creado su `.env` primero verá el contenedor arrancar con variables vacías/inexistentes y fallar de forma confusa (no hay validación explícita de que `.env` exista antes de levantar). | `.env.example` documenta las claves requeridas como plantilla; se puede reforzar con una nota en el README indicando el paso manual `cp .env.example .env` antes de `docker-compose up`. |
| La conexión a base de datos depende del túnel de Cloudflare y la red local específica del usuario actual — otro miembro del equipo que quiera levantar el compose necesita su propio acceso a esa infraestructura (o su propia BD), reduciendo la reproducibilidad "un comando y funciona" en comparación con SPEC 06 (Testcontainers, 100% efímero y portable). | Aceptado como decisión arquitectónica explícita del usuario — prioriza paridad con el entorno real de producción sobre portabilidad entre desarrolladores. Si el equipo crece, una BD local en compose (opcional, no default) es candidato a spec futuro. |

---

## Lo que **no** está en este spec

- Servicio de SQL Server, Redis, RabbitMQ o MailServer en el compose.
- `healthcheck`/`depends_on` de base de datos.
- Volumen nombrado para persistencia de datos de BD.
- Pipeline de CI/CD para build/push de la imagen a un registro.
- Manifiestos de Kubernetes/Azure/AWS.
- Integración de Prometheus/monitoreo.
- Modificaciones a `Program.cs`, `TransactionBehavior.cs`, `PerformanceBehavior.cs`, `UnhandledExceptionBehavior.cs`, `LoggingBehavior.cs`, la configuración de Serilog, la resiliencia HTTP/EF Core, o los documentos `specs/01-*.md` a `specs/06-*.md`.

Cada uno de estos, si se necesita, va en su propio spec.
