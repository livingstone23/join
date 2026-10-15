using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Handles <see cref="UpdateCustomerCommand"/>: moves the customer to a live, active stage of its company.
/// Never touches the active state (SPEC 55 rule 6).
/// </summary>
public sealed class UpdateCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<UpdateCustomerCommand, Response<CustomerResponseDto>>
{
    public async Task<Response<CustomerResponseDto>> Handle(
        UpdateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CustomerResponseDto>.Error(
                CustomerErrorCodes.CompanyRequired,
                ["The X-Company-Id header is required."]);
        }

        var companyId = currentUserService.CompanyId;
        var customer = await unitOfWork.Customers.GetByIdAsync(companyId, request.Id, cancellationToken);
        if (customer is null)
        {
            return Response<CustomerResponseDto>.Error(
                CustomerErrorCodes.CustomerNotFound,
                ["Customer not found."]);
        }

        var stageIsUsable = (await unitOfWork.GetRepository<CustomerStage>().GetAllIncludingDeletedAsync(s =>
                s.Id == request.CustomerStageId && s.CompanyId == companyId && s.GcRecord == 0 && s.IsActive))
            .Any();

        if (!stageIsUsable)
        {
            return Response<CustomerResponseDto>.Error(
                CustomerErrorCodes.InvalidCustomerStage,
                ["The customer stage does not exist in the current company, is deleted or inactive."]);
        }

        if (customer.CustomerStageId != request.CustomerStageId)
        {
            customer.ChangeStage(request.CustomerStageId);
            await unitOfWork.Customers.UpdateAsync(customer);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new Response<CustomerResponseDto>
        {
            IsSuccess = true,
            Message = "Customer updated successfully.",
            Data = await CustomerDtoBuilder.BuildAsync(unitOfWork, customer, null)
        };
    }
}
