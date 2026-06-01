using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Handles soft delete operations for person business profiles using Entity Framework Core.
/// </summary>
public sealed class DeletePersonBusinessProfileCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<DeletePersonBusinessProfileCommand, Response<bool>>
{
    /// <summary>
    /// Marks the person business profile as logically deleted for the current tenant.
    /// </summary>
    public async Task<Response<bool>> Handle(DeletePersonBusinessProfileCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.PersonBusinessProfiles;
        var entity = await repository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

        if (entity is null)
        {
            return Response<bool>.Error(
                "PERSON_BUSINESS_PROFILE_NOT_FOUND",
                ["No active business profile was found for the current tenant."]);
        }

        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);

        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);

        if (affected <= 0)
        {
            return Response<bool>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the person business profile."]);
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Person business profile deleted successfully.",
            Data = true
        };
    }
}
