using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonEmployments;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Handles soft delete operations for person employment records using Entity Framework Core.
/// </summary>
public sealed class DeletePersonEmploymentCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonEmploymentCurrentCoordinator currentCoordinator) : IRequestHandler<DeletePersonEmploymentCommand, Response<bool>>
{
    /// <summary>
    /// Marks the person employment record as logically deleted for the current tenant.
    /// </summary>
    public async Task<Response<bool>> Handle(DeletePersonEmploymentCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.PersonEmployments;
        var entity = await repository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

        if (entity is null)
        {
            return Response<bool>.Error(
                "PERSON_EMPLOYMENT_NOT_FOUND",
                ["No active employment record was found for the current tenant."]);
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
                return Response<bool>.Error("INVALID_EMPLOYMENT_CURRENT", [ex.Message]);
            }
        }

        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);

        if (affected <= 0)
        {
            return Response<bool>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the person employment."]);
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Person employment deleted successfully.",
            Data = true
        };
    }
}
