using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Handles person business profile updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdatePersonBusinessProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<UpdatePersonBusinessProfileCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a person business profile for the current tenant.
    /// </summary>
    /// <param name="request">The update-business-profile command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the updated business profile identifier.</returns>
    public async Task<Response<Guid>> Handle(UpdatePersonBusinessProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;

        var profileRepository = _unitOfWork.GetRepository<PersonBusinessProfile>();
        var profiles = await profileRepository.GetAllAsync();

        var entity = profiles.FirstOrDefault(profile =>
            profile.Id == request.Id &&
            profile.CompanyId == companyId);

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

        entity.IndustryId = request.IndustryId;
        entity.TaxRegimeId = request.TaxRegimeId;
        entity.Website = request.Website?.Trim();
        entity.FoundationDate = request.FoundationDate?.Date;

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value)
            {
                entity.Reactivate();
            }
            else
            {
                entity.Deactivate();
            }
        }

        await profileRepository.UpdateAsync(entity);

        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

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

        var industry = await _unitOfWork.GetRepository<Industry>().GetAsync(industryId);
        if (industry is null || industry.CompanyId != companyId || industry.GcRecord != 0)
        {
            referenceErrors.Add($"IndustryId '{industryId}' does not exist in the current tenant.");
        }

        var taxRegime = await _unitOfWork.GetRepository<TaxRegime>().GetAsync(taxRegimeId);
        if (taxRegime is null || taxRegime.CompanyId != companyId || taxRegime.GcRecord != 0)
        {
            referenceErrors.Add($"TaxRegimeId '{taxRegimeId}' does not exist in the current tenant.");
        }

        return referenceErrors;
    }
}
