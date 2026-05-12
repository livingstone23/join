using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Commands;

/// <summary>
/// Handles customer contact updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdateCustomerContactCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<UpdateCustomerContactCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a customer contact for the current tenant.
    /// </summary>
    /// <param name="request">The update-contact command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the updated contact identifier.</returns>
    public async Task<Response<Guid>> Handle(UpdateCustomerContactCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var contactRepository = _unitOfWork.GetRepository<CustomerContact>();
        var contacts = await contactRepository.GetAllAsync();

        var entity = contacts.FirstOrDefault(contact =>
            contact.Id == request.Id &&
            contact.CompanyId == currentUserService.CompanyId);

        if (entity is null)
        {
            throw new NotFoundException(
                nameof(CustomerContact),
                request.Id,
                "Customer contact not found for the current tenant.");
        }

        if (entity.CustomerId != request.CustomerId)
        {
            throw new NotFoundException(
                nameof(CustomerContact),
                request.Id,
                "Customer contact not found for the requested customer.");
        }

        entity.ContactType = request.ContactType;
        entity.ContactValue = request.ContactValue.Trim();
        entity.IsPrimary = request.IsPrimary;
        entity.Comments = request.Comments?.Trim();

        await contactRepository.UpdateAsync(entity);

        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the customer contact."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Customer contact updated successfully.",
            Data = entity.Id
        };
    }
}
