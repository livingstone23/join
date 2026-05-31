using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

/// <summary>
/// Handles soft delete operations for customer addresses using Entity Framework Core.
/// </summary>
public sealed class DeletePersonAddressCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonAddressDefaultCoordinator defaultCoordinator) : IRequestHandler<DeletePersonAddressCommand, Response<bool>>
{
    /// <summary>
    /// Marks the customer address as logically deleted for the current tenant.
    /// </summary>
    /// <param name="request">The delete command.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response indicating whether the soft delete succeeded.</returns>
    public async Task<Response<bool>> Handle(DeletePersonAddressCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.PersonAddresses;
        var entity = await repository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

        if (entity is null)
        {
            return Response<bool>.Error(
                "PERSON_ADDRESS_NOT_FOUND",
                ["Person address not found for the current tenant."]);
        }

        var wasDefault = entity.IsDefault;

        entity.Deactivate();
        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);

        if (wasDefault)
        {
            try
            {
                await defaultCoordinator.PromoteNextDefaultAsync(
                    companyId,
                    entity.PersonId,
                    entity.Id,
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return Response<bool>.Error("INVALID_ADDRESS_DEFAULT", [ex.Message]);
            }
        }

        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);

        if (affected <= 0)
        {
            return Response<bool>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the customer address."]);
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Person address deleted successfully.",
            Data = true
        };
    }
}
