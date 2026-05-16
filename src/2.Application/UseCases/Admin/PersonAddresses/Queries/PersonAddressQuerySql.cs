using System.Globalization;
using JOIN.Application.DTO.Admin;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Queries;

/// <summary>
/// Shared SQL projection and mapping for person address read queries.
/// </summary>
internal static class PersonAddressQuerySql
{
    internal const string SelectWithCatalogNames = """
        SELECT
            a.Id,
            a.PersonId,
            a.AddressLine1,
            a.AddressLine2,
            a.ZipCode,
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
            a.IsDefault,
            a.Created
        FROM Admin.PersonAddresses a
        LEFT JOIN Common.StreetTypes st ON a.StreetTypeId = st.Id
        LEFT JOIN Common.Countries cn ON a.CountryId = cn.Id
        LEFT JOIN Admin.Regions re ON a.RegionId = re.Id
        LEFT JOIN Common.Provinces pr ON a.ProvinceId = pr.Id
        LEFT JOIN Common.Municipalities mu ON a.MunicipalityId = mu.Id
        """;

    internal static PersonAddressResponseDto ToResponseDto(PersonAddressReadRow row) =>
        new()
        {
            Id = row.Id,
            PersonId = row.PersonId,
            AddressLine1 = row.AddressLine1 ?? string.Empty,
            AddressLine2 = row.AddressLine2,
            ZipCode = row.ZipCode ?? string.Empty,
            StreetTypeId = row.StreetTypeId,
            StreetTypeName = row.StreetTypeName,
            CountryId = row.CountryId,
            CountryName = row.CountryName,
            RegionId = row.RegionId,
            RegionName = row.RegionName,
            ProvinceId = row.ProvinceId,
            MunicipalityId = row.MunicipalityId,
            MunicipalityName = row.MunicipalityName,
            ProvinceName = row.ProvinceName,
            IsDefault = row.IsDefault,
            CreatedAt = row.Created.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        };
}

/// <summary>
/// Dapper row model for person address queries including catalog display names.
/// </summary>
internal sealed class PersonAddressReadRow
{
    public Guid Id { get; init; }
    public Guid PersonId { get; init; }
    public string? AddressLine1 { get; init; }
    public string? AddressLine2 { get; init; }
    public string? ZipCode { get; init; }
    public Guid StreetTypeId { get; init; }
    public string? StreetTypeName { get; init; }
    public Guid CountryId { get; init; }
    public string? CountryName { get; init; }
    public Guid? RegionId { get; init; }
    public string? RegionName { get; init; }
    public Guid ProvinceId { get; init; }
    public string? ProvinceName { get; init; }
    public Guid MunicipalityId { get; init; }
    public string? MunicipalityName { get; init; }
    public bool IsDefault { get; init; }
    public DateTime Created { get; init; }
}
