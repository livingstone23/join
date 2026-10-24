# SPEC 88 — Perfil fiscal por país (estándar), primero Nicaragua: impuestos y retenciones base, numeración autorizada, moneda de referencia y punto de conexión para factura electrónica

> **Status:** Borrador
> **Depends on:** SPEC 56 (monedas y tasas), SPEC 58 (identificación de la persona), **SPEC 81** (tipos de documento, configuración y `CompanyCustomers`), **SPEC 82** (impuestos y grupos fiscales), **SPEC 83** (emisión), **SPEC 84** (PDF), **SPEC 85** (alertas de Leads), SPEC 91 (layout del libro en la lista de verificación). Rama de trabajo: `spec-88-fiscal-country-profiles-nicaragua`, creada desde **`main_postgresql`**, con PR hacia `main_postgresql`. Aplican todas las "Convenciones PostgreSQL" de `specs/README.md`.
> **Date:** 2026-10-24
> **Objective:** Separar del núcleo todo lo que depende del país. Cada empresa elige un **perfil fiscal por país** que siembra sus impuestos y retenciones base, valida la identificación tributaria, controla la **numeración autorizada** por la autoridad tributaria y fija la moneda de referencia. El primer perfil es **Nicaragua**. Agregar otro país (Costa Rica, Honduras, El Salvador, Guatemala, Panamá…) es sembrar su perfil y, si exige factura electrónica, implementar su proveedor, sin cambiar el modelo.
> **Valores de enums (revisión de consistencia):** `ElectronicStatus`: `NotRequired = 0`, `Pending = 1`, `Accepted = 2`, `Rejected = 3`.
> **Ajuste por revisión de consistencia (2026-10-24):** (1) **Moneda local y moneda base:** la moneda local del perfil (NIO) y la base de la empresa (SPEC 56) pueden diferir. `LocalCurrencyRate` = unidades de moneda local por 1 unidad de la moneda del documento: si local = base, es `ExchangeRateToBase`; si no, se calcula cruzando por la base con las tasas del día. (2) `Admin.IdentificationTypes` (global) suma `Code` (único) y el seed de `RUC` es global. (3) Numeración autorizada por `BaseKind` (`NI`: factura y nota de crédito). (4) Sin la puesta en marcha confirmada solo se bloquea emitir facturas y notas de crédito. (5) Los libros fiscales son SPEC 91. (6) Idioma y nombres de moneda del perfil: SPEC 93. (7) Menú y permisos declarados. (8) Seed en `JOIN-NI-001` (SPEC 100).
> **Ajuste (2026-10-24, decisión del usuario):** puesta en marcha fiscal obligatoria confirmada por un usuario con permiso de contador antes de facturar (regla 8). El aviso de vencimiento de numeración por fecha pasa al proceso programado (Hangfire).
> **Ajuste por SPEC 93 (2026-10-24):** el seed `NI` ya no crea los grupos "Venta a agente retenedor" ni "Servicios profesionales a agente retenedor": las retenciones son del cliente (`CustomerWithholdings`). El perfil suma idioma y nombres de moneda para el monto en letras.
> **Origen:** pedido del usuario (2026-10-24): "por el momento el sistema será para operar en Centroamérica, inicialmente en Nicaragua; quiero que se mantenga estándar por si lo queremos implementar en otros países".

---

## Reglas de negocio (decisión del usuario, 2026-10-24)

1. **Núcleo neutral, país en perfiles.** Los documentos, impuestos, existencias y crédito (SPEC 81–87) no conocen ningún país. Lo propio de cada país vive en `FiscalCountryProfiles` (catálogo global, sembrado por el sistema) y en los datos que el perfil siembra para cada empresa.
2. **Perfil por empresa:** `CompanyCustomers.FiscalCountryCode` (ISO 3166-1 alfa-2, `Common.Countries`). Al elegirlo por primera vez se siembran, **solo si faltan**, los impuestos, retenciones, grupos fiscales y leyendas de ese país. La empresa puede editarlos después. Cambiar de país con documentos emitidos → 409 `FISCAL_COUNTRY_LOCKED`.
3. **Contenido del perfil:**
   - nombre y validación de la identificación tributaria del emisor y del receptor (tipo de identificación de SPEC 58 con su `ValidationPattern`);
   - moneda local y moneda de referencia frecuente;
   - impuestos y retenciones base, y grupos fiscales base;
   - leyendas obligatorias u opcionales por tipo de documento;
   - si exige **numeración autorizada** (rangos por resolución);
   - clave del proveedor de **factura electrónica** (`ElectronicInvoicingProvider`), vacía si el país no lo exige o aún no está implementado.
4. **Perfil Nicaragua (`NI`), valores de partida que la empresa confirma con su contador** antes de operar:
   - Autoridad: DGI. Identificación tributaria: **RUC** (persona jurídica: `J` + 13 dígitos; persona natural: número de cédula sin guiones, 13 dígitos + letra).
   - Moneda local **córdoba (`NIO`)**; moneda de referencia **dólar (`USD`)**, con la tasa oficial del Banco Central de Nicaragua cargada en las tasas de cambio de SPEC 56.
   - Impuestos: `IVA15` (IVA 15 %), `IVA0` (exento), `IVA0-EXP` (exportación 0 %).
   - Retenciones (las aplica el comprador agente de retención; se cargan para cuando la empresa vende a uno de ellos o compra como agente): `IR2` (IR 2 %, bienes y servicios en general), `IR10` (IR 10 %, servicios profesionales), `IMI1` (retención municipal 1 %).
   - Grupos fiscales: "General" (`IVA15`, predeterminado), "Exento" (`IVA0`), "Exportación" (`IVA0-EXP`). Las retenciones `IR2`, `IR10` e `IMI1` se asignan al cliente agente retenedor (SPEC 93), no a grupos.
   - **Numeración autorizada:** sí (rangos autorizados por la DGI para el sistema de facturación).
   - Factura electrónica: **sin proveedor** por ahora. Si la DGI la exige para el tipo de contribuyente, se implementa como proveedor `NI-DGI` en una spec propia.
5. **Numeración autorizada** (`LeadDocumentNumberingAuthorizations`), para los tipos cuyo `BaseKind` está en `AuthorizedNumberingKinds` del perfil (en `NI`: factura y nota de crédito; los demás tipos siguen con el correlativo de SPEC 81): número de resolución o autorización, fecha de autorización, serie, **rango desde–hasta** y fecha límite de uso opcional. Al emitir, el número sale del rango activo (no del correlativo libre de SPEC 81):
   - rango agotado o vencido → 409 `NUMBERING_AUTHORIZATION_EXHAUSTED` / `NUMBERING_AUTHORIZATION_EXPIRED`;
   - al emitir, si quedan `N` números o menos (configurable, por defecto 50) → alerta `NumberingRangeLow` (SPEC 85). El aviso por fecha de vencimiento (`NumberingRangeExpiring`, `D` días antes, por defecto 15) es un proceso programado pendiente (Hangfire, `specs/PROCESSES.md`); mientras tanto la pantalla de autorizaciones muestra los días que faltan;
   - el PDF imprime los datos de la autorización (variables `{{Autorizacion.Numero}}`, `{{Autorizacion.Fecha}}`, `{{Autorizacion.Rango}}`, `{{Autorizacion.Vence}}`).
6. **Moneda de referencia en el documento:** si el documento está en una moneda distinta de la local, se guarda y se imprime el **equivalente en moneda local** (total, impuestos y retenciones) con la tasa del día de emisión. En Nicaragua: factura en USD con su equivalente en NIO.
7. **Punto de conexión estándar para factura electrónica:** `IElectronicInvoicingProvider` (Application) con `SubmitAsync`, `GetStatusAsync` y `CancelAsync`, elegido por la clave del perfil. Los documentos guardan `ElectronicStatus` (`NotRequired`, `Pending`, `Accepted`, `Rejected`), `ElectronicId?`, `ElectronicResponse?` (`jsonb`) y la fecha. Sin proveedor, todo documento queda `NotRequired` y no cambia nada del flujo. Los países con factura electrónica obligatoria (Costa Rica, Guatemala, El Salvador, Panamá, Honduras según el caso) se agregan con su proveedor, cada uno en su spec.

8. **Puesta en marcha con contador (obligatoria):** antes de emitir la primera factura o nota de crédito, la empresa completa una **lista de verificación fiscal** y un usuario con el recurso `FiscalSetup` y `CanExecute` (rol de contador o administrador) la **confirma** (`CompanyCustomers.FiscalSetupConfirmedAt` y `FiscalSetupConfirmedByUserId`). Sin confirmar, solo se bloquea emitir facturas y notas de crédito (los demás tipos se emiten normal); emitir facturas o notas de crédito → 409 `FISCAL_SETUP_NOT_CONFIRMED`. Ítems de la lista (cada uno con estado y enlace a su pantalla):
   - datos del emisor completos (razón social, RUC válido, dirección);
   - perfil fiscal elegido;
   - impuestos y retenciones **revisados** por el contador (marca de revisión con usuario y fecha en `Taxes`);
   - autorización de numeración vigente para factura y nota de crédito (si el perfil la exige);
   - leyendas obligatorias del perfil cargadas en la configuración de factura;
   - layout del libro de ventas revisado (SPEC 91);
   - moneda local y tasa de cambio del día cargada.

   Cambiar después cualquiera de esos datos **no** quita la confirmación, pero queda en la bitácora de la lista con usuario y fecha. Un usuario con `FiscalSetup` y `CanExecute` puede revocar la confirmación.

## Scope

**In:**

**A. Esquema**
1. `Customer.FiscalCountryProfiles` (global, sin empresa): `CountryCode` (único), `TaxAuthorityName`, `TaxIdLabel` (ej. "RUC"), `TaxIdIdentificationTypeCode?` (código de `Admin.IdentificationTypes`), `AuthorizedNumberingKinds` (`jsonb`: `BaseKind` que exigen numeración autorizada; `NI`: `Invoice`, `CreditNote`), `LocalCurrencyCode`, `ReferenceCurrencyCode?`, `ElectronicInvoicingProvider?`, `SeedDefinition` (`jsonb`: impuestos, grupos y leyendas a sembrar), `IsActive`.
2. `Customer.CompanyCustomers` (SPEC 81) suma `FiscalCountryCode?`, `NumberingAlertRemaining` (por defecto 50) y `NumberingAlertDays` (por defecto 15).
3. `Customer.LeadDocumentNumberingAuthorizations` (`BaseTenantEntity`): `DocumentTypeId`, `AuthorizationNumber` (máx. 100), `AuthorizationDate`, `Series?` (máx. 20), `RangeFrom`, `RangeTo`, `NextNumber`, `ValidUntil?`, `IsActive`. Un solo rango activo por tipo (índice único filtrado); los rangos de un tipo no se solapan (`NUMBERING_RANGE_OVERLAP`).
4. `Customer.LeadCommercialDocuments` (SPEC 83) suma `NumberingAuthorizationId?`, `LocalCurrencyTotal?`, `LocalCurrencyTaxTotal?`, `LocalCurrencyTotalWithTaxes?`, `LocalCurrencyWithholdingTotal?`, `LocalCurrencyRate?`, `ElectronicStatus`, `ElectronicId?`, `ElectronicResponse?`, `ElectronicStatusAt?`.
5. `Customer.LeadAlerts` (SPEC 85) suma los tipos `NumberingRangeLow` y `NumberingRangeExpiring` (este último lo generará el proceso programado).
6. `Customer.CompanyCustomers` suma `FiscalSetupConfirmedAt?`, `FiscalSetupConfirmedByUserId?`. `Customer.Taxes` suma `ReviewedAt?` y `ReviewedByUserId?`. `Customer.FiscalSetupEvents` (solo inserción): confirmación, revocación y cambios de datos fiscales después de confirmar.
7. `Admin.IdentificationTypes` (global) suma `Code` (máx. 20, único). Seed global del tipo `RUC` con su patrón, si no existe; los tipos existentes reciben su código (`CEDULA`, `PASAPORTE`, …).

**B. Endpoints (Administración)**
8. `GET /api/v1/FiscalCountryProfiles` (activos; `[SkipDynamicAuthorization]`).
9. `PUT /api/v1/CompanyCustomer/fiscal-country` `{ countryCode }`: fija el país y siembra lo que falte. Responde qué sembró.
10. Puesta en marcha (recurso `FiscalSetup`: `CanRead` para ver la lista; confirmar, revocar y marcar impuestos revisados con `[RequirePermission(CanExecute)]`): `GET /api/v1/FiscalSetup/checklist`, `POST /api/v1/FiscalSetup/confirm`, `POST /api/v1/FiscalSetup/revoke` `{ reason }`, `POST /api/v1/Taxes/{id}/mark-reviewed`.
11. `GET/POST/PUT /api/v1/LeadDocumentNumberingAuthorizations?documentTypeId=` + `POST /{id}/deactivate`. No se edita el rango de una autorización que ya numeró documentos (solo `ValidUntil` y `IsActive`).
12. La emisión (SPEC 83) toma el número del rango activo con `SELECT … FOR UPDATE` cuando el perfil lo exige, y valida la identificación tributaria del receptor según el perfil en los tipos que la piden (factura y nota de crédito; el consumidor final de SPEC 89 queda exento de la validación).
13. El PDF (SPEC 84) imprime los datos de la autorización y el equivalente en moneda local.

**C. Menú y permisos**
14. Administración: `FiscalSetup` → `/administration/fiscal-setup` (lista de verificación y país; recurso `FiscalSetup`), `NumberingAuthorizations` → `/administration/numbering-authorizations` (`ControllerName = "LeadDocumentNumberingAuthorizations"`). Permisos: SuperAdmin y Admin con todo; un rol de contador puede recibir `FiscalSetup` con `CanExecute`.

**D. Factura electrónica (solo el punto de conexión)**
15. `IElectronicInvoicingProvider` y `ElectronicInvoicingProviderResolver` (por clave). Implementación `NoElectronicInvoicingProvider` para los perfiles sin clave. Ningún proveedor real en esta spec.

**Out of scope:**
- Proveedores reales de factura electrónica de cualquier país.
- Descarga automática de la tasa del Banco Central (se carga a mano en SPEC 56; una spec futura puede automatizarla).
- Libros fiscales y resumen de impuestos: SPEC 91.

## Errores

| Código | HTTP | Cuándo |
|---|---|---|
| `FISCAL_COUNTRY_NOT_SUPPORTED` | 400 | País sin perfil activo |
| `FISCAL_COUNTRY_LOCKED` | 409 | Cambiar de país con documentos emitidos |
| `NUMBERING_AUTHORIZATION_REQUIRED` | 409 | Emitir sin un rango activo cuando el perfil lo exige |
| `NUMBERING_AUTHORIZATION_EXHAUSTED` / `NUMBERING_AUTHORIZATION_EXPIRED` | 409 | Rango agotado o vencido |
| `NUMBERING_RANGE_OVERLAP` | 409 | Rango que se solapa con otro del mismo tipo |
| `INVALID_TAX_ID` | 400 | Identificación tributaria que no cumple el patrón del perfil |
| `FISCAL_SETUP_NOT_CONFIRMED` | 409 | Emitir factura o nota de crédito sin la puesta en marcha confirmada |
| `FISCAL_SETUP_INCOMPLETE` | 409 | Confirmar con ítems obligatorios pendientes (devuelve cuáles) |

## Criterios de aceptación

1. Elegir `NI` en una empresa nueva siembra `IVA15`, `IVA0`, `IVA0-EXP`, `IR2`, `IR10`, `IMI1` y los tres grupos, con "General" como predeterminado. Repetirlo no duplica nada.
2. Con un rango autorizado 1–100 para factura, la primera factura emitida toma el 1. Al emitir la 50 (quedan 50 números) se genera `NumberingRangeLow`. Agotado el rango → 409.
3. Una factura en USD emitida con tasa 36,6243 muestra en el PDF el total en USD y su equivalente en NIO, y los datos de la autorización.
4. Un RUC `J0310000012345` es válido; `J123` → 400 `INVALID_TAX_ID`.
5. Sin proveedor de factura electrónica, los documentos quedan `ElectronicStatus = NotRequired` y el flujo de SPEC 83 no cambia.
6. Un perfil de prueba de otro país (en tests) se siembra y opera sin cambios de código en el núcleo.
7. Unitarios: siembra idempotente, numeración por rango (concurrencia, agotamiento, vencimiento, alertas), validación de identificación y equivalente en moneda local. Integración: flujo de factura con perfil `NI`.

## Decisiones

- **Perfil como datos (`jsonb` sembrado) más un proveedor opcional por país:** agregar un país sin factura electrónica es solo datos. Uno con factura electrónica suma una clase que implementa `IElectronicInvoicingProvider`.
- **Valores de Nicaragua como punto de partida editable:** tasas y reglas tributarias cambian. El sistema siembra, y la empresa y su contador confirman y ajustan.

## Riesgos

- Requisitos de la DGI para sistemas de facturación computarizados (autorización del sistema, leyendas exactas, formato) deben confirmarse con un contador o con la DGI antes de operar. La spec deja todo configurable para no depender de un texto fijo.
- Si la DGI exige factura electrónica al tipo de contribuyente de la empresa, operar solo con facturas internas no basta.

## Seed de desarrollo (Nicaragua, datos simulados)

Solo en `Development`, solo para la empresa de desarrollo `JOIN-NI-001` (SPEC 100), idempotente (inserta lo que falte). Todos los nombres, RUC, cuentas y referencias son **ficticios** (ver "Datos de ejemplo de Nicaragua" en `specs/README.md`).

- Perfil `NI` activo en `FiscalCountryProfiles` y `JOIN-NI-001` con `FiscalCountryCode = NI`, `NIO` local y `USD` referencia.
- Tasas USD → NIO: las siembra SPEC 100.
- Autorizaciones de numeración simuladas: factura `DGI-SIM-2026-0001`, rango 1–5000; nota de crédito `DGI-SIM-2026-0002`, rango 1–1000; ambas vigentes hasta el 31/12/2027.
- Tipo de identificación `RUC` con su patrón.
