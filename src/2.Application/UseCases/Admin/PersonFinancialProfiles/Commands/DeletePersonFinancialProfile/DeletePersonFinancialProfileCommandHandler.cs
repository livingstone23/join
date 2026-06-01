using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonFinancialProfiles;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Handles soft delete operations for person financial profiles using Entity Framework Core.
/// </summary>
public sealed class DeletePersonFinancialProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonFinancialProfileCurrentCoordinator currentCoordinator) : IRequestHandler<DeletePersonFinancialProfileCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeletePersonFinancialProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.PersonFinancialProfiles;
        var entity = await repository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

        if (entity is null)
        {
            return Response<bool>.Error(
                "PERSON_FINANCIAL_PROFILE_NOT_FOUND",
                ["No active financial profile was found for the current tenant."]);
        }

        var wasCurrent = entity.IsCurrent;
        var personId = entity.PersonId;

        entity.Deactivate();
        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);

        if (wasCurrent)
        {
            try
            {
                await currentCoordinator.PromoteNextCurrentAsync(
                    companyId,
                    personId,
                    entity.Id,
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return Response<bool>.Error("INVALID_FINANCIAL_PROFILE_CURRENT", [ex.Message]);
            }
        }

        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);

        if (affected <= 0)
        {
            return Response<bool>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the person financial profile."]);
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Person financial profile deleted successfully.",
            Data = true
        };
    }
}
