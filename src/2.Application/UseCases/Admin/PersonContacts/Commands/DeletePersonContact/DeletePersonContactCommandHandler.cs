using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonContacts;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;

/// <summary>
/// Handles soft delete operations for person contacts using Entity Framework Core.
/// </summary>
public sealed class DeletePersonContactCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonContactPrimaryCoordinator primaryCoordinator) : IRequestHandler<DeletePersonContactCommand, Response<bool>>
{
    /// <summary>
    /// Marks the person contact as logically deleted for the current tenant.
    /// </summary>
    public async Task<Response<bool>> Handle(DeletePersonContactCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.PersonContacts;
        var entity = await repository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

        if (entity is null)
        {
            return Response<bool>.Error(
                "PERSON_CONTACT_NOT_FOUND",
                ["Person contact not found for the current tenant."]);
        }

        var wasPrimary = entity.IsPrimary;
        var contactType = entity.ContactType;

        entity.Deactivate();
        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);

        if (wasPrimary)
        {
            try
            {
                await primaryCoordinator.PromoteNextPrimaryAsync(
                    companyId,
                    entity.PersonId,
                    contactType,
                    entity.Id,
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return Response<bool>.Error("INVALID_CONTACT_PRIMARY", [ex.Message]);
            }
        }

        var affected = await unitOfWork.SaveChangesAsync(cancellationToken);

        if (affected <= 0)
        {
            return Response<bool>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the person contact."]);
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Person contact deleted successfully.",
            Data = true
        };
    }
}
