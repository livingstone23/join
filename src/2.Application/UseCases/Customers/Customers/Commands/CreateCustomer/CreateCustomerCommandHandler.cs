using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Handles <see cref="CreateCustomerCommand"/> through <see cref="ICustomerActivationService"/> (source <c>Manual</c>).
/// <see cref="Response{T}.Message"/> is the resulting action: <c>Created</c>, <c>Activated</c> or <c>Confirmed</c>.
/// </summary>
public sealed class CreateCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICustomerActivationService activationService) : IRequestHandler<CreateCustomerCommand, Response<CustomerResponseDto>>
{
    public async Task<Response<CustomerResponseDto>> Handle(
        CreateCustomerCommand request,
        CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CustomerResponseDto>.Error(
                CustomerErrorCodes.CompanyRequired,
                ["The X-Company-Id header is required."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId))
        {
            return Response<CustomerResponseDto>.Error(
                CustomerErrorCodes.UserRequired,
                ["The authenticated user could not be resolved."]);
        }

        var activation = await activationService.EnsureActiveAsync(
            currentUserService.CompanyId,
            request.PersonId,
            CustomerStatusSource.Manual,
            userId,
            customerStageId: request.CustomerStageId,
            cancellationToken: cancellationToken);

        if (!activation.IsSuccess || activation.Data is null)
        {
            return Response<CustomerResponseDto>.Error(activation.Message, activation.Errors);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var (customer, action) = activation.Data;
        return new Response<CustomerResponseDto>
        {
            IsSuccess = true,
            Message = action!.Value.ToString(),
            Data = await CustomerDtoBuilder.BuildAsync(unitOfWork, customer, action)
        };
    }
}
