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

    /// <summary>
    /// Copies to the company every active global calendar activity type and status it does not have yet, with
    /// the company's <c>PENDING</c> status as the initial status of the new types (SPEC 44). Idempotent. Does not
    /// create <c>CalendarCompany</c>: choosing its default user is a human decision.
    /// </summary>
    /// <param name="companyId">Identifier of the company that receives the catalogs.</param>
    /// <param name="cancellationToken">Token used to cancel the seeding process.</param>
    /// <returns>How many activity types and statuses were added.</returns>
    Task<CalendarCatalogSyncResult> SeedCalendarCatalogsForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// SPEC 69 — creates the unique live row of <c>CalendarAttachmentSettings</c> for the company with the spec
    /// defaults (PDF / Word / Excel / Image, 10 MB per file, 5 per comment, 200 per day). Idempotent: a row
    /// already present is preserved.
    /// </summary>
    Task SeedCalendarAttachmentSettingsForCompanyAsync(Guid companyId, CancellationToken cancellationToken = default);


}

/// <summary>
/// Result of <see cref="ICompanyCatalogSeeder.SeedCalendarCatalogsForCompanyAsync"/>.
/// </summary>
/// <param name="ActivitiesAdded">Activity types copied to the company.</param>
/// <param name="StatusesAdded">Statuses copied to the company.</param>
public sealed record CalendarCatalogSyncResult(int ActivitiesAdded, int StatusesAdded);
