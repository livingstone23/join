using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Handles person financial profile updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdatePersonFinancialProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonFinancialProfileCurrentCoordinator currentCoordinator) : IRequestHandler<UpdatePersonFinancialProfileCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(UpdatePersonFinancialProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var profileRepository = unitOfWork.PersonFinancialProfiles;
        var entity = await profileRepository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

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

        var incomeRange = await unitOfWork.GetRepository<IncomeRange>().GetAsync(request.IncomeRangeId);
        if (incomeRange is null || incomeRange.CompanyId != companyId || incomeRange.GcRecord != 0)
        {
            return Response<Guid>.Error(
                "INVALID_REFERENCES",
                [$"IncomeRangeId '{request.IncomeRangeId}' does not exist in the current tenant."]);
        }

        try
        {
            entity.Update(request.IncomeRangeId, request.SourceOfFunds, request.DeclaredDate);

            if (request.IsActive == true)
            {
                entity.Reactivate();
            }
            else if (request.IsActive == false)
            {
                entity.Deactivate();
            }

            if (request.IsCurrent == true)
            {
                await currentCoordinator.ArchiveOtherCurrentAsync(
                    companyId,
                    request.PersonId,
                    entity.Id,
                    cancellationToken);
                entity.SetAsCurrent();
            }
            else if (request.IsCurrent == false)
            {
                entity.Archive();
            }
        }
        catch (ArgumentException ex)
        {
            return Response<Guid>.Error("INVALID_FINANCIAL_PROFILE_DATA", [ex.Message]);
        }
        catch (InvalidOperationException ex)
        {
            return Response<Guid>.Error("INVALID_FINANCIAL_PROFILE_CURRENT", [ex.Message]);
        }

        await profileRepository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);

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
