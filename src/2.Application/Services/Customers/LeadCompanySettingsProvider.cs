using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.Services.Customers;

/// <inheritdoc />
public sealed class LeadCompanySettingsProvider(IUnitOfWork unitOfWork) : ILeadCompanySettingsProvider
{
    /// <inheritdoc />
    public async Task<LeadCompanySettings> GetAsync(Guid companyId) =>
        (await unitOfWork.GetRepository<LeadCompanySettings>()
            .GetAllIncludingDeletedAsync(s => s.CompanyId == companyId && s.GcRecord == 0))
        .FirstOrDefault() ?? LeadCompanySettings.Defaults(companyId);
}
