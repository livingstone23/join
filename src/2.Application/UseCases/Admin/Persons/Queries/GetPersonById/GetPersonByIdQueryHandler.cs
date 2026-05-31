using System.Globalization;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Persons.Queries;

/// <summary>
/// Handles the GetPersonByIdQuery using high-performance read operations (Dapper).
/// Eliminates Entity Framework tracking overhead and ensures Multi-Tenancy isolation.
/// </summary>
/// <param name="connectionFactory">The factory to create DB agnostic connections.</param>
/// <param name="currentUserService">The service that retrieves the current user's CompanyId (Tenant).</param>
public class GetPersonByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetPersonByIdQuery, Response<PersonDetailDto>>
{
    /// <summary>
    /// Executes the high-performance SQL query to retrieve a person and related data.
    /// </summary>
    public async Task<Response<PersonDetailDto>> Handle(GetPersonByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PersonDetailDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var tenantId = currentUserService.CompanyId;

        const string sql = """
            SELECT
                c.Id,
                c.CompanyId,
                co.Name AS CompanyName,
                c.PersonType,
                CASE
                    WHEN c.PersonType = 1 THEN 'Natural'
                    WHEN c.PersonType = 2 THEN 'Jurídica'
                    ELSE 'Desconocido'
                END AS PersonTypeName,
                c.GenderId,
                g.Name AS GenderName,
                c.IsActive,
                c.FirstName,
                c.MiddleName,
                c.LastName,
                c.SecondLastName,
                c.CommercialName,
                c.IdentificationTypeId,
                it.Name AS IdentificationTypeName,
                c.IdentificationNumber
            FROM Admin.Persons c
            INNER JOIN Common.Companies co
                ON co.Id = c.CompanyId
               AND co.GcRecord = 0
            LEFT JOIN Admin.IdentificationTypes it ON c.IdentificationTypeId = it.Id
            LEFT JOIN Admin.Genders g
                ON g.Id = c.GenderId
               AND g.CompanyId = @TenantId
               AND g.GcRecord = 0
            WHERE c.Id = @Id AND c.CompanyId = @TenantId AND c.GcRecord = 0;

            SELECT
                a.Id,
                a.AddressLine1,
                a.AddressLine2,
                a.ZipCode,
                a.IsDefault,
                a.StreetTypeId,
                st.Name AS StreetTypeName,
                a.CountryId,
                cn.Name AS CountryName,
                a.RegionId,
                re.Name AS RegionName,
                a.ProvinceId,
                pr.Name AS ProvinceName,
                a.MunicipalityId,
                mu.Name AS MunicipalityName,
                a.Created
            FROM Admin.PersonAddresses a
            LEFT JOIN Common.StreetTypes st ON a.StreetTypeId = st.Id
            LEFT JOIN Common.Countries cn ON a.CountryId = cn.Id
            LEFT JOIN Admin.Regions re ON a.RegionId = re.Id
            LEFT JOIN Common.Provinces pr ON a.ProvinceId = pr.Id
            LEFT JOIN Common.Municipalities mu ON a.MunicipalityId = mu.Id
            WHERE a.PersonId = @Id AND a.CompanyId = @TenantId AND a.GcRecord = 0;

            SELECT
                pc.Id,
                pc.ContactType,
                pc.ContactValue,
                pc.IsPrimary,
                pc.Comments,
                pc.Created
            FROM Admin.PersonContacts pc
            WHERE pc.PersonId = @Id AND pc.CompanyId = @TenantId AND pc.GcRecord = 0;

            SELECT
                pe.Id,
                pe.EmployerName,
                pe.JobTitle,
                pe.StartDate,
                pe.EndDate,
                pe.IsCurrent,
                pe.IsActive,
                pe.Created AS CreatedAt
            FROM Admin.PersonEmployments pe
            WHERE pe.PersonId = @Id
              AND pe.CompanyId = @TenantId
              AND pe.GcRecord = 0
            ORDER BY pe.IsCurrent DESC, pe.StartDate DESC;

            SELECT
                pbp.Id,
                pbp.IndustryId,
                i.Name AS IndustryName,
                pbp.TaxRegimeId,
                tr.Name AS TaxRegimeName,
                pbp.Website,
                pbp.FoundationDate,
                pbp.IsActive,
                pbp.Created AS CreatedAt
            FROM Admin.PersonBusinessProfiles pbp
            INNER JOIN Admin.Industries i
                ON i.Id = pbp.IndustryId
               AND i.CompanyId = @TenantId
               AND i.GcRecord = 0
            INNER JOIN Admin.TaxRegimes tr
                ON tr.Id = pbp.TaxRegimeId
               AND tr.CompanyId = @TenantId
               AND tr.GcRecord = 0
            WHERE pbp.PersonId = @Id
              AND pbp.CompanyId = @TenantId
              AND pbp.GcRecord = 0
            ORDER BY pbp.IsActive DESC, pbp.Created DESC;

            SELECT
                pfp.Id,
                pfp.IncomeRangeId,
                ir.DisplayName AS IncomeRangeName,
                pfp.SourceOfFunds,
                pfp.DeclaredDate,
                pfp.IsCurrent,
                pfp.IsActive,
                pfp.Created AS CreatedAt
            FROM Admin.PersonFinancialProfiles pfp
            INNER JOIN Admin.IncomeRanges ir
                ON ir.Id = pfp.IncomeRangeId
               AND ir.CompanyId = @TenantId
               AND ir.GcRecord = 0
            WHERE pfp.PersonId = @Id
              AND pfp.CompanyId = @TenantId
              AND pfp.GcRecord = 0
            ORDER BY pfp.IsCurrent DESC, pfp.DeclaredDate DESC;
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                sql,
                new { Id = request.PersonId, TenantId = tenantId },
                cancellationToken: cancellationToken));

        var person = await multi.ReadFirstOrDefaultAsync<PersonListItemDto>();

        if (person is null)
        {
            return Response<PersonDetailDto>.Error(
                "PERSON_NOT_FOUND",
                ["Person not found."]);
        }

        var addressesRaw = (await multi.ReadAsync<dynamic>()).ToList();
        var contactsRaw = (await multi.ReadAsync<dynamic>()).ToList();
        var employments = (await multi.ReadAsync<PersonEmploymentDetailDto>()).ToList();
        var businessProfiles = (await multi.ReadAsync<PersonBusinessProfileDetailDto>()).ToList();
        var financialProfiles = (await multi.ReadAsync<PersonFinancialProfileDetailDto>()).ToList();

        var addresses = addressesRaw.Count > 0
            ? addressesRaw.Select(a => new PersonAddressDto
            {
                Id = a.Id,
                AddressLine1 = a.AddressLine1 ?? string.Empty,
                AddressLine2 = a.AddressLine2,
                ZipCode = a.ZipCode ?? string.Empty,
                IsDefault = a.IsDefault ?? false,
                StreetTypeId = a.StreetTypeId ?? Guid.Empty,
                CountryId = a.CountryId ?? Guid.Empty,
                RegionId = a.RegionId,
                ProvinceId = a.ProvinceId ?? Guid.Empty,
                MunicipalityId = a.MunicipalityId ?? Guid.Empty,
                StreetTypeName = a.StreetTypeName,
                CountryName = a.CountryName,
                RegionName = a.RegionName,
                ProvinceName = a.ProvinceName,
                MunicipalityName = a.MunicipalityName,
                CreatedAt = a.Created != null
                    ? ((DateTime)a.Created).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                    : string.Empty
            }).ToList()
            : null;

        var contacts = contactsRaw.Count > 0
            ? contactsRaw.Select(c =>
            {
                var contactTypeCode = Convert.ToInt32(c.ContactType, CultureInfo.InvariantCulture);
                var contactTypeName = Enum.IsDefined(typeof(ContactType), contactTypeCode)
                    ? ((ContactType)contactTypeCode).GetDisplayName()
                    : string.Empty;

                return new PersonContactDto
                {
                    Id = c.Id,
                    ContactType = contactTypeCode.ToString(CultureInfo.InvariantCulture),
                    ContactName = contactTypeName,
                    ContactValue = c.ContactValue ?? string.Empty,
                    IsPrimary = c.IsPrimary ?? false,
                    Comments = c.Comments,
                    CreatedAt = c.Created != null
                        ? ((DateTime)c.Created).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                        : string.Empty
                };
            }).ToList()
            : null;

        return new Response<PersonDetailDto>
        {
            IsSuccess = true,
            Message = "Person retrieved successfully.",
            Data = new PersonDetailDto
            {
                Person = person,
                Addresses = addresses,
                Contacts = contacts,
                Employments = employments.Count > 0 ? employments : null,
                BusinessProfiles = businessProfiles.Count > 0 ? businessProfiles : null,
                FinancialProfiles = financialProfiles.Count > 0 ? financialProfiles : null
            }
        };
    }
}
