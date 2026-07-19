# SPEC 09 — Restaurar el build de CI (deuda de tests Person* + copia condicional de appsettings.Development.json)

> **Status:** Draft
> **Depends on:** SPEC 06 (introdujo `JOIN.IntegrationTests.csproj`, origen del `MSB3030`); referencia informativa a SPEC 01-05/07/08, ya mergeados a `main` — este spec no los modifica.
> **Date:** 2026-07-19
> **Objective:** Restaurar el build de CI a verde arreglando 6 archivos de test que quedaron desincronizados con la API encapsulada de `PersonAddress`/`PersonContact` (39 errores CS0200/CS7036), y evitando que la ausencia esperada de `appsettings.Development.json` en un checkout de CI rompa el build de `JOIN.IntegrationTests.csproj` (`MSB3030`).

---

## Scope

**In:**

- Modificar `tests/IntegrationTests/JOIN.IntegrationTests.csproj`: agregar `Condition="Exists('..\..\src\4.Services.WebApi\appsettings.Development.json')"` a los dos `<None Include>` de `appsettings.json`/`appsettings.Development.json`, para que la ausencia del segundo (bloqueado por `.gitignore` desde SPEC 07) no aborte el build con `MSB3030`. `appsettings.json` sí viaja siempre y no necesita la condición, pero se agrega por consistencia y defensividad.
- Corregir los **6 archivos de test** que no compilan contra la API encapsulada de `PersonAddress`/`PersonContact` (`private set` + `Create()`/`Update()`/`SetAsPrimary()`/`SetAsDefault()`, introducida antes de SPEC 01 pero nunca reflejada en los tests):
  - `tests/UnitTests/JOIN.Application.UnitTest/Mappings/Admin/PersonMapperTests.cs`
  - `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/Persons/Commands/CreatePerson/CreatePersonCommandHandlerTests.cs`
  - `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/Persons/Commands/UpdatePerson/UpdatePersonCommandHandlerTests.cs`
  - `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Admin/Persons/Commands/DeletePerson/DeletePersonCommandHandlerTests.cs`
  - `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Common/Municipalities/Commands/DeleteMunicipality/DeleteMunicipalityCommandHandlerTests.cs`
  - `tests/UnitTests/JOIN.Application.UnitTest/UseCases/Common/Provinces/Commands/DeleteProvince/DeleteProvinceCommandHandlerTests.cs`
- Regla de corrección uniforme a aplicar en los 6 archivos:
  - Toda construcción `new PersonContact { PersonId = ..., ContactType = ..., ContactValue = ..., IsPrimary = ..., Comments = ... }` pasa a `PersonContact.Create(companyId, personId, contactType, contactValue, comments)`, seguido de `.SetAsPrimary()` cuando el test necesitaba `IsPrimary = true`.
  - Toda construcción `new PersonAddress { ..., IsDefault = true, ... }` quita `IsDefault` del inicializador y agrega `.SetAsDefault()` inmediatamente después.
  - Los `Callback`/mocks que simulaban `ApplyUpdate` reasignando `target.ContactType`/`ContactValue`/`Comments` directamente pasan a invocar `target.Update(contactType, contactValue, comments)`; los que reasignaban `IsPrimary`/`IsDefault` pasan a `SetAsPrimary()`/`RemovePrimary()`/`SetAsDefault()`/`RemoveDefault()` según corresponda.
- `UpdatePersonCommandHandlerTests.cs` — fix adicional específico: agregar `Mock<IPersonContactRepository>` al `UpdatePersonTestContext` (distinto del `Mock<IGenericRepository<PersonContact>>` ya existente), instanciar `new PersonContactPrimaryCoordinator(mock.Object)`, y pasarlo como 4º argumento en `CreateHandler()`.

**Out of scope (para specs futuros o ya decidido):**

- Modificar `PersonAddress.cs`/`PersonContact.cs` (dominio) — la encapsulación es correcta e intencional; el problema es 100% del lado de los tests.
- Modificar cualquier handler de producción (`CreatePersonCommandHandler`, `UpdatePersonCommandHandler`, `DeletePersonCommandHandler`, `DeleteMunicipalityCommandHandler`, `DeleteProvinceCommandHandler`, `PersonMapper`) — el desajuste es puramente de compilación en el lado de test.
- Agregar casos de test nuevos, cambiar aserciones existentes, o modificar el % de cobertura objetivo — el alcance es "que compile y siga pasando lo que ya pasaba", no ampliar la suite.
- Tocar `RegisterEndpointTests.cs` o `CustomWebApplicationFactory.cs` (SPEC 06) más allá de la condición en el `.csproj`.
- Reabrir la política general de `.gitignore` sobre `appsettings.*.json` (ya decidida en specs previos) — este spec no mueve ni excluye ningún archivo de `.gitignore`, solo hace que el `.csproj` tolere la ausencia esperada de uno de ellos.
- Cualquier otro test roto no listado explícitamente en el log de CI provisto por el usuario.
- Cambios a `.github/workflows/ci.yml` — el fix es en el `.csproj`/tests, no en el pipeline.

---

## Data model

Este spec no introduce entidades, DTOs ni configuración nueva — son correcciones de código existente. Los artefactos concretos son los patrones de fix a aplicar:

```xml
<!-- tests/IntegrationTests/JOIN.IntegrationTests.csproj (antes / después) -->
<None Include="..\..\src\4.Services.WebApi\appsettings.json" Link="appsettings\appsettings.json" CopyToOutputDirectory="PreserveNewest" />
<None Include="..\..\src\4.Services.WebApi\appsettings.Development.json" Link="appsettings\appsettings.Development.json" CopyToOutputDirectory="PreserveNewest" />
<!-- --- -->
<None Include="..\..\src\4.Services.WebApi\appsettings.json" Link="appsettings\appsettings.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('..\..\src\4.Services.WebApi\appsettings.json')" />
<None Include="..\..\src\4.Services.WebApi\appsettings.Development.json" Link="appsettings\appsettings.Development.json" CopyToOutputDirectory="PreserveNewest" Condition="Exists('..\..\src\4.Services.WebApi\appsettings.Development.json')" />
```

```csharp
// PersonContact — patrón de fix (antes / después)
var contact = new PersonContact
{
    PersonId = customerId,
    CompanyId = companyId,
    ContactType = ContactType.PrimaryEmail,
    ContactValue = "jane@example.com",
    IsPrimary = true,
    Comments = "Main email"
};
// ---
var contact = PersonContact.Create(companyId, customerId, ContactType.PrimaryEmail, "jane@example.com", "Main email");
contact.SetAsPrimary();
```

```csharp
// PersonAddress — patrón de fix (antes / después)
var address = new PersonAddress { /* ...campos públicos sin cambio... */ IsDefault = true };
// ---
var address = new PersonAddress { /* ...campos públicos sin cambio... */ };
address.SetAsDefault();
```

```csharp
// Callback de ApplyUpdate simulando mutación — patrón de fix (antes / después)
target.ContactType = parsedType;
target.ContactValue = source.ContactValue;
target.IsPrimary = source.IsPrimary;
target.Comments = source.Comments;
// ---
target.Update(parsedType, source.ContactValue, source.Comments);
if (source.IsPrimary) target.SetAsPrimary(); else target.RemovePrimary();
```

```csharp
// UpdatePersonCommandHandlerTests.cs — UpdatePersonTestContext (fix adicional)
public Mock<IPersonContactRepository> PersonContactRepoForCoordinatorMock { get; } = new();

public UpdatePersonCommandHandler CreateHandler()
{
    var coordinator = new PersonContactPrimaryCoordinator(PersonContactRepoForCoordinatorMock.Object);
    return new UpdatePersonCommandHandler(
        UnitOfWorkMock.Object,
        MapperMock.Object,
        CurrentUserServiceMock.Object,
        coordinator);
}
```

---

## Implementation plan

1. Modificar `tests/IntegrationTests/JOIN.IntegrationTests.csproj`: agregar `Condition="Exists(...)"` a los dos `<None Include>`. Verificación: renombrar temporalmente `appsettings.Development.json` a otro nombre, correr `dotnet build tests/IntegrationTests/JOIN.IntegrationTests.csproj`, confirmar que ya no lanza `MSB3030`; restaurar el nombre del archivo al terminar. Paso independiente del resto.

2. Corregir `PersonMapperTests.cs` (`CreateValidAddress()`/`CreateValidContact()`): aplicar el patrón `PersonContact.Create(...)` + `SetAsPrimary()` y `PersonAddress` + `SetAsDefault()`. Build de `JOIN.Application.UnitTest` — el conteo de errores baja en los que correspondan a este archivo.

3. Corregir `CreatePersonCommandHandlerTests.cs` (builder de `Addresses`/`Contacts` del comando esperado): mismo patrón.

4. Corregir `DeletePersonCommandHandlerTests.cs` (entidad `Person` existente con `Addresses`/`Contacts` embebidos): mismo patrón.

5. Corregir `DeleteMunicipalityCommandHandlerTests.cs` y `DeleteProvinceCommandHandlerTests.cs` (una sola ocurrencia de `PersonAddress` cada uno, dentro del mock de `GetAllAsync()`): mismo patrón de `PersonAddress` + `SetAsDefault()`.

6. Corregir `UpdatePersonCommandHandlerTests.cs` — el más extenso, en dos partes:
   - Reemplazar los inicializadores/asignaciones directas en los mocks de `ToAddressEntity`/`ToContactEntity` y en los `Callback` de `ApplyUpdate` (4 bloques repetidos en distintos métodos de test) por el patrón `Create`/`Update`/`SetAsPrimary`/`SetAsDefault` según corresponda.
   - Agregar `Mock<IPersonContactRepository>` a `UpdatePersonTestContext`, construir `PersonContactPrimaryCoordinator` a partir de ese mock, y pasarlo como 4º argumento en `CreateHandler()`.

7. Build completo de `tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj`. Verificación: 0 errores (los 39 CS0200/CS7036 del log de CI desaparecen).

8. Correr `dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj /p:CollectCoverage=true /p:Threshold=90 /p:ThresholdType=line`. Verificación: todos los tests pasan (no solo compilan) y la cobertura se mantiene ≥90% — sin regresión de comportamiento, solo de compilación.

9. Build completo de la solución (`dotnet build`), replicando el step "🔨 Build" de `ci.yml`. Verificación: 0 errores — confirma que el `MSB3030` de `IntegrationTests` también desapareció en un build de solución completa.

10. Verificación funcional manual (si Docker está disponible): `dotnet test tests/IntegrationTests/JOIN.IntegrationTests.csproj` — confirma que el proyecto de integración (SPEC 06) sigue funcionando end-to-end tras el cambio de condición en el `.csproj`. No bloqueante si Docker no está disponible en el entorno donde se aplica el spec.

---

## Acceptance criteria

- [ ] `tests/IntegrationTests/JOIN.IntegrationTests.csproj` construye sin `MSB3030` aunque `appsettings.Development.json` no exista en el filesystem.
- [ ] `appsettings.json` y `appsettings.Development.json` en `JOIN.IntegrationTests.csproj` tienen `Condition="Exists(...)"`.
- [ ] Ningún archivo de test usa `new PersonContact { ... }` con inicializador directo de `PersonId`/`ContactType`/`ContactValue`/`IsPrimary`/`Comments` — todos usan `PersonContact.Create(...)` + `SetAsPrimary()` cuando corresponde.
- [ ] Ningún archivo de test asigna `IsDefault` directamente sobre `PersonAddress` — todos usan `SetAsDefault()`/`RemoveDefault()`.
- [ ] Ningún callback de test reasigna `ContactType`/`ContactValue`/`Comments`/`IsPrimary` de `PersonContact` directamente — usan `Update(...)` + `SetAsPrimary()`/`RemovePrimary()`.
- [ ] `UpdatePersonCommandHandlerTests.CreateHandler()` pasa un `PersonContactPrimaryCoordinator` real (construido sobre `Mock<IPersonContactRepository>`) como 4º argumento del handler.
- [ ] `dotnet build tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj` compila con 0 errores.
- [ ] `dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj` pasa el 100% de los tests existentes, sin tests nuevos ni aserciones modificadas.
- [ ] La cobertura de línea de `JOIN.Application.UnitTest` sigue ≥90% (gate de CI existente, sin degradación).
- [ ] `dotnet build` de la solución completa compila con 0 errores, incluyendo `tests/IntegrationTests/JOIN.IntegrationTests.csproj`.
- [ ] `PersonAddress.cs`, `PersonContact.cs`, y todos los handlers de producción (`CreatePersonCommandHandler`, `UpdatePersonCommandHandler`, `DeletePersonCommandHandler`, `DeleteMunicipalityCommandHandler`, `DeleteProvinceCommandHandler`, `PersonMapper`) permanecen sin modificaciones.
- [ ] `.github/workflows/ci.yml` permanece sin modificaciones.

---

## Decisiones

- **Sí:** usar `Condition="Exists(...)"` en el `.csproj` en vez de abrir una excepción en `.gitignore` para `appsettings.Development.json`. Decisión explícita del usuario — se prefiere que el build tolere la ausencia del archivo antes que tocar la política de secretos por ambiente ya establecida en specs previos (04, 06, 07).

- **Sí:** en los tests, usar la API pública real del dominio (`PersonContact.Create(...)`, `.Update(...)`, `.SetAsPrimary()`, `PersonAddress.SetAsDefault()`) en vez de reflexión (`GetProperty(...).SetValue(...)`) para poblar las propiedades ahora encapsuladas. Ejercita las mismas invariantes que el código de producción y es consistente con el único uso legítimo de reflexión que ya existe en estos tests (`BaseEntity.Id`, que no tiene setter público en ningún punto de la jerarquía — ahí reflexión es la única vía posible).

- **Sí:** agregar un `Mock<IPersonContactRepository>` nuevo en `UpdatePersonTestContext`, en vez de reutilizar el `Mock<IGenericRepository<PersonContact>>` ya existente. Son interfaces distintas — `IPersonContactRepository` expone `GetActiveWithPrimaryByTypeAsync`/`GetMostRecentActiveByTypeAsync` (los métodos que `PersonContactPrimaryCoordinator` necesita), no intercambiable con el CRUD genérico.

- **No:** modificar `PersonAddress.cs`/`PersonContact.cs`. La encapsulación (`private set` + factory/mutators) es correcta e intencional; el desajuste es enteramente deuda del lado de los tests, que nunca se actualizaron cuando el dominio se refactorizó.

- **No:** agregar tests nuevos para la lógica interna de `PersonContactPrimaryCoordinator` (`ClearOtherPrimariesAsync`/`PromoteNextPrimaryAsync`). Fuera de alcance — este spec restaura el build a verde, no amplía cobertura de comportamiento nuevo.

- **No:** aplicar `Condition="Exists(...)"` a otros `.csproj` — verificado que `JOIN.IntegrationTests.csproj` es el único que referencia `appsettings.Development.json` vía `<None Include>`; en runtime, `Program.cs` ya tolera su ausencia de forma nativa a través de `IConfiguration` (simplemente no aplica overrides), sin necesidad de cambios.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Corrección mecánica repetida en 6 archivos (~40 puntos de cambio): riesgo de omitir un `SetAsPrimary()`/`SetAsDefault()` puntual y dejar un test "compilando pero silenciosamente incorrecto" (ej. `IsPrimary` queda en `false` cuando el test esperaba `true`). | El criterio de aceptación exige que los tests **pasen**, no solo compilen (paso 8 del plan) — una omisión así muy probablemente rompe una aserción existente relacionada a `IsPrimary`/`IsDefault`, autodetectándose. Los criterios de aceptación también son auditables por grep (cero asignación directa de esas propiedades). |
| `PersonContact.Create(...)` valida `personId`/`contactValue` no vacíos (`ArgumentException`) — el inicializador de objeto roto que se está reemplazando nunca llegó a compilar, así que no hay garantía de que todos los datos de prueba existentes cumplan esa validación. | Si algún test revela un `ArgumentException` nuevo al migrar a `Create()`, se corrige el dato de entrada del test (ej. un GUID o string vacío usado por error), nunca la validación del dominio — la validación de `Create()` es correcta y ya estaba antes de este spec. |
| El fix de `Condition="Exists(...)"` hace que el comportamiento de `JOIN.IntegrationTests` difiera sutilmente entre un desarrollador local (con `appsettings.Development.json` presente) y CI (sin el archivo) — configuración de `Jwt`/`SendGrid` no se carga en CI. | Bajo riesgo hoy: verificado que `RegisterEndpointTests` (único test existente) no depende de `Jwt`/`SendGrid`. Si una integración futura sí depende de valores de `appsettings.Development.json`, `CustomWebApplicationFactory` deberá inyectarlos explícitamente vía `ConfigureAppConfiguration`, igual que ya hace con `ConnectionStrings:DefaultConnection`. |

---

## Lo que **no** está en este spec

- Modificaciones a `PersonAddress.cs`, `PersonContact.cs`, o cualquier handler/mapper de producción.
- Tests nuevos para `PersonContactPrimaryCoordinator`.
- Cambios a `.gitignore` o a la política de secretos por ambiente.
- Cambios a `.github/workflows/ci.yml`.
- Cambios a `RegisterEndpointTests.cs`/`CustomWebApplicationFactory.cs` más allá de la condición en el `.csproj`.
- Cualquier otro test roto no listado explícitamente en el log de CI provisto.

Cada uno de estos, si se necesita, va en su propio spec.
