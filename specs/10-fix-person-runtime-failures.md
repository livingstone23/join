# SPEC 10 — Corregir fallas runtime post-SPEC 09: mapper de Person, Gender nulo y IsDefault de direcciones

> **Status:** Aprobado
> **Depends on:** SPEC 09 (dejó el build compilando pero con 19 fallas en runtime; este spec resuelve las 15 que caen en los 3 archivos que SPEC 09 tocó); referencia informativa a SPEC 01 (`ITransactionalCommand`, ya cubre `CreatePersonCommand`/`UpdatePersonCommand`) y SPEC 06 (gate de cobertura).
> **Date:** 2026-07-19
> **Objective:** Completar el objetivo de SPEC 09 ("CI en verde") corrigiendo 15 fallas de test en `PersonMapperTests.cs`/`CreatePersonCommandHandlerTests.cs`/`UpdatePersonCommandHandlerTests.cs`, y en el camino corregir dos bugs de producción reales que esas fallas destaparon: `Person.Gender` no-nullable causando `NullReferenceException` en personas Legal, y `PersonAddress.IsDefault` nunca aplicado porque `PersonAddressDefaultCoordinator` no está inyectado en los handlers de creación/actualización de `Person`.

---

## Scope

**In:**

- **Fix de dominio** — `src/1.Domain/Admin/Person.cs`: cambiar `public virtual Gender Gender { get; set; } = null!;` a `public virtual Gender? Gender { get; set; }`. No requiere cambio de configuración EF Core (`PersonConfiguration.cs` ya tiene `.IsRequired(false)` en la relación) ni migración (la columna `GenderId` ya es `Guid?` en base de datos) — es un cambio de tipo CLR puro. Mapperly regenera automáticamente `GenderName = person.Gender?.Name` al detectar la nueva nulabilidad del origen.
- **Fix de producción** — `CreatePersonCommandHandler.cs`: inyectar `PersonAddressDefaultCoordinator` (ya registrado en DI, ya usado por los handlers standalone de dirección, nunca inyectado aquí); al construir las direcciones nuevas, llamar `address.SetAsDefault()` cuando el DTO trae `IsDefault: true`, con la misma lógica de "solo un default a la vez dentro del batch" que ya existe para `IsPrimary` en contactos (`primariesByType` → equivalente para direcciones).
- **Fix de producción** — `UpdatePersonCommandHandler.cs`: inyectar `PersonAddressDefaultCoordinator` como 5º parámetro del constructor (mismo patrón que `PersonContactPrimaryCoordinator`, ya presente); en `SyncAddresses`, replicar exactamente la lógica que `SyncContacts` ya aplica para `IsPrimary`: `ClearOtherDefaultsAsync` + `SetAsDefault()` para direcciones nuevas marcadas como default, y `ClearOtherDefaultsAsync`/`SetAsDefault()`/`RemoveDefault()` para direcciones existentes según el valor entrante.
- **Fix de tests** — `PersonMapperTests.cs`: corregir las aserciones de `ToContactEntity`/`ApplyUpdate(contact)`/`ToAddressEntity`/`ApplyUpdate(address)` (llamadas aisladas al mapper) para que reflejen el contrato real y deliberado (`[MapperIgnoreTarget]`): estos métodos **no** pueblan `ContactType`/`ContactValue`/`IsPrimary`/`Comments`/`IsDefault` — eso es responsabilidad del handler vía el dominio. Las 3 fallas de `ToDto`/`ProjectToDto` deberían resolverse solas al aplicar el fix de `Gender` (sin cambio de test necesario, se verifica en el plan).
- **Fix de tests** — `CreatePersonCommandHandlerTests.cs`: agregar el `GenderRepositoryMock.Setup(...)` faltante en los 4 tests que hoy retornan `INVALID_REFERENCES` prematuramente (`Handle_WhenContactTypesAreInvalid`, `Handle_WhenAddressReferencesAreInvalid`, `Handle_WhenPersonAlreadyExists`, `Handle_WhenSaveAsyncReturnsZero`); actualizar `CreateHandler()`/contexto para inyectar `PersonAddressDefaultCoordinator` (mismo patrón que SPEC 09 ya aplicó para `PersonContactPrimaryCoordinator` en `UpdatePersonCommandHandlerTests`); ajustar/agregar aserciones que verifiquen `IsDefault` end-to-end ahora que el handler realmente lo aplica.
- **Fix de tests** — `UpdatePersonCommandHandlerTests.cs`: corregir el `Verify(x => x.InsertAsync(...))` que apunta al mock genérico incorrecto (`IGenericRepository<PersonContact>`) — debe verificar contra el mock de `IPersonContactRepository` (`_unitOfWork.PersonContacts`, que es el que el handler realmente usa); agregar `Mock<IPersonAddressRepository>` + `PersonAddressDefaultCoordinator` y pasarlo como 5º argumento en `CreateHandler()`.

**Out of scope (para specs futuros o ya decidido):**

- Las 4 fallas de `CompanyModulesQueryHandlerTests`/`CreateCompanyModulesCommandHandlerTests`/`UpdateCompanyModulesCommandHandlerTests` — no relacionadas, no tocadas por SPEC 09, quedan para otro spec.
- Migraciones de base de datos — confirmado que no hace falta ninguna.
- `PersonContactPrimaryCoordinator`/lógica de `IsPrimary` en contactos — ya está correctamente implementada, no se toca.
- Auditoría de si existe el mismo patrón de "coordinator no inyectado" en otros agregados del sistema más allá de `Person` — si aparece, es candidato a spec futuro.
- Cambiar las anotaciones `[MapperIgnoreTarget]` de `PersonMapper.cs` — son correctas y deliberadas; el fix va en el dominio (`Gender?`) y en los handlers (wiring del coordinator), no en el diseño del mapper.
- Cualquier cambio de UI/frontend.

---

## Data model

Este spec no introduce entidades ni DTOs nuevos. Los cambios concretos son:

```csharp
// src/1.Domain/Admin/Person.cs (antes / después)
public virtual Gender Gender { get; set; } = null!;
// ---
public virtual Gender? Gender { get; set; }
```

```csharp
// CreatePersonCommandHandler.cs — inyección + patrón para direcciones (espejo del ya existente para contactos)
public class CreatePersonCommandHandler(
    IUnitOfWork unitOfWork,
    IPersonMapper customerMapper,
    ICurrentUserService currentUserService,
    PersonAddressDefaultCoordinator addressDefaultCoordinator) : IRequestHandler<CreatePersonCommand, Response<Guid>>
{
    // ...
    if (request.Addresses is { Count: > 0 })
    {
        PersonAddress? defaultAddress = null;

        foreach (var address in customerEntity.Addresses)
        {
            address.CompanyId = currentUserService.CompanyId;
            address.PersonId = customerEntity.Id;
        }

        foreach (var (addressDto, address) in request.Addresses.Zip(customerEntity.Addresses))
        {
            if (!addressDto.IsDefault) continue;
            defaultAddress?.RemoveDefault();
            address.SetAsDefault();
            defaultAddress = address;
        }
    }
}
```

```csharp
// UpdatePersonCommandHandler.cs — SyncAddresses, mismo patrón que SyncContacts ya usa para IsPrimary
if (incomingAddress.IsDefault)
{
    await addressDefaultCoordinator.ClearOtherDefaultsAsync(companyId, customerEntity.Id, existingAddress?.Id, cancellationToken);
    newAddress.SetAsDefault(); // o existingAddress.SetAsDefault() según la rama
}
else
{
    existingAddress?.RemoveDefault();
}
```

```csharp
// PersonMapperTests.cs — patrón de fix de aserción (antes / después)
target.ContactType.Should().Be(ContactType.PrimaryEmail);
target.IsDefault.Should().Be(source.IsDefault);
// ---
target.ContactType.Should().Be(default); // el mapper no lo puebla; lo hace el handler
target.IsDefault.Should().BeFalse();     // ídem
```

```csharp
// CreatePersonCommandHandlerTests.cs — mock faltante que causaba el corte prematuro en INVALID_REFERENCES
context.GenderRepositoryMock
    .Setup(x => x.GetAsync(request.GenderId!.Value))
    .ReturnsAsync(new Gender { CompanyId = companyId, GcRecord = 0 });
```

```csharp
// UpdatePersonCommandHandlerTests.cs — Verify corregido (antes / después)
context.PersonContactRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonContact>(c => ...)), Times.Once);
// ---
context.PersonContactsNamedRepositoryMock.Verify(x => x.InsertAsync(It.Is<PersonContact>(c => ...)), Times.Once);
```

---

## Implementation plan

1. `Person.cs`: cambiar `Gender Gender` a `Gender? Gender`. Build de `1.Domain`/`2.Application` — Mapperly regenera el flatten de `GenderName` como null-safe. Verificación: 0 errores.

2. Verificación puntual (sin tocar tests todavía): correr los 3 tests `ToDto_WhenPersonIsFullyPopulated_ShouldMapAllSupportedFields`, `ToDto_WhenPersonCollectionsAreEmpty_ShouldMapEmptyCollections`, `ProjectToDto_WhenQueryContainsPersons_ShouldProjectAllSupportedFields` — deberían pasar solo con el cambio del paso 1, sin ninguna modificación de test. Si alguno sigue fallando, hay una causa adicional no contemplada y se documenta antes de seguir.

3. `CreatePersonCommandHandler.cs`: inyectar `PersonAddressDefaultCoordinator`, aplicar `SetAsDefault()` a la dirección marcada como default en el request (con "solo un default a la vez" dentro del batch). Build de `2.Application` sin errores — rompe `CreatePersonCommandHandlerTests.cs` momentáneamente (constructor con nuevo parámetro), esperado hasta el paso 5.

4. `UpdatePersonCommandHandler.cs`: inyectar `PersonAddressDefaultCoordinator` como 5º parámetro; replicar en `SyncAddresses` la lógica que `SyncContacts` ya usa para `IsPrimary`. Build de `2.Application` sin errores — rompe `UpdatePersonCommandHandlerTests.cs` momentáneamente, esperado hasta el paso 6.

5. `CreatePersonCommandHandlerTests.cs`: agregar los 4 `GenderRepositoryMock.Setup(...)` faltantes, wirear `PersonAddressDefaultCoordinator` en `CreateHandler()`, agregar aserción de `IsDefault` end-to-end. Correr `dotnet test --filter FullyQualifiedName~CreatePersonCommandHandlerTests` — todos pasan.

6. `UpdatePersonCommandHandlerTests.cs`: corregir el `Verify(InsertAsync)` para apuntar al mock de `IPersonContactRepository` en vez del genérico, wireear `PersonAddressDefaultCoordinator`. Correr `dotnet test --filter FullyQualifiedName~UpdatePersonCommandHandlerTests` — todos pasan.

7. `PersonMapperTests.cs`: corregir las 6 aserciones que esperaban que el mapper aislado poblara campos que son responsabilidad del handler. Correr `dotnet test --filter FullyQualifiedName~PersonMapperTests` — todos pasan.

8. Build completo de la solución (`dotnet build`). Verificación: 0 errores.

9. Correr `dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj /p:CollectCoverage=true /p:Threshold=90 /p:ThresholdType=line`. Verificación: **887/887 tests pasan** (las 4 fallas de `CompanyModules`, no tocadas por este spec, quedan documentadas como fuera de alcance si persisten) y cobertura ≥90%.

10. Verificación funcional manual (si hay BD disponible): crear una persona Legal (sin género) y confirmar que no explota; crear una persona con dos direcciones donde la segunda tiene `IsDefault: true` y confirmar que solo esa queda con `IsDefault = true` en base de datos.

---

## Acceptance criteria

- [ ] `Person.Gender` es `Gender?` (nullable).
- [ ] No se generó ninguna migración de EF Core nueva (la relación ya era `.IsRequired(false)` y `GenderId` ya era `Guid?`).
- [ ] `CreatePersonCommandHandler` inyecta `PersonAddressDefaultCoordinator` y aplica `SetAsDefault()` a la dirección marcada como default en el request, garantizando un único default por batch.
- [ ] `UpdatePersonCommandHandler` inyecta `PersonAddressDefaultCoordinator` como 5º parámetro del constructor y `SyncAddresses` aplica `SetAsDefault()`/`RemoveDefault()`/`ClearOtherDefaultsAsync()` con la misma lógica que `SyncContacts` ya usa para `IsPrimary`.
- [ ] Los tests de `PersonMapperTests.cs` que llaman `ToContactEntity`/`ApplyUpdate`/`ToAddressEntity` de forma aislada ya no esperan que el mapper puebla `ContactType`/`ContactValue`/`IsPrimary`/`Comments`/`IsDefault`.
- [ ] `ToDto_WhenPersonIsFullyPopulated_ShouldMapAllSupportedFields`, `ToDto_WhenPersonCollectionsAreEmpty_ShouldMapEmptyCollections` y `ProjectToDto_WhenQueryContainsPersons_ShouldProjectAllSupportedFields` pasan sin `NullReferenceException`.
- [ ] `CreatePersonCommandHandlerTests.cs`: los 4 tests que retornaban `INVALID_REFERENCES` prematuramente ahora configuran `GenderRepositoryMock` y devuelven el mensaje/código esperado por cada caso.
- [ ] `CreatePersonCommandHandlerTests.cs` incluye una aserción que verifica que `IsDefault` se aplica correctamente end-to-end tras el fix del handler.
- [ ] `UpdatePersonCommandHandlerTests.cs`: el `Verify(InsertAsync)` de contactos apunta al mock de `IPersonContactRepository`, no al genérico.
- [ ] `dotnet build` de la solución completa compila con 0 errores.
- [ ] `dotnet test tests/UnitTests/JOIN.Application.UnitTest/JOIN.Application.UnitTest.csproj` — 0 fallas atribuibles a los archivos de este spec (las 4 fallas de `CompanyModules`, si persisten, quedan documentadas como fuera de alcance).
- [ ] La cobertura de línea de `JOIN.Application.UnitTest` es ≥90%.
- [ ] `PersonContactPrimaryCoordinator` y toda la lógica de `IsPrimary` en contactos permanecen sin modificaciones.
- [ ] `IPersonMapper.cs` y las anotaciones `[MapperIgnoreTarget]` de `PersonMapper.cs` permanecen sin modificaciones.

---

## Decisiones

- **Sí:** hacer `Person.Gender` nullable en el dominio en vez de solo parchear `PersonMapper.cs` con un método manual null-safe. Decisión explícita del usuario — resuelve la inconsistencia de raíz (ya era inconsistente que `GenderId` fuera `Guid?` pero `Gender` no), sin necesidad de migración (la relación EF Core ya era opcional), y deja que Mapperly regenere el flatten automáticamente sin código manual adicional.

- **Sí:** incluir el bug de `IsDefault` en direcciones en el mismo spec que el de `Gender`. Decisión explícita del usuario — ambos son bugs de producción descubiertos por el mismo barrido de tests, del mismo tamaño (conectar un coordinator ya existente), y separarlos en dos specs distintos sería fragmentar artificialmente un mismo hallazgo.

- **Sí:** usar `PersonAddressDefaultCoordinator` (ya existente, ya registrado en DI, ya usado por los handlers standalone de dirección) en vez de reimplementar la lógica de "un solo default" directamente en `CreatePersonCommandHandler`/`UpdatePersonCommandHandler`. Es exactamente el mismo patrón que `PersonContactPrimaryCoordinator` ya establece para `IsPrimary` — reutilizar evita duplicar la invariante de negocio en dos lugares.

- **Sí:** en `PersonMapperTests.cs`, ajustar las aserciones al contrato real y deliberado del mapper (`[MapperIgnoreTarget]`) en vez de forzar al mapper a poblar esos campos. El mapper delega `ContactType`/`ContactValue`/`IsPrimary`/`Comments`/`IsDefault` al dominio a propósito (`Create()`/`Update()`/`SetAsPrimary()`/`SetAsDefault()`) — cambiar eso violaría el encapsulamiento que la refactorización del dominio (previa a SPEC 01) ya estableció correctamente.

- **No:** modificar `PersonContactPrimaryCoordinator` ni la lógica de `IsPrimary` en contactos — ya está correctamente implementada en ambos handlers, sirve de referencia/espejo para el fix de `IsDefault`.

- **No:** agregar ninguna migración de EF Core — confirmado que la relación `Gender` ya era `.IsRequired(false)` y la columna `GenderId` ya era nullable en base de datos.

- **No:** arreglar las 4 fallas de `CompanyModulesQueryHandlerTests`/`CreateCompanyModulesCommandHandlerTests`/`UpdateCompanyModulesCommandHandlerTests`. No relacionadas con SPEC 09 ni con los bugs encontrados aquí — van en su propio spec si se necesitan.

- **No:** hacer un barrido general del resto del sistema buscando otros "coordinators registrados pero no inyectados". Fuera de alcance — si aparece un caso similar en otro agregado, es candidato a spec futuro con su propia investigación.

---

## Riesgos

| Riesgo | Mitigación |
| --- | --- |
| Agregar `PersonAddressDefaultCoordinator` como parámetro nuevo en el constructor de `CreatePersonCommandHandler`/`UpdatePersonCommandHandler` es un cambio de firma — cualquier código que construya el handler manualmente (fuera de MediatR/DI) rompería. | Verificado: ambos handlers se resuelven exclusivamente vía DI/MediatR, y `PersonAddressDefaultCoordinator` ya está registrado (`services.AddScoped<PersonAddressDefaultCoordinator>()`). Único lugar a actualizar manualmente son los tests (ya cubierto en el plan). |
| Hacer `Person.Gender` nullable podría generar warnings de nulabilidad (`CS8602`) en código que lo lea sin chequeo. | Verificado por grep: ningún archivo de producción fuera de `PersonMapper.cs` (auto-regenerado) y `PersonConfiguration.cs` (ya configurado como opcional) accede a `.Gender.` directamente. Sin superficie de riesgo adicional. |
| Datos legacy: si ya existen personas con más de una dirección marcada `IsDefault = true` en base de datos (posible, dado que el bug nunca lo impidió), el fix de `PersonAddressDefaultCoordinator` corrige el comportamiento **hacia adelante** pero no limpia retroactivamente filas ya inconsistentes. | Aceptado como límite de este spec — es un problema de datos, no de código; una limpieza de datos históricos (si se necesita) es un spec/script aparte. |

---

## Lo que **no** está en este spec

- Las 4 fallas de `CompanyModulesQueryHandlerTests`/`CreateCompanyModulesCommandHandlerTests`/`UpdateCompanyModulesCommandHandlerTests`.
- Cambios a `PersonContactPrimaryCoordinator` o a la lógica de `IsPrimary` en contactos.
- Migraciones de base de datos.
- Un barrido general de otros agregados buscando coordinators registrados pero no inyectados.
- Cambios a `IPersonMapper.cs` o a las anotaciones `[MapperIgnoreTarget]` de `PersonMapper.cs`.
- Limpieza de datos legacy con múltiples direcciones marcadas como default.
- Cualquier cambio de UI/frontend.

Cada uno de estos, si se necesita, va en su propio spec.
