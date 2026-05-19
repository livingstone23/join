using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Handles soft delete for customers.
/// </summary>
public sealed class DeleteCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<DeleteCustomerCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteCustomerCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var customerRepository = unitOfWork.GetRepository<Customer>();
        var entity = await customerRepository.GetAsync(request.Id);

        if (entity is null || entity.CompanyId != currentUserService.CompanyId)
        {
            return Response<Guid>.Error(
                "CUSTOMER_NOT_FOUND",
                ["Customer not found."]);
        }

        entity.MarkAsDeleted();
        await customerRepository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the customer."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Customer deleted successfully.",
            Data = entity.Id
        };
    }
}
