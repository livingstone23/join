using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Handles soft-delete operations for customer aggregates.
/// </summary>
/// <param name="unitOfWork">Unit of Work used for transactional persistence.</param>
/// <param name="currentUserService">Current tenant context.</param>
public class DeleteCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<DeleteCustomerCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Marks a customer as deleted by storing a date-stamp integer in GcRecord.
    /// </summary>
    /// <param name="request">The delete command payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A response containing the deleted customer identifier.</returns>
    public async Task<Response<Guid>> Handle(DeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var company = await companyRepository.GetAsync(currentUserService.CompanyId);
        if (company is null)
        {
            return Response<Guid>.Error(
                "INVALID_COMPANY",
                ["The provided company does not exist or is inactive."]);
        }

        var customerEntity = await _unitOfWork.Customers.GetForUpdateAsync(request.Id, currentUserService.CompanyId);
        if (customerEntity is null)
        {
            return Response<Guid>.Error(
                "CUSTOMER_NOT_FOUND",
                ["Customer not found for the current company."]);
        }

        var deletedAtUtc = DateTime.UtcNow;

        customerEntity.MarkAsDeleted(deletedAtUtc);

        foreach (var address in customerEntity.Addresses.Where(a => a.GcRecord == 0))
        {
            address.MarkAsDeleted(deletedAtUtc);
        }

        foreach (var contact in customerEntity.Contacts.Where(c => c.GcRecord == 0))
        {
            contact.MarkAsDeleted(deletedAtUtc);
        }

        await _unitOfWork.Customers.UpdateAsync(customerEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the customer."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Data = customerEntity.Id,
            Message = "Customer deleted successfully."
        };
    }
}
