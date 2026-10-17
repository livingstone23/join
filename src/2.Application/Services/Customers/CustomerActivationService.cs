using JOIN.Application.Common;
using JOIN.Application.Interface.Customers;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers;

namespace JOIN.Application.Services.Customers;

/// <inheritdoc />
public sealed class CustomerActivationService(
    IUnitOfWork unitOfWork,
    ICustomerCodeGenerator customerCodeGenerator) : ICustomerActivationService
{
    /// <inheritdoc />
    public async Task<Response<CustomerActivationResult>> EnsureActiveAsync(
        Guid companyId,
        Guid personId,
        CustomerStatusSource source,
        Guid userId,
        Guid? sourceReferenceId = null,
        string? notes = null,
        Guid? customerStageId = null,
        CancellationToken cancellationToken = default)
    {
        // Serializes every event on the same person: the second one reads the row the first one committed.
        await unitOfWork.AcquireCustomerPersonLockAsync(companyId, personId, cancellationToken);

        var person = (await unitOfWork.GetRepository<Person>()
                .GetAllIncludingDeletedAsync(p => p.Id == personId && p.CompanyId == companyId && p.GcRecord == 0))
            .FirstOrDefault();

        if (person is null || !person.IsActive)
        {
            return Response<CustomerActivationResult>.Error(
                CustomerErrorCodes.InvalidPerson,
                ["The person does not exist, is inactive or deleted in the current company."]);
        }

        // SPEC 58 rule 4: every customer has an identification (SPEC 59 makes it configurable per company).
        if (!person.HasIdentification)
        {
            return Response<CustomerActivationResult>.Error(
                CustomerErrorCodes.PersonIdentificationRequired,
                ["The person needs an identification type and number before becoming a customer."]);
        }

        var customer = await unitOfWork.Customers.GetByPersonAsync(companyId, personId, cancellationToken);

        if (customer is null)
        {
            var stage = await ResolveStageAsync(companyId, customerStageId);
            if (stage is null)
            {
                return Response<CustomerActivationResult>.Error(
                    CustomerErrorCodes.InvalidCustomerStage,
                    ["The customer stage does not exist in the current company, is deleted or inactive."]);
            }

            var code = await customerCodeGenerator.GenerateNextAsync(companyId, cancellationToken);
            customer = Customer.Create(companyId, personId, userId, code, stage.Id);
            await unitOfWork.Customers.InsertAsync(customer);
            await LogAsync(customer, CustomerStatusAction.Created, source, userId, sourceReferenceId, notes);

            return Success(customer, CustomerStatusAction.Created);
        }

        CustomerStatusAction action;
        if (customer.Reactivate())
        {
            await unitOfWork.Customers.UpdateAsync(customer);
            action = CustomerStatusAction.Activated;
        }
        else
        {
            action = CustomerStatusAction.Confirmed;
        }

        await LogAsync(customer, action, source, userId, sourceReferenceId, notes);
        return Success(customer, action);
    }

    /// <inheritdoc />
    public async Task<Response<CustomerActivationResult>> DeactivateAsync(
        Guid companyId,
        Guid customerId,
        CustomerStatusSource source,
        Guid userId,
        Guid? sourceReferenceId = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        var customer = await unitOfWork.Customers.GetByIdAsync(companyId, customerId, cancellationToken);
        if (customer is null)
        {
            return Response<CustomerActivationResult>.Error(
                CustomerErrorCodes.CustomerNotFound,
                ["Customer not found."]);
        }

        if (!customer.Deactivate())
        {
            return Success(customer, null);
        }

        await unitOfWork.Customers.UpdateAsync(customer);
        await LogAsync(customer, CustomerStatusAction.Deactivated, source, userId, sourceReferenceId, notes);
        return Success(customer, CustomerStatusAction.Deactivated);
    }

    private async Task<CustomerStage?> ResolveStageAsync(Guid companyId, Guid? customerStageId)
    {
        var stages = await unitOfWork.GetRepository<CustomerStage>().GetAllIncludingDeletedAsync(s =>
            s.CompanyId == companyId
            && s.GcRecord == 0
            && s.IsActive
            && (customerStageId.HasValue ? s.Id == customerStageId.Value : s.IsDefault));

        return stages.FirstOrDefault();
    }

    private Task<bool> LogAsync(
        Customer customer,
        CustomerStatusAction action,
        CustomerStatusSource source,
        Guid userId,
        Guid? sourceReferenceId,
        string? notes) =>
        unitOfWork.GetRepository<CustomerStatusHistory>().InsertAsync(
            CustomerStatusHistory.Create(customer.CompanyId, customer.Id, action, source, userId, sourceReferenceId, notes));

    private static Response<CustomerActivationResult> Success(Customer customer, CustomerStatusAction? action) =>
        new()
        {
            IsSuccess = true,
            Message = action?.ToString() ?? "Unchanged",
            Data = new CustomerActivationResult(customer, action)
        };
}
