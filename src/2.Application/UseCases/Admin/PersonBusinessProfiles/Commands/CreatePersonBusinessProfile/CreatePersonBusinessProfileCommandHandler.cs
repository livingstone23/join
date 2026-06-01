using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Handles person business profile creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreatePersonBusinessProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonBusinessProfileActiveCoordinator activeCoordinator) : IRequestHandler<CreatePersonBusinessProfileCommand, Response<Guid>>
{
    /// <summary>
    /// Creates a person business profile associated with the authenticated tenant company.
    /// </summary>
    public async Task<Response<Guid>> Handle(CreatePersonBusinessProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var person = await unitOfWork.GetRepository<Person>().GetAsync(request.PersonId);

        if (person is null || person.CompanyId != companyId || person.GcRecord != 0)
        {
            return Response<Guid>.Error("PERSON_NOT_FOUND", ["The requested person does not exist in the current tenant."]);
        }

        var referenceErrors = await ValidateCatalogReferencesAsync(request.IndustryId, request.TaxRegimeId, companyId);
        if (referenceErrors.Count != 0)
        {
            return Response<Guid>.Error("INVALID_REFERENCES", referenceErrors);
        }

        PersonBusinessProfile entity;
        try
        {
            entity = PersonBusinessProfile.Create(
                companyId,
                request.PersonId,
                request.IndustryId,
                request.TaxRegimeId,
                request.Website,
                request.FoundationDate);

            if (request.IsActive == false)
            {
                entity.Deactivate();
            }
            else
            {
                await activeCoordinator.DeactivateOtherActiveProfilesAsync(
                    companyId,
                    request.PersonId,
                    null,
                    cancellationToken);
            }
        }
        catch (ArgumentException ex)
        {
            return Response<Guid>.Error("INVALID_BUSINESS_PROFILE_DATA", [ex.Message]);
        }

        await unitOfWork.PersonBusinessProfiles.InsertAsync(entity);

        var result = await unitOfWork.SaveAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("BUSINESS_PROFILE_CREATE_FAILED", ["The person business profile could not be created due to a persistence error."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person business profile created successfully.",
            Data = entity.Id
        };
    }

    private async Task<List<string>> ValidateCatalogReferencesAsync(Guid industryId, Guid taxRegimeId, Guid companyId)
    {
        var referenceErrors = new List<string>();

        var industry = await unitOfWork.GetRepository<Industry>().GetAsync(industryId);
        if (industry is null || industry.CompanyId != companyId || industry.GcRecord != 0)
        {
            referenceErrors.Add($"IndustryId '{industryId}' does not exist in the current tenant.");
        }

        var taxRegime = await unitOfWork.GetRepository<TaxRegime>().GetAsync(taxRegimeId);
        if (taxRegime is null || taxRegime.CompanyId != companyId || taxRegime.GcRecord != 0)
        {
            referenceErrors.Add($"TaxRegimeId '{taxRegimeId}' does not exist in the current tenant.");
        }

        return referenceErrors;
    }
}
