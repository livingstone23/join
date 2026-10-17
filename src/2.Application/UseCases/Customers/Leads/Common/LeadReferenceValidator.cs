using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Admin;
using JOIN.Domain.Catalog;
using JOIN.Domain.Customers.Leads;

namespace JOIN.Application.UseCases.Customers.Leads.Common;

/// <summary>
/// Reference checks of the lead commands (SPEC 61): each catalog row must be live, active and of the company.
/// </summary>
public sealed class LeadReferenceValidator(IUnitOfWork unitOfWork)
{
    public async Task<Response<T>?> SourceAsync<T>(Guid companyId, Guid id) =>
        await ActiveAsync<LeadSource>(companyId, id) is null
            ? Response<T>.Error(LeadErrorCodes.InvalidLeadSource, ["The lead source is inactive or does not exist in the company."])
            : null;

    public async Task<Response<T>?> RelationshipTypeAsync<T>(Guid companyId, Guid id) =>
        await ActiveAsync<RelationshipType>(companyId, id) is null
            ? Response<T>.Error(LeadErrorCodes.InvalidRelationshipType, ["The relationship type is inactive or does not exist in the company."])
            : null;

    public async Task<LeadReason?> ReasonAsync(Guid companyId, Guid id, LeadReasonUsage usage)
    {
        var reason = await ActiveAsync<LeadReason>(companyId, id);
        return reason?.UsedFor == usage ? reason : null;
    }

    /// <summary>A live person of the company (<paramref name="mustBeActive"/> also requires it active).</summary>
    public async Task<Person?> PersonAsync(Guid companyId, Guid id, bool mustBeActive = true)
    {
        var person = (await unitOfWork.GetRepository<Person>().GetAllIncludingDeletedAsync(p => p.Id == id && p.CompanyId == companyId && p.GcRecord == 0))
            .FirstOrDefault();
        return person is not null && (!mustBeActive || person.IsActive) ? person : null;
    }

    public async Task<Product?> ProductAsync(Guid companyId, Guid id)
    {
        var product = (await unitOfWork.GetRepository<Product>().GetAllIncludingDeletedAsync(p => p.Id == id && p.CompanyId == companyId && p.GcRecord == 0))
            .FirstOrDefault();
        return product is { IsActive: true } ? product : null;
    }

    /// <summary>Optional project and area must be live rows of the company.</summary>
    public async Task<Response<T>?> ProjectAndAreaAsync<T>(Guid companyId, Guid? projectId, Guid? areaId)
    {
        if (projectId is { } p && !(await unitOfWork.GetRepository<Project>().GetAllIncludingDeletedAsync(x => x.Id == p && x.CompanyId == companyId && x.GcRecord == 0)).Any())
        {
            return Response<T>.Error(LeadErrorCodes.InvalidReference, ["The project does not exist in the company."]);
        }

        if (areaId is { } a && !(await unitOfWork.GetRepository<Area>().GetAllIncludingDeletedAsync(x => x.Id == a && x.CompanyId == companyId && x.GcRecord == 0)).Any())
        {
            return Response<T>.Error(LeadErrorCodes.InvalidReference, ["The area does not exist in the company."]);
        }

        return null;
    }

    /// <summary>Live stages of the company, for the stage rules.</summary>
    public async Task<List<LeadStage>> StagesAsync(Guid companyId) =>
        (await unitOfWork.GetRepository<LeadStage>().GetAllIncludingDeletedAsync(s => s.CompanyId == companyId && s.GcRecord == 0)).ToList();

    private async Task<T?> ActiveAsync<T>(Guid companyId, Guid id) where T : class, ICommercialCatalogItem =>
        (await unitOfWork.GetRepository<T>().GetAllIncludingDeletedAsync(x => x.Id == id && x.CompanyId == companyId && x.GcRecord == 0 && x.IsActive))
        .FirstOrDefault();
}
