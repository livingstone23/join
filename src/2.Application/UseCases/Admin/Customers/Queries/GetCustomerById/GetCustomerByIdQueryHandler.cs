


using System.Globalization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;
using JOIN.Application.Interface;
using Dapper;



namespace JOIN.Application.UseCases.Admin.Customers.Queries;



/// <summary>
/// Handles the GetCustomerByIdQuery using high-performance read operations (Dapper).
/// Eliminates Entity Framework tracking overhead and ensures Multi-Tenancy isolation.
/// </summary>
/// <param name="connectionFactory">The factory to create DB agnostic connections.</param>
/// <param name="currentUserService">The service to retrieve the current user's CompanyId (Tenant).</param>
public class GetCustomerByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService) 
    : IRequestHandler<GetCustomerByIdQuery, Response<CustomerDto>>
{
    /// <summary>
    /// Executes the high-performance SQL query to retrieve a customer and its related data.
    /// </summary>
    public async Task<Response<CustomerDto>> Handle(GetCustomerByIdQuery request, CancellationToken cancellationToken)
    {
        
        var response = new Response<CustomerDto>();

        // 1. Create and open the database connection
        using var connection = connectionFactory.CreateConnection();

        // 2. Define the Raw SQL Query using multiple result sets.
        // GOLDEN RULE: Always filter by CompanyId (@TenantId) to maintain data isolation.
        // NOTE: Columns updated to match physical database tables (GcRecord instead of IsDeleted, etc.)
        const string sql = """
            -- Query 1: Fetch Customer with Identification Type Name
            SELECT 
                c.Id, c.CompanyId,c.FirstName, c.MiddleName, c.LastName, c.SecondLastName, c.PersonType,
                c.IdentificationTypeId, it.Name AS IdentificationTypeName, 
                c.IdentificationNumber, c.Created
            FROM Admin.Customers c
            LEFT JOIN Admin.IdentificationTypes it ON c.IdentificationTypeId = it.Id
            WHERE c.Id = @Id AND c.CompanyId = @TenantId AND c.GcRecord = 0;

            -- Query 2: Fetch Customer Addresses with Country, Province, etc.
            SELECT a.Id, a.AddressLine1, a.AddressLine2, a.ZipCode, a.IsDefault, 
                a.StreetTypeId, st.Name AS StreetTypeName,
                a.CountryId, co.Name AS CountryName,
                a.RegionId, re.Name AS RegionName,
                a.ProvinceId, pr.Name AS ProvinceName,
                a.MunicipalityId, mu.Name AS MunicipalityName,
                a.Created
            FROM Admin.CustomerAddresses a
            LEFT JOIN Common.StreetTypes st ON a.StreetTypeId = st.Id
            LEFT JOIN Common.Countries co ON a.CountryId = co.Id
            LEFT JOIN Common.Regions re ON a.RegionId = re.Id
            LEFT JOIN Common.Provinces pr ON a.ProvinceId = pr.Id
            LEFT JOIN Common.Municipalities mu ON a.MunicipalityId = mu.Id
            WHERE a.CustomerId = @Id AND a.CompanyId = @TenantId AND a.GcRecord = 0;

            -- Query 3: Fetch Customer Contacts
            SELECT 
                co.Id, co.ContactType, co.ContactValue, co.IsPrimary, co.Comments, co.Created
            FROM Admin.CustomerContacts co
            WHERE co.CustomerId = @Id AND co.CompanyId = @TenantId AND co.GcRecord = 0;
            """;

        // 3. Execute the multiple queries via Dapper
        using var multi = await connection.QueryMultipleAsync(sql, new 
        { 
            Id = request.CustomerId, // Ensure this matches the property in GetCustomerByIdQuery
            TenantId = currentUserService.CompanyId 
        });

        // 4. Map the first result set directly to CustomerDto
        var customerDto = await multi.ReadFirstOrDefaultAsync<CustomerDto>();

        if (customerDto == null)
        {
            response.IsSuccess = false;
            response.Message = "Customer not found.";
            return response;
        }

        // 5. Read the related collections
        var addressesRaw = await multi.ReadAsync<dynamic>();
        var contactsRaw = await multi.ReadAsync<dynamic>();

        // 6. Map Addresses dynamically matching the CustomerAddressDto properties
        var addresses = addressesRaw.Any()
            ? addressesRaw.Select(a => new CustomerAddressDto
                {
                    Id = a.Id,
                    AddressLine1 = a.AddressLine1 ?? string.Empty,
                    AddressLine2 = a.AddressLine2,
                    ZipCode = a.ZipCode ?? string.Empty,
                    IsDefault = a.IsDefault ?? false,
                    
                    // Map IDs
                    StreetTypeId = a.StreetTypeId ?? Guid.Empty,
                    CountryId = a.CountryId ?? Guid.Empty,
                    RegionId = a.RegionId,
                    ProvinceId = a.ProvinceId ?? Guid.Empty,
                    MunicipalityId = a.MunicipalityId ?? Guid.Empty,

                    // Map Names (These were null before)
                    StreetTypeName = a.StreetTypeName,
                    CountryName = a.CountryName,
                    RegionName = a.RegionName,
                    ProvinceName = a.ProvinceName,
                    MunicipalityName = a.MunicipalityName,

                    // Format date
                    CreatedAt = a.Created != null 
                        ? ((DateTime)a.Created).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) 
                        : string.Empty
                }).ToList()
            : null;

        // 7. Map Contacts dynamically matching the CustomerContactDto properties
        var contacts = contactsRaw.Any()
            ? contactsRaw.Select(c => new CustomerContactDto
                {
                    Id = c.Id,
                    // Safe ToString() conversion for the ContactType Enum (int in DB)
                    ContactType = c.ContactType?.ToString() ?? string.Empty,
                    ContactValue = c.ContactValue ?? string.Empty,
                    IsPrimary = c.IsPrimary ?? false,
                    Comments = c.Comments,
                    // Safe date parsing to avoid null reference exceptions
                    CreatedAt = c.Created != null ? ((DateTime)c.Created).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty
                }).ToList()
            : null;

        // 8. Attach the mapped collections to the immutable record using 'with' expression
        response.Data = customerDto with
        {
            Addresses = addresses,
            Contacts = contacts
        };
        
        response.IsSuccess = true;
        response.Message = "Customer retrieved successfully.";

        return response;
    }
}
