using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonBusinessProfiles;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Handles person business profile updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdatePersonBusinessProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonBusinessProfileActiveCoordinator activeCoordinator) : IRequestHandler<UpdatePersonBusinessProfileCommand, Response<Guid>>
{
    /// <summary>
    /// Updates a person business profile for the current tenant.
    /// </summary>
    public async Task<Response<Guid>> Handle(UpdatePersonBusinessProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var profileRepository = unitOfWork.PersonBusinessProfiles;
        var entity = await profileRepository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException(
                nameof(PersonBusinessProfile),
                request.Id,
                "Person business profile not found for the current tenant.");
        }

        if (entity.PersonId != request.PersonId)
        {
            throw new NotFoundException(
                nameof(PersonBusinessProfile),
                request.Id,
                "Person business profile not found for the requested person.");
        }

        var referenceErrors = await ValidateCatalogReferencesAsync(request.IndustryId, request.TaxRegimeId, companyId);
        if (referenceErrors.Count != 0)
        {
            return Response<Guid>.Error("INVALID_REFERENCES", referenceErrors);
        }

        try
        {
            entity.Update(
                request.IndustryId,
                request.TaxRegimeId,
                request.Website,
                request.FoundationDate);

            if (request.IsActive == true)
            {
                await activeCoordinator.DeactivateOtherActiveProfilesAsync(
                    companyId,
                    request.PersonId,
                    entity.Id,
                    cancellationToken);
                entity.Reactivate();
            }
            else if (request.IsActive == false)
            {
                entity.Deactivate();
            }
        }
        catch (ArgumentException ex)
        {
            return Response<Guid>.Error("INVALID_BUSINESS_PROFILE_DATA", [ex.Message]);
        }

        await profileRepository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the person business profile."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person business profile updated successfully.",
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
