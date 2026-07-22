# SPEC 11 — Cerrar las 4 fallas restantes de CompanyModules (test-only)

> **Status:** Implementado
> **Depends on:** SPEC 09/SPEC 10 (dejaron estas 4 fallas de `CompanyModules` explícitamente fuera de alcance, documentadas como pre-existentes y no relacionadas); referencia informativa a SPEC 06 (gate de cobertura ≥90%, que estas 4 fallas bloquean).
> **Date:** 2026-07-22
> **Objective:** Corregir las 4 pruebas fallando de `CompanyModules` (query y commands) ajustando únicamente los archivos de test para que reflejen el comportamiento real y correcto del código de producción — sin modificar ningún handler ni validador.

---

## Scope

**In:**

- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/CompanyModules/Queries/GetCompanyModules/GetCompanyModulesQueryHandlerTests.cs`:
  - Reescribir `Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError` (renombrado a `Handle_WhenCompanyIdIsEmptyGuid_ShouldReturnEmptyPagedResult`) para reflejar que `CompanyId: Guid.Empty` es un filtro válido, no un error: configurar el fake connection con un resultset vacío (mismo patrón que `Handle_WhenNoModulesMatch_ShouldReturnEmptyPagedResult`), quitar el `Verify(CreateConnection, Times.Never)`, y assertar `response.IsSuccess == true` con `Items` vacío.
  - En `Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedModules`: reemplazar la aserción única `Contain("WHERE cm.CompanyId = @CompanyId AND cm.GcRecord = 0")` por dos aserciones independientes: `Contain("cm.CompanyId = @CompanyId")` y `Contain("cm.GcRecord = 0")`.
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/CompanyModules/Commands/CreateCompanyModules/CreateCompanyModulesCommandHandlerTests.cs`: en `Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError`, cambiar `response.Errors.Should().Contain("The X-Company-Id header is required.")` a `.Contain("CompanyId is required.")`.
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/CompanyModules/Commands/UpdateCompanyModules/UpdateCompanyModulesCommandHandlerTests.cs`: mismo fix que el anterior.

**Out of scope (para specs futuros o ya decidido):**

- `GetCompanyModulesQueryHandler.cs` (producción) — no se toca; `CompanyId` en esta query es un filtro opcional genuino, no proviene del token, y el handler correctamente no lo trata como requerido. Decisión explícita del usuario.
- `CreateCompanyModulesCommandValidator.cs`/`UpdateCompanyModulesCommandValidator.cs` — ya emiten `"CompanyId is required."` correctamente, no se tocan.
- La diferencia de convención de mensajes entre la Query (filtro opcional, sin concepto de "requerido") y los Commands (CompanyId de tenant vía token, validado por FluentValidation) — no es una inconsistencia real una vez entendido que son conceptualmente distintos; no requiere unificación.
- Las 12 fallas de `FluentValidation` por locale documentadas en SPEC 10 — no relacionadas, no tocadas.
- Cualquier otro archivo de test fuera de los 3 listados arriba.

---

## Data model

Este spec no introduce estructuras nuevas — son correcciones de aserciones y arrange existentes. Los cambios concretos:

```csharp
// GetCompanyModulesQueryHandlerTests.cs — test repurposado (antes / después)
[Fact]
public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError()
{
    var context = new GetCompanyModulesQueryTestContext(useNpgsqlConnection: false);
    var query = new GetCompanyModulesQuery(Guid.Empty);
    var handler = context.CreateHandler();

    var response = await handler.Handle(query, CancellationToken.None);

    response.IsSuccess.Should().BeFalse();
    response.Message.Should().Be("INVALID_COMPANY_ID");
    response.Errors.Should().Contain("The X-Company-Id header is required.");
    context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
}
// ---
[Fact]
public async Task Handle_WhenCompanyIdIsEmptyGuid_ShouldReturnEmptyPagedResult()
{
    var context = new GetCompanyModulesQueryTestContext(useNpgsqlConnection: false);
    context.Connection.SetResults(
        FakeResultSet.Empty("Id", "CompanyId", "CompanyName", "ModuleId", "ModuleName", "IsActive", "CreatedAt"),
        FakeResultSet.FromScalar(0));

    var query = new GetCompanyModulesQuery(Guid.Empty);
    var handler = context.CreateHandler();

    var response = await handler.Handle(query, CancellationToken.None);

    response.IsSuccess.Should().BeTrue();
    response.Data!.Items.Should().BeEmpty();
    context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Once);
}
```

```csharp
// GetCompanyModulesQueryHandlerTests.cs — aserción SQL desacoplada del orden (antes / después)
context.Connection.LastCommandText.Should().Contain("WHERE cm.CompanyId = @CompanyId AND cm.GcRecord = 0");
// ---
context.Connection.LastCommandText.Should().Contain("cm.CompanyId = @CompanyId");
context.Connection.LastCommandText.Should().Contain("cm.GcRecord = 0");
```

```csharp
// CreateCompanyModulesCommandHandlerTests.cs / UpdateCompanyModulesCommandHandlerTests.cs (antes / después)
response.Errors.Should().Contain("The X-Company-Id header is required.");
// ---
response.Errors.Should().Contain("CompanyId is required.");
```

---

## Implementation plan

1. `GetCompanyModulesQueryHandlerTests.cs`: reescribir `Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError` → `Handle_WhenCompanyIdIsEmptyGuid_ShouldReturnEmptyPagedResult`, con el arrange de resultset vacío y las nuevas aserciones. Correr `dotnet test --filter FullyQualifiedName~Handle_WhenCompanyIdIsEmptyGuid` — pasa.

2. `GetCompanyModulesQueryHandlerTests.cs`: dividir la aserción SQL de `Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedModules` en las dos aserciones independientes. Correr `dotnet test --filter FullyQualifiedName~Handle_WhenFiltersAreProvided` — pasa.

3. `CreateCompanyModulesCommandHandlerTests.cs`: actualizar el mensaje esperado en `Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError`. Correr `dotnet test --filter FullyQualifiedName~CreateCompanyModulesCommandHandlerTests.Handle_WhenCompanyIdIsEmpty` — pasa.

4. `UpdateCompanyModulesCommandHandlerTests.cs`: mismo fix. Correr `dotnet test --filter FullyQualifiedName~UpdateCompanyModulesCommandHandlerTests.Handle_WhenCompanyIdIsEmpty` — pasa.

5. Build completo de la solución (`dotnet build`). Verificación: 0 errores.

6. Correr la suite completa de `JOIN.Application.UnitTest`. Verificación: las 4 fallas de este spec desaparecen; ningún test que pasaba antes empieza a fallar.

7. Intentar generar el reporte de cobertura (`/p:CollectCoverage=true /p:Threshold=90 /p:ThresholdType=line`). Con estas 4 fallas resueltas (y las de SPEC 10 ya resueltas), coverlet debería poder generar el reporte por primera vez desde SPEC 06 — verificar si la cobertura real es ≥90% o si las fallas de locale de `FluentValidation` (fuera de alcance) siguen bloqueándolo en el entorno donde se corra.

---

## Acceptance criteria

- [x] `Handle_WhenCompanyIdIsEmptyGuid_ShouldReturnEmptyPagedResult` (renombrado) pasa: `IsSuccess == true`, `Items` vacío, `CreateConnection` invocado exactamente una vez.
- [x] `Handle_WhenFiltersAreProvided_ShouldApplyFiltersAndReturnPagedModules` pasa con las dos aserciones SQL independientes.
- [x] `CreateCompanyModulesCommandHandlerTests.Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError` pasa esperando `"CompanyId is required."`.
- [x] `UpdateCompanyModulesCommandHandlerTests.Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError` pasa esperando `"CompanyId is required."`.
- [x] `dotnet build` de la solución completa compila con 0 errores.
- [x] `GetCompanyModulesQueryHandler.cs` permanece sin modificaciones.
- [x] `CreateCompanyModulesCommandValidator.cs` y `UpdateCompanyModulesCommandValidator.cs` permanecen sin modificaciones.
- [x] Ningún test que pasaba antes de este spec empieza a fallar.
- [x] Se documenta si el reporte de cobertura logró generarse tras este fix, y su valor real (ya sea ≥90% o bloqueado por las fallas de locale fuera de alcance).

---

## Decisiones

- **Sí:** no tocar `GetCompanyModulesQueryHandler.cs`, tratando `CompanyId` como el filtro opcional que genuinamente es. Decisión explícita del usuario — corrige la premisa inicial (propuesta por el brief original) de que debía agregarse una validación de "requerido"; el handler nunca inyectó `ICurrentUserService` ni trató este parámetro como tenant-scope, así que el diseño actual es correcto y el test estaba desactualizado.

- **Sí:** renombrar el test repurposado de `Handle_WhenCompanyIdIsEmpty_ShouldReturnInvalidCompanyIdError` a `Handle_WhenCompanyIdIsEmptyGuid_ShouldReturnEmptyPagedResult`. El nombre viejo describía un comportamiento de error que ya no aplica; dejarlo generaría confusión sobre qué verifica el test.

- **Sí:** reemplazar la aserción SQL única por dos `.Contain()` independientes en vez de fijar el orden exacto de las condiciones en el `WHERE` builder. Evita acoplar el test a un detalle de implementación (orden de construcción del `StringBuilder`) que no forma parte del contrato observable de la query.

- **Sí:** solo actualizar el texto esperado en los tests de `CreateCompanyModules`/`UpdateCompanyModules`, sin tocar los validadores. Confirmado por lectura directa: ambos ya emiten `"CompanyId is required."` correctamente — el desajuste era 100% del lado del test.

- **No:** unificar el mensaje de error entre la Query (`"The X-Company-Id header is required."`) y los Commands (`"CompanyId is required."`). Son conceptualmente distintos — uno es un filtro opcional sin concepto de "requerido", el otro es el campo de tenant validado por FluentValidation — no hace falta ni corresponde unificarlos en este spec.

- **No:** tocar las 12 fallas de `FluentValidation` por locale documentadas en SPEC 10. Fuera de alcance, no relacionadas con `CompanyModules`.

---

## Lo que **no** está en este spec

- `GetCompanyModulesQueryHandler.cs` (producción).
- `CreateCompanyModulesCommandValidator.cs`/`UpdateCompanyModulesCommandValidator.cs`.
- Unificación del mensaje de error entre Query y Commands.
- Las 12 fallas de `FluentValidation` por locale (SPEC 10).
- Cualquier otro archivo de test fuera de los 3 listados en Scope.

Cada uno de estos, si se necesita, va en su propio spec.

---

## Notas de implementación (2026-07-22)

**Build:** `dotnet build JOIN.slnx` → 0 errores.

**Tests:** `dotnet test tests/UnitTests/JOIN.Application.UnitTest` → **875/887 pasan**, 12 fallan.

**0 fallos en los 3 archivos de este spec:**
- `GetCompanyModulesQueryHandlerTests` 3/3 (test repurposado pasa, test de filtros pasa, test de empty sigue pasando).
- `CreateCompanyModulesCommandHandlerTests` 6/6 (todas las variantes, incluido `Handle_WhenCompanyIdIsEmpty`).
- `UpdateCompanyModulesCommandHandlerTests` (todos, incluido `Handle_WhenCompanyIdIsEmpty`).

**12 fallos preexistentes — fuera de alcance de este spec (idénticos a los documentados en SPEC 10):**
- 12 fallos de `FluentValidation` por mensaje localizado en español vs aserción en inglés (dependiente del locale del runner):
  - `CreateAreaCommandValidatorTests.Validate_WhenNameIsEmpty_ShouldHaveValidationError`
  - `UpdateAreaCommandValidatorTests.Validate_WhenNameIsEmpty_ShouldHaveValidationError`
  - `CreateEntityStatusCommandValidatorTests.Validate_WhenNameIsEmpty_ShouldHaveValidationError`
  - `UpdateEntityStatusCommandValidatorTests.Validate_WhenNameIsEmpty_ShouldHaveValidationError`
  - `CreateIdentificationTypeCommandValidatorTests.Validate_WhenNameIsEmpty/Whitespace_ShouldHaveValidationError` (x2)
  - `UpdateIdentificationTypeCommandValidatorTests.Validate_WhenNameIsEmpty/Whitespace_ShouldHaveValidationError` (x2)
  - `CreateProjectCommandValidatorTests.Validate_WhenNameIsEmpty/Whitespace_ShouldHaveValidationError` (x2)
  - `UpdateProjectCommandValidatorTests.Validate_WhenNameIsEmpty/Whitespace_ShouldHaveValidationError` (x2)
  - Error real: `'Name' no debería estar vacío.` vs aserción `'Name' must not be empty.`

**Cobertura:** coverlet emitió `coverage.cobertura.xml`. Reporta `JOIN.Application = 5.69%` line — la métrica no es representativa (el setup local no instrumenta automáticamente los PDBs del assembly de `JOIN.Application`, los archivos del paquete salen `line-rate="0"`). **Misma limitación documentada en SPEC 10**: la cobertura ≥90% real solo es verificable en CI con PDBs instrumentados. Las 12 fallas locale preexistentes (fuera de alcance) bloquean el `Threshold=90` en este runner. Debe re-validarse cuando esas 12 fallas se arreglen en specs futuros.

**Verificación de no-regresión:** todos los tests que pasaban antes de este spec (871) siguen pasando después. Las 12 fallas son exactamente las mismas preexistentes, no se agregó ninguna.

**Archivos modificados (3):**
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/CompanyModules/Queries/GetCompanyModules/GetCompanyModulesQueryHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/CompanyModules/Commands/CreateCompanyModules/CreateCompanyModulesCommandHandlerTests.cs`
- `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/CompanyModules/Commands/UpdateCompanyModules/UpdateCompanyModulesCommandHandlerTests.cs`

**Producción sin tocar (confirmado):** `GetCompanyModulesQueryHandler.cs`, `CreateCompanyModulesCommandValidator.cs`, `UpdateCompanyModulesCommandValidator.cs`, `CreateCompanyModulesCommandHandler.cs`, `UpdateCompanyModulesCommandHandler.cs`, ningún handler, ningún validador.
