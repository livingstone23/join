using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CustomerAddresses.Commands;

/// <summary>
/// Handles soft delete operations for customer addresses using Entity Framework Core.
/// </summary>
/// <param name="unitOfWork">Unit of work used to coordinate persistence.</param>
/// <param name="currentUserService">Service that exposes the active tenant identifier.</param>
public sealed class DeleteCustomerAddressCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<DeleteCustomerAddressCommand, Response<bool>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Marks the customer address as logically deleted for the current tenant.
    /// </summary>
    /// <param name="request">The delete command.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A response indicating whether the soft delete succeeded.</returns>
    public async Task<Response<bool>> Handle(DeleteCustomerAddressCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<bool>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<CustomerAddress>();
        var addresses = await repository.GetAllAsync();

        var entity = addresses.FirstOrDefault(address =>
            address.Id == request.Id &&
            address.CompanyId == currentUserService.CompanyId);

        if (entity is null)
        {
            return Response<bool>.Error(
                "CUSTOMER_ADDRESS_NOT_FOUND",
                ["No active address was found for the current tenant."]);
        }

        entity.MarkAsDeleted();

        await repository.UpdateAsync(entity);

        var affected = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (affected <= 0)
        {
            return Response<bool>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the customer address."]);
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Customer address deleted successfully.",
            Data = true
        };
    }
}