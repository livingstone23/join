using JOIN.Application.Common;
using JOIN.Domain.Security;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Handles customer updates for lifecycle stage and active state.
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
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var tenantId = currentUserService.CompanyId;
        var customerRepository = unitOfWork.GetRepository<Customer>();
        var entity = await customerRepository.GetAsync(request.Id);

        if (entity is null || entity.CompanyId != tenantId)
        {
            return Response<CustomerResponseDto>.Error(
                "CUSTOMER_NOT_FOUND",
                ["Customer not found."]);
        }

        entity.UpdateLifecycle(request.PersonLifecycleStage);

        if (request.IsActive)
        {
            entity.Reactivate();
        }
        else
        {
            entity.Deactivate();
        }

        await customerRepository.UpdateAsync(entity);
        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<CustomerResponseDto>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the customer."]);
        }

        var person = await unitOfWork.GetRepository<Person>().GetAsync(entity.PersonId);
        var user = await unitOfWork.GetRepository<ApplicationUser>().GetAsync(entity.UserId);

        return new Response<CustomerResponseDto>
        {
            IsSuccess = true,
            Message = "Customer updated successfully.",
            Data = new CustomerResponseDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                PersonId = entity.PersonId,
                UserId = entity.UserId,
                CustomerCode = entity.CustomerCode,
                PersonLifecycleStage = (int)entity.PersonLifecycleStage,
                PersonLifecycleStageName = entity.PersonLifecycleStage.GetDisplayName(),
                PersonName = person is null ? string.Empty : BuildPersonName(person),
                UserEmail = user?.Email ?? string.Empty,
                IsActive = entity.IsActive,
                ActivatedAt = entity.ActivatedAt,
                DeactivatedAt = entity.DeactivatedAt,
                CreatedAt = entity.Created
            }
        };
    }

    private static string BuildPersonName(Person person)
    {
        if (person.PersonType == Domain.Enums.PersonType.Legal)
        {
            return person.CommercialName ?? person.FirstName;
        }

        return string.Join(
            ' ',
            new[] { person.FirstName, person.MiddleName, person.LastName, person.SecondLastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
