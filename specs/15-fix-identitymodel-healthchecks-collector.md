# SPEC 15 — Fix FileNotFoundException de IdentityModel en HealthChecks.UI collector

> **Status:** Draft
> **Depends on:** [[13-fix-nuget-vulnerabilities-and-ef-warnings]] (introdujo el override de `KubernetesClient` que causó esta regresión).
> **Date:** 2026-07-25
> **Objective:** Agregar `IdentityModel.OidcClient` como `PackageReference` directo en `JOIN.Services.WebApi.csproj` para restaurar el ensamblado `IdentityModel.dll` que `HealthChecks.UI.Core` necesita en runtime, eliminando el `FileNotFoundException` recurrente del `HealthCheckCollectorHostedService` sin revertir el parche de `KubernetesClient` (CVE/NU1902).

---

## Scope

**In:**

- `src/4.Services.WebApi/JOIN.Services.WebApi.csproj`: agregar `<PackageReference Include="IdentityModel.OidcClient" Version="5.2.1" />` al `ItemGroup` de paquetes, con un comentario corto explicando por qué se agrega (restaurar dependencia runtime que `KubernetesClient 18.0.13` dejó de traer transitivamente, tras el override de SPEC 13). Esto trae `IdentityModel 5.2.0` de forma transitiva, que es el ensamblado que falta.
- `dotnet restore` para confirmar que la versión se resuelve.
- Verificación manual: arrancar la API en Development y confirmar que el `HealthCheckCollectorHostedService` ya no lanza `FileNotFoundException` en ningún ciclo de recolección (esperar al menos 2 ciclos de 30s, ver `HealthChecksUI:EvaluationTimeInSeconds` en `appsettings.json`).
- Verificación manual: abrir `/health-ui` y confirmar que el dashboard muestra el estado del healthcheck `JOIN CRM - Database` correctamente (sin errores en consola del navegador ni en el log del servidor).
- Confirmar con `dotnet list src/4.Services.WebApi/JOIN.Services.WebApi.csproj package --vulnerable --include-transitive` que agregar `IdentityModel.OidcClient` no introduce ninguna vulnerabilidad nueva.

**Out of scope (para specs futuros o decisión ya tomada):**

- Cualquier cambio de código C# (Program.cs, DI, controllers, etc.) — el fix es exclusivamente una dependencia NuGet nueva. No se toca arquitectura ni lógica de negocio.
- Revertir o modificar el override de `KubernetesClient` (se mantiene en `18.0.13`, parcheado) — no se reintroduce la vulnerabilidad NU1902.
- Actualizar `AspNetCore.HealthChecks.UI` más allá de `9.0.0` — ya se confirmó que es la última versión disponible en NuGet, no hay upgrade posible.
- Investigar o remover la funcionalidad de Kubernetes discovery de `AspNetCore.HealthChecks.UI` (causa raíz de por qué necesita `IdentityModel` en primer lugar) — no está en uso activo en este repo (no hay `docker-compose`/K8s), y tocar esa configuración sería un cambio de alcance mayor al pedido.
- Condicionar `AddHealthChecksUI()`/`MapHealthChecksUI()` por entorno (hoy corren igual en Development y Production) — no se pidió y sería un cambio de comportamiento fuera del bug puntual reportado.
- Cualquier otra vulnerabilidad o warning de NuGet no relacionado con este error puntual.

---

## Data model

No aplica — este spec no introduce ni modifica entidades, tablas ni estructuras de datos. Es exclusivamente un cambio de dependencia NuGet.

---

## Implementation plan

1. **NuGet — restaurar IdentityModel.OidcClient**: agregar `<PackageReference Include="IdentityModel.OidcClient" Version="5.2.1" />` al `ItemGroup` de `PackageReference` en `src/4.Services.WebApi/JOIN.Services.WebApi.csproj`, junto a un comentario corto explicando que restaura una dependencia runtime que `KubernetesClient 18.0.13` (SPEC 13) dejó de traer transitivamente.
2. **Restore**: correr `dotnet restore` y confirmar que `IdentityModel.OidcClient 5.2.1` e `IdentityModel 5.2.0` aparecen resueltos (`dotnet list package --include-transitive | grep -i identitymodel`).
3. **Verificación de build**: `dotnet build` en Release, confirmar 0 errores y que no aparecen nuevos warnings `NU1902`/`NU1903`.
4. **Verificación de vulnerabilidades**: `dotnet list src/4.Services.WebApi/JOIN.Services.WebApi.csproj package --vulnerable --include-transitive` — confirmar que sigue sin reportar paquetes vulnerables (ni `KubernetesClient` ni `IdentityModel.OidcClient` deben aparecer).
5. **Verificación en runtime**: `dotnet run --project src/4.Services.WebApi/JOIN.Services.WebApi.csproj` en Development, esperar al menos 2 ciclos de `HealthCheckCollectorHostedService` (60s+, dado `EvaluationTimeInSeconds: 30` en `appsettings.json`), confirmar que el log **ya no** muestra `[ERR] HealthCheck collector HostedService threw an error... FileNotFoundException`.
6. **Verificación del dashboard**: abrir `http://localhost:<puerto>/health-ui` en el navegador, confirmar que el healthcheck `JOIN CRM - Database` aparece con su estado (`Healthy`/`Unhealthy`) sin errores de consola.
7. **dotnet test**: confirmar que la suite sigue pasando sin cambios (no se tocó código, solo referencia de paquete).

---

## Acceptance criteria

- [ ] `JOIN.Services.WebApi.csproj` tiene `<PackageReference Include="IdentityModel.OidcClient" Version="5.2.1" />`.
- [ ] `dotnet restore` resuelve `IdentityModel.OidcClient 5.2.1` e `IdentityModel 5.2.0` sin conflictos.
- [ ] `dotnet build` en Release compila con 0 errores y sin `NU1902`/`NU1903`.
- [ ] `dotnet list package --vulnerable --include-transitive` no reporta vulnerabilidades nuevas tras el cambio.
- [ ] Al arrancar la API en Development y esperar 2+ ciclos de recolección de healthchecks, el log **no** muestra `FileNotFoundException` para `IdentityModel`.
- [ ] `/health-ui` renderiza correctamente el estado del healthcheck `JOIN CRM - Database`.
- [ ] `KubernetesClient` sigue resuelto en `18.0.13` (no se revirtió el parche de SPEC 13).
- [ ] `dotnet test` sigue pasando sin cambios.
- [ ] No se modificó ningún archivo `.cs` — el diff se limita al `.csproj` (y opcionalmente `obj/project.assets.json`/lock files regenerados por restore).

---

## Decisions taken and discarded

- **Agregar `IdentityModel.OidcClient` como override directo (elegido)** vs. esperar un upgrade de `AspNetCore.HealthChecks.UI`: ya se confirmó investigando NuGet.org que `9.0.0` es la última versión publicada del paquete — no existe upgrade disponible que resuelva esto. Pinnear la dependencia faltante directamente es la única vía sin esperar a upstream, mismo patrón ya usado en SPEC 13 para `KubernetesClient`/`Microsoft.OpenApi` (nearest-wins de NuGet).
- **`IdentityModel.OidcClient 5.2.1` en vez de pinnear `IdentityModel 5.2.0` (paquete base) directo**: se elige `.OidcClient` porque es exactamente el paquete que `KubernetesClient 15.0.1` (la versión contra la que fue compilado `HealthChecks.UI.Core`) traía originalmente, reproduciendo el mismo grafo de dependencias que existía antes del override de SPEC 13, minimizando el riesgo de una superficie de API distinta a la esperada.
- **No revertir el override de `KubernetesClient`** (decisión ya tomada, confirmada explícitamente por el usuario): el objetivo es corregir el error puntual sin reintroducir la vulnerabilidad NU1902 que SPEC 13 ya había parchado.
- **No tocar Program.cs ni condicionar `AddHealthChecksUI()` por entorno** (decisión explícita del usuario: "mantener la arquitectura y la lógica base del sistema"): el fix se limita a una dependencia NuGet, sin cambios de código.
- **No remover ni deshabilitar la funcionalidad de Kubernetes discovery de `AspNetCore.HealthChecks.UI`** (causa raíz de por qué el paquete necesita `IdentityModel` en primer lugar): investigar esa dependencia interna implicaría un cambio de alcance mayor (posiblemente cambiar de librería de healthchecks UI) no pedido por el usuario.

---

## Identified risks

- **`IdentityModel.OidcClient 5.2.1` no resuelve el error si `HealthChecks.UI.Core` requiere una versión distinta de `IdentityModel` en tiempo de ejecución**: el mensaje de excepción original especifica `Version=5.2.0.0` exacto, que es justo lo que trae `IdentityModel.OidcClient 5.2.1` — bajo riesgo, pero si el binding no coincide exactamente, mitigación es pinnear `IdentityModel 5.2.0` como `PackageReference` adicional y explícito.
- **`IdentityModel.OidcClient` es un paquete sin actualizaciones recientes**: agregarlo manualmente reintroduce una dependencia que NuGet había dejado de traer transitivamente. Mitigación: el acceptance criteria de `dotnet list package --vulnerable` cubre esto — si en el futuro aparece un CVE sobre este paquete, va a aparecer en ese chequeo igual que pasó con `KubernetesClient`.
- **Fix cosmético, no estructural**: este spec no elimina la causa raíz (que `HealthChecks.UI.Core` dependa de una superficie de API de `KubernetesClient 15.0.1` aunque el proyecto no use Kubernetes) — solo restaura el ensamblado faltante. Si en el futuro se actualiza `AspNetCore.HealthChecks.UI` o se remueve el override de `KubernetesClient`, este `PackageReference` extra debería revisarse (podría volverse redundante o insuficiente).
