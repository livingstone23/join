using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Admin.PersonContacts;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonContacts.Commands;

/// <summary>
/// Handles customer contact updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdatePersonContactCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonContactPrimaryCoordinator primaryCoordinator) : IRequestHandler<UpdatePersonContactCommand, Response<Guid>>
{
    /// <summary>
    /// Updates a customer contact for the current tenant.
    /// </summary>
    public async Task<Response<Guid>> Handle(UpdatePersonContactCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var contactRepository = unitOfWork.PersonContacts;
        var entity = await contactRepository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException(
                nameof(PersonContact),
                request.Id,
                "Person contact not found for the current tenant.");
        }

        if (entity.PersonId != request.PersonId)
        {
            throw new NotFoundException(
                nameof(PersonContact),
                request.Id,
                "Person contact not found for the requested customer.");
        }

        var previousContactType = entity.ContactType;
        var wasPrimary = entity.IsPrimary;

        try
        {
            entity.Update(request.ContactType, request.ContactValue, request.Comments);

            if (previousContactType != request.ContactType && wasPrimary)
            {
                await primaryCoordinator.PromoteNextPrimaryAsync(
                    companyId,
                    request.PersonId,
                    previousContactType,
                    entity.Id,
                    cancellationToken);
            }

            if (request.IsPrimary)
            {
                await primaryCoordinator.ClearOtherPrimariesAsync(
                    companyId,
                    request.PersonId,
                    request.ContactType,
                    entity.Id,
                    cancellationToken);
                entity.SetAsPrimary();
            }
            else
            {
                entity.RemovePrimary();
            }
        }
        catch (ArgumentException ex)
        {
            return Response<Guid>.Error("INVALID_CONTACT_DATA", [ex.Message]);
        }
        catch (InvalidOperationException ex)
        {
            return Response<Guid>.Error("INVALID_CONTACT_PRIMARY", [ex.Message]);
        }

        await contactRepository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the customer contact."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person contact updated successfully.",
            Data = entity.Id
        };
    }
}
