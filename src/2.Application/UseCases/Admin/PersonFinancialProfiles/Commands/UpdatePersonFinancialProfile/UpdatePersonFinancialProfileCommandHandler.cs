using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Handles person financial profile updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdatePersonFinancialProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<UpdatePersonFinancialProfileCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a person financial profile for the current tenant.
    /// </summary>
    /// <param name="request">The update-financial-profile command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the updated financial profile identifier.</returns>
    public async Task<Response<Guid>> Handle(UpdatePersonFinancialProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;

        var profileRepository = _unitOfWork.GetRepository<PersonFinancialProfile>();
        var profiles = await profileRepository.GetAllAsync();

        var entity = profiles.FirstOrDefault(profile =>
            profile.Id == request.Id &&
            profile.CompanyId == companyId);

        if (entity is null)
        {
            throw new NotFoundException(
                nameof(PersonFinancialProfile),
                request.Id,
                "Person financial profile not found for the current tenant.");
        }

        if (entity.PersonId != request.PersonId)
        {
            throw new NotFoundException(
                nameof(PersonFinancialProfile),
                request.Id,
                "Person financial profile not found for the requested person.");
        }

        var incomeRange = await _unitOfWork.GetRepository<IncomeRange>().GetAsync(request.IncomeRangeId);
        if (incomeRange is null || incomeRange.CompanyId != companyId || incomeRange.GcRecord != 0)
        {
            return Response<Guid>.Error(
                "INVALID_REFERENCES",
                [$"IncomeRangeId '{request.IncomeRangeId}' does not exist in the current tenant."]);
        }

        entity.IncomeRangeId = request.IncomeRangeId;
        entity.SourceOfFunds = request.SourceOfFunds.Trim();
        entity.DeclaredDate = request.DeclaredDate;

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

        if (request.IsCurrent == false)
        {
            entity.Archive();
        }

        await profileRepository.UpdateAsync(entity);

        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the person financial profile."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person financial profile updated successfully.",
            Data = entity.Id
        };
    }
}
