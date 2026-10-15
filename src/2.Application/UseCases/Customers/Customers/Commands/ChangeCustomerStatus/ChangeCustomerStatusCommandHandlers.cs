using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Handles <see cref="ActivateCustomerCommand"/> through <see cref="ICustomerActivationService.EnsureActiveAsync"/>:
/// fails with <c>INVALID_PERSON</c> while the customer's person is inactive (rule 7).
/// </summary>
public sealed class ActivateCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICustomerActivationService activationService) : IRequestHandler<ActivateCustomerCommand, Response<CustomerResponseDto>>
{
    public async Task<Response<CustomerResponseDto>> Handle(ActivateCustomerCommand request, CancellationToken cancellationToken)
    {
        if (CustomerStatusChange.ResolveContext(currentUserService) is { } contextError)
        {
            return contextError;
        }

        var companyId = currentUserService.CompanyId;
        var customer = await unitOfWork.Customers.GetByIdAsync(companyId, request.Id, cancellationToken);
        if (customer is null)
        {
            return Response<CustomerResponseDto>.Error(CustomerErrorCodes.CustomerNotFound, ["Customer not found."]);
        }

        var activation = await activationService.EnsureActiveAsync(
            companyId,
            customer.PersonId,
            CustomerStatusSource.Manual,
            Guid.Parse(currentUserService.UserId!),
            notes: request.Notes,
            cancellationToken: cancellationToken);

        return await CustomerStatusChange.CompleteAsync(unitOfWork, activation, cancellationToken);
    }
}

/// <summary>
/// Handles <see cref="DeactivateCustomerCommand"/> through <see cref="ICustomerActivationService.DeactivateAsync"/>.
/// </summary>
public sealed class DeactivateCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICustomerActivationService activationService) : IRequestHandler<DeactivateCustomerCommand, Response<CustomerResponseDto>>
{
    public async Task<Response<CustomerResponseDto>> Handle(DeactivateCustomerCommand request, CancellationToken cancellationToken)
    {
        if (CustomerStatusChange.ResolveContext(currentUserService) is { } contextError)
        {
            return contextError;
        }

        var activation = await activationService.DeactivateAsync(
            currentUserService.CompanyId,
            request.Id,
            CustomerStatusSource.Manual,
            Guid.Parse(currentUserService.UserId!),
            notes: request.Notes,
            cancellationToken: cancellationToken);

        return await CustomerStatusChange.CompleteAsync(unitOfWork, activation, cancellationToken);
    }
}

/// <summary>
/// Shared steps of the activate/deactivate handlers.
/// </summary>
internal static class CustomerStatusChange
{
    internal static Response<CustomerResponseDto>? ResolveContext(ICurrentUserService currentUserService)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CustomerResponseDto>.Error(CustomerErrorCodes.CompanyRequired, ["The X-Company-Id header is required."]);
        }

        return Guid.TryParse(currentUserService.UserId, out _)
            ? null
            : Response<CustomerResponseDto>.Error(CustomerErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
    }

    internal static async Task<Response<CustomerResponseDto>> CompleteAsync(
        IUnitOfWork unitOfWork,
        Response<CustomerActivationResult> activation,
        CancellationToken cancellationToken)
    {
        if (!activation.IsSuccess || activation.Data is null)
        {
            return Response<CustomerResponseDto>.Error(activation.Message, activation.Errors);
        }

        var (customer, action) = activation.Data;
        if (action.HasValue)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new Response<CustomerResponseDto>
        {
            IsSuccess = true,
            Message = action?.ToString() ?? "Unchanged",
            Data = await CustomerDtoBuilder.BuildAsync(unitOfWork, customer, action)
        };
    }
}
