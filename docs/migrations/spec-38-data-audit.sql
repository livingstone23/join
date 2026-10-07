-- SPEC 38 — Pre-merge data audit
-- Run against staging / dev DB before applying the global tenant query filter to:
--   Admin.Regions, Admin.Customers, Admin.Genders, Admin.Industries, Admin.TaxRegimes,
--   Admin.IncomeRanges, Admin.PersonBusinessProfiles, Admin.PersonEmployments, Admin.PersonFinancialProfiles,
--   Security.RoleCompanies, Admin.CompanyModules (these two moved from soft-delete only to tenant filter)
--
-- Read-only: every statement is a SELECT. Run each block and paste the counts in the PR.
-- Capture counts in the PR. If any are > 0, decide before merging whether to reassign
-- the rows (with a data-fix script) or leave them as orphans that will become invisible.

-- ============================================================
-- A. Filas con CompanyId = Guid.Empty (quedan invisibles al aplicar el filtro)
-- ============================================================
SELECT 'Admin.Regions'              AS [Table], COUNT(*) AS [EmptyCompanyId] FROM [Admin].[Regions]              WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.Customers'             , COUNT(*) FROM [Admin].[Customers]             WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.Genders'               , COUNT(*) FROM [Admin].[Genders]               WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.Industries'            , COUNT(*) FROM [Admin].[Industries]            WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.TaxRegimes'            , COUNT(*) FROM [Admin].[TaxRegimes]            WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.IncomeRanges'          , COUNT(*) FROM [Admin].[IncomeRanges]          WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.PersonBusinessProfiles', COUNT(*) FROM [Admin].[PersonBusinessProfiles] WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.PersonEmployments'      , COUNT(*) FROM [Admin].[PersonEmployments]      WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.PersonFinancialProfiles', COUNT(*) FROM [Admin].[PersonFinancialProfiles] WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Security.RoleCompanies'       , COUNT(*) FROM [Security].[RoleCompanies]       WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000'
UNION ALL
SELECT 'Admin.CompanyModules'         , COUNT(*) FROM [Admin].[CompanyModules]         WHERE [CompanyId] = '00000000-0000-0000-0000-000000000000';

-- ============================================================
-- B. Referencias cruzadas entre empresas (CompanyId del registro != CompanyId del catálogo)
-- ============================================================

-- Persons.GenderId -> Genders.CompanyId
SELECT 'Admin.Persons.GenderId' AS [Relation], COUNT(*) AS [CrossTenant]
FROM [Admin].[Persons] p
INNER JOIN [Admin].[Genders] g ON g.[Id] = p.[GenderId]
WHERE p.[CompanyId] <> g.[CompanyId] OR g.[CompanyId] = '00000000-0000-0000-0000-000000000000';

-- PersonAddresses.RegionId -> Regions.CompanyId
SELECT 'Admin.PersonAddresses.RegionId',
       COUNT(*)
FROM [Admin].[PersonAddresses] a
INNER JOIN [Admin].[Regions] r ON r.[Id] = a.[RegionId]
WHERE a.[CompanyId] <> r.[CompanyId] OR r.[CompanyId] = '00000000-0000-0000-0000-000000000000';

-- PersonBusinessProfiles.IndustryId -> Industries.CompanyId
SELECT 'Admin.PersonBusinessProfiles.IndustryId',
       COUNT(*)
FROM [Admin].[PersonBusinessProfiles] pbp
INNER JOIN [Admin].[Industries] i ON i.[Id] = pbp.[IndustryId]
WHERE pbp.[CompanyId] <> i.[CompanyId] OR i.[CompanyId] = '00000000-0000-0000-0000-000000000000';

-- PersonBusinessProfiles.TaxRegimeId -> TaxRegimes.CompanyId
SELECT 'Admin.PersonBusinessProfiles.TaxRegimeId',
       COUNT(*)
FROM [Admin].[PersonBusinessProfiles] pbp
INNER JOIN [Admin].[TaxRegimes] tr ON tr.[Id] = pbp.[TaxRegimeId]
WHERE pbp.[CompanyId] <> tr.[CompanyId] OR tr.[CompanyId] = '00000000-0000-0000-0000-000000000000';

-- PersonFinancialProfiles.IncomeRangeId -> IncomeRanges.CompanyId
SELECT 'Admin.PersonFinancialProfiles.IncomeRangeId',
       COUNT(*)
FROM [Admin].[PersonFinancialProfiles] pfp
INNER JOIN [Admin].[IncomeRanges] ir ON ir.[Id] = pfp.[IncomeRangeId]
WHERE pfp.[CompanyId] <> ir.[CompanyId] OR ir.[CompanyId] = '00000000-0000-0000-0000-000000000000';

-- ============================================================
-- C. Provincias globales que apuntan a una región (catálogo privado de una empresa)
--    Common.Provinces no tiene CompanyId: es un catálogo global (SPEC 38, Out of scope).
--    Con el filtro de tenant en Region, toda provincia con RegionId activo apunta a la
--    región de UNA empresa; las demás la ven con RegionName = null (mitigado por F2.2).
--    Se agrupa por la empresa dueña de la región para dimensionar el impacto.
-- ============================================================
SELECT 'Common.Provinces.RegionId -> region of company' AS [Relation],
       r.[CompanyId]                                     AS [RegionCompanyId],
       COUNT(*)                                          AS [Provinces]
FROM [Common].[Provinces] p
INNER JOIN [Admin].[Regions] r ON r.[Id] = p.[RegionId]
WHERE p.[GcRecord] = 0
  AND r.[GcRecord] = 0
GROUP BY r.[CompanyId];

-- ============================================================
-- D. Sanity check: volumen de RoleCompanies / CompanyModules que pasan a filtro de tenant (sección E)
-- ============================================================
SELECT 'Security.RoleCompanies rows' AS [Check], COUNT(*) AS [Rows] FROM [Security].[RoleCompanies] WHERE [GcRecord] = 0;
SELECT 'Admin.CompanyModules rows'   AS [Check], COUNT(*) AS [Rows] FROM [Admin].[CompanyModules]   WHERE [GcRecord] = 0;
