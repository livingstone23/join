


using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Handles the creation of a new Customer, ensuring transactional integrity.
/// </summary>
/// <param name="unitOfWork">Unit of Work used for command-side persistence and reference validation.</param>
/// <param name="currentUserService">Current user context used to enforce tenant isolation.</param>
public class CreateCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<CreateCustomerCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a customer after validating tenant context and foreign key references.
    /// </summary>
    /// <param name="request">The command containing the customer payload.</param>
    /// <param name="cancellationToken">A cancellation token for the async workflow.</param>
    /// <returns>A standard response with the created customer identifier when successful.</returns>
    public async Task<Response<Guid>> Handle(CreateCustomerCommand request, CancellationToken cancellationToken)
    {
        var response = new Response<Guid>();
        var mapper = new CustomerMapper();

        if (currentUserService.CompanyId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.Message = "CompanyId header or claim is required.";
            response.Errors = ["The X-Company-Id header is required."];
            return response;
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var company = await companyRepository.GetAsync(currentUserService.CompanyId);
        if (company is null)
        {
            response.IsSuccess = false;
            response.Message = "The provided company does not exist or is inactive.";
            response.Errors = ["Invalid CompanyId."];
            return response;
        }

        var identificationTypeRepository = _unitOfWork.GetRepository<IdentificationType>();
        var identificationType = await identificationTypeRepository.GetAsync(request.CustomerDto.IdentificationTypeId);
        if (identificationType is null)
        {
            response.IsSuccess = false;
            response.Message = "The provided identification type does not exist or is inactive.";
            response.Errors = ["Invalid IdentificationTypeId."];
            return response;
        }

        var customerAlreadyExists = await _unitOfWork.Customers.ExistsByCompanyAndIdentificationAsync(
            currentUserService.CompanyId,
            request.CustomerDto.IdentificationNumber);

        if (customerAlreadyExists)
        {
            return Response<Guid>.Error(
                "CUSTOMER_ALREADY_EXISTS",
                ["A customer with the same identification number already exists for this company."]);
        }

        // 1. Map DTO to Entity (Mapperly ignores the Id automatically to let DB/Constructor handle it)
        var customerEntity = mapper.ToEntity(request.CustomerDto);
        customerEntity.CompanyId = currentUserService.CompanyId;

        // 2. Insert into Context (EF Core Change Tracker picks this up)
        await _unitOfWork.Customers.InsertAsync(customerEntity);

        // 3. Commit Transaction (This triggers the AuditableEntitySaveChangesInterceptor)
        var result = await _unitOfWork.SaveAsync(cancellationToken);

        if (result > 0)
        {
            response.Data = customerEntity.Id;
            response.IsSuccess = true;
            response.Message = "Customer created successfully.";
        }
        else
        {
            response.IsSuccess = false;
            response.Message = "Failed to create the customer due to a database error.";
        }

        return response;
        
    }
}