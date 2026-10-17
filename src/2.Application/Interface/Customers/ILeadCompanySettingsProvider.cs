using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.Interface.Customers;

/// <summary>
/// Reads the lead module rules of a company (SPEC 59). A company without a row gets
/// <see cref="LeadCompanySettings.Defaults"/>; it never fails.
/// </summary>
public interface ILeadCompanySettingsProvider
{
    Task<LeadCompanySettings> GetAsync(Guid companyId);
}
