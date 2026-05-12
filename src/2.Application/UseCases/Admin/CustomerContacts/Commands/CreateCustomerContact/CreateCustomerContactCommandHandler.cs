using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerContacts.Commands;

/// <summary>
/// Handles customer contact creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreateCustomerContactCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<CreateCustomerContactCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a customer contact associated with the authenticated tenant company.
    /// </summary>
    /// <param name="request">The create-contact command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the created contact identifier.</returns>
    public async Task<Response<Guid>> Handle(CreateCustomerContactCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var customerRepository = _unitOfWork.GetRepository<Customer>();
        var customer = await customerRepository.GetAsync(request.CustomerId);

        if (customer is null || customer.CompanyId != currentUserService.CompanyId || customer.GcRecord != 0)
        {
            return Response<Guid>.Error("CUSTOMER_NOT_FOUND", ["The requested customer does not exist in the current tenant."]);
        }

        var entity = new CustomerContact
        {
            CustomerId = request.CustomerId,
            ContactType = request.ContactType,
            ContactValue = request.ContactValue.Trim(),
            IsPrimary = request.IsPrimary,
            Comments = request.Comments?.Trim(),
            CompanyId = currentUserService.CompanyId
        };

        var contactRepository = _unitOfWork.GetRepository<CustomerContact>();
        await contactRepository.InsertAsync(entity);

        var result = await _unitOfWork.SaveAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("CONTACT_CREATE_FAILED", ["The customer contact could not be created due to a persistence error."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Customer contact created successfully.",
            Data = entity.Id
        };
    }
}
