namespace JOIN.Application.Interface;

/// <summary>
/// Defines application-facing operations required to seed tenant-specific default catalogs for a newly created company.
/// </summary>
public interface ICompanyCatalogSeeder
{
    /// <summary>
    /// Seeds the default catalogs required for the specified company.
    /// </summary>
    /// <param name="companyId">Identifier of the company that will own the seeded catalog data.</param>
    /// <param name="cancellationToken">Token used to cancel the seeding process.</param>
    Task SeedDefaultCatalogsForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);
}
